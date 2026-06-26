from __future__ import annotations

from typing import Any, Optional

from cityarena.tasks.loaders import load_tasks_from_csv
from cityarena.tasks.prompt_templates import LOCALIZATION_STEP_LIMIT
from cityarena.tasks.types import Task, TaskType

__all__ = [
    "LOCALIZATION_STEP_LIMIT",
    "TASKS",
    "Task",
    "TaskType",
    "get_task_by_id",
    "get_tasks_by_type",
    "iter_all_tasks",
    "load_tasks_from_csv",
]


TASKS: tuple[Task, ...] = load_tasks_from_csv()
_TASK_INDEX: dict[int, Task] = {task.id: task for task in TASKS}


def iter_all_tasks() -> list[Task]:
    return [task.clone() for task in TASKS]


def get_task_by_id(task_id: Any) -> Optional[Task]:
    try:
        key = int(task_id)
    except (TypeError, ValueError):
        return None
    base = _TASK_INDEX.get(key)
    return base.clone() if base else None


def get_tasks_by_type(task_type: TaskType) -> list[Task]:
    return [task.clone() for task in TASKS if task.task_type == task_type]
