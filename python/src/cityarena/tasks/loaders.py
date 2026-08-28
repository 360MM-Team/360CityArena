from __future__ import annotations

import json
import os
from collections import Counter
from dataclasses import dataclass
from functools import lru_cache
from pathlib import Path
from typing import Any

from cityarena.paths import BENCHMARK_ROOT, resolve_repo_path
from cityarena.tasks.types import Task, TaskType


_MANIFEST_PATH = BENCHMARK_ROOT / "manifests" / "task_manifest.json"
_EXPECTED_TASKS = 175
_EXPECTED_TASKS_PER_FAMILY = 25


@dataclass(frozen=True)
class DatasetSource:
    repo_id: str
    config: str
    split: str
    revision: str


def _required_value(row: dict[str, Any], key: str, row_number: int) -> Any:
    value = row.get(key)
    if value is None or (isinstance(value, str) and not value.strip()):
        raise ValueError(f"Hugging Face dataset row {row_number}: missing '{key}'")
    return value


@lru_cache(maxsize=1)
def get_dataset_source() -> DatasetSource:
    try:
        manifest = json.loads(_MANIFEST_PATH.read_text(encoding="utf-8"))
        source = manifest["source"]
    except (OSError, KeyError, TypeError, json.JSONDecodeError) as exc:
        raise RuntimeError(f"Invalid dataset manifest: {_MANIFEST_PATH}") from exc

    return DatasetSource(
        repo_id=os.getenv("CITYARENA_DATASET_REPO", source["repo_id"]),
        config=os.getenv("CITYARENA_DATASET_CONFIG", source["config"]),
        split=os.getenv("CITYARENA_DATASET_SPLIT", source["split"]),
        revision=os.getenv("CITYARENA_DATASET_REVISION", source["revision"]),
    )


def _metadata_from_row(
    row: dict[str, Any], row_number: int, source: DatasetSource
) -> dict[str, Any]:
    raw_source_record = row.get("source_record")
    if raw_source_record:
        try:
            metadata = json.loads(raw_source_record)
        except (TypeError, json.JSONDecodeError) as exc:
            raise ValueError(
                f"Hugging Face dataset row {row_number}: invalid source_record"
            ) from exc
    else:
        metadata = {}

    normalized_fields = {
        "task_id": row.get("task_id"),
        "initial_index": row.get("initial_index"),
        "difficulty": row.get("difficulty"),
        "reference_image_path": row.get("reference_image_path"),
        "source_file": row.get("source_file"),
        "source_row": row.get("source_row"),
        "landmark": row.get("goal_landmark"),
        "directions": row.get("directions"),
        "relation": row.get("relation"),
        "object": row.get("counting_object"),
        "range": row.get("counting_range"),
        "gt_x": row.get("goal_x"),
        "gt_y": row.get("goal_y"),
        "gt_grid_x": row.get("goal_grid_x"),
        "gt_grid_y": row.get("goal_grid_y"),
        "photo_id": row.get("photo_id"),
        "map_id": row.get("map_id"),
    }
    metadata.update(
        {key: value for key, value in normalized_fields.items() if value is not None}
    )
    metadata.update(
        {
            "dataset_repo": source.repo_id,
            "dataset_config": source.config,
            "dataset_split": source.split,
            "dataset_revision": source.revision,
            "dataset_row": row_number,
        }
    )
    return metadata


def _reference_images(row: dict[str, Any], row_number: int) -> list[str]:
    relative_path = row.get("reference_image_path")
    if not relative_path:
        return []
    path = Path(str(relative_path))
    if path.is_absolute() or ".." in path.parts:
        raise ValueError(
            f"Hugging Face dataset row {row_number}: unsafe reference image path"
        )
    resolved = resolve_repo_path(path)
    if not resolved.is_file():
        raise FileNotFoundError(
            f"Task reference image is not available in the runner checkout: {resolved}"
        )
    return [str(path)]


def _task_from_row(
    row: dict[str, Any], row_number: int, source: DatasetSource
) -> Task:
    task_type_value = str(_required_value(row, "task_type", row_number))
    try:
        task_type = TaskType(task_type_value)
    except ValueError as exc:
        raise ValueError(
            f"Hugging Face dataset row {row_number}: unknown task_type "
            f"{task_type_value!r}"
        ) from exc

    return Task(
        id=int(_required_value(row, "task_id", row_number)),
        prompt=str(_required_value(row, "prompt", row_number)),
        task_type=task_type,
        requires_current_location=bool(row.get("requires_current_location")),
        additional_task_images=_reference_images(row, row_number),
        start_index=int(_required_value(row, "initial_index", row_number)),
        answer=str(_required_value(row, "answer", row_number)),
        difficulty=(str(row["difficulty"]) if row.get("difficulty") else None),
        metadata=_metadata_from_row(row, row_number, source),
    )


def _validate_inventory(tasks: list[Task]) -> None:
    if len(tasks) != _EXPECTED_TASKS:
        raise ValueError(
            f"Expected {_EXPECTED_TASKS} Hugging Face tasks, found {len(tasks)}"
        )

    task_ids = [task.id for task in tasks]
    duplicates = sorted(
        task_id for task_id, count in Counter(task_ids).items() if count > 1
    )
    if duplicates:
        raise ValueError(f"Duplicate task IDs in Hugging Face dataset: {duplicates}")

    family_counts = Counter(task.task_type for task in tasks)
    if len(family_counts) != 7 or any(
        count != _EXPECTED_TASKS_PER_FAMILY for count in family_counts.values()
    ):
        readable_counts = {
            task_type.value: count for task_type, count in family_counts.items()
        }
        raise ValueError(f"Unexpected task-family inventory: {readable_counts}")


@lru_cache(maxsize=1)
def load_tasks_from_hub() -> tuple[Task, ...]:
    try:
        from datasets import load_dataset
    except ImportError as exc:
        raise RuntimeError(
            "The `datasets` package is required to load the 360CityArena task catalog."
        ) from exc

    source = get_dataset_source()
    try:
        dataset = load_dataset(
            source.repo_id,
            source.config,
            split=source.split,
            revision=source.revision,
        )
    except Exception as exc:
        raise RuntimeError(
            "Unable to load the pinned 360CityArena dataset from Hugging Face. "
            "Authenticate with `hf auth login` while the repository is private, "
            "or make sure the pinned revision is available in the local HF cache. "
            f"Source: {source.repo_id}/{source.config}@{source.revision}"
        ) from exc

    # Reference images are supplied by the runner checkout. Removing the embedded
    # image column avoids decoding all 75 images while constructing the catalog.
    if "reference_image" in dataset.column_names:
        dataset = dataset.remove_columns("reference_image")

    tasks = [
        _task_from_row(dict(row), row_number, source)
        for row_number, row in enumerate(dataset, start=1)
    ]
    _validate_inventory(tasks)
    return tuple(sorted(tasks, key=lambda task: task.id))
