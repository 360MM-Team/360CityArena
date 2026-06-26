from __future__ import annotations

import csv
import logging
from functools import lru_cache
from pathlib import Path
from typing import Any, Callable, Optional

from cityarena.paths import BENCHMARK_ROOT, resolve_repo_path
from cityarena.tasks.prompt_templates import (
    COUNTING_PROMPT,
    LANDMARK_SEARCH_WITH_IMAGE_PROMPT,
    LANDMARK_SEARCH_WITH_LANGUAGE_PROMPT,
    LANGUAGE_GUIDED_NAVIGATION_PROMPT,
    LOCALIZATION_IMAGES,
    LOCALIZATION_PROMPT,
    MAP_NAVIGATION_PROMPT,
    RELATIONAL_SPATIAL_REASONING_PROMPT,
)
from cityarena.tasks.types import Task, TaskType

logger = logging.getLogger(__name__)
_TASKS_DIR = BENCHMARK_ROOT / "tasks"
TaskBuilder = Callable[[dict[str, Any], Path, int], Optional[Task]]


def _clean_value(value: Any) -> Optional[str]:
    if value is None:
        return None
    if isinstance(value, str):
        value = value.strip()
        if not value:
            return None
        return value
    return str(value)


def _optional_field(row: dict[str, Any], key: str) -> Optional[str]:
    return _clean_value(row.get(key))


def _require_field(row: dict[str, Any], key: str, path: Path, row_number: int) -> str:
    value = _optional_field(row, key)
    if value is None:
        raise ValueError(f"{path.name} row {row_number}: missing value for '{key}'")
    return value


def _require_int(row: dict[str, Any], key: str, path: Path, row_number: int) -> int:
    raw_value = _require_field(row, key, path, row_number)
    try:
        return int(raw_value)
    except ValueError as exc:
        raise ValueError(
            f"{path.name} row {row_number}: field '{key}' expects an integer, got {raw_value!r}"
        ) from exc


def _format_number(value: Any) -> str:
    cleaned = _clean_value(value)
    if cleaned is None:
        return ""
    try:
        num = float(cleaned)
    except ValueError:
        return cleaned
    if abs(num - round(num)) < 1e-6:
        return str(int(round(num)))
    return f"{num:.2f}".rstrip("0").rstrip(".")


def _format_xy(x: Any, y: Any) -> str:
    return f"x:{_format_number(x)} y:{_format_number(y)}"


def _build_metadata(
    row: dict[str, Any],
    path: Path,
    row_number: int,
    extra: Optional[dict[str, Any]] = None,
) -> dict[str, Any]:
    metadata: dict[str, Any] = {}
    for key, value in row.items():
        cleaned = _clean_value(value)
        if cleaned is not None:
            metadata[key] = cleaned
    metadata["csv"] = path.name
    metadata["row"] = row_number
    if extra:
        metadata.update(extra)
    return metadata


def _resolve_image_path(relative: Optional[str]) -> Optional[str]:
    rel = _clean_value(relative)
    if rel is None:
        return None
    abs_path = resolve_repo_path(rel)
    if not abs_path.exists():
        logger.warning("Task image not found: %s", abs_path)
    return rel


def _load_csv_tasks(file_name: str, builder: TaskBuilder) -> list[Task]:
    path = _TASKS_DIR / file_name
    tasks: list[Task] = []
    if not path.exists():
        raise FileNotFoundError(f"Task CSV not found: {path}")
    with open(path, newline="", encoding="utf-8") as csvfile:
        reader = csv.DictReader(csvfile)
        for row_number, raw_row in enumerate(reader, start=2):
            row: dict[str, Any] = {
                key: _clean_value(value) for key, value in (raw_row or {}).items()
            }
            if not any(value is not None for value in row.values()):
                continue
            task = builder(row, path, row_number)
            if task:
                tasks.append(task)
    return tasks


def _load_localization_tasks() -> list[Task]:
    def builder(row: dict[str, Any], path: Path, row_number: int) -> Task:
        task_id = _require_int(row, "task_id", path, row_number)
        start_index = _require_int(row, "initial_index", path, row_number)
        answer = _format_xy(row.get("gt_grid_x"), row.get("gt_grid_y"))
        difficulty = _optional_field(row, "difficulty")
        metadata = _build_metadata(row, path, row_number)
        return Task(
            id=task_id,
            prompt=LOCALIZATION_PROMPT,
            task_type=TaskType.Localization,
            requires_current_location=False,
            additional_task_images=list(LOCALIZATION_IMAGES),
            start_index=start_index,
            answer=answer,
            difficulty=difficulty,
            metadata=metadata,
        )

    return _load_csv_tasks("localization.csv", builder)


def _load_landmark_language_tasks() -> list[Task]:
    def builder(row: dict[str, Any], path: Path, row_number: int) -> Task:
        task_id = _require_int(row, "task_id", path, row_number)
        start_index = _require_int(row, "initial_index", path, row_number)
        landmark = _require_field(row, "landmark", path, row_number)
        answer = _format_xy(row.get("gt_x"), row.get("gt_y"))
        difficulty = _optional_field(row, "difficulty")
        metadata = _build_metadata(row, path, row_number, {"landmark": landmark})
        return Task(
            id=task_id,
            prompt=LANDMARK_SEARCH_WITH_LANGUAGE_PROMPT.format(LandmarkName=landmark),
            task_type=TaskType.LandmarkSearchWithLanguage,
            requires_current_location=True,
            additional_task_images=[],
            start_index=start_index,
            answer=answer,
            difficulty=difficulty,
            metadata=metadata,
        )

    return _load_csv_tasks("landmark_search_language.csv", builder)


def _load_landmark_image_tasks() -> list[Task]:
    def builder(row: dict[str, Any], path: Path, row_number: int) -> Task:
        task_id = _require_int(row, "task_id", path, row_number)
        start_index = _require_int(row, "initial_index", path, row_number)
        landmark = _require_field(row, "landmark_name", path, row_number)
        photo_id = _require_field(row, "photo_id", path, row_number)
        answer = _format_xy(row.get("gt_x"), row.get("gt_y"))
        difficulty = _optional_field(row, "difficulty")
        image_rel = _resolve_image_path(f"benchmark/assets/landmark_images/{photo_id}.png")
        metadata = _build_metadata(
            row,
            path,
            row_number,
            {
                "landmark_name": landmark,
                "photo_id": photo_id,
            },
        )
        return Task(
            id=task_id,
            prompt=LANDMARK_SEARCH_WITH_IMAGE_PROMPT,
            task_type=TaskType.LandmarkSearchWithImage,
            requires_current_location=True,
            additional_task_images=[image_rel] if image_rel else [],
            start_index=start_index,
            answer=answer,
            difficulty=difficulty,
            metadata=metadata,
        )

    return _load_csv_tasks("landmark_search_image.csv", builder)


def _load_language_guided_navigation_tasks() -> list[Task]:
    def builder(row: dict[str, Any], path: Path, row_number: int) -> Task:
        task_id = _require_int(row, "task_id", path, row_number)
        start_index = _require_int(row, "initial_index", path, row_number)
        directions = _require_field(row, "directions", path, row_number)
        answer = _format_xy(row.get("gt_x"), row.get("gt_y"))
        difficulty = _optional_field(row, "difficulty")
        metadata = _build_metadata(row, path, row_number)
        return Task(
            id=task_id,
            prompt=LANGUAGE_GUIDED_NAVIGATION_PROMPT.format(Directions=directions),
            task_type=TaskType.LanguageGuidedNavigation,
            requires_current_location=True,
            additional_task_images=[],
            start_index=start_index,
            answer=answer,
            difficulty=difficulty,
            metadata=metadata,
        )

    return _load_csv_tasks("language_guided_navigation.csv", builder)


def _load_relational_spatial_reasoning_tasks() -> list[Task]:
    def builder(row: dict[str, Any], path: Path, row_number: int) -> Task:
        task_id = _require_int(row, "task_id", path, row_number)
        start_index = _require_int(row, "initial_index", path, row_number)
        landmark = _require_field(row, "landmark_name", path, row_number)
        relation = _require_field(row, "relation", path, row_number)
        answer = _require_field(row, "gt", path, row_number)
        difficulty = _optional_field(row, "difficulty")
        metadata = _build_metadata(
            row,
            path,
            row_number,
            {
                "landmark_name": landmark,
                "relation": relation,
            },
        )
        return Task(
            id=task_id,
            prompt=RELATIONAL_SPATIAL_REASONING_PROMPT.format(
                LandmarkName=landmark,
                Relation=relation,
            ),
            task_type=TaskType.RelationalSpatialReasoning,
            requires_current_location=True,
            additional_task_images=[],
            start_index=start_index,
            answer=answer,
            difficulty=difficulty,
            metadata=metadata,
        )

    return _load_csv_tasks("relational_spatial_reasoning.csv", builder)


def _load_map_navigation_tasks() -> list[Task]:
    def builder(row: dict[str, Any], path: Path, row_number: int) -> Task:
        task_id = _require_int(row, "task_id", path, row_number)
        start_index = _require_int(row, "initial_index", path, row_number)
        answer = _format_xy(row.get("gt_x"), row.get("gt_y"))
        map_id = _optional_field(row, "map_id")
        image_rel = (
            _resolve_image_path(f"benchmark/assets/navigation_maps/{map_id}.png")
            if map_id
            else None
        )
        difficulty = _optional_field(row, "dificulty") or _optional_field(
            row, "difficulty"
        )
        metadata = _build_metadata(
            row,
            path,
            row_number,
            {"map_id": map_id} if map_id else None,
        )
        if difficulty:
            metadata["difficulty"] = difficulty
        return Task(
            id=task_id,
            prompt=MAP_NAVIGATION_PROMPT,
            task_type=TaskType.MapNavigation,
            requires_current_location=True,
            additional_task_images=[image_rel] if image_rel else [],
            start_index=start_index,
            answer=answer,
            difficulty=difficulty,
            metadata=metadata,
        )

    return _load_csv_tasks("map_navigation.csv", builder)


def _load_counting_tasks() -> list[Task]:
    def builder(row: dict[str, Any], path: Path, row_number: int) -> Task:
        task_id = _require_int(row, "task_id", path, row_number)
        start_index = _require_int(row, "initial_index", path, row_number)
        range_text = _require_field(row, "range", path, row_number)
        object_text = _require_field(row, "object", path, row_number)
        answer = _require_field(row, "gt", path, row_number)
        difficulty = _optional_field(row, "difficulty")
        metadata = _build_metadata(
            row,
            path,
            row_number,
            {
                "range": range_text,
                "object": object_text,
            },
        )
        return Task(
            id=task_id,
            prompt=COUNTING_PROMPT.format(Object=object_text, Range=range_text),
            task_type=TaskType.Counting,
            requires_current_location=True,
            additional_task_images=[],
            start_index=start_index,
            answer=answer,
            difficulty=difficulty,
            metadata=metadata,
        )

    return _load_csv_tasks("counting.csv", builder)


def _deduplicate_tasks(tasks: list[Task]) -> list[Task]:
    unique: list[Task] = []
    seen: set[int] = set()
    for task in tasks:
        if task.id in seen:
            raise ValueError(f"Duplicate task_id {task.id} encountered in CSV catalog.")
        unique.append(task)
        seen.add(task.id)
    return unique


def _load_all_tasks_from_csv() -> list[Task]:
    tasks: list[Task] = []
    tasks.extend(_load_localization_tasks())
    tasks.extend(_load_landmark_language_tasks())
    tasks.extend(_load_landmark_image_tasks())
    tasks.extend(_load_language_guided_navigation_tasks())
    tasks.extend(_load_relational_spatial_reasoning_tasks())
    tasks.extend(_load_map_navigation_tasks())
    tasks.extend(_load_counting_tasks())
    return tasks


@lru_cache(maxsize=1)
def load_tasks_from_csv() -> tuple[Task, ...]:
    tasks = _deduplicate_tasks(_load_all_tasks_from_csv())
    if not tasks:
        raise ValueError(f"No tasks loaded from CSV files under {_TASKS_DIR}")
    return tuple(tasks)
