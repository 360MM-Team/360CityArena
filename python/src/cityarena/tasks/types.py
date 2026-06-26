from __future__ import annotations

from dataclasses import dataclass, field
from enum import Enum
from typing import Any, Optional


class TaskType(Enum):
    Localization = "Localization"
    LandmarkSearchWithLanguage = "Landmark Search with Language"
    LandmarkSearchWithImage = "Landmark Search with Image"
    Counting = "Counting"
    MapNavigation = "Map Navigation"
    ConstraintSatisficationNavigation = "Constraint Satisfaction Navigation"
    LanguageGuidedNavigation = "Language Guided Navigation"
    RelationalSpatialReasoning = "Relational Spatial Reasoning"
    ExplorationToMapMatching = "Exploration-to-Map Matching"


@dataclass
class Task:
    id: int
    prompt: str
    task_type: TaskType
    requires_current_location: bool = False
    additional_task_images: list[str] = field(default_factory=list)
    start_index: Optional[int] = None
    answer: Optional[str] = None
    difficulty: Optional[str] = None
    metadata: dict[str, Any] = field(default_factory=dict)

    def clone(self) -> "Task":
        """
        Return a detached copy of the task so that per-run state can mutate without
        affecting the shared catalog.
        """
        return Task(
            id=self.id,
            prompt=self.prompt,
            task_type=self.task_type,
            requires_current_location=self.requires_current_location,
            additional_task_images=list(self.additional_task_images),
            start_index=self.start_index,
            answer=self.answer,
            difficulty=self.difficulty,
            metadata=dict(self.metadata),
        )

    def validate_answer(
        self,
        user_answer: str = "",
        position_x: Optional[float] = None,
        position_z: Optional[float] = None,
        validation_model: Optional[str] = None,
        validation_provider: Optional[str] = None,
        validation_pretrained: Optional[str] = None,
    ) -> dict:
        from cityarena.tasks.validators import validate_task_answer

        return validate_task_answer(
            self,
            user_answer=user_answer,
            position_x=position_x,
            position_z=position_z,
            validation_model=validation_model,
            validation_provider=validation_provider,
            validation_pretrained=validation_pretrained,
        )
