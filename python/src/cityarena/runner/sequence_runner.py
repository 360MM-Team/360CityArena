#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""
Run multiple 360CityArena tasks sequentially using the JSON-lines agent server.

Tasks are loaded from the pinned Hugging Face Dataset release. You can
select specific task IDs, whole task categories (TaskType), or execute the entire
catalog.
"""

from __future__ import annotations

import argparse
import csv
import hashlib
import json
import logging
import os
import socket
import subprocess
import sys
import threading
import time
from datetime import datetime
from typing import Iterable, List
from pathlib import Path

from cityarena.paths import (
    DEFAULT_OUTPUT_ROOT,
    PACKAGE_ROOT,
    REPO_ROOT,
    UNITY_ROOT,
)
from cityarena.runner.agent_server import AgentServer, configure_benchmark_logging
from cityarena.tasks.catalog import (
    get_dataset_source,
    get_task_by_id,
    get_tasks_by_type,
    iter_all_tasks,
)
from cityarena.tasks.types import Task, TaskType
from cityarena.runner.unity_controller import UnityController


def _project_root() -> Path:
    return REPO_ROOT


def _sanitize_run_id(value: str) -> str:
    cleaned = "".join(ch if ch.isalnum() or ch in "._-" else "_" for ch in value.strip())
    return cleaned or datetime.now().strftime("run_%Y%m%d_%H%M%S")


def _resolve_output_root(value: str | None) -> Path:
    root = Path(value) if value else DEFAULT_OUTPUT_ROOT
    if not root.is_absolute():
        root = _project_root() / root
    return root.resolve()


def _hash_files(paths: Iterable[Path]) -> str:
    digest = hashlib.sha256()
    for path in sorted(paths):
        digest.update(str(path.relative_to(REPO_ROOT)).encode("utf-8"))
        digest.update(b"\0")
        digest.update(path.read_bytes())
        digest.update(b"\0")
    return digest.hexdigest()


def _task_catalog_hash() -> str:
    return get_dataset_source().revision


def _prompt_hash() -> str:
    prompt_files = [
        PACKAGE_ROOT / "prompts" / "system.py",
        PACKAGE_ROOT / "tasks" / "prompt_templates.py",
        PACKAGE_ROOT / "tasks" / "validators.py",
    ]
    return _hash_files(path for path in prompt_files if path.exists())


def _file_hash(path: Path) -> str:
    return _hash_files([path]) if path.exists() else ""


def _git_metadata() -> dict[str, str | bool]:
    try:
        commit = subprocess.check_output(
            ["git", "rev-parse", "HEAD"],
            cwd=REPO_ROOT,
            text=True,
            stderr=subprocess.DEVNULL,
        ).strip()
    except Exception:
        commit = ""
    try:
        status = subprocess.check_output(
            ["git", "status", "--porcelain"],
            cwd=REPO_ROOT,
            text=True,
            stderr=subprocess.DEVNULL,
        )
        dirty = bool(status.strip())
    except Exception:
        dirty = False
    return {"git_commit": commit, "git_dirty": dirty}


def _unity_version() -> str:
    version_path = UNITY_ROOT / "ProjectSettings" / "ProjectVersion.txt"
    if not version_path.exists():
        return ""
    try:
        for line in version_path.read_text(encoding="utf-8").splitlines():
            if line.startswith("m_EditorVersion:"):
                return line.split(":", 1)[1].strip()
    except Exception:
        return ""
    return ""


def _write_run_metadata(
    output_dir: Path,
    args: argparse.Namespace,
    tasks: list[Task],
    status: str,
    started_at: datetime,
    ended_at: datetime | None = None,
) -> None:
    dataset_source = get_dataset_source()
    payload = {
        "benchmark_name": "360CityArena",
        "result_schema_version": 3,
        "run_id": args.run_id,
        "experiment_id": args.experiment_id or "",
        "model": args.model,
        "provider": args.provider,
        "pretrained": args.pretrained or "",
        "max_tokens": args.max_tokens,
        "temperature": "" if args.temperature is None else args.temperature,
        "validation_model": args.validation_model or "",
        "validation_provider": args.validation_provider or "",
        "validation_pretrained": args.validation_pretrained or "",
        "task_catalog_hash": _task_catalog_hash(),
        "dataset_repo": dataset_source.repo_id,
        "dataset_config": dataset_source.config,
        "dataset_split": dataset_source.split,
        "dataset_revision": dataset_source.revision,
        "prompt_hash": _prompt_hash(),
        "python_lock_hash": _file_hash(REPO_ROOT / "python" / "uv.lock"),
        "unity_version": _unity_version(),
        **_git_metadata(),
        "step_limit_default": os.getenv("STEP_LIMIT_DEFAULT", "300"),
        "location_epsilon": os.getenv("LOCATION_EPSILON", "0.05"),
        "location_stagnant_limit": os.getenv("LOCATION_STAGNANT_LIMIT", "20"),
        "goal_away_consecutive_limit": os.getenv("GOAL_AWAY_CONSECUTIVE_LIMIT", "5"),
        "status": status,
        "started_at": started_at.isoformat(timespec="seconds"),
        "ended_at": ended_at.isoformat(timespec="seconds") if ended_at else "",
        "task_count": len(tasks),
        "task_ids": [task.id for task in tasks],
        "output_root": str(Path(args.output_root).name),
        "results_file": "results.csv",
        "save_debug_artifacts": bool(args.save_debug_artifacts),
        "append_results": bool(getattr(args, "append_results", False)),
    }
    metadata_path = output_dir / "run_metadata.json"
    with open(metadata_path, "w", encoding="utf-8") as f:
        json.dump(payload, f, ensure_ascii=False, indent=2)


def _parse_task_type(value: str) -> TaskType:
    normalized = value.strip().lower()
    aliases = {
        "constraintsatisficationnavigation": TaskType.ConstraintSatisficationNavigation,
        "constraint satisfication navigation": TaskType.ConstraintSatisficationNavigation,
        "constraint_satisfication_navigation": TaskType.ConstraintSatisficationNavigation,
        "constraintsatisfactionnavigation": TaskType.ConstraintSatisficationNavigation,
        "constraint satisfaction navigation": TaskType.ConstraintSatisficationNavigation,
        "constraint_satisfaction_navigation": TaskType.ConstraintSatisficationNavigation,
    }
    if normalized in aliases:
        return aliases[normalized]
    for task_type in TaskType:
        if normalized in {task_type.name.lower(), task_type.value.lower()}:
            return task_type
    raise argparse.ArgumentTypeError(f"Unknown task type: {value!r}")


def _unique_by_id(tasks: Iterable[Task]) -> list[Task]:
    unique: list[Task] = []
    seen: set[int] = set()
    for task in tasks:
        if task.id in seen:
            continue
        unique.append(task)
        seen.add(task.id)
    return unique


def _describe_task(task: Task) -> str:
    parts: list[str] = [f"id={task.id}", task.task_type.value]
    if task.difficulty:
        parts.append(f"difficulty={task.difficulty}")
    landmark = task.metadata.get("landmark") or task.metadata.get("landmark_name")
    if landmark:
        parts.append(f"landmark={landmark}")
    map_id = task.metadata.get("map_id")
    if map_id:
        parts.append(f"map_id={map_id}")
    return ", ".join(parts)


def _csv_truthy(value: object) -> bool:
    return str(value).strip().lower() in {"true", "1", "yes"}


def _display_csv_bool(value: object) -> str:
    text = str(value).strip()
    if not text:
        return "-"
    return "yes" if _csv_truthy(text) else "no"


def _percent(numerator: float, denominator: int) -> str:
    if denominator <= 0:
        return "0.00%"
    return f"{numerator / denominator:.2%}"


def _row_evaluation_score(row: dict[str, str] | None) -> float:
    if row is None:
        return 0.0
    raw_score = row.get("evaluation_score", "")
    if raw_score not in ("", None):
        try:
            return min(1.0, max(0.0, float(raw_score)))
        except (TypeError, ValueError):
            pass
    return 1.0 if _csv_truthy(row.get("scored_success", "")) else 0.0


def _shorten(value: object, width: int) -> str:
    text = str(value or "").replace("\n", " ")
    if len(text) <= width:
        return text
    if width <= 3:
        return text[:width]
    return f"{text[: width - 3]}..."


def _format_table_line(values: list[object], widths: list[int]) -> str:
    cells = [
        _shorten(value, width).ljust(width)
        for value, width in zip(values, widths, strict=True)
    ]
    return "  ".join(cells).rstrip()


def _load_result_rows(summary_path: Path, run_id: str) -> list[dict[str, str]]:
    if not summary_path.exists():
        return []
    with open(summary_path, newline="", encoding="utf-8") as f:
        reader = csv.DictReader(f)
        return [row for row in reader if row.get("run_id", "") == run_id]


def _build_benchmark_summary_lines(
    summary_path: Path,
    tasks: list[Task],
    run_id: str,
) -> list[str]:
    lines = ["Benchmark summary", f"results_file={summary_path}"]
    if not summary_path.exists():
        lines.append("No results.csv was written for this run.")
        return lines

    result_rows = _load_result_rows(summary_path, run_id)
    latest_row_by_task_id: dict[str, dict[str, str]] = {}
    for row in result_rows:
        task_id = row.get("task_id", "")
        if task_id:
            latest_row_by_task_id[task_id] = row

    scheduled_results = [
        (task, latest_row_by_task_id.get(str(task.id))) for task in tasks
    ]
    total = len(tasks)
    rows_written = sum(1 for _, row in scheduled_results if row is not None)
    success_count = sum(
        1
        for _, row in scheduled_results
        if row is not None and _csv_truthy(row.get("scored_success", ""))
    )
    evaluation_score_sum = sum(
        _row_evaluation_score(row) for _, row in scheduled_results
    )
    answer_count = sum(
        1
        for _, row in scheduled_results
        if row is not None and row.get("status", "") == "ANSWER"
    )
    validation_available_count = sum(
        1
        for _, row in scheduled_results
        if row is not None and _csv_truthy(row.get("validation_available", ""))
    )
    correct_count = sum(
        1
        for _, row in scheduled_results
        if row is not None and _csv_truthy(row.get("is_correct", ""))
        and _csv_truthy(row.get("validation_available", ""))
    )

    lines.append(
        f"official_score={_percent(evaluation_score_sum, total)} "
        f"binary_success={success_count}/{total} "
        f"result_rows={rows_written}/{total} csv_rows_for_run={len(result_rows)}"
    )
    lines.append(
        f"answer_completions={answer_count}/{total} "
        f"validator_correct={correct_count}/{validation_available_count}"
    )
    if rows_written < total:
        missing_ids = [
            str(task.id) for task, row in scheduled_results if row is None
        ]
        lines.append(f"missing_result_rows={','.join(missing_ids)}")

    type_stats: dict[str, dict[str, int | float]] = {}
    type_order: list[str] = []
    for task, row in scheduled_results:
        task_type = task.task_type.value
        if task_type not in type_stats:
            type_stats[task_type] = {
                "total": 0,
                "rows": 0,
                "success": 0,
                "score": 0.0,
            }
            type_order.append(task_type)
        type_stats[task_type]["total"] += 1
        if row is not None:
            type_stats[task_type]["rows"] += 1
            type_stats[task_type]["score"] += _row_evaluation_score(row)
            if _csv_truthy(row.get("scored_success", "")):
                type_stats[task_type]["success"] += 1

    lines.append("Task type summary:")
    for task_type in type_order:
        stats = type_stats[task_type]
        lines.append(
            "  "
            f"{task_type}: score={_percent(stats['score'], stats['total'])}, "
            f"binary_success={stats['success']}/{stats['total']}, "
            f"result_rows={stats['rows']}/{stats['total']}"
        )

    headers = [
        "task_id",
        "task_type",
        "difficulty",
        "status",
        "success",
        "score",
        "correct",
        "steps",
        "elapsed",
    ]
    widths = [7, 32, 10, 14, 7, 7, 7, 5, 8]
    lines.append("Task results:")
    lines.append(_format_table_line(headers, widths))
    lines.append("  ".join("-" * width for width in widths))
    for task, row in scheduled_results:
        difficulty = row.get("difficulty", "") if row else ""
        values = [
            task.id,
            task.task_type.value,
            difficulty or task.difficulty or "",
            row.get("status", "MISSING") if row else "MISSING",
            _display_csv_bool(row.get("scored_success", "")) if row else "no",
            f"{_row_evaluation_score(row):.2f}" if row else "0.00",
            _display_csv_bool(row.get("is_correct", "")) if row else "-",
            row.get("steps", "-") if row else "-",
            row.get("elapsed_seconds", "-") if row else "-",
        ]
        lines.append(_format_table_line(values, widths))
    return lines


def _has_console_info_handler() -> bool:
    root = logging.getLogger()
    if root.getEffectiveLevel() > logging.INFO:
        return False
    for handler in root.handlers:
        if isinstance(handler, logging.FileHandler):
            continue
        if (
            isinstance(handler, logging.StreamHandler)
            and handler.level <= logging.INFO
        ):
            return True
    return False


def _log_benchmark_summary(
    summary_path: Path,
    tasks: list[Task],
    run_id: str,
) -> None:
    lines = _build_benchmark_summary_lines(summary_path, tasks, run_id)
    print_to_stdout = not _has_console_info_handler()
    for line in lines:
        logging.info("%s", line)
        if print_to_stdout:
            print(line)


def _select_tasks(
    task_ids: list[int],
    task_types: list[str],
    run_all: bool,
) -> list[Task]:
    if run_all:
        return iter_all_tasks()

    selected: list[Task] = []
    seen: set[int] = set()

    for task_id in task_ids:
        task = get_task_by_id(task_id)
        if task is None:
            raise ValueError(f"Task id {task_id} not found in the dataset catalog.")
        if task.id not in seen:
            selected.append(task)
            seen.add(task.id)

    for raw_type in task_types:
        task_type = _parse_task_type(raw_type)
        for task in get_tasks_by_type(task_type):
            if task.id in seen:
                continue
            selected.append(task)
            seen.add(task.id)

    return selected


def _expand_task_ids(raw_ids: list[str]) -> list[int]:
    expanded: list[int] = []
    for token in raw_ids:
        if token is None:
            continue
        s = str(token).strip()
        if not s:
            continue
        # Split comma-separated values first, then treat each part as an ID or range.
        for part in (p.strip() for p in s.split(",") if p.strip()):
            if "-" in part:
                range_parts = part.split("-", 1)
                try:
                    start = int(range_parts[0].strip())
                    end = int(range_parts[1].strip())
                except ValueError:
                    raise ValueError(f"Invalid --task-id range: {part!r}")
                if start > end:
                    start, end = end, start
                expanded.extend(range(start, end + 1))
            else:
                try:
                    expanded.append(int(part))
                except ValueError:
                    raise ValueError(f"Invalid --task-id: {part!r}")
    return expanded


def _expand_task_types(raw_types: list[str]) -> list[str]:
    expanded: list[str] = []
    for token in raw_types:
        if token is None:
            continue
        s = str(token).strip()
        if not s:
            continue
        # Expand comma-separated values.
        parts = [p.strip() for p in s.split(",") if p.strip()]
        expanded.extend(parts)
    return expanded


def _wait_for_port(host: str, port: int, timeout: float) -> None:
    deadline = time.monotonic() + timeout
    while time.monotonic() < deadline:
        try:
            with socket.create_connection((host, port), timeout=1.0):
                return
        except OSError:
            time.sleep(0.2)
    raise TimeoutError(
        f"Timeout waiting for server {host}:{port} to accept connections (timeout={timeout}s)"
    )


def _poke_server(host: str, port: int) -> None:
    try:
        with socket.create_connection((host, port), timeout=1.0):
            pass
    except OSError:
        pass


def _run_sequence(
    tasks: List[Task], args: argparse.Namespace, controller: UnityController | None
) -> None:
    if not tasks:
        raise ValueError("No tasks selected for execution.")

    total = len(tasks)
    logging.info("Scheduled %d task(s) for execution.", total)
    consecutive_errors = 0
    for idx, task in enumerate(tasks, start=1):
        logging.info(
            "=== [%d/%d] Starting task: %s ===", idx, total, _describe_task(task)
        )
        server = AgentServer(
            host=args.host,
            port=args.port,
            api_key=args.api_key,
            model=args.model,
            provider=args.provider,
            pretrained=args.pretrained,
            max_tokens=args.max_tokens,
            temperature=args.temperature,
            validation_model=args.validation_model,
            validation_provider=args.validation_provider,
            validation_pretrained=args.validation_pretrained,
            log_file=args.log_file,
            task_id=task.id,
            api_base=args.api_base,
            extra_headers=args.extra_headers,
            organization=args.organization,
            experiment_id=getattr(args, "experiment_id", None),
            startup_activity_timeout=args.unity_activity_timeout,
            idle_activity_timeout=args.unity_idle_timeout,
            output_root=args.output_root,
            run_id=args.run_id,
            summary_path=args.summary_path,
            save_debug_artifacts=args.save_debug_artifacts,
            progress_log_interval=args.progress_log_interval,
        )

        server_thread = threading.Thread(
            target=server.serve_forever, name=f"AgentServer-{task.id}", daemon=True
        )
        server_thread.start()

        completed = False
        try:
            _wait_for_port(args.host, args.port, args.server_ready_timeout)
            if controller:
                controller.ensure_play()
            else:
                logging.info("Server ready. Awaiting Unity client connection...")

            logging.info("Task %s in progress. Waiting for completion...", task.id)
            server_thread.join()
            if server.fatal_error is not None:
                raise RuntimeError(
                    f"Task {task.id} failed with a fatal agent server error: {server.fatal_error}"
                ) from server.fatal_error
            completed = True
            logging.info(
                "Task %s finished status=%s steps=%d act_requests=%d",
                task.id,
                server.final_status or "unknown",
                server.step_count,
                server.act_request_count,
            )
        except KeyboardInterrupt:
            logging.info(
                "Interrupted by user during task %s. Aborting sequence.", task.id
            )
            raise
        except Exception:
            logging.exception("Task %s terminated with an unexpected error.", task.id)
            if server.fatal_error is not None or args.stop_on_error:
                raise
        finally:
            server.shutdown_requested = True
            _poke_server(args.host, args.port)
            server_thread.join(timeout=5.0)

            if controller:
                try:
                    controller.ensure_stop()
                    if args.post_stop_delay > 0:
                        time.sleep(args.post_stop_delay)
                except Exception:
                    logging.exception("Failed to stop Unity Play mode cleanly.")

        if not completed and not args.stop_on_error:
            logging.warning(
                "Continuing after task %s failure (stop-on-error disabled).", task.id
            )
            consecutive_errors += 1
        else:
            consecutive_errors = 0

        if (
            getattr(args, "max_consecutive_errors", 0)
            and args.max_consecutive_errors > 0
        ):
            if consecutive_errors >= args.max_consecutive_errors:
                logging.error(
                    "Aborting: reached max consecutive errors (%d).",
                    args.max_consecutive_errors,
                )
                raise RuntimeError("Max consecutive task failures reached")

        if idx < total and args.restart_delay > 0:
            time.sleep(args.restart_delay)


def build_argument_parser() -> argparse.ArgumentParser:
    parser = argparse.ArgumentParser(
        description="Execute 360CityArena benchmark tasks sequentially."
    )
    parser.add_argument("--host", default="127.0.0.1")
    parser.add_argument("--port", type=int, default=8765)
    parser.add_argument("--api-key", default=os.getenv("OPENAI_API_KEY"))
    parser.add_argument(
        "--model",
        default=os.getenv("MODEL_NAME", os.getenv("LLM_MODEL")),
    )
    parser.add_argument(
        "--provider",
        default=os.getenv("PROVIDER", os.getenv("LLM_PROVIDER")),
    )
    parser.add_argument("--pretrained", default=os.getenv("LLM_PRETRAINED"))
    parser.add_argument(
        "--max-tokens",
        type=int,
        default=int(os.getenv("LLM_MAX_TOKENS", "8192")),
        help="Maximum output tokens requested from the model.",
    )
    parser.add_argument(
        "--temperature",
        type=float,
        default=float(os.getenv("LLM_TEMPERATURE")) if os.getenv("LLM_TEMPERATURE") else None,
        help="Sampling temperature. Omit to use the provider/model default.",
    )
    parser.add_argument(
        "--validation-model",
        default=os.getenv("LLM_VALIDATION_MODEL"),
        help="Model used for LLM-based answer validation.",
    )
    parser.add_argument(
        "--validation-provider",
        default=os.getenv("LLM_VALIDATION_PROVIDER"),
        help="Provider used for LLM-based answer validation.",
    )
    parser.add_argument(
        "--validation-pretrained",
        default=os.getenv("LLM_VALIDATION_PRETRAINED"),
        help="Deployment/model identifier for LLM-based validation when needed.",
    )
    parser.add_argument("--log-file", default="agent_server.log")
    parser.add_argument("--api-base", default=os.getenv("LLM_API_BASE"))
    parser.add_argument("--extra-headers", default=os.getenv("LLM_EXTRA_HEADERS"))
    parser.add_argument("--organization", default=os.getenv("OPENAI_ORG"))
    parser.add_argument(
        "--runner-log-file",
        default=None,
        help="Write runner logs to this file (disables console logging when set).",
    )
    parser.add_argument(
        "--experiment-id",
        help="Experiment ID to aggregate multiple runs under the same ID.",
    )
    parser.add_argument(
        "--output-root",
        default=os.getenv("OUTPUT_ROOT", "outputs"),
        help="Directory for run outputs. Relative paths are resolved under the repository root.",
    )
    parser.add_argument(
        "--run-id",
        default=os.getenv("RUN_ID"),
        help="Run directory name under --output-root. Defaults to --experiment-id or a timestamp.",
    )
    parser.add_argument(
        "--save-debug-artifacts",
        action="store_true",
        help="Persist debug artifacts such as prompt context, images, and per-task logs.",
    )
    parser.add_argument(
        "--append-results",
        action="store_true",
        help="Allow appending to an existing results.csv for an explicit rerun/resume.",
    )
    parser.add_argument(
        "--progress-log-interval",
        type=int,
        default=int(os.getenv("PROGRESS_LOG_INTERVAL", "10")),
        help="Write one INFO progress log every N ACT requests (0 disables).",
    )

    parser.add_argument(
        "--manage-unity",
        action="store_true",
        help="Automatically control Unity Play mode and launch the editor if needed.",
    )
    parser.add_argument("--unity-url", default="http://127.0.0.1:5005")
    parser.add_argument("--unity-path", default=os.getenv("UNITY_EDITOR_PATH"))
    parser.add_argument(
        "--unity-project-path",
        default=os.getenv("UNITY_PROJECT_PATH", str(UNITY_ROOT)),
    )
    parser.add_argument(
        "--unity-start-arg",
        dest="unity_start_args",
        action="append",
        default=[],
        help="Additional argument to pass when launching Unity (repeatable).",
    )
    parser.add_argument("--unity-startup-delay", type=float, default=8.0)
    parser.add_argument("--unity-request-timeout", type=float, default=5.0)
    parser.add_argument("--unity-max-attempts", type=int, default=10)
    parser.add_argument("--unity-retry-base-delay", type=float, default=2.0)
    parser.add_argument("--unity-retry-max-delay", type=float, default=15.0)
    parser.add_argument(
        "--unity-quit",
        action="store_true",
        help="Terminate Unity if this script launched it once the sequence finishes.",
    )
    parser.add_argument("--server-ready-timeout", type=float, default=10.0)
    parser.add_argument("--post-stop-delay", type=float, default=1.5)
    parser.add_argument(
        "--unity-activity-timeout",
        type=float,
        default=120.0,
        help="Abort if Unity never sends an ACT request within this many seconds (0 disables).",
    )
    parser.add_argument(
        "--unity-idle-timeout",
        type=float,
        default=240.0,
        help="Abort if Unity stops sending ACT requests for this many seconds after starting (0 disables).",
    )

    parser.add_argument(
        "--task-id",
        "--task-id:",
        dest="task_ids",
        type=str,
        action="append",
        default=[],
        help="Run specific task id(s). Repeatable. Supports comma lists (e.g. '5001,5002') and ranges (e.g. 5001-5010).",
    )
    parser.add_argument(
        "--task-type",
        dest="task_types",
        action="append",
        default=[],
        help=(
            "Run tasks of given TaskType(s). Repeatable. Also supports comma-separated list, "
            "e.g. --task-type Localization,MapNavigation"
        ),
    )
    parser.add_argument(
        "--all",
        action="store_true",
        help="Run every task defined in the pinned Hugging Face dataset.",
    )
    parser.add_argument(
        "--restart-delay",
        type=float,
        default=3.0,
        help="Seconds to wait between task runs (allows Unity to reset).",
    )
    parser.add_argument(
        "--stop-on-error",
        action="store_true",
        help="Abort immediately if any task run raises an exception.",
    )
    parser.add_argument(
        "--max-consecutive-errors",
        type=int,
        default=int(os.getenv("MAX_CONSECUTIVE_ERRORS", "0")),
        help="Abort after this many consecutive task failures (0 disables).",
    )
    parser.add_argument(
        "--log-level",
        default="INFO",
        help="Logging level (DEBUG, INFO, WARNING, ERROR).",
    )
    return parser


def main(argv: list[str] | None = None) -> int:
    parser = build_argument_parser()
    args = parser.parse_args(argv)
    if not args.model:
        parser.error("--model or MODEL_NAME/LLM_MODEL is required")
    if not args.provider:
        parser.error("--provider or PROVIDER/LLM_PROVIDER is required")

    started_at = datetime.now()
    output_root = _resolve_output_root(args.output_root)
    args.output_root = str(output_root)
    run_timestamp = started_at.strftime("%Y%m%d_%H%M%S_%f")
    if args.run_id:
        args.run_id = _sanitize_run_id(str(args.run_id))
    elif args.experiment_id:
        args.run_id = _sanitize_run_id(f"{_sanitize_run_id(str(args.experiment_id))}_{run_timestamp}")
    else:
        args.run_id = f"run_{run_timestamp}"
    output_dir = output_root / args.run_id

    try:
        expanded_ids = _expand_task_ids(args.task_ids)
        expanded_types = _expand_task_types(args.task_types)
        tasks = _select_tasks(expanded_ids, expanded_types, args.all)
        tasks = _unique_by_id(tasks)
    except ValueError as exc:
        parser.error(str(exc))

    if not tasks:
        parser.error("No tasks selected. Use --task-id, --task-type, or --all.")
    if any(
        task.task_type == TaskType.RelationalSpatialReasoning for task in tasks
    ) and (not args.validation_model or not args.validation_provider):
        parser.error(
            "Relational Spatial Reasoning tasks require --validation-model and "
            "--validation-provider, or LLM_VALIDATION_MODEL and LLM_VALIDATION_PROVIDER."
        )

    output_dir.mkdir(parents=True, exist_ok=True)
    args.summary_path = str(output_dir / "results.csv")
    if Path(args.summary_path).exists() and not args.append_results:
        parser.error(
            f"{args.summary_path} already exists. Use a fresh --run-id or pass "
            "--append-results to append intentionally."
        )

    if args.runner_log_file:
        runner_log_path = Path(args.runner_log_file)
        if not runner_log_path.is_absolute():
            runner_log_path = output_dir / runner_log_path
        runner_log_path.parent.mkdir(parents=True, exist_ok=True)
        args.runner_log_file = str(runner_log_path)
    elif args.save_debug_artifacts:
        args.runner_log_file = str(output_dir / "runner.log")

    if args.runner_log_file:
        logging.basicConfig(
            level=getattr(logging, args.log_level.upper(), logging.INFO),
            format="%(asctime)s [%(levelname)s] %(message)s",
            handlers=[logging.FileHandler(args.runner_log_file, encoding="utf-8")],
        )
    else:
        logging.basicConfig(
            level=getattr(logging, args.log_level.upper(), logging.INFO),
            format="%(asctime)s [%(levelname)s] %(message)s",
        )
    configure_benchmark_logging()

    _write_run_metadata(output_dir, args, tasks, status="running", started_at=started_at)
    logging.info(
        "run_started run_id=%s output_dir=%s task_count=%d model=%s provider=%s",
        args.run_id,
        output_dir,
        len(tasks),
        args.model,
        args.provider,
    )

    controller: UnityController | None = None
    if args.manage_unity:
        controller = UnityController(
            base_url=args.unity_url,
            unity_path=args.unity_path,
            project_path=args.unity_project_path,
            start_args=args.unity_start_args,
            startup_delay=args.unity_startup_delay,
            request_timeout=args.unity_request_timeout,
            max_attempts=args.unity_max_attempts,
            retry_base_delay=args.unity_retry_base_delay,
            retry_max_delay=args.unity_retry_max_delay,
        )

    status = "completed"
    exit_code = 0
    try:
        _run_sequence(tasks, args, controller)
    except KeyboardInterrupt:
        logging.info("Sequence interrupted by user.")
        status = "interrupted"
        exit_code = 130
    except RuntimeError as exc:
        logging.error(str(exc))
        status = "failed"
        exit_code = 1
    except Exception:
        logging.exception("Sequence failed.")
        status = "failed"
        exit_code = 1
    finally:
        if controller:
            controller.shutdown(terminate=args.unity_quit)
        try:
            _write_run_metadata(
                output_dir,
                args,
                tasks,
                status=status,
                started_at=started_at,
                ended_at=datetime.now(),
            )
        except Exception:
            logging.exception("Failed to write run metadata.")

    logging.info("run_finished run_id=%s status=%s", args.run_id, status)
    try:
        _log_benchmark_summary(Path(args.summary_path), tasks, args.run_id)
    except Exception:
        logging.exception("Failed to print benchmark summary.")
    return exit_code


if __name__ == "__main__":
    sys.exit(main())
