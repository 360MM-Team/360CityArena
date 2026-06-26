"""Utility module providing LLM client abstractions for multiple providers."""

from __future__ import annotations

import base64
import binascii
import io
import json
import logging
import os
from typing import Any, Dict, List, Optional
from urllib.parse import parse_qsl, urlencode, urlparse, urlunparse

import httpx
import requests

try:  # Optional dependency for OpenAI
    from openai import OpenAI
except Exception:  # pragma: no cover - optional import guard
    OpenAI = None

try:  # Optional dependency for Anthropic Claude
    import anthropic
except Exception:  # pragma: no cover - optional import guard
    anthropic = None

try:  # Optional dependency for Google Gemini (new SDK)
    from google import genai
    from google.genai import types as genai_types
except Exception:  # pragma: no cover - optional import guard
    genai = None
    genai_types = None

try:  # Optional dependency for image resizing (Claude uploads)
    from PIL import Image, ImageOps
except Exception:  # pragma: no cover - optional import guard
    Image = None
    ImageOps = None


class LLMClientError(RuntimeError):
    """Raised when LLM client interaction fails."""


def _extract_base64_from_data_url(data_url: str) -> tuple[str, str]:
    """Parse a data URL into its mime-type and base64 payload."""

    if not isinstance(data_url, str):
        raise ValueError("image payload must be a string data URL")

    if not data_url.startswith("data:"):
        # Treat as remote URL – some providers may accept this as-is.
        return "", data_url

    header, _, b64_data = data_url.partition(",")
    if not b64_data:
        raise ValueError("invalid data URL: missing base64 payload")
    mime = "image/png"
    if ";" in header:
        mime = header.split(";", 1)[0].split(":", 1)[-1] or "image/png"
    elif header.startswith("data:") and len(header) > 5:
        mime = header[5:]
    return mime, b64_data


def _append_query_params(url: str, params: Dict[str, str]) -> str:
    parsed = urlparse(url)
    query = dict(parse_qsl(parsed.query, keep_blank_values=True))
    for key, value in params.items():
        if value and key not in query:
            query[key] = value
    return urlunparse(parsed._replace(query=urlencode(query)))


def _has_query_param(url: str, param: str) -> bool:
    parsed = urlparse(url)
    query = dict(parse_qsl(parsed.query, keep_blank_values=True))
    return param in query


class BaseLLMClient:
    def __init__(self, model: str):
        self.model = model
        self.last_response_metadata: Dict[str, Any] = {}

    def generate_response(
        self,
        system_prompt: str,
        context_messages: List[Dict[str, Any]],
        user_content: List[Dict[str, Any]],
        context_text: str,
        timeout: int,
        max_tokens: int,
        temperature: Optional[float] = None,
    ) -> str:
        raise NotImplementedError


class OpenAIClient(BaseLLMClient):
    def __init__(
        self,
        model: str,
        api_key: Optional[str] = None,
    ):
        if OpenAI is None:
            raise LLMClientError("openai package is not available; install openai>=1.0")
        super().__init__(model)

        client_kwargs: Dict[str, Any] = {}
        if api_key:
            client_kwargs["api_key"] = api_key

        try:
            self._client = OpenAI(**client_kwargs)
        except Exception as exc:  # pragma: no cover - network call
            raise LLMClientError(f"failed to init OpenAI client: {exc}") from exc

    def generate_response(
        self,
        system_prompt: str,
        context_messages: List[Dict[str, Any]],
        user_content: List[Dict[str, Any]],
        context_text: str,
        timeout: int,
        max_tokens: int,
        temperature: Optional[float] = None,
    ) -> str:
        messages: List[Dict[str, Any]] = []
        if system_prompt:
            messages.append({"role": "system", "content": system_prompt})
        messages.extend(context_messages)
        messages.append({"role": "user", "content": user_content})

        try:
            request_kwargs: Dict[str, Any] = {
                "model": self.model,
                "messages": messages,
                "max_completion_tokens": max_tokens,
                "timeout": timeout,
            }
            if temperature is not None:
                request_kwargs["temperature"] = temperature
            response = self._client.chat.completions.create(**request_kwargs)
        except Exception as exc:  # pragma: no cover - network call
            self.last_response_metadata = {}
            raise LLMClientError(f"OpenAI.chat.completions failed: {exc}") from exc

        try:
            choice = response.choices[0]
            usage = getattr(response, "usage", None)
            self.last_response_metadata = {
                "provider": "openai",
                "response_id": getattr(response, "id", ""),
                "response_model": getattr(response, "model", self.model),
                "finish_reason": getattr(choice, "finish_reason", ""),
                "prompt_tokens": getattr(usage, "prompt_tokens", None) if usage else None,
                "completion_tokens": getattr(usage, "completion_tokens", None) if usage else None,
                "total_tokens": getattr(usage, "total_tokens", None) if usage else None,
            }
            return response.choices[0].message.content or ""
        except (AttributeError, IndexError):
            self.last_response_metadata = {}
            raise LLMClientError("OpenAI response missing content")


class OpenAICompatibleClient(BaseLLMClient):
    def __init__(
        self,
        model: str,
        api_base: str,
        api_key: Optional[str] = None,
        extra_headers: Optional[Dict[str, str]] = None,
        pretrained: Optional[str] = None,
    ):
        super().__init__(model)
        if not api_base:
            raise LLMClientError("api_base is required for OpenAI-compatible client")
        if not pretrained:
            raise LLMClientError("pretrained is required for OpenAI-compatible client")

        self.model = model
        self.pretrained = pretrained

        self.api_base = api_base.rstrip("/")
        self.api_key = api_key
        self.extra_headers = extra_headers or {}

    def generate_response(
        self,
        system_prompt: str,
        context_messages: List[Dict[str, Any]],
        user_content: List[Dict[str, Any]],
        context_text: str,
        timeout: int,
        max_tokens: int,
        temperature: Optional[float] = None,
    ) -> str:
        messages: List[Dict[str, Any]] = []
        if system_prompt:
            messages.append({"role": "system", "content": system_prompt})
        messages.extend(context_messages)
        messages.append({"role": "user", "content": user_content})

        payload = {
            "model": self.pretrained,
            "messages": messages,
            "max_tokens": max_tokens,
        }
        if temperature is not None:
            payload["temperature"] = temperature

        headers = {"Content-Type": "application/json"}
        if self.api_key:
            headers["Authorization"] = f"Bearer {self.api_key}"
        headers.update(self.extra_headers)

        try:
            response = requests.post(
                f"{self.api_base}/chat/completions",
                json=payload,
                headers=headers,
                timeout=timeout,
            )
            response.raise_for_status()
            data = response.json()
        except Exception as exc:  # pragma: no cover - network call
            self.last_response_metadata = {}
            raise LLMClientError(f"OpenAI-compatible request failed: {exc}") from exc

        try:
            choice = data["choices"][0]
            usage = data.get("usage") or {}
            self.last_response_metadata = {
                "provider": "openai-compatible",
                "response_id": data.get("id", ""),
                "response_model": data.get("model", self.pretrained),
                "finish_reason": choice.get("finish_reason", ""),
                "prompt_tokens": usage.get("prompt_tokens"),
                "completion_tokens": usage.get("completion_tokens"),
                "total_tokens": usage.get("total_tokens"),
            }
            return data["choices"][0]["message"]["content"] or ""
        except (KeyError, IndexError, TypeError) as exc:
            self.last_response_metadata = {}
            raise LLMClientError(
                f"Unexpected OpenAI-compatible response: {exc}"
            ) from exc


class AzureOpenAIClient(BaseLLMClient):
    def __init__(
        self,
        model: str,
        endpoint: Optional[str] = None,
        deployment: Optional[str] = None,
        api_key: Optional[str] = None,
        api_version: Optional[str] = None,
        extra_headers: Optional[Dict[str, str]] = None,
        api_base: Optional[str] = None,
    ):
        super().__init__(model)
        base = (api_base or endpoint or "").rstrip("/")
        if not base:
            raise LLMClientError("Azure OpenAI endpoint is required")

        request_url = base
        if "/chat/completions" not in base:
            if "/openai/deployments/" in base:
                request_url = f"{base}/chat/completions"
            else:
                if not deployment:
                    raise LLMClientError("Azure OpenAI deployment name is required")
                request_url = f"{base}/openai/deployments/{deployment}/chat/completions"

        if not _has_query_param(request_url, "api-version"):
            if not api_version:
                raise LLMClientError("Azure OpenAI api-version is required")
            request_url = _append_query_params(
                request_url, {"api-version": api_version}
            )

        self.api_key = api_key
        self.extra_headers = extra_headers or {}
        self.request_url = request_url

    def generate_response(
        self,
        system_prompt: str,
        context_messages: List[Dict[str, Any]],
        user_content: List[Dict[str, Any]],
        context_text: str,
        timeout: int,
        max_tokens: int,
        temperature: Optional[float] = None,
    ) -> str:
        messages: List[Dict[str, Any]] = []
        if system_prompt:
            messages.append({"role": "system", "content": system_prompt})
        messages.extend(context_messages)
        messages.append({"role": "user", "content": user_content})

        payload = {
            "messages": messages,
            "max_completion_tokens": max_tokens,
        }
        if temperature is not None:
            payload["temperature"] = temperature

        headers = {"Content-Type": "application/json"}
        if (
            self.api_key
            and "api-key" not in self.extra_headers
            and "Authorization" not in self.extra_headers
        ):
            headers["api-key"] = self.api_key
        headers.update(self.extra_headers)

        try:
            response = requests.post(
                self.request_url,
                json=payload,
                headers=headers,
                timeout=timeout,
            )
            response.raise_for_status()
            data = response.json()
        except Exception as exc:  # pragma: no cover - network call
            self.last_response_metadata = {}
            raise LLMClientError(f"Azure OpenAI request failed: {exc}") from exc

        try:
            choice = data["choices"][0]
            usage = data.get("usage") or {}
            self.last_response_metadata = {
                "provider": "azure-openai",
                "response_id": data.get("id", ""),
                "response_model": data.get("model", self.model),
                "finish_reason": choice.get("finish_reason", ""),
                "prompt_tokens": usage.get("prompt_tokens"),
                "completion_tokens": usage.get("completion_tokens"),
                "total_tokens": usage.get("total_tokens"),
            }
            return data["choices"][0]["message"]["content"] or ""
        except (KeyError, IndexError, TypeError) as exc:
            self.last_response_metadata = {}
            raise LLMClientError(f"Unexpected Azure OpenAI response: {exc}") from exc


_ANTHROPIC_MAX_IMAGE_BASE64_BYTES = 5 * 1024 * 1024


class AnthropicClient(BaseLLMClient):
    def __init__(self, model: str, api_key: Optional[str] = None):
        if anthropic is None:
            raise LLMClientError(
                "anthropic package is not available; install anthropic>=0.33 to use Claude"
            )
        super().__init__(model)
        resolved_key = api_key or os.getenv("ANTHROPIC_API_KEY")
        if not resolved_key:
            raise LLMClientError("Anthropic API key is required for Claude models")
        self._http_client: Optional[httpx.Client] = None
        try:
            self._http_client = httpx.Client(follow_redirects=True)
            self._client = anthropic.Anthropic(
                api_key=resolved_key, http_client=self._http_client
            )
        except Exception as exc:  # pragma: no cover - network call
            if self._http_client is not None:
                self._http_client.close()
            raise LLMClientError(f"failed to init Anthropic client: {exc}") from exc

    def _convert_content_blocks(
        self, user_content: List[Dict[str, Any]]
    ) -> List[Dict[str, Any]]:
        blocks: List[Dict[str, Any]] = []
        for item in user_content:
            if item.get("type") == "text":
                blocks.append({"type": "text", "text": item.get("text", "")})
            elif item.get("type") == "image_url":
                image_url = item.get("image_url", {}).get("url", "")
                if not image_url:
                    continue
                mime, data = _extract_base64_from_data_url(image_url)
                if not mime:
                    # Claude currently expects inline base64 images.
                    continue
                try:
                    mime, data = self._ensure_image_within_limits(mime, data)
                except LLMClientError:
                    raise
                except Exception as exc:
                    logging.warning(
                        "failed to process image for Claude: %s", exc, exc_info=True
                    )
                    raise LLMClientError("failed to process image for Claude") from exc
                blocks.append(
                    {
                        "type": "image",
                        "source": {
                            "type": "base64",
                            "media_type": mime or "image/png",
                            "data": data,
                        },
                    }
                )
        return blocks

    def _ensure_image_within_limits(
        self, mime: str, base64_data: str
    ) -> tuple[str, str]:
        if len(base64_data) <= _ANTHROPIC_MAX_IMAGE_BASE64_BYTES:
            return mime, base64_data
        if Image is None:
            raise LLMClientError(
                "Claude image payload exceeds 5 MB and Pillow is not available to resize it"
            )
        try:
            image_bytes = base64.b64decode(base64_data, validate=True)
        except (binascii.Error, ValueError) as exc:
            raise LLMClientError("invalid base64 image payload for Claude") from exc

        try:
            image = Image.open(io.BytesIO(image_bytes))
        except Exception as exc:
            raise LLMClientError("failed to decode base64 image for Claude") from exc

        try:
            if ImageOps is not None:
                image = ImageOps.exif_transpose(image)
        except Exception:
            # Orientation fixes are best-effort; continue even on failure.
            pass

        if image.mode not in ("RGB", "L"):
            image = image.convert("RGB")
        elif image.mode == "L":
            image = image.convert("RGB")

        resized = image
        quality = 90
        for _ in range(12):
            buffer = io.BytesIO()
            try:
                resized.save(buffer, format="JPEG", quality=quality, optimize=True)
            except OSError:
                resized = resized.convert("RGB")
                buffer = io.BytesIO()
                resized.save(buffer, format="JPEG", quality=quality, optimize=True)
            encoded = base64.b64encode(buffer.getvalue()).decode("utf-8")
            if len(encoded) <= _ANTHROPIC_MAX_IMAGE_BASE64_BYTES:
                return "image/jpeg", encoded
            if quality > 55:
                quality = max(40, quality - 10)
                continue
            new_width = max(1, int(resized.width * 0.85))
            new_height = max(1, int(resized.height * 0.85))
            if new_width < 64 or new_height < 64:
                break
            resized = resized.resize((new_width, new_height), Image.LANCZOS)

        raise LLMClientError(
            "Claude image payload exceeds 5 MB even after automatic resizing; "
            "reduce the source image resolution"
        )

    def generate_response(
        self,
        system_prompt: str,
        context_messages: List[Dict[str, Any]],
        user_content: List[Dict[str, Any]],
        context_text: str,
        timeout: int,
        max_tokens: int,
        temperature: Optional[float] = None,
    ) -> str:
        blocks: List[Dict[str, Any]] = []
        if context_text:
            blocks.append({"type": "text", "text": context_text})
        blocks.extend(self._convert_content_blocks(user_content))
        if not blocks:
            blocks.append({"type": "text", "text": ""})

        try:
            request_kwargs: Dict[str, Any] = {
                "model": self.model,
                "system": system_prompt or None,
                "messages": [{"role": "user", "content": blocks}],
                "max_tokens": max_tokens,
                "timeout": timeout,
            }
            if temperature is not None:
                request_kwargs["temperature"] = temperature
            response = self._client.messages.create(**request_kwargs)
        except Exception as exc:  # pragma: no cover - network call
            self.last_response_metadata = {}
            raise LLMClientError(f"Anthropic Claude request failed: {exc}") from exc

        usage = getattr(response, "usage", None)
        self.last_response_metadata = {
            "provider": "anthropic",
            "response_id": getattr(response, "id", ""),
            "response_model": getattr(response, "model", self.model),
            "finish_reason": getattr(response, "stop_reason", ""),
            "prompt_tokens": getattr(usage, "input_tokens", None) if usage else None,
            "completion_tokens": getattr(usage, "output_tokens", None) if usage else None,
            "total_tokens": (
                getattr(usage, "input_tokens", 0) + getattr(usage, "output_tokens", 0)
                if usage
                and getattr(usage, "input_tokens", None) is not None
                and getattr(usage, "output_tokens", None) is not None
                else None
            ),
        }
        texts: List[str] = []
        for block in getattr(response, "content", []) or []:
            if getattr(block, "type", "") == "text":
                texts.append(getattr(block, "text", ""))
        if not texts:
            self.last_response_metadata = {}
            raise LLMClientError("Claude response did not contain text content")
        return "\n".join(texts)


class GeminiClient(BaseLLMClient):
    def __init__(self, model: str, api_key: Optional[str] = None):
        if genai is None or genai_types is None:
            raise LLMClientError(
                "google-genai package is not available; install google-genai"
            )
        super().__init__(model)
        resolved_key = (
            api_key
            or os.getenv("GOOGLE_API_KEY")
            or os.getenv("GEMINI_API_KEY")
            or os.getenv("GOOGLE_GENAI_API_KEY")
        )
        use_vertex = os.getenv("GOOGLE_GENAI_USE_VERTEXAI", "").strip().lower() in {
            "1",
            "true",
            "yes",
        }
        if not resolved_key and not use_vertex:
            raise LLMClientError(
                "Google API key is required for Gemini models (or set GOOGLE_GENAI_USE_VERTEXAI for Vertex AI)"
            )
        try:
            if resolved_key:
                self._client = genai.Client(api_key=resolved_key)
            else:
                self._client = genai.Client()
        except Exception as exc:  # pragma: no cover - network call
            raise LLMClientError(
                f"failed to configure google.genai client: {exc}"
            ) from exc

    def _convert_user_parts(
        self, user_content: List[Dict[str, Any]], context_text: str
    ) -> List[Any]:
        parts: List[Any] = []
        if context_text:
            parts.append(genai_types.Part.from_text(text=context_text))
        for item in user_content:
            if item.get("type") == "text":
                parts.append(genai_types.Part.from_text(text=item.get("text", "")))
            elif item.get("type") == "image_url":
                image_url = item.get("image_url", {}).get("url", "")
                if not image_url:
                    continue
                if image_url.startswith("data:"):
                    mime, data = _extract_base64_from_data_url(image_url)
                    if not data:
                        continue
                    try:
                        image_bytes = base64.b64decode(data)
                    except (binascii.Error, ValueError):
                        continue
                    parts.append(
                        genai_types.Part.from_bytes(
                            data=image_bytes, mime_type=mime or "image/png"
                        )
                    )
                else:
                    parts.append(
                        genai_types.Part.from_uri(
                            file_uri=image_url, mime_type="image/png"
                        )
                    )
        return parts

    def generate_response(
        self,
        system_prompt: str,
        context_messages: List[Dict[str, Any]],
        user_content: List[Dict[str, Any]],
        context_text: str,
        timeout: int,
        max_tokens: int,
        temperature: Optional[float] = None,
    ) -> str:
        contents: List[Dict[str, Any]] = []
        for message in context_messages:
            content = message.get("content", "")
            if isinstance(content, list):
                try:
                    content = json.dumps(content, ensure_ascii=False)
                except Exception:
                    content = str(content)
            if not isinstance(content, str):
                content = str(content)
            role = message.get("role", "assistant")
            parts = [genai_types.Part.from_text(text=content)]
            contents.append(
                genai_types.Content(
                    role="user" if role == "user" else "model",
                    parts=parts,
                )
            )

        user_parts = self._convert_user_parts(user_content, context_text)
        if not user_parts:
            user_parts = [genai_types.Part.from_text(text="")]

        contents.append(genai_types.Content(role="user", parts=user_parts))

        generation_config_kwargs: Dict[str, Any] = {
            "system_instruction": system_prompt or None,
            "max_output_tokens": max_tokens,
        }
        if temperature is not None:
            generation_config_kwargs["temperature"] = temperature
        generation_config = genai_types.GenerateContentConfig(**generation_config_kwargs)

        try:
            response = self._client.models.generate_content(
                model=self.model,
                contents=contents,
                config=generation_config,
            )
        except Exception as exc:  # pragma: no cover - network call
            self.last_response_metadata = {}
            raise LLMClientError(f"Gemini request failed: {exc}") from exc

        usage = getattr(response, "usage_metadata", None)
        finish_reason = ""
        try:
            finish_reason = str(getattr(response.candidates[0], "finish_reason", "") or "")
        except (AttributeError, IndexError, TypeError):
            finish_reason = ""
        self.last_response_metadata = {
            "provider": "gemini",
            "response_id": "",
            "response_model": self.model,
            "finish_reason": finish_reason,
            "prompt_tokens": getattr(usage, "prompt_token_count", None) if usage else None,
            "completion_tokens": getattr(usage, "candidates_token_count", None) if usage else None,
            "total_tokens": getattr(usage, "total_token_count", None) if usage else None,
        }

        # Using response.text directly is the simplest path.
        # The Gemini SDK returns the first candidate's text via response.text.
        try:
            text = response.text
            if text:
                return text
        except (AttributeError, ValueError):
            pass

        # Fallback: extract manually from candidates.
        try:
            candidate = response.candidates[0]
            content = getattr(candidate, "content", None)
            if content is not None:
                # When content is an object.
                parts = getattr(content, "parts", None)
                if parts is None and isinstance(content, dict):
                    # When content is a dictionary.
                    parts = content.get("parts", [])
                if parts:
                    texts: List[str] = []
                    for part in parts:
                        text = getattr(part, "text", None) or (
                            part.get("text") if isinstance(part, dict) else None
                        )
                        if text:
                            texts.append(text)
                    if texts:
                        return "\n".join(texts)
        except (AttributeError, IndexError, TypeError):
            pass

        self.last_response_metadata = {}
        raise LLMClientError("Gemini response did not contain text content")


def build_llm_client(
    model: str,
    api_key: Optional[str] = None,
    api_base: Optional[str] = None,
    extra_headers: Optional[Dict[str, str]] = None,
    organization: Optional[str] = None,
    pretrained: Optional[str] = None,
    provider_hint: Optional[str] = None,
) -> BaseLLMClient:
    if not provider_hint or not provider_hint.strip():
        raise LLMClientError(
            "provider_hint is required; pass an explicit provider key"
        )
    provider_key = provider_hint.strip().lower()

    if provider_key in {"openai", "oai"}:
        resolved_key = api_key or os.getenv("OPENAI_API_KEY")
        return OpenAIClient(model=model, api_key=resolved_key)
    if provider_key in {"azure", "azure-openai", "azure_openai"}:
        resolved_key = (
            api_key
            or os.getenv("AZURE_OPENAI_API_KEY")
            or os.getenv("OPENAI_API_KEY")
            or os.getenv("LLM_API_KEY")
        )
        resolved_endpoint = (
            api_base or os.getenv("AZURE_OPENAI_ENDPOINT") or os.getenv("LLM_API_BASE")
        )
        resolved_version = (
            os.getenv("AZURE_OPENAI_API_VERSION")
            or os.getenv("OPENAI_API_VERSION")
            or os.getenv("LLM_API_VERSION")
        )
        resolved_deployment = (
            pretrained or os.getenv("AZURE_OPENAI_DEPLOYMENT") or model
        )
        extra: Dict[str, str] = {}
        if extra_headers:
            extra.update(extra_headers)
        elif os.getenv("LLM_EXTRA_HEADERS"):
            try:
                extra.update(json.loads(os.getenv("LLM_EXTRA_HEADERS", "{}")))
            except json.JSONDecodeError:
                extra = {}
        return AzureOpenAIClient(
            model=model,
            endpoint=resolved_endpoint,
            deployment=resolved_deployment,
            api_key=resolved_key,
            api_version=resolved_version,
            extra_headers=extra,
            api_base=api_base,
        )
    if provider_key in {
        "openai-compatible",
        "openai_compatible",
        "openaicompatible",
        "vllm",
        "ollama",
    }:
        resolved_key = (
            api_key or os.getenv("LLM_API_KEY") or os.getenv("OPENAI_API_KEY")
        )
        resolved_base = api_base or os.getenv("LLM_API_BASE")
        if not resolved_base:
            raise LLMClientError(
                "LLM_API_BASE is required for OpenAI-compatible providers"
            )
        if not pretrained:
            raise LLMClientError(
                "pretrained is required for OpenAI-compatible providers"
            )
        extra: Dict[str, str] = {}
        if extra_headers:
            extra.update(extra_headers)
        elif os.getenv("LLM_EXTRA_HEADERS"):
            try:
                extra.update(json.loads(os.getenv("LLM_EXTRA_HEADERS", "{}")))
            except json.JSONDecodeError:
                extra = {}
        return OpenAICompatibleClient(
            model=model,
            api_base=resolved_base,
            api_key=resolved_key,
            extra_headers=extra,
            pretrained=pretrained,
        )
    if provider_key in {"anthropic", "claude"}:
        resolved_key = api_key or os.getenv("ANTHROPIC_API_KEY")
        return AnthropicClient(model=model, api_key=resolved_key)
    if provider_key in {"gemini", "google", "googleai", "google-genai"}:
        resolved_key = (
            api_key
            or os.getenv("GOOGLE_API_KEY")
            or os.getenv("GEMINI_API_KEY")
            or os.getenv("GOOGLE_GENAI_API_KEY")
        )
        return GeminiClient(model=model, api_key=resolved_key)

    raise LLMClientError(f"Unsupported LLM provider '{provider_key}'")


__all__ = [
    "BaseLLMClient",
    "LLMClientError",
    "OpenAIClient",
    "OpenAICompatibleClient",
    "AzureOpenAIClient",
    "AnthropicClient",
    "GeminiClient",
    "build_llm_client",
]
