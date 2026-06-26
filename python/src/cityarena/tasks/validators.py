from __future__ import annotations

import os
import re
from decimal import Decimal, InvalidOperation
from typing import Optional, TYPE_CHECKING

from cityarena.tasks.types import TaskType

if TYPE_CHECKING:
    from cityarena.tasks.types import Task


LLM_VALIDATION_PROMPT = """You are a helpful assistant.
You are given a user answer and an expected answer.
Please determine if the user answer is correct.

The answers do not have to match exactly word-for-word - If you determine that different names refer to the same landmark, return True."""


def validate_task_answer(
    task: "Task",
    user_answer: str = "",
    position_x: Optional[float] = None,
    position_z: Optional[float] = None,
    validation_model: Optional[str] = None,
    validation_provider: Optional[str] = None,
    validation_pretrained: Optional[str] = None,
) -> dict:
    """
    Validate whether the task answer is correct.
    """
    expected_answer = task.answer.strip() if task.answer else ""
    user_answer_cleaned = user_answer.strip()

    if task.task_type == TaskType.Localization:
        return validate_localization_grid_answer(user_answer_cleaned, expected_answer)
    if task.task_type == TaskType.MapNavigation:
        return validate_location_answer(
            expected_answer,
            position_x,
            position_z,
            epsilon=20,
        )
    if task.task_type in [
        TaskType.LandmarkSearchWithLanguage,
        TaskType.LandmarkSearchWithImage,
        TaskType.LanguageGuidedNavigation,
    ]:
        return validate_location_answer(
            expected_answer,
            position_x,
            position_z,
            epsilon=10,
        )
    if task.task_type == TaskType.Counting:
        return validate_counting_answer(user_answer_cleaned, expected_answer)
    if task.task_type == TaskType.RelationalSpatialReasoning:
        return validate_with_llm(
            user_answer_cleaned,
            expected_answer,
            validation_model=validation_model,
            validation_provider=validation_provider,
            validation_pretrained=validation_pretrained,
        )

    return {
        "is_correct": False,
        "metric": "unsupported",
        "score": 0.0,
        "expected": expected_answer,
        "user_answer": user_answer_cleaned,
        "message": f"Validation not implemented for {task.task_type.value}",
    }


def validate_localization_grid_answer(user_answer: str, expected_answer: str) -> dict:
    """
    Validate the answer for a Localization task.
    """
    pattern = r"x\s*:\s*(\d+)\s+y\s*:\s*(\d+)"

    user_match = re.search(pattern, user_answer.lower())
    expected_match = re.search(pattern, expected_answer.lower())

    if not user_match:
        return {
            "is_correct": False,
            "metric": "exact_match",
            "score": 0.0,
            "expected": expected_answer,
            "user_answer": user_answer,
            "message": f"Invalid answer format. Expected format: 'x:[number] y:[number]', got: '{user_answer}'",
        }

    if not expected_match:
        return {
            "is_correct": False,
            "metric": "exact_match",
            "score": 0.0,
            "expected": expected_answer,
            "user_answer": user_answer,
            "message": "Error: Expected answer format is invalid",
        }

    user_x, user_y = int(user_match.group(1)), int(user_match.group(2))
    expected_x, expected_y = (
        int(expected_match.group(1)),
        int(expected_match.group(2)),
    )

    is_correct = (user_x == expected_x) and (user_y == expected_y)

    if is_correct:
        message = f"Correct! The answer is x:{expected_x} y:{expected_y}"
    else:
        message = f"Incorrect. Expected x:{expected_x} y:{expected_y}, but got x:{user_x} y:{user_y}"

    return {
        "is_correct": is_correct,
        "metric": "exact_match",
        "score": 1.0 if is_correct else 0.0,
        "expected": expected_answer,
        "user_answer": user_answer,
        "message": message,
    }


def validate_location_answer(
    expected_answer: str,
    position_x: Optional[float] = None,
    position_z: Optional[float] = None,
    epsilon: float = 10,
) -> dict:
    """
    Validate an answer based on final coordinates.
    """
    if position_x is None or position_z is None:
        return {
            "is_correct": False,
            "metric": "coordinate_match",
            "score": 0.0,
            "expected": expected_answer,
            "user_answer": "",
            "message": "Position information is required for Map Navigation task",
        }

    expected_parts = expected_answer.split("x:")[1].split("y:")
    expected_x = float(expected_parts[0].strip())
    expected_y = float(expected_parts[1].strip())

    distance = ((position_x - expected_x) ** 2 + (position_z - expected_y) ** 2) ** 0.5
    is_correct = distance < epsilon

    if is_correct:
        message = f"Correct! Goal reached at ({position_x:.2f}, {position_z:.2f}). Expected: ({expected_x:.2f}, {expected_y:.2f}). Distance: {distance:.2f}"
    else:
        message = f"Incorrect. Goal at ({expected_x:.2f}, {expected_y:.2f}), but you are at ({position_x:.2f}, {position_z:.2f}). Distance: {distance:.2f} (threshold: {epsilon})"

    return {
        "is_correct": is_correct,
        "metric": "coordinate_match",
        "score": 1.0 if is_correct else 0.0,
        "expected": expected_answer,
        "user_answer": f"{position_x:.2f} {position_z:.2f}",
        "message": message,
        "distance": distance,
        "threshold": epsilon,
    }


def validate_counting_answer(user_answer: str, expected_answer: str) -> dict:
    """
    Validate a Counting answer with mean relative accuracy (MRA).

    The paper defines MRA over C = {0.50, 0.55, ..., 0.95} using a strict
    relative-error inequality. ``is_correct`` remains an exact numeric match for
    backward compatibility, while ``score`` is the official per-task MRA value.
    """
    try:
        predicted = Decimal(user_answer.strip())
        expected = Decimal(expected_answer.strip())
        if not predicted.is_finite() or not expected.is_finite():
            raise ValueError("count answers must be finite")
    except (InvalidOperation, ValueError):
        return {
            "is_correct": False,
            "metric": "mean_relative_accuracy",
            "score": 0.0,
            "expected": expected_answer,
            "user_answer": user_answer,
            "message": (
                f"Invalid numeric answer. Expected {expected_answer}, but got "
                f"{user_answer}. MRA: 0.0"
            ),
        }

    is_correct = predicted == expected
    if expected == 0:
        # Relative error is undefined at zero; exact agreement is the only
        # meaningful successful outcome.
        relative_error = Decimal(0) if is_correct else Decimal("Infinity")
        score = 1.0 if is_correct else 0.0
    else:
        relative_error = abs(predicted - expected) / abs(expected)
        thresholds = (Decimal(value) / Decimal(100) for value in range(50, 100, 5))
        score = sum(relative_error < (Decimal(1) - theta) for theta in thresholds) / 10

    if is_correct:
        message = f"Correct! The answer is {expected_answer}. MRA: {score:.1f}"
    else:
        message = (
            f"Expected {expected_answer}, but got {user_answer}. "
            f"Relative error: {relative_error}. MRA: {score:.1f}"
        )

    return {
        "is_correct": is_correct,
        "metric": "mean_relative_accuracy",
        "score": score,
        "expected": expected_answer,
        "user_answer": user_answer,
        "message": message,
        "relative_error": (
            float(relative_error) if relative_error.is_finite() else None
        ),
    }


def validate_with_llm(
    user_answer: str,
    expected_answer: str,
    validation_model: Optional[str] = None,
    validation_provider: Optional[str] = None,
    validation_pretrained: Optional[str] = None,
) -> dict:
    """
    Validate an answer using an LLM judge.
    """
    validation_model = validation_model or os.getenv("LLM_VALIDATION_MODEL")
    validation_provider = validation_provider or os.getenv("LLM_VALIDATION_PROVIDER")
    validation_pretrained = (
        validation_pretrained or os.getenv("LLM_VALIDATION_PRETRAINED")
    )
    if not validation_model:
        raise RuntimeError("LLM validation model is required for this task type")
    if not validation_provider:
        raise RuntimeError("LLM validation provider is required for this task type")
    try:
        from cityarena.models.llm_client import build_llm_client, LLMClientError
    except ImportError as exc:
        raise RuntimeError(f"validation LLM dependencies are unavailable: {exc}") from exc

    try:
        client = build_llm_client(
            model=validation_model,
            api_base=os.getenv("LLM_API_BASE"),
            pretrained=validation_pretrained,
            provider_hint=validation_provider,
        )
        content = client.generate_response(
            system_prompt=LLM_VALIDATION_PROMPT,
            context_messages=[
                {
                    "role": "user",
                    "content": f"User answer: {user_answer}\nExpected answer: {expected_answer}",
                }
            ],
            user_content=[
                {
                    "type": "text",
                    "text": "Output the answer in the following format: is_correct: True/False",
                }
            ],
            context_text="",
            timeout=int(os.getenv("LLM_TIMEOUT", "150")),
            max_tokens=64,
        )
    except LLMClientError as exc:
        raise RuntimeError(f"validation LLM request failed: {exc}") from exc

    match = re.search(r"\bis_correct\s*:\s*(true|false)\b", content, re.IGNORECASE)
    if not match:
        raise RuntimeError(f"validation LLM returned an invalid verdict: {content!r}")
    is_correct = match.group(1).lower() == "true"

    if is_correct:
        message = f"Correct! The answer is {expected_answer}"
    else:
        message = f"Incorrect. Expected {expected_answer}, but got {user_answer}. The answer is {content}"

    return {
        "is_correct": is_correct,
        "metric": "fuzzy_match",
        "score": 1.0 if is_correct else 0.0,
        "expected": expected_answer,
        "user_answer": user_answer,
        "message": message,
    }
