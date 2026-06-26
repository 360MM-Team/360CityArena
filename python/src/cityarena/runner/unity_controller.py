#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""
Utilities for controlling the Unity Editor via the HTTP endpoints exposed by
`EditorController` (Assets/Editor/EditorController.cs).

The controller supports:
  - Launching the Unity Editor if the control endpoint is unreachable.
  - Requesting Play mode start (`POST /play`) with configurable retries/backoff.
  - Requesting Play mode stop (`POST /stop`) with retries.
  - Optionally terminating the Unity process that was spawned by the script.
"""

from __future__ import annotations

import logging
import os
import subprocess
import time
from typing import Iterable, Optional

import requests


class UnityController:
    """High-level helper that automates Unity start/stop operations."""

    def __init__(
        self,
        base_url: str = "http://127.0.0.1:5005",
        unity_path: str | None = None,
        project_path: str | None = None,
        start_args: Optional[Iterable[str]] = None,
        startup_delay: float = 8.0,
        request_timeout: float = 5.0,
        max_attempts: int = 10,
        retry_base_delay: float = 2.0,
        retry_max_delay: float = 15.0,
        kill_timeout: float = 20.0,
    ) -> None:
        self.base_url = base_url.rstrip("/")
        self.unity_path = unity_path or os.getenv("UNITY_EDITOR_PATH")
        self.project_path = project_path or os.getenv("UNITY_PROJECT_PATH")
        self.start_args = list(start_args or [])
        self.startup_delay = startup_delay
        self.request_timeout = request_timeout
        self.max_attempts = max_attempts
        self.retry_base_delay = retry_base_delay
        self.retry_max_delay = retry_max_delay
        self.kill_timeout = kill_timeout

        self._process: subprocess.Popen[str] | None = None
        self._spawned = False

    # ------------------------------------------------------------------ #
    # Public API
    # ------------------------------------------------------------------ #

    def ensure_play(self) -> None:
        """Ensure Unity is running and switch the editor to Play mode."""
        self._post_with_retry("/play", allow_spawn=True)

    def ensure_stop(self) -> None:
        """Request Unity to exit Play mode."""
        try:
            self._post_with_retry("/stop", allow_spawn=False)
        except RuntimeError as exc:
            logging.warning("Failed to send Unity /stop request: %s", exc)

    def shutdown(self, terminate: bool = True) -> None:
        """
        Stop Play mode and optionally terminate the Unity process that was
        started by this controller.
        """
        self.ensure_stop()
        if terminate and self._spawned and self._process:
            if self._process.poll() is None:
                logging.info("Terminating Unity process (pid=%s)...", self._process.pid)
                try:
                    self._process.terminate()
                    self._process.wait(timeout=self.kill_timeout)
                    logging.info("Unity process terminated.")
                except subprocess.TimeoutExpired:
                    logging.warning(
                        "Unity did not exit in %.1fs; killing.", self.kill_timeout
                    )
                    self._process.kill()
            self._process = None
            self._spawned = False

    # ------------------------------------------------------------------ #
    # Internal helpers
    # ------------------------------------------------------------------ #

    def _launch_unity(self) -> None:
        if not self.unity_path or not self.project_path:
            raise RuntimeError(
                "Unity control endpoint unreachable and no Unity executable/project path provided. "
                "Set --unity-path and --unity-project-path (or UNITY_EDITOR_PATH / UNITY_PROJECT_PATH)."
            )
        if self._process and self._process.poll() is None:
            logging.debug("Unity process already running (pid=%s).", self._process.pid)
            return

        cmd = [
            self.unity_path,
            "-projectPath",
            self.project_path,
            *self.start_args,
        ]
        logging.info("Launching Unity Editor: %s", " ".join(cmd))
        try:
            self._process = subprocess.Popen(cmd)
        except OSError as exc:
            raise RuntimeError(f"Failed to launch Unity Editor: {exc}") from exc

        self._spawned = True
        logging.info(
            "Unity Editor started (pid=%s). Waiting %.1fs for startup...",
            self._process.pid,
            self.startup_delay,
        )
        if self.startup_delay > 0:
            time.sleep(self.startup_delay)

    def _post_with_retry(self, endpoint: str, allow_spawn: bool) -> None:
        last_error: Exception | None = None
        for attempt in range(1, self.max_attempts + 1):
            try:
                resp = self._post(endpoint)
                if resp.status_code >= 400:
                    raise RuntimeError(f"HTTP {resp.status_code} {resp.text.strip()}")
                if attempt > 1:
                    logging.info("Unity %s succeeded on attempt %d.", endpoint, attempt)
                return
            except Exception as exc:  # noqa: BLE001
                last_error = exc
                if attempt == 1 and allow_spawn:
                    if self.unity_path and self.project_path:
                        logging.info(
                            "Unity endpoint %s unreachable. Attempting to launch Unity.",
                            endpoint,
                        )
                        try:
                            self._launch_unity()
                        except Exception as launch_error:  # noqa: BLE001
                            logging.error("Failed to launch Unity: %s", launch_error)
                            raise
                    else:
                        raise RuntimeError(
                            "Unity endpoint unreachable and no Unity executable/project path configured. "
                            "Provide --unity-path and --unity-project-path or set UNITY_EDITOR_PATH/UNITY_PROJECT_PATH."
                        ) from exc
                else:
                    logging.debug(
                        "Unity request %s failed on attempt %d/%d: %s",
                        endpoint,
                        attempt,
                        self.max_attempts,
                        exc,
                    )
                if attempt >= self.max_attempts:
                    break
                delay = min(
                    self.retry_base_delay * (2 ** (attempt - 1)), self.retry_max_delay
                )
                logging.info(
                    "Retrying Unity request %s in %.1fs (attempt %d/%d).",
                    endpoint,
                    delay,
                    attempt + 1,
                    self.max_attempts,
                )
                time.sleep(delay)
        raise RuntimeError(
            f"Unity request {endpoint} failed after {self.max_attempts} attempts: {last_error}"
        )

    def _post(self, endpoint: str) -> requests.Response:
        url = f"{self.base_url}{endpoint}"
        logging.debug("POST %s", url)
        return requests.post(url, timeout=self.request_timeout)

    # ------------------------------------------------------------------ #
    # Context manager helpers
    # ------------------------------------------------------------------ #

    def __enter__(self) -> "UnityController":
        return self

    def __exit__(self, exc_type, exc, tb) -> None:
        if exc:
            logging.debug("UnityController exiting due to exception: %s", exc)
        self.shutdown(terminate=False)
