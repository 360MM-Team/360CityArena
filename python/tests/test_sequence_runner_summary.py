import csv

from cityarena.runner.sequence_runner import _build_benchmark_summary_lines
from cityarena.tasks.types import Task, TaskType


def _task(task_id: int, task_type: TaskType, difficulty: str = "Easy") -> Task:
    return Task(
        id=task_id,
        prompt="test prompt",
        task_type=task_type,
        difficulty=difficulty,
    )


def _write_results(path, rows):
    fieldnames = [
        "run_id",
        "task_id",
        "task_type",
        "difficulty",
        "status",
        "scored_success",
        "is_correct",
        "validation_available",
        "steps",
        "elapsed_seconds",
    ]
    with open(path, "w", newline="", encoding="utf-8") as f:
        writer = csv.DictWriter(f, fieldnames=fieldnames)
        writer.writeheader()
        writer.writerows(rows)


def test_benchmark_summary_counts_official_success_and_missing_rows(tmp_path):
    results_path = tmp_path / "results.csv"
    _write_results(
        results_path,
        [
            {
                "run_id": "run_a",
                "task_id": "1",
                "task_type": "Localization",
                "difficulty": "Easy",
                "status": "ANSWER",
                "scored_success": "true",
                "is_correct": "true",
                "validation_available": "true",
                "steps": "3",
                "elapsed_seconds": "1.25",
            },
            {
                "run_id": "run_a",
                "task_id": "2",
                "task_type": "Counting",
                "difficulty": "Hard",
                "status": "STEP_LIMIT",
                "scored_success": "false",
                "is_correct": "true",
                "validation_available": "true",
                "steps": "300",
                "elapsed_seconds": "45.00",
            },
            {
                "run_id": "other_run",
                "task_id": "3",
                "task_type": "Counting",
                "difficulty": "Easy",
                "status": "ANSWER",
                "scored_success": "true",
                "is_correct": "true",
                "validation_available": "true",
                "steps": "1",
                "elapsed_seconds": "1.00",
            },
        ],
    )
    tasks = [
        _task(1, TaskType.Localization),
        _task(2, TaskType.Counting, "Hard"),
        _task(3, TaskType.MapNavigation),
    ]

    summary = "\n".join(_build_benchmark_summary_lines(results_path, tasks, "run_a"))

    assert "official_score=33.33% binary_success=1/3" in summary
    assert "result_rows=2/3 csv_rows_for_run=2" in summary
    assert "answer_completions=1/3 validator_correct=2/2" in summary
    assert "missing_result_rows=3" in summary
    assert "3        Map Navigation                    Easy        MISSING" in summary


def test_benchmark_summary_uses_latest_row_for_appended_results(tmp_path):
    results_path = tmp_path / "results.csv"
    _write_results(
        results_path,
        [
            {
                "run_id": "run_a",
                "task_id": "1",
                "task_type": "Localization",
                "difficulty": "Easy",
                "status": "STEP_LIMIT",
                "scored_success": "false",
                "is_correct": "false",
                "validation_available": "true",
                "steps": "300",
                "elapsed_seconds": "30.00",
            },
            {
                "run_id": "run_a",
                "task_id": "1",
                "task_type": "Localization",
                "difficulty": "Easy",
                "status": "ANSWER",
                "scored_success": "true",
                "is_correct": "true",
                "validation_available": "true",
                "steps": "4",
                "elapsed_seconds": "2.00",
            },
        ],
    )
    tasks = [_task(1, TaskType.Localization)]

    summary = "\n".join(_build_benchmark_summary_lines(results_path, tasks, "run_a"))

    assert "official_score=100.00% binary_success=1/1" in summary
    assert "result_rows=1/1 csv_rows_for_run=2" in summary
    assert "ANSWER" in summary


def test_benchmark_summary_uses_fractional_evaluation_score(tmp_path):
    results_path = tmp_path / "results.csv"
    rows = [
        {
            "run_id": "run_a",
            "task_id": "1",
            "task_type": "Counting",
            "difficulty": "Easy",
            "status": "ANSWER",
            "scored_success": "false",
            "is_correct": "false",
            "validation_available": "true",
            "evaluation_score": "0.6",
            "steps": "3",
            "elapsed_seconds": "1.25",
        }
    ]
    fieldnames = list(rows[0])
    with open(results_path, "w", newline="", encoding="utf-8") as f:
        writer = csv.DictWriter(f, fieldnames=fieldnames)
        writer.writeheader()
        writer.writerows(rows)

    summary = "\n".join(
        _build_benchmark_summary_lines(
            results_path,
            [_task(1, TaskType.Counting)],
            "run_a",
        )
    )

    assert "official_score=60.00% binary_success=0/1" in summary
    assert "Counting: score=60.00%, binary_success=0/1" in summary
