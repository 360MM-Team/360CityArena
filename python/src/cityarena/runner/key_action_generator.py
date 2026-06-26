#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""
Key action generation script for the maze agent.
Called from Unity C# to decide key actions using the selected LLM provider
(OpenAI, Anthropic, Gemini, OpenAI-compatible endpoints, and others).
"""

import sys
import json
import base64
import re
from typing import Dict, Any, List, Optional
import os
import logging
import time
from datetime import datetime

from cityarena.paths import DEFAULT_OUTPUT_ROOT, resolve_repo_path
from cityarena.models.llm_client import build_llm_client, LLMClientError
from cityarena.prompts.system import SYSTEM_PROMPTS, REFLECTION_PROMPT
from cityarena.tasks.catalog import get_task_by_id
from cityarena.tasks.types import TaskType



class Message:
    """Container for a message in the conversation history."""

    def __init__(
        self, role: str, content: Any, has_image: bool = False, tokens: int = 0
    ):
        self.role = role
        self.content = content
        self.has_image = has_image
        self.tokens = tokens  # Approximate token count
        self.timestamp = datetime.now()

    def __str__(self):
        if isinstance(self.content, str):
            preview = (
                self.content[:50] + "..." if len(self.content) > 50 else self.content
            )
            return f"{self.role}: {preview}"
        else:
            return f"{self.role}: [Complex content with image={self.has_image}]"


class KeyActionGenerator:
    def __init__(
        self,
        api_key: Optional[str] = None,
        model: str | None = None,
        provider: str | None = None,
        pretrained: Optional[str] = None,
        max_tokens: int | None = None,
        temperature: float | None = None,
        validation_model: str | None = None,
        validation_provider: str | None = None,
        validation_pretrained: str | None = None,
        run_dir: str | None = None,
        task_id: int | None = None,
        api_base: Optional[str] = None,
        extra_headers: Optional[Dict[str, str]] = None,
        save_debug_artifacts: bool = False,
    ):
        # Model request timeout in seconds.
        try:
            self.timeout = int(os.getenv("LLM_TIMEOUT", "150"))
        except Exception:
            self.timeout = 150

        if not model:
            raise ValueError("model is required")
        if not provider:
            raise ValueError("provider is required")

        self.model = model
        self.provider = provider
        self.pretrained = pretrained if pretrained is not None else os.getenv("LLM_PRETRAINED")
        self.api_base = api_base or os.getenv("LLM_API_BASE")
        self.max_tokens = (
            max_tokens
            if max_tokens is not None
            else int(os.getenv("LLM_MAX_TOKENS", "8192"))
        )
        if temperature is not None:
            self.temperature = temperature
        elif os.getenv("LLM_TEMPERATURE"):
            self.temperature = float(os.getenv("LLM_TEMPERATURE", ""))
        else:
            self.temperature = None
        self.validation_model = validation_model or os.getenv("LLM_VALIDATION_MODEL")
        self.validation_provider = validation_provider or os.getenv("LLM_VALIDATION_PROVIDER")
        self.validation_pretrained = (
            validation_pretrained
            if validation_pretrained is not None
            else os.getenv("LLM_VALIDATION_PRETRAINED")
        )
        self.save_debug_artifacts = save_debug_artifacts

        if extra_headers is not None:
            self.extra_headers = extra_headers
        else:
            headers_env = os.getenv("LLM_EXTRA_HEADERS")
            if headers_env:
                try:
                    self.extra_headers = json.loads(headers_env)
                except json.JSONDecodeError:
                    logging.warning("Failed to parse LLM_EXTRA_HEADERS; ignoring value")
                    self.extra_headers = None
            else:
                self.extra_headers = None

        # Task.
        self.current_task = None
        if task_id is not None:
            resolved = get_task_by_id(task_id)
            if resolved is None:
                logging.warning("Task id %s not found in task catalog loaded from CSV.", task_id)
            else:
                self.current_task = resolved
                if (
                    self.current_task.task_type == TaskType.RelationalSpatialReasoning
                    and (not self.validation_model or not self.validation_provider)
                ):
                    raise ValueError(
                        "Relational Spatial Reasoning tasks require "
                        "validation_model and validation_provider"
                    )

        try:
            self.client = build_llm_client(
                model=self.model,
                api_key=api_key,
                api_base=self.api_base,
                extra_headers=self.extra_headers,
                pretrained=self.pretrained,
                provider_hint=self.provider,
            )
        except LLMClientError as exc:
            logging.error("Failed to initialize LLM client: %s", exc)
            raise
        # Per-run output directory, used only when debug artifacts are enabled.
        try:
            if run_dir:
                self.run_dir = run_dir
                if self.save_debug_artifacts:
                    os.makedirs(self.run_dir, exist_ok=True)
            else:
                if self.save_debug_artifacts:
                    base_dir = str(DEFAULT_OUTPUT_ROOT)
                    os.makedirs(base_dir, exist_ok=True)
                    ts = datetime.now().strftime("%Y%m%d_%H%M%S")
                    self.run_dir = os.path.join(base_dir, f"run_{ts}")
                    os.makedirs(self.run_dir, exist_ok=True)
                else:
                    self.run_dir = None
        except Exception:
            # Fallback to the old behavior if directory creation fails.
            self.run_dir = None

        # Image output directory.
        try:
            if self.save_debug_artifacts and self.run_dir:
                self.images_dir = os.path.join(self.run_dir, "images")
                os.makedirs(self.images_dir, exist_ok=True)
            else:
                self.images_dir = None
        except Exception:
            self.images_dir = None

        # Persistent file paths.
        self._memory_file = (
            os.path.join(self.run_dir, "memo.txt")
            if self.save_debug_artifacts and self.run_dir
            else None
        )
        self._context_file = (
            os.path.join(self.run_dir, "context.jsonl")
            if self.save_debug_artifacts and self.run_dir
            else None
        )

        # Keep the history of observations and actions from Unity.
        # Each item: {"token": str, "action": Dict[str, bool], "raw_response": str, "thought": str}
        self.context_history = []
        self.reflection_memory = ""
        self._reset_last_call_state()

        if self.save_debug_artifacts:
            # Preserve context while running and restore it from persistent files if present.
            try:
                self._load_persistent_state()
            except Exception:
                # Continue even if restoration fails.
                pass

    def _reset_last_call_state(self) -> None:
        self.last_raw_response = ""
        self.last_parsed_json: Dict[str, Any] = {}
        self.last_parse_error = ""
        self.last_response_metadata: Dict[str, Any] = {}
        self.last_latency_seconds: Optional[float] = None
        self.last_error_type = ""
        self.last_error_message = ""

    def _load_image_as_base64(
        self, image_path: str = None
    ) -> str:
        """
        Return the image file encoded as Base64.

        Args:
            image_path: Path to the image.

        Returns:
            Base64-encoded image string.
        """
        try:
            resolved_path = resolve_repo_path(image_path)
            if not resolved_path.exists():
                logging.warning("Image not found: %s", resolved_path)
                return ""

            with open(resolved_path, "rb") as image_file:
                image_data = image_file.read()
                return base64.b64encode(image_data).decode("utf-8")
        except Exception as e:
            logging.error(f"Failed to load image: {e}")
            return ""

    def _ensure_data_url(self, image_payload: Any) -> str:
        """Convert base64 payloads to data URLs for image messages."""

        if image_payload is None:
            return ""
        if isinstance(image_payload, str):
            if image_payload.startswith("data:image"):
                return image_payload
            return f"data:image/png;base64,{image_payload}"
        if isinstance(image_payload, bytes):
            return "data:image/png;base64," + base64.b64encode(image_payload).decode("utf-8")
        return ""

    def _iter_image_payloads(self, payload: Any) -> List[str]:
        if payload is None:
            return []
        if isinstance(payload, list):
            return [p for p in payload if p]
        return [payload] if payload else []

    def generate_key_action_from_base64(
        self, camera_image_b64: str, map_image_b64: Any = "", position_x: float = 0.0, position_z: float = 0.0
    ) -> Dict[str, Any]:
        """
        Generate a key action from Base64-encoded images for server mode.
        """
        self._reset_last_call_state()
        try:
            if not self.current_task:
                self.last_error_type = "task_not_found"
                self.last_error_message = "current_task not found"
                return {"success": False, "error": "current_task not found"}
            
            if self.save_debug_artifacts:
                # Save images that will be sent, but continue if saving fails.
                try:
                    self._save_base64_image(camera_image_b64, "camera")
                    self._save_base64_image(map_image_b64, "map")
                except Exception:
                    pass

            # Build the system message.
            system_message_content = f"{SYSTEM_PROMPTS}\n\n{self.current_task.prompt}"

            # Task text for the user context.
            user_task = f"{REFLECTION_PROMPT}\n\n[memory]:\n{self.reflection_memory}\n\nCurrent situation analysis based on the camera view:"

            # Keep only the most recent context messages.
            context_messages = [
                {"role": m.role, "content": m.content}
                for m in self.context_history[-5:]
            ]

            context_text = ""
            if context_messages:
                context_lines: List[str] = []
                for msg in context_messages:
                    content = msg.get("content", "")
                    if isinstance(content, str):
                        formatted = content
                    else:
                        try:
                            formatted = json.dumps(content, ensure_ascii=False)
                        except Exception:
                            formatted = str(content)
                    context_lines.append(f"{msg.get('role', 'assistant')}: {formatted}")
                if context_lines:
                    context_text = "\n".join(context_lines)
                    user_task = f"{user_task}\n\n[recent_actions]\n{context_text}"

            user_content = [
                {"type": "text", "text": user_task},
                {"type": "image_url", "image_url": {"url": self._ensure_data_url(camera_image_b64)}},
            ]
            
            if self.current_task.requires_current_location:
                user_content.append(
                    {
                        "type": "text",
                        "text": "Current Location: Use this image for understanding the current location",
                    }
                )
                
                for idx, map_payload in enumerate(self._iter_image_payloads(map_image_b64)):
                    user_content.append(
                        {
                            "type": "image_url",
                            "image_url": {"url": self._ensure_data_url(map_payload)},
                        }
                    )

            # Add map images for the user context when available.
            if self.current_task.additional_task_images:
                user_content.append(
                    {
                        "type": "text",
                        "text": "Task Reference Images: Use these images for understanding the task requirements",
                    }
                )
                
                for image_path in self.current_task.additional_task_images:
                    b64 = self._load_image_as_base64(image_path)
                    user_content.append(
                        {
                            "type": "image_url",
                            "image_url": {"url": self._ensure_data_url(b64)},
                        }
                    )

            request_started = time.monotonic()
            try:
                content = self.client.generate_response(
                    system_prompt=system_message_content,
                    context_messages=context_messages,
                    user_content=user_content,
                    context_text=context_text,
                    timeout=self.timeout,
                    max_tokens=self.max_tokens,
                    temperature=self.temperature,
                )
                self.last_latency_seconds = time.monotonic() - request_started
                self.last_raw_response = content or ""
                self.last_response_metadata = dict(
                    getattr(self.client, "last_response_metadata", {}) or {}
                )
            except LLMClientError as api_error:
                self.last_latency_seconds = time.monotonic() - request_started
                error_type = type(api_error).__name__
                error_msg = str(api_error)
                self.last_error_type = "llm_api_error"
                self.last_error_message = f"{error_type}: {error_msg}"
                logging.error(f"LLM API error: {error_type} - {error_msg}")
                return {"success": False, "error": f"LLM API error: {error_type} - {error_msg}"}

            json_content = self.extract_json_from_content(content)
            self.last_parsed_json = json_content
            updated_memory = json_content.get("memory", "")
            if isinstance(updated_memory, str) and updated_memory.strip():
                self.reflection_memory = updated_memory
            thought = json_content.get("thought", "")
            action = json_content.get("action", "")
            answer = json_content.get("answer", "")

            if not self.validate_action(action):
                error_msg = f"Invalid action: {action}"
                self.last_error_type = "parse_error" if self.last_parse_error else "invalid_action"
                self.last_error_message = error_msg
                return {"success": False, "error": error_msg}

            # Forward duration in seconds: convert answer=SMALL/MEDIUM/LARGE for W.
            # Defaults: W=2.0 seconds, other actions=0.5 seconds.
            def _size_to_seconds(ans: str, default_w: float = 2.0) -> float:
                if not ans:
                    return default_w
                s = str(ans).strip().upper()
                if s in ("SMALL", "S"):
                    return 1.0
                if s in ("MEDIUM", "M"):
                    return 3.0
                if s in ("LARGE", "L", "BIG"):
                    return 5.0
                # Unknown values use the default.
                return default_w

            # Rotation amount from AI to Unity uses size labels (S/M/L).
            # Angles: SMALL(S)=30 degrees / MEDIUM(M)=60 degrees / LARGE(L)=90 degrees.
            # Seconds are hard-coded from Unity's expected rotation speed.
            #   Assumption: CameraRotator.cs rotates 0.2 * _rotateSpeedRatio degrees per frame.
            #               The Akihabara scene uses _rotateSpeedRatio=2.
            #               At 60 fps: 0.2 * 2 * 60 = 24 deg/s.
            DEG_PER_SEC = 24.0

            def _parse_hold_seconds_from_answer(
                ans: str, default_other: float = 0.5
            ) -> float:
                if not ans:
                    return default_other
                s = str(ans).strip().upper()
                # Presets: map S/M/L to angles.
                deg_per_sec = DEG_PER_SEC
                if s in ("SMALL", "S"):
                    return 30.0 / deg_per_sec
                if s in ("MEDIUM", "M"):
                    return 60.0 / deg_per_sec
                if s in ("LARGE", "L", "BIG"):
                    return 90.0 / deg_per_sec
                return default_other

            if action == "W":
                hold_seconds = _size_to_seconds(answer, default_w=2.0)
            else:
                hold_seconds = _parse_hold_seconds_from_answer(
                    answer, default_other=0.5
                )

            result = {
                "success": True,
                "action": action,
                "answer": answer,
                "hold_seconds": hold_seconds,
                "thought": thought,
                "reflection": self.reflection_memory,
            }
            
            # Run validation when the action is ANSWER.
            if action == "ANSWER":
                try:
                    validation_result = self.current_task.validate_answer(
                        answer,
                        position_x,
                        position_z,
                        validation_model=self.validation_model,
                        validation_provider=self.validation_provider,
                        validation_pretrained=self.validation_pretrained,
                    )
                except Exception as validation_error:
                    self.last_error_type = "validation_error"
                    self.last_error_message = str(validation_error)
                    validation_result = {
                        "is_correct": False,
                        "expected": self.current_task.answer or "",
                        "user_answer": answer,
                        "message": f"Validation error: {validation_error}",
                        "validation_error": type(validation_error).__name__,
                    }
                result["validation"] = validation_result

            self.context_history.append(
                Message(
                    role="assistant",
                    content=json.dumps(
                        {
                            "thought": thought,
                            "action": action,
                        }
                    ),
                )
            )

            if self.save_debug_artifacts:
                # Persist the memo and append to the context.
                try:
                    self._persist_step(thought=thought, action=action)
                except Exception:
                    pass

            logging.debug(
                "llm_result task_id=%s success=%s action=%s has_validation=%s latency=%.3f error_type=%s",
                getattr(self.current_task, "id", ""),
                result.get("success"),
                result.get("action"),
                "validation" in result,
                self.last_latency_seconds if self.last_latency_seconds is not None else -1.0,
                self.last_error_type,
            )
            return result
        
        except Exception as e:
            # Log details for unexpected errors.
            error_type = type(e).__name__
            error_msg = str(e)
            self.last_error_type = "unexpected_error"
            self.last_error_message = f"{error_type}: {error_msg}"
            logging.error(f"Unexpected error in generate_key_action_from_base64: {error_type} - {error_msg}", exc_info=True)
            print(f"Error: {error_type} - {error_msg}", file=sys.stderr)
            return {"success": False, "error": f"{error_type}: {error_msg}"}

    def extract_json_from_content(self, content: str) -> dict:
        """Extract JSON from the response."""
        if not content:
            self.last_parse_error = "empty_response"
            return {}
        candidate = content
        match = re.search(r"```json\s*(.*?)```", content, flags=re.IGNORECASE | re.DOTALL)
        if match:
            candidate = match.group(1).strip()
        try:
            parsed = json.loads(candidate)
            if not isinstance(parsed, dict):
                self.last_parse_error = "json_root_not_object"
                return {}
            return parsed
        except json.JSONDecodeError as exc:
            self.last_parse_error = f"json_decode_error: {exc.msg}"
            return {}

    def _save_base64_image(self, base64_image: Any, image_type: str = "camera") -> str:
        """Save a received Base64 image as PNG and return the path, or an empty string on failure."""
        try:
            if not self.save_debug_artifacts:
                return ""
            if isinstance(base64_image, list):
                last_saved = ""
                for idx, item in enumerate(base64_image):
                    name = f"{image_type}_{idx}"
                    last_saved = self._save_base64_image(item, name)
                return last_saved

            data = base64_image
            if isinstance(data, str) and data.startswith("data:image"):
                comma_index = data.find(",")
                if comma_index != -1:
                    data = data[comma_index + 1 :]
            if not data:
                return ""
            if isinstance(data, bytes):
                image_bytes = data
            else:
                image_bytes = base64.b64decode(data)
            if self.images_dir:
                out_dir = self.images_dir
            else:
                out_dir = str(DEFAULT_OUTPUT_ROOT / "captured_images")
            os.makedirs(out_dir, exist_ok=True)
            ts = datetime.now().strftime("%Y%m%d_%H%M%S_%f")
            filename = f"{image_type}_image_{ts}.png"
            out_path = os.path.join(out_dir, filename)
            with open(out_path, "wb") as f:
                f.write(image_bytes)
            return out_path
        except Exception:
            return ""

    def validate_action(self, action: str) -> bool:
        """Check whether an action is valid."""
        if action in ["W", "S", "A", "D", "Q", "E", "LEFT", "RIGHT", "UP", "ANSWER"]:
            return True
        return False

    # --- Persistence and Restore Utilities ---
    def _persist_step(self, thought: str, action: str) -> None:
        """Save the reflection memo and append the context history."""
        if not self.save_debug_artifacts:
            return
        if self._memory_file:
            try:
                with open(self._memory_file, "w", encoding="utf-8") as f:
                    f.write(self.reflection_memory or "")
            except Exception:
                pass
        if self._context_file:
            try:
                record = {
                    "ts": datetime.now().isoformat(timespec="seconds"),
                    "role": "assistant",
                    "action": action,
                    "thought": thought,
                }
                with open(self._context_file, "a", encoding="utf-8") as f:
                    f.write(json.dumps(record, ensure_ascii=False) + "\n")
                # Keep the history bounded to roughly the latest 50 items.
                if len(self.context_history) > 50:
                    self.context_history = self.context_history[-50:]
            except Exception:
                pass

    def _load_persistent_state(self) -> None:
        """Restore memo and context.jsonl at startup if they exist."""
        if not self.save_debug_artifacts:
            return
        # memo
        if self._memory_file and os.path.exists(self._memory_file):
            try:
                with open(self._memory_file, "r", encoding="utf-8") as f:
                    self.reflection_memory = f.read().strip()
            except Exception:
                pass
