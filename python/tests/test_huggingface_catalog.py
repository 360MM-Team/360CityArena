import json
from collections import Counter
from datetime import datetime

from cityarena.runner.sequence_runner import build_argument_parser, _write_run_metadata
from cityarena.tasks.catalog import TASKS, get_dataset_source, get_task_by_id


def test_pinned_huggingface_inventory():
    source = get_dataset_source()
    counts = Counter(task.task_type.value for task in TASKS)

    assert source.repo_id == "hal-utokyo/360CityArena"
    assert len(source.revision) == 40
    assert len(TASKS) == 175
    assert len(counts) == 7
    assert all(count == 25 for count in counts.values())
    assert sum(bool(task.additional_task_images) for task in TASKS) == 75


def test_corrected_task_7006_is_loaded_from_hub():
    task = get_task_by_id(7006)

    assert task is not None
    assert task.start_index == 70
    assert task.answer == "x:212.68 y:408.86"
    assert task.difficulty == "Easy"
    assert task.metadata["dataset_repo"] == "hal-utokyo/360CityArena"
    assert task.metadata["directions"] == (
        "1. Go straight.\n"
        "2. Turn right at the second intersection.\n"
        "3. Stop when you see Mister Donut on your right."
    )


def test_run_metadata_records_pinned_dataset_source(tmp_path):
    task = get_task_by_id(7006)
    assert task is not None

    args = build_argument_parser().parse_args(
        ["--model", "gpt-5", "--provider", "openai", "--task-id", "7006"]
    )
    args.run_id = "hf-source-test"
    args.output_root = str(tmp_path)
    now = datetime.now()
    _write_run_metadata(tmp_path, args, [task], "completed", now, now)

    metadata = json.loads((tmp_path / "run_metadata.json").read_text(encoding="utf-8"))
    source = get_dataset_source()
    assert metadata["task_catalog_hash"] == source.revision
    assert metadata["dataset_repo"] == source.repo_id
    assert metadata["dataset_config"] == source.config
    assert metadata["dataset_split"] == source.split
    assert metadata["dataset_revision"] == source.revision
