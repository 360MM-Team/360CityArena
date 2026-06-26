#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""
Long-running server that receives observations from Unity as Base64 camera images
and returns the next action. Conversation history is kept on the Python side.
The protocol is simple TCP JSON Lines.

Protocol:
  - Request: one JSON string per line
      {
        "type": "act",
        "image_b64": "...",
        "meta": { optional }
      }
  - Response: one JSON string per line, matching cityarena.runner.key_action_generator
      {
        "success": true,
        "action": "W|S|A|D|Q|E|LEFT|RIGHT|UP|ANSWER",
        "answer": "S|M|L (etc.)",
        "hold_seconds": number,
        "thought": "...",
        "reflection": "..."
      }

Examples:
  uv run python -m cityarena.runner.agent_server --api-key $OPENAI_API_KEY --host 127.0.0.1 --port 8765 --model gpt-5 --provider openai --task-id 0
  uv run python -m cityarena.runner.agent_server --api-key $ANTHROPIC_API_KEY --host 127.0.0.1 --port 8765 --model claude-sonnet-4-5 --provider anthropic --task-id 0
"""

import argparse
import csv
import json
import logging
import math
import socket
import time
from datetime import datetime
from logging.handlers import RotatingFileHandler
from typing import Any, Optional, Tuple

import os

from cityarena.paths import DEFAULT_OUTPUT_ROOT
from cityarena.tasks.prompt_templates import LOCALIZATION_STEP_LIMIT


class SummaryWriteError(RuntimeError):
    """Raised when a benchmark result row cannot be persisted."""


def KeyActionGenerator(*args: Any, **kwargs: Any) -> Any:
    from cityarena.runner.key_action_generator import (
        KeyActionGenerator as _KeyActionGenerator,
    )

    return _KeyActionGenerator(*args, **kwargs)


RESULT_FIELDNAMES = [
    "timestamp",
    "run_id",
    "experiment_id",
    "task_id",
    "task_type",
    "difficulty",
    "map_id",
    "landmark",
    "model",
    "provider",
    "pretrained",
    "max_tokens",
    "temperature",
    "validation_model",
    "validation_provider",
    "validation_pretrained",
    "status",
    "scored_success",
    "is_correct",
    "validation_available",
    "completed_by_answer",
    "evaluation_metric",
    "evaluation_score",
    "steps",
    "act_requests",
    "elapsed_seconds",
    "final_action",
    "final_position_x",
    "final_position_z",
    "final_segment_path",
    "user_answer",
    "expected",
    "distance_to_goal",
    "validation_threshold",
    "message",
    "stop_reason_detail",
    "llm_request_count",
    "llm_error_count",
    "parse_error_count",
    "invalid_action_count",
    "request_error_count",
    "total_prompt_tokens",
    "total_completion_tokens",
    "total_tokens",
    "total_llm_latency_seconds",
    "avg_llm_latency_seconds",
    "last_finish_reason",
    "last_response_model",
    "last_error_type",
    "last_error_message",
    "artifact_dir",
]

LOCATION_VALIDATED_TASK_TYPES = {
    "MapNavigation",
    "LandmarkSearchWithLanguage",
    "LandmarkSearchWithImage",
    "LanguageGuidedNavigation",
}

NOISY_HTTP_LOGGERS = ("httpx", "httpcore", "openai", "anthropic", "urllib3")


def configure_benchmark_logging() -> None:
    for logger_name in NOISY_HTTP_LOGGERS:
        logging.getLogger(logger_name).setLevel(logging.WARNING)


def _csv_bool(value: Any) -> str:
    if isinstance(value, bool):
        return "true" if value else "false"
    if value in ("", None):
        return ""
    return str(value).lower() if str(value).lower() in {"true", "false"} else str(value)


def _optional_float(value: Any) -> float | None:
    try:
        if value in ("", None):
            return None
        return float(value)
    except (TypeError, ValueError):
        return None


def _optional_int(value: Any) -> int | None:
    try:
        if value in ("", None):
            return None
        return int(value)
    except (TypeError, ValueError):
        return None


class AgentServer:
    """JSON-Lines TCP server for agent inference."""

    def __init__(
        self,
        host: str,
        port: int,
        api_key: str | None,
        model: str,
        provider: str,
        pretrained: str | None,
        max_tokens: int,
        temperature: float | None,
        validation_model: str | None,
        validation_provider: str | None,
        validation_pretrained: str | None,
        log_file: str,
        task_id: int | None = None,
        api_base: str | None = None,
        extra_headers: str | None = None,
        organization: str | None = None,
        experiment_id: str | int | None = None,
        startup_activity_timeout: float | None = None,
        idle_activity_timeout: float | None = None,
        output_root: str | None = None,
        run_id: str | None = None,
        summary_path: str | None = None,
        save_debug_artifacts: bool = False,
        progress_log_interval: int = 10,
    ):
        configure_benchmark_logging()
        self.host = host
        self.port = port
        self.api_key = api_key
        if not model:
            raise ValueError("model is required")
        if not provider:
            raise ValueError("provider is required")
        self.model = model
        self.provider = provider
        self.pretrained = pretrained
        self.max_tokens = max_tokens
        self.temperature = temperature
        self.validation_model = validation_model
        self.validation_provider = validation_provider
        self.validation_pretrained = validation_pretrained
        self.log_file = log_file
        self.shutdown_requested = False
        self.fatal_error: Optional[BaseException] = None
        self.task_id = task_id
        self.api_base = api_base or os.getenv("LLM_API_BASE")
        self.extra_headers = None
        self.organization = organization or os.getenv("OPENAI_ORG")
        if extra_headers:
            try:
                self.extra_headers = json.loads(extra_headers)
            except json.JSONDecodeError:
                logging.warning("Failed to parse --extra-headers JSON; ignoring value")
        # Experiment ID, used to group trials with the same ID in one folder.
        self.experiment_id = str(experiment_id) if experiment_id is not None else None
        self.save_debug_artifacts = save_debug_artifacts
        self.progress_log_interval = max(0, int(progress_log_interval or 0))
        self.project_root = str(DEFAULT_OUTPUT_ROOT.parent)
        resolved_output_root = output_root or str(DEFAULT_OUTPUT_ROOT)
        if not os.path.isabs(resolved_output_root):
            resolved_output_root = os.path.join(self.project_root, resolved_output_root)
        self.output_root = os.path.abspath(resolved_output_root)
        if run_id:
            raw_run_id = str(run_id)
        elif self.experiment_id:
            raw_run_id = f"{self.experiment_id}_{datetime.now().strftime('%Y%m%d_%H%M%S_%f')}"
        else:
            raw_run_id = datetime.now().strftime("run_%Y%m%d_%H%M%S_%f")
        self.run_id = "".join(
            ch if ch.isalnum() or ch in "._-" else "_" for ch in raw_run_id.strip()
        ) or datetime.now().strftime("run_%Y%m%d_%H%M%S")
        self.output_run_dir = os.path.join(self.output_root, self.run_id)
        os.makedirs(self.output_run_dir, exist_ok=True)
        if summary_path:
            self.summary_path = (
                summary_path
                if os.path.isabs(summary_path)
                else os.path.join(self.output_run_dir, summary_path)
            )
        else:
            self.summary_path = os.path.join(self.output_run_dir, "results.csv")
        os.makedirs(os.path.dirname(self.summary_path), exist_ok=True)
        # Location-change monitoring.
        try:
            self.location_epsilon = float(os.getenv("LOCATION_EPSILON", "0.05"))
        except Exception:
            self.location_epsilon = 0.05
        try:
            self.stagnant_limit = int(os.getenv("LOCATION_STAGNANT_LIMIT", "20"))
        except Exception:
            self.stagnant_limit = 20
        self._prev_position: Optional[Tuple[float, float]] = None
        self._unchanged_count: int = 0
        self._prev_action: Optional[str] = None
        # Distance-to-goal tracking for MapNavigation.
        self._prev_distance_to_goal: Optional[float] = None
        self._away_from_goal_count: int = 0
        try:
            self.away_from_goal_consecutive_limit = int(os.getenv("GOAL_AWAY_CONSECUTIVE_LIMIT", "5"))
        except Exception:
            self.away_from_goal_consecutive_limit = 5

        # Task metrics.
        self.task_started_at: Optional[datetime] = None
        self.act_request_count: int = 0
        self.step_count: int = 0
        self._server_start_monotonic = time.monotonic()
        self._last_client_contact: float | None = None
        self._last_act_time: float | None = None
        self._first_act_received = False
        self.startup_activity_timeout = (
            float(startup_activity_timeout)
            if startup_activity_timeout and startup_activity_timeout > 0
            else None
        )
        self.idle_activity_timeout = (
            float(idle_activity_timeout)
            if idle_activity_timeout and idle_activity_timeout > 0
            else None
        )
        self._timeout_event_emitted = False
        # Step limits, with per-type overrides.
        try:
            self.step_limit_default = int(os.getenv("STEP_LIMIT_DEFAULT", "300"))
        except Exception:
            self.step_limit_default = 50
        self.step_limit_by_type = {
            # Keyed by the tasks.TaskType name value.
            "Localization": LOCALIZATION_STEP_LIMIT,
            "LandmarkSearchWithLanguage": 50,
            "LandmarkSearchWithImage": 50,
            "Counting": 50,
            "MapNavigation":50,
            "ConstraintSatisfactionNavigation": 50,
            "ConstraintSatisficationNavigation": 50,
            "LanguageGuidedNavigation": 50,
            "RelationalSpatialReasoning": 50,
            "ExplorationToMapMatching": 50,
        }

        # Debug artifacts are opt-in. Summary rows always go to outputs/<run_id>/results.csv.
        if self.save_debug_artifacts:
            task_label = f"task_{self.task_id}" if self.task_id is not None else "server"
            self.run_dir = os.path.join(self.output_run_dir, "debug", task_label)
            os.makedirs(self.run_dir, exist_ok=True)
        else:
            self.run_dir = self.output_run_dir
        self.step_trace_path = (
            os.path.join(self.run_dir, "steps.jsonl")
            if self.save_debug_artifacts and self.run_dir
            else None
        )

        # Logging settings. File logs are kept only when debug artifacts are enabled.
        logger = logging.getLogger()
        logger.setLevel(logging.INFO)
        self._log_handler: RotatingFileHandler | None = None
        if self.save_debug_artifacts and self.log_file:
            if not os.path.isabs(self.log_file):
                log_path = os.path.join(self.run_dir, self.log_file)
            else:
                log_path = self.log_file
            handler = RotatingFileHandler(log_path, maxBytes=5 * 1024 * 1024, backupCount=3)
            handler.setFormatter(logging.Formatter("%(asctime)s [%(levelname)s] %(message)s"))
            logger.addHandler(handler)
            self._log_handler = handler

        # KeyActionGenerator stores visualizations and files under run_dir.
        # It also creates images/ and similar directories internally as needed.
        self.generator = KeyActionGenerator(
            api_key=self.api_key,
            model=self.model,
            provider=self.provider,
            pretrained=self.pretrained,
            max_tokens=self.max_tokens,
            temperature=self.temperature,
            validation_model=self.validation_model,
            validation_provider=self.validation_provider,
            validation_pretrained=self.validation_pretrained,
            run_dir=self.run_dir,
            task_id=self.task_id,
            api_base=self.api_base,
            extra_headers=self.extra_headers,
            save_debug_artifacts=self.save_debug_artifacts,
        )

        # Prevent duplicate summary writes.
        self._summary_written = False
        self.final_status = ""

        self._latest_position: Optional[Tuple[float, float]] = None
        self._latest_segment_path = ""
        self._latest_result_action = ""
        self._last_finish_reason = ""
        self._last_response_model = ""
        self._last_error_type = ""
        self._last_error_message = ""
        self._stop_reason_detail = ""
        self.llm_request_count = 0
        self.llm_error_count = 0
        self.parse_error_count = 0
        self.invalid_action_count = 0
        self.request_error_count = 0
        self.total_prompt_tokens = 0
        self.total_completion_tokens = 0
        self.total_tokens = 0
        self.total_llm_latency_seconds = 0.0

    def _record_llm_metrics(self) -> None:
        latency = getattr(self.generator, "last_latency_seconds", None)
        if isinstance(latency, (int, float)):
            self.llm_request_count += 1
            self.total_llm_latency_seconds += float(latency)

        metadata = dict(getattr(self.generator, "last_response_metadata", {}) or {})
        prompt_tokens = _optional_int(metadata.get("prompt_tokens"))
        completion_tokens = _optional_int(metadata.get("completion_tokens"))
        total_tokens = _optional_int(metadata.get("total_tokens"))
        if total_tokens is None and prompt_tokens is not None and completion_tokens is not None:
            total_tokens = prompt_tokens + completion_tokens
        if prompt_tokens is not None:
            self.total_prompt_tokens += prompt_tokens
        if completion_tokens is not None:
            self.total_completion_tokens += completion_tokens
        if total_tokens is not None:
            self.total_tokens += total_tokens
        self._last_finish_reason = str(metadata.get("finish_reason") or "")
        self._last_response_model = str(metadata.get("response_model") or "")

    def _record_result_error(self, result: dict) -> None:
        generator_error_type = str(getattr(self.generator, "last_error_type", "") or "")
        generator_error_message = str(getattr(self.generator, "last_error_message", "") or "")
        parse_error = str(getattr(self.generator, "last_parse_error", "") or "")
        if result.get("success"):
            if generator_error_type:
                self._last_error_type = generator_error_type
                self._last_error_message = generator_error_message
            return

        error_message = str(result.get("error") or generator_error_message or "")
        if parse_error or generator_error_type == "parse_error":
            self.parse_error_count += 1
            error_type = "parse_error"
            if parse_error:
                error_message = f"{error_message} ({parse_error})" if error_message else parse_error
        elif generator_error_type == "llm_api_error" or error_message.startswith("LLM API error"):
            self.llm_error_count += 1
            error_type = "llm_api_error"
        elif generator_error_type == "invalid_action" or error_message.startswith("Invalid action"):
            self.invalid_action_count += 1
            error_type = "invalid_action"
        else:
            self.request_error_count += 1
            error_type = generator_error_type or "request_error"
        self._last_error_type = error_type
        self._last_error_message = error_message

    def _validation_for_summary(
        self, status: str, validation: Optional[dict]
    ) -> tuple[dict, bool]:
        if isinstance(validation, dict):
            return validation, True

        task = getattr(self.generator, "current_task", None)
        task_type = getattr(getattr(task, "task_type", None), "name", "") if task else ""
        expected = getattr(task, "answer", "") or ""
        if task and task_type in LOCATION_VALIDATED_TASK_TYPES and self._latest_position is not None:
            try:
                computed = task.validate_answer(
                    "",
                    self._latest_position[0],
                    self._latest_position[1],
                    validation_model=self.validation_model,
                    validation_provider=self.validation_provider,
                    validation_pretrained=self.validation_pretrained,
                )
                message = computed.get("message", "")
                computed["message"] = (
                    f"{status}: final-position validation without ANSWER. {message}"
                )
                return computed, True
            except Exception as exc:
                return {
                    "is_correct": False,
                    "expected": expected,
                    "user_answer": "",
                    "message": f"{status}: validation error: {exc}",
                    "validation_error": type(exc).__name__,
                }, True

        if task:
            return {
                "is_correct": False,
                "expected": expected,
                "user_answer": "",
                "message": f"{status}: no validated ANSWER was produced.",
            }, True

        return {
            "is_correct": False,
            "expected": "",
            "user_answer": "",
            "message": f"{status}: validation unavailable because task metadata was not loaded.",
        }, False

    def _append_step_trace(
        self,
        request_index: int,
        req: dict,
        model_result: dict,
        returned_result: dict,
    ) -> None:
        if not self.step_trace_path:
            return
        map_images_b64 = req.get("map_images_b64")
        map_image_count = len(map_images_b64) if isinstance(map_images_b64, list) else 0
        if req.get("map_image_b64"):
            map_image_count += 1
        record = {
            "timestamp": datetime.now().isoformat(timespec="seconds"),
            "run_id": self.run_id,
            "task_id": self.task_id,
            "act_request": request_index,
            "successful_steps": self.step_count,
            "position": {
                "x": req.get("position_x"),
                "z": req.get("position_z"),
            },
            "segment_path": req.get("segment_path", ""),
            "inputs": {
                "has_camera_image": bool(req.get("image_b64")),
                "map_image_count": map_image_count,
            },
            "model_result": {
                "success": model_result.get("success"),
                "action": model_result.get("action"),
                "answer": model_result.get("answer"),
                "hold_seconds": model_result.get("hold_seconds"),
                "error": model_result.get("error"),
                "validation": model_result.get("validation"),
            },
            "returned_result": {
                "success": returned_result.get("success"),
                "action": returned_result.get("action"),
                "answer": returned_result.get("answer"),
                "hold_seconds": returned_result.get("hold_seconds"),
                "error": returned_result.get("error"),
            },
            "llm": {
                "latency_seconds": getattr(self.generator, "last_latency_seconds", None),
                "response_metadata": getattr(self.generator, "last_response_metadata", {}),
                "parse_error": getattr(self.generator, "last_parse_error", ""),
                "error_type": getattr(self.generator, "last_error_type", ""),
                "error_message": getattr(self.generator, "last_error_message", ""),
                "parsed_json": getattr(self.generator, "last_parsed_json", {}),
                "raw_response": getattr(self.generator, "last_raw_response", ""),
            },
        }
        try:
            with open(self.step_trace_path, "a", encoding="utf-8") as f:
                f.write(json.dumps(record, ensure_ascii=False) + "\n")
        except Exception:
            logging.exception("Failed to append step trace to %s", self.step_trace_path)

    def _append_summary(
        self,
        status: str,
        validation: Optional[dict] = None,
        stop_reason_detail: str = "",
    ) -> None:
        if self._summary_written:
            return
        try:
            self.final_status = status
            # Collected data.
            end_time = datetime.now()
            start_time = self.task_started_at or end_time
            elapsed = (end_time - start_time).total_seconds()
            task = getattr(self.generator, "current_task", None)
            task_type = getattr(getattr(task, "task_type", None), "name", "") if task else ""
            difficulty = getattr(task, "difficulty", "") if task else ""
            metadata = getattr(task, "metadata", {}) if task else {}
            map_id = metadata.get("map_id", "") if isinstance(metadata, dict) else ""
            landmark = metadata.get("landmark") or metadata.get("landmark_name") if isinstance(metadata, dict) else ""

            validation_payload, validation_available = self._validation_for_summary(
                status, validation
            )
            is_correct = validation_payload.get("is_correct", "")
            is_correct_bool = is_correct is True or str(is_correct).lower() == "true"
            message = validation_payload.get("message", "")
            user_answer = validation_payload.get("user_answer", "")
            expected = validation_payload.get("expected", "")
            distance_to_goal = validation_payload.get("distance", "")
            validation_threshold = validation_payload.get("threshold", "")
            completed_by_answer = status == "ANSWER"
            scored_success = bool(completed_by_answer and is_correct_bool)
            evaluation_metric = str(validation_payload.get("metric") or "")
            validation_score = _optional_float(validation_payload.get("score"))
            if validation_score is None:
                validation_score = 1.0 if is_correct_bool else 0.0
            evaluation_score = (
                min(1.0, max(0.0, validation_score))
                if completed_by_answer and validation_available
                else 0.0
            )
            artifact_dir = ""
            if self.save_debug_artifacts and self.run_dir:
                try:
                    artifact_dir = os.path.relpath(self.run_dir, self.output_run_dir)
                except ValueError:
                    artifact_dir = ""
            final_x = ""
            final_z = ""
            if self._latest_position is not None:
                final_x = f"{self._latest_position[0]:.4f}"
                final_z = f"{self._latest_position[1]:.4f}"
            avg_latency = (
                self.total_llm_latency_seconds / self.llm_request_count
                if self.llm_request_count
                else 0.0
            )

            row = {
                "timestamp": end_time.isoformat(timespec="seconds"),
                "run_id": self.run_id,
                "experiment_id": self.experiment_id or "",
                "task_id": self.task_id if self.task_id is not None else "",
                "task_type": task_type,
                "difficulty": difficulty,
                "map_id": map_id,
                "landmark": landmark,
                "model": self.model,
                "provider": self.provider,
                "pretrained": self.pretrained or "",
                "max_tokens": self.max_tokens,
                "temperature": "" if self.temperature is None else self.temperature,
                "validation_model": self.validation_model or "",
                "validation_provider": self.validation_provider or "",
                "validation_pretrained": self.validation_pretrained or "",
                "status": status,
                "scored_success": _csv_bool(scored_success),
                "is_correct": _csv_bool(is_correct),
                "validation_available": _csv_bool(validation_available),
                "completed_by_answer": _csv_bool(completed_by_answer),
                "evaluation_metric": evaluation_metric,
                "evaluation_score": f"{evaluation_score:.4f}",
                "steps": self.step_count,
                "act_requests": self.act_request_count,
                "elapsed_seconds": f"{elapsed:.2f}",
                "final_action": self._latest_result_action,
                "final_position_x": final_x,
                "final_position_z": final_z,
                "final_segment_path": self._latest_segment_path,
                "user_answer": user_answer,
                "expected": expected,
                "distance_to_goal": (
                    f"{float(distance_to_goal):.4f}"
                    if isinstance(distance_to_goal, (int, float))
                    else distance_to_goal
                ),
                "validation_threshold": validation_threshold,
                "message": message,
                "stop_reason_detail": stop_reason_detail or self._stop_reason_detail,
                "llm_request_count": self.llm_request_count,
                "llm_error_count": self.llm_error_count,
                "parse_error_count": self.parse_error_count,
                "invalid_action_count": self.invalid_action_count,
                "request_error_count": self.request_error_count,
                "total_prompt_tokens": self.total_prompt_tokens,
                "total_completion_tokens": self.total_completion_tokens,
                "total_tokens": self.total_tokens,
                "total_llm_latency_seconds": f"{self.total_llm_latency_seconds:.4f}",
                "avg_llm_latency_seconds": f"{avg_latency:.4f}",
                "last_finish_reason": self._last_finish_reason,
                "last_response_model": self._last_response_model,
                "last_error_type": self._last_error_type,
                "last_error_message": self._last_error_message,
                "artifact_dir": artifact_dir,
            }

            write_header = not os.path.exists(self.summary_path)
            if not write_header:
                with open(self.summary_path, newline="", encoding="utf-8") as ef:
                    try:
                        existing_header = next(csv.reader(ef))
                    except StopIteration:
                        existing_header = []
                if not existing_header:
                    write_header = True
                elif existing_header != RESULT_FIELDNAMES:
                    raise ValueError(
                        "existing results.csv header does not match the current schema; "
                        "use a fresh run_id or migrate the file before appending"
                    )
            with open(self.summary_path, "a", newline="", encoding="utf-8") as ef:
                writer = csv.DictWriter(ef, fieldnames=RESULT_FIELDNAMES)
                if write_header:
                    writer.writeheader()
                writer.writerow(row)
            self._summary_written = True
        except Exception as exc:
            error = SummaryWriteError(
                f"Failed to append benchmark summary to {self.summary_path}: {exc}"
            )
            self.fatal_error = error
            raise error from exc

    def _log_progress(self, action: Any) -> None:
        if self.progress_log_interval <= 0:
            return
        if self.act_request_count % self.progress_log_interval != 0:
            return
        final_x = ""
        final_z = ""
        if self._latest_position is not None:
            final_x = f"{self._latest_position[0]:.2f}"
            final_z = f"{self._latest_position[1]:.2f}"
        logging.info(
            "progress run_id=%s task_id=%s act_request=%d steps=%d last_action=%s pos=(%s,%s) llm_latency=%.3f",
            self.run_id,
            self.task_id,
            self.act_request_count,
            self.step_count,
            action or "",
            final_x,
            final_z,
            getattr(self.generator, "last_latency_seconds", None) or -1.0,
        )

    def _current_task_type_name(self) -> str:
        try:
            tt = getattr(self.generator.current_task, "task_type", None)
            name = getattr(tt, "name", None)
            return name or str(tt) or ""
        except Exception:
            return ""

    def _get_step_limit(self) -> int:
        try:
            type_name = self._current_task_type_name()
            limit = self.step_limit_by_type.get(type_name)
            if isinstance(limit, int) and limit > 0:
                return limit
            return self.step_limit_default
        except Exception:
            return self.step_limit_default

    def early_stopping(self, result: dict, req: dict, addr: Tuple[str, int]) -> dict:
        """Centralize early stopping before returning ANSWER.

        - Return ANSWER when the step limit is reached.
        - Return ANSWER when both position and action stagnate.
        - When action=ANSWER is detected, log validation, append the summary, and shut down gracefully.
        """
        try:
            action_tok = result.get("action")
            pos_x = req.get("position_x", 0.0)
            pos_z = req.get("position_z", 0.0)

            # --- Step limit check ---
            try:
                limit = self._get_step_limit()
                is_answer = (
                    isinstance(action_tok, str)
                    and action_tok.strip().upper() == "ANSWER"
                )
                if self.step_count >= limit and not is_answer:
                    logging.info(
                        "STEP LIMIT REACHED: type=%s limit=%d steps=%d",
                        self._current_task_type_name(),
                        limit,
                        self.step_count,
                    )
                    self.shutdown_requested = True
                    # Basic timing log.
                    _end = datetime.now()
                    _start = self.task_started_at or _end
                    _elapsed = (_end - _start).total_seconds()
                    logging.info("Task Steps: %d", self.step_count)
                    logging.info(
                        "Task Time: %.2f sec (start=%s end=%s)",
                        _elapsed,
                        _start.isoformat(timespec="seconds"),
                        _end.isoformat(timespec="seconds"),
                    )
                    # Append to the aggregate CSV.
                    reason = f"step_limit={limit}"
                    self._stop_reason_detail = reason
                    self._append_summary(
                        status="STEP_LIMIT",
                        validation=result.get("validation"),
                        stop_reason_detail=reason,
                    )
                    return {
                        "success": True,
                        "action": "ANSWER",
                        "thought": "Terminating due to step limit reached.",
                        "reflection": reason,
                    }
            except SummaryWriteError:
                raise
            except Exception:
                pass

            # --- Stagnation check for unchanged position and action ---
            has_position = ("position_x" in req) and ("position_z" in req)
            if has_position:
                try:
                    # Check whether the position changed.
                    position_unchanged = False
                    if self._prev_position is not None:
                        dx = float(pos_x) - float(self._prev_position[0])
                        dz = float(pos_z) - float(self._prev_position[1])
                        dist = math.hypot(dx, dz)
                        position_unchanged = dist < self.location_epsilon

                    # Check whether the action changed.
                    current_action_norm: Optional[str] = None
                    if isinstance(action_tok, str):
                        current_action_norm = action_tok.strip().upper()

                    action_unchanged = (
                        current_action_norm is not None
                        and self._prev_action is not None
                        and current_action_norm == self._prev_action
                    )

                    # Increment only when both position and action are unchanged.
                    if position_unchanged and action_unchanged:
                        self._unchanged_count += 1
                    else:
                        self._unchanged_count = 0

                    # Update state.
                    self._prev_position = (float(pos_x), float(pos_z))
                    self._prev_action = current_action_norm
                except Exception:
                    # On any failure, reset the count and only update state.
                    self._unchanged_count = 0
                    try:
                        self._prev_position = (float(pos_x), float(pos_z))
                    except Exception:
                        self._prev_position = None
                    self._prev_action = action_tok.strip().upper() if isinstance(action_tok, str) else None

                if self._unchanged_count >= self.stagnant_limit:
                    # Treat this as stagnation and stop Unity as if ANSWER was returned.
                    reason = (
                        f"Location and action unchanged for {self._unchanged_count} consecutive requests "
                        f"(movement < {self.location_epsilon})."
                    )
                    logging.error("STAGNATION DETECTED: %s", reason)
                    # Write the result file only when debug artifacts are enabled.
                    if self.save_debug_artifacts:
                        try:
                            failure = {
                                "success": False,
                                "error": "stagnant_location_and_action",
                                "message": reason,
                                "last_position": {"x": pos_x, "z": pos_z},
                                "last_action": self._prev_action,
                                "unchanged_count": self._unchanged_count,
                                "threshold": self.location_epsilon,
                                "timestamp": datetime.now().isoformat(timespec="seconds"),
                            }
                            out_path = os.path.join(self.run_dir, "failure.json")
                            with open(out_path, "w", encoding="utf-8") as f:
                                json.dump(failure, f, ensure_ascii=False, indent=2)
                        except Exception:
                            pass

                    # Request shutdown and return ANSWER so Unity stops.
                    self.shutdown_requested = True
                    # Append to the aggregate CSV.
                    self._stop_reason_detail = reason
                    self._append_summary(
                        status="STAGNATION",
                        validation=result.get("validation"),
                        stop_reason_detail=reason,
                    )
                    return {
                        "success": True,
                        "action": "ANSWER",
                        "thought": "Terminating due to detected stagnation.",
                        "reflection": reason,
                    }

            # --- MapNavigation: stop after moving away from the goal consecutively ---
            try:
                task = getattr(self.generator, "current_task", None)
                task_type_name = getattr(getattr(task, "task_type", None), "name", "")
                if has_position and task and task_type_name == "MapNavigation":
                    expected = getattr(task, "answer", "") or ""
                    if expected and isinstance(expected, str) and ("x:" in expected and "y:" in expected):
                        try:
                            parts = expected.split("x:")[1].split("y:")
                            expected_x = float(parts[0].strip())
                            expected_y = float(parts[1].strip())
                            dist = math.hypot(float(pos_x) - expected_x, float(pos_z) - expected_y)

                            moved_away = False
                            if isinstance(self._prev_distance_to_goal, (int, float)):
                                eps = max(self.location_epsilon, 0.01)
                                moved_away = dist > (self._prev_distance_to_goal + eps)

                            if moved_away:
                                self._away_from_goal_count += 1
                            else:
                                self._away_from_goal_count = 0

                            self._prev_distance_to_goal = dist

                            if self._away_from_goal_count >= self.away_from_goal_consecutive_limit:
                                reason = (
                                    f"Moved away from goal for {self._away_from_goal_count} consecutive steps. "
                                    f"latest_distance={dist:.2f}"
                                )
                                logging.error("AWAY FROM GOAL: %s", reason)
                                self.shutdown_requested = True
                                self._stop_reason_detail = reason
                                self._append_summary(
                                    status="AWAY_FROM_GOAL",
                                    validation=result.get("validation"),
                                    stop_reason_detail=reason,
                                )
                                return {
                                    "success": True,
                                    "action": "ANSWER",
                                    "thought": "Terminating due to moving away from goal consecutively.",
                                    "reflection": reason,
                                }
                        except SummaryWriteError:
                            raise
                        except Exception:
                            # Skip this condition if parsing fails.
                            self._prev_distance_to_goal = None
                            self._away_from_goal_count = 0
            except SummaryWriteError:
                raise
            except Exception:
                # Ignore unexpected failures and continue.
                pass

            # --- Post-processing when ANSWER is detected: logs and summary ---
            if isinstance(action_tok, str) and action_tok.strip().upper() == "ANSWER":
                self.shutdown_requested = True
                logging.info("ANSWER detected. Scheduling server shutdown after response.")

                # Record validation results in logs and result files.
                validation = result.get("validation")
                if validation:
                    is_correct = validation.get("is_correct", False)
                    message = validation.get("message", "")
                    user_answer = validation.get("user_answer", "")
                    expected = validation.get("expected", "")

                    # Log validation details.
                    logging.info("=" * 60)
                    logging.info("ANSWER VALIDATION RESULT")
                    logging.info("=" * 60)
                    logging.info("User Answer: %s", user_answer)
                    logging.info("Expected Answer: %s", expected)
                    logging.info("Result: %s", "CORRECT" if is_correct else "INCORRECT")
                    logging.info("Message: %s", message)
                    logging.info("=" * 60)

                # Basic step and elapsed-time logs when ANSWER is detected.
                try:
                    _end = datetime.now()
                    _start = self.task_started_at or _end
                    _elapsed = (_end - _start).total_seconds()
                    logging.info("Task Steps: %d", self.step_count)
                    logging.info(
                        "Task Time: %.2f sec (start=%s end=%s)",
                        _elapsed,
                        _start.isoformat(timespec="seconds"),
                        _end.isoformat(timespec="seconds"),
                    )
                except Exception:
                    pass

                # Append the final ANSWER result to the aggregate CSV.
                self._append_summary(status="ANSWER", validation=validation)

        except SummaryWriteError:
            raise
        except Exception:
            # On exceptions, return the original result unchanged.
            return result

        return result

    # --- Network I/O Utilities ---
    def _send_json_line(self, conn: socket.socket, obj: dict) -> None:
        try:
            data = (json.dumps(obj, ensure_ascii=False) + "\n").encode("utf-8")
            conn.sendall(data)
        except Exception as e:
            logging.warning("send_failed: %s", e)

    def _iter_lines(self, conn: socket.socket):
        buffer = b""
        while not self.shutdown_requested:
            try:
                chunk = conn.recv(4096)
            except socket.timeout:
                continue
            except OSError:
                break
            if not chunk:
                break
            buffer += chunk
            while b"\n" in buffer:
                line, buffer = buffer.split(b"\n", 1)
                yield line

    # --- Request Handling ---
    def _handle_request(self, req: dict, addr: Tuple[str, int]) -> dict:
        rtype = req.get("type")
        if rtype == "ping":
            self._record_client_contact(is_act=False)
            logging.debug("ping from %s", addr)
            return {"ok": True}

        if rtype == "get_start_index":
            self._record_client_contact(is_act=False)
            logging.info("get_start_index request from %s", addr)
            try:
                start_index  = self.generator.current_task.start_index
                if start_index is None:
                    raise Exception("start_index is not set")
                logging.info("resolved start_index from TASKS: %d", start_index)
            except Exception:
                logging.error("failed to get start_index from TASKS")
                raise Exception("failed to get start_index from TASKS")
            return {"success": True, "start_index": start_index}

        if rtype != "act":
            self._record_client_contact(is_act=False)
            logging.warning("unknown_type from %s: %s", addr, rtype)
            return {"success": False, "error": "unknown_type"}

        self._record_client_contact(is_act=True)
        self.act_request_count += 1
        request_index = self.act_request_count

        image_b64 = req.get("image_b64", "")
        if not image_b64:
            self.request_error_count += 1
            self._last_error_type = "missing_image_b64"
            self._last_error_message = "ACT request did not include image_b64"
            logging.warning("missing_image_b64 from %s", addr)
            return {"success": False, "error": "missing_image_b64"}

        # Record the task start time on the first act request.
        if self.task_started_at is None:
            self.task_started_at = datetime.now()
            logging.info(
                "task_started run_id=%s task_id=%s at=%s",
                self.run_id,
                self.task_id,
                self.task_started_at.isoformat(timespec="seconds"),
            )

        # Extract coordinates and log them.
        pos_x = req.get("position_x", 0.0)
        pos_z = req.get("position_z", 0.0)
        segment_path = req.get("segment_path", "")
        pos_x_float = _optional_float(pos_x)
        pos_z_float = _optional_float(pos_z)
        if pos_x_float is not None and pos_z_float is not None:
            self._latest_position = (pos_x_float, pos_z_float)
        self._latest_segment_path = str(segment_path or "")
        log_x = pos_x_float if pos_x_float is not None else 0.0
        log_z = pos_z_float if pos_z_float is not None else 0.0
        logging.debug(
            "observation run_id=%s task_id=%s act_request=%d pos=(%.2f, %.2f) path=%s from=%s",
            self.run_id,
            self.task_id,
            request_index,
            log_x,
            log_z,
            segment_path,
            addr,
        )

        # Evaluate position changes after action generation so the action can be included.

        # Optional map images, either single or multiple.
        map_images_b64 = req.get("map_images_b64")
        if isinstance(map_images_b64, list) and map_images_b64:
            map_payload: Optional[object] = map_images_b64
        else:
            map_payload = req.get("map_image_b64", "")

        logging.debug(
            "llm_request_started run_id=%s task_id=%s act_request=%d",
            self.run_id,
            self.task_id,
            request_index,
        )
        result = self.generator.generate_key_action_from_base64(image_b64, map_payload, log_x, log_z)
        self._record_llm_metrics()
        self._record_result_error(result)
        if isinstance(result, dict) and result.get("success"):
            self.step_count += 1
        action_tok = result.get("action")
        self._latest_result_action = str(action_tok or "")
        logging.debug(
            "llm_request_finished run_id=%s task_id=%s act_request=%d success=%s action=%s latency=%.3f error_type=%s",
            self.run_id,
            self.task_id,
            request_index,
            result.get("success"),
            action_tok,
            getattr(self.generator, "last_latency_seconds", None) or -1.0,
            self._last_error_type,
        )
        # Centralized early stopping.
        model_result = dict(result)
        result = self.early_stopping(result, req, addr)
        self._log_progress(action_tok)
        self._append_step_trace(request_index, req, model_result, result)

        return result

    def _handle_client(self, conn: socket.socket, addr: Tuple[str, int]) -> None:
        try:
            with conn:
                try:
                    conn.settimeout(1.0)
                except OSError:
                    pass
                for raw_line in self._iter_lines(conn):
                    try:
                        line_str = raw_line.decode("utf-8", errors="ignore").strip()
                    except Exception:
                        line_str = ""
                    if not line_str:
                        continue
                    try:
                        req = json.loads(line_str)
                    except Exception:
                        logging.warning("invalid_json from %s", addr)
                        self.request_error_count += 1
                        self._last_error_type = "invalid_json_request"
                        self._last_error_message = "Client sent invalid JSON"
                        self._send_json_line(conn, {"success": False, "error": "invalid_json"})
                        continue

                    resp = self._handle_request(req, addr)
                    self._send_json_line(conn, resp)
                    if self.shutdown_requested:
                        return
        except SummaryWriteError as e:
            self.fatal_error = e
            self.shutdown_requested = True
            logging.exception("fatal_summary_write_error from %s: %s", addr, e)
        except Exception as e:
            logging.exception("client_handler_error from %s: %s", addr, e)

    def _record_client_contact(self, is_act: bool) -> None:
        now = time.monotonic()
        self._last_client_contact = now
        if is_act:
            self._last_act_time = now
            if not self._first_act_received:
                self._first_act_received = True

    def _emit_timeout_event(self, status: str, reason: str, event: str) -> None:
        if self._timeout_event_emitted:
            return
        payload = {
            "event": event,
            "status": status,
            "message": reason,
            "host": self.host,
            "port": self.port,
        }
        try:
            print(json.dumps(payload))
        except Exception:
            pass
        self._timeout_event_emitted = True

    def _check_timeouts(self) -> None:
        if self.shutdown_requested:
            return
        now = time.monotonic()
        if self.startup_activity_timeout and not self._first_act_received:
            elapsed = now - self._server_start_monotonic
            if elapsed >= self.startup_activity_timeout:
                reason = (
                    f"No Unity ACT traffic within {self.startup_activity_timeout:.1f}s "
                    "of server start. Check if PythonAIInputClient is running."
                )
                logging.error("UNITY_ACTIVITY_TIMEOUT: %s", reason)
                self.shutdown_requested = True
                self._stop_reason_detail = reason
                self._append_summary(status="NO_ACTIVITY", stop_reason_detail=reason)
                self._emit_timeout_event("NO_ACTIVITY", reason, "server_timeout")
                return

        if self.idle_activity_timeout and self._first_act_received:
            baseline = self._last_act_time or self._last_client_contact
            if baseline and (now - baseline) >= self.idle_activity_timeout:
                reason = (
                    f"No Unity activity for {self.idle_activity_timeout:.1f}s after start. "
                    "Unity may be stuck or paused."
                )
                logging.error("UNITY_IDLE_TIMEOUT: %s", reason)
                self.shutdown_requested = True
                self._stop_reason_detail = reason
                self._append_summary(status="IDLE_TIMEOUT", stop_reason_detail=reason)
                self._emit_timeout_event("IDLE_TIMEOUT", reason, "server_timeout")

    # --- Main Loop ---
    def serve_forever(self) -> None:
        srv = socket.socket(socket.AF_INET, socket.SOCK_STREAM)
        srv.setsockopt(socket.SOL_SOCKET, socket.SO_REUSEADDR, 1)
        srv.bind((self.host, self.port))
        srv.listen(1)
        # Use a timeout so accept can be interrupted periodically.
        srv.settimeout(1.0)

        payload = {"event": "server_started", "host": self.host, "port": self.port, "run_id": self.run_id}
        if self.save_debug_artifacts and self.run_dir:
            try:
                payload["artifact_dir"] = os.path.relpath(self.run_dir, self.output_run_dir)
            except ValueError:
                pass
        print(json.dumps(payload))
        logging.info("server_started host=%s port=%s", self.host, self.port)

        try:
            while not self.shutdown_requested:
                self._check_timeouts()
                if self.shutdown_requested:
                    break
                try:
                    conn, addr = srv.accept()
                except socket.timeout:
                    continue
                except OSError as exc:
                    if self.shutdown_requested:
                        break
                    logging.warning("accept_failed: %s", exc)
                    continue
                logging.info("client_connected %s", addr)
                self._handle_client(conn, addr)
                logging.info("client_disconnected %s", addr)
        finally:
            logging.info("server_stopping")
            if self._log_handler is not None:
                root_logger = logging.getLogger()
                try:
                    root_logger.removeHandler(self._log_handler)
                    self._log_handler.close()
                except Exception:
                    pass
                self._log_handler = None
            try:
                srv.close()
            except Exception:
                pass


def serve(
    host: str,
    port: int,
    api_key: str | None,
    model: str,
    provider: str,
    pretrained: str | None,
    max_tokens: int,
    temperature: float | None,
    validation_model: str | None,
    validation_provider: str | None,
    validation_pretrained: str | None,
    log_file: str,
    task_id: int | None = None,
    api_base: str | None = None,
    extra_headers: str | None = None,
    organization: str | None = None,
    experiment_id: str | int | None = None,
    startup_activity_timeout: float | None = None,
    idle_activity_timeout: float | None = None,
    output_root: str | None = None,
    run_id: str | None = None,
    summary_path: str | None = None,
    save_debug_artifacts: bool = False,
    progress_log_interval: int = 10,
):
    server = AgentServer(
        host=host,
        port=port,
        api_key=api_key,
        model=model,
        provider=provider,
        pretrained=pretrained,
        max_tokens=max_tokens,
        temperature=temperature,
        validation_model=validation_model,
        validation_provider=validation_provider,
        validation_pretrained=validation_pretrained,
        log_file=log_file,
        task_id=task_id,
        api_base=api_base,
        extra_headers=extra_headers,
        organization=organization,
        experiment_id=experiment_id,
        startup_activity_timeout=startup_activity_timeout,
        idle_activity_timeout=idle_activity_timeout,
        output_root=output_root,
        run_id=run_id,
        summary_path=summary_path,
        save_debug_artifacts=save_debug_artifacts,
        progress_log_interval=progress_log_interval,
    )
    server.serve_forever()


def main():
    p = argparse.ArgumentParser()
    p.add_argument("--host", default="127.0.0.1")
    p.add_argument("--port", type=int, default=8765)
    p.add_argument("--api-key", default=os.getenv("OPENAI_API_KEY"))
    p.add_argument(
        "--model",
        default=os.getenv("MODEL_NAME", os.getenv("LLM_MODEL")),
    )
    p.add_argument(
        "--provider",
        default=os.getenv("PROVIDER", os.getenv("LLM_PROVIDER")),
    )
    p.add_argument("--pretrained", default=os.getenv("LLM_PRETRAINED"))
    p.add_argument("--max-tokens", type=int, default=int(os.getenv("LLM_MAX_TOKENS", "8192")))
    p.add_argument(
        "--temperature",
        type=float,
        default=float(os.getenv("LLM_TEMPERATURE")) if os.getenv("LLM_TEMPERATURE") else None,
    )
    p.add_argument("--validation-model", default=os.getenv("LLM_VALIDATION_MODEL"))
    p.add_argument("--validation-provider", default=os.getenv("LLM_VALIDATION_PROVIDER"))
    p.add_argument("--validation-pretrained", default=os.getenv("LLM_VALIDATION_PRETRAINED"))
    p.add_argument("--task-id", type=int, default=None)
    p.add_argument("--log-file", default="agent_server.log")
    p.add_argument("--api-base", default=os.getenv("LLM_API_BASE"))
    p.add_argument("--extra-headers", help="JSON string for additional HTTP headers", default=os.getenv("LLM_EXTRA_HEADERS"))
    p.add_argument("--organization", default=os.getenv("OPENAI_ORG"))
    p.add_argument("--experiment-id", help="Experiment ID to aggregate multiple runs under the same ID.")
    p.add_argument("--output-root", default=os.getenv("OUTPUT_ROOT", str(DEFAULT_OUTPUT_ROOT)))
    p.add_argument("--run-id", default=os.getenv("RUN_ID"))
    p.add_argument(
        "--summary-path",
        default=None,
        help="CSV summary path. Relative paths are resolved under outputs/<run_id>.",
    )
    p.add_argument(
        "--save-debug-artifacts",
        action="store_true",
        help="Persist debug artifacts such as images, context.jsonl, memo.txt, and agent logs.",
    )
    p.add_argument(
        "--progress-log-interval",
        type=int,
        default=int(os.getenv("PROGRESS_LOG_INTERVAL", "10")),
        help="Write one INFO progress log every N ACT requests (0 disables).",
    )
    p.add_argument(
        "--startup-activity-timeout",
        type=float,
        default=0.0,
        help="Seconds to wait for the first Unity ACT request before timing out (0 disables).",
    )
    p.add_argument(
        "--idle-activity-timeout",
        type=float,
        default=0.0,
        help="Abort if Unity stops sending ACT requests for this many seconds after start (0 disables).",
    )
    args = p.parse_args()
    if not args.model:
        p.error("--model or MODEL_NAME/LLM_MODEL is required")
    if not args.provider:
        p.error("--provider or PROVIDER/LLM_PROVIDER is required")
    serve(
        args.host,
        args.port,
        args.api_key,
        args.model,
        args.provider,
        args.pretrained,
        args.max_tokens,
        args.temperature,
        args.validation_model,
        args.validation_provider,
        args.validation_pretrained,
        args.log_file,
        args.task_id,
        args.api_base,
        args.extra_headers,
        args.organization,
        args.experiment_id,
        startup_activity_timeout=args.startup_activity_timeout,
        idle_activity_timeout=args.idle_activity_timeout,
        output_root=args.output_root,
        run_id=args.run_id,
        summary_path=args.summary_path,
        save_debug_artifacts=args.save_debug_artifacts,
        progress_log_interval=args.progress_log_interval,
    )


if __name__ == "__main__":
    main()
