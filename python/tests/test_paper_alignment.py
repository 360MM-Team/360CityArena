from cityarena.prompts.system import SYSTEM_PROMPTS
from cityarena.runner.agent_server import AgentServer
from cityarena.runner.key_action_generator import KeyActionGenerator
from cityarena.tasks.catalog import get_task_by_id
from cityarena.tasks.types import Task, TaskType
from cityarena.tasks.validators import LLM_VALIDATION_PROMPT, validate_counting_answer


def test_localization_prompt_expands_step_limit_and_omits_location_map():
    task = get_task_by_id(1001)

    assert task is not None
    assert "within 50 steps" in task.prompt
    assert "{LOCALIZATION_STEP_LIMIT}" not in task.prompt
    assert task.requires_current_location is False


def test_landmark_language_includes_location_map():
    task = get_task_by_id(2001)

    assert task is not None
    assert task.requires_current_location is True


def test_object_count_includes_location_map():
    task = get_task_by_id(4001)

    assert task is not None
    assert task.requires_current_location is True


def test_prompt_wording_matches_paper():
    assert "Another example of changing direction:" in SYSTEM_PROMPTS
    assert "Another example of right clicking:" not in SYSTEM_PROMPTS
    assert "word-for-word - If" in LLM_VALIDATION_PROMPT
    assert "word-for-word — If" not in LLM_VALIDATION_PROMPT


def test_counting_uses_mean_relative_accuracy():
    exact = validate_counting_answer("10", "10")
    partial = validate_counting_answer("8", "10")
    invalid = validate_counting_answer("eight", "10")

    assert exact["metric"] == "mean_relative_accuracy"
    assert exact["score"] == 1.0
    assert exact["is_correct"] is True
    assert partial["score"] == 0.6
    assert partial["is_correct"] is False
    assert invalid["score"] == 0.0


class _FakeClient:
    last_response_metadata = {}

    def __init__(self, response: str):
        self.response = response

    def generate_response(self, **_kwargs):
        return self.response


def _memory_generator(response: str) -> KeyActionGenerator:
    generator = KeyActionGenerator.__new__(KeyActionGenerator)
    generator.current_task = Task(
        id=1,
        prompt="test",
        task_type=TaskType.Localization,
    )
    generator.save_debug_artifacts = False
    generator.reflection_memory = "existing memory"
    generator.context_history = []
    generator.client = _FakeClient(response)
    generator.timeout = 1
    generator.max_tokens = 64
    generator.temperature = None
    generator.validation_model = None
    generator.validation_provider = None
    generator.validation_pretrained = None
    return generator


def test_empty_memory_keeps_previous_reflection():
    generator = _memory_generator(
        '{"thought":"","action":"S","memory":"","answer":""}'
    )

    result = generator.generate_key_action_from_base64("camera")

    assert result["success"] is True
    assert result["reflection"] == "existing memory"
    assert generator.reflection_memory == "existing memory"


def test_nonempty_memory_replaces_previous_reflection():
    generator = _memory_generator(
        '{"thought":"","action":"S","memory":"updated memory","answer":""}'
    )

    result = generator.generate_key_action_from_base64("camera")

    assert result["success"] is True
    assert result["reflection"] == "updated memory"


def _step_limit_server():
    server = AgentServer.__new__(AgentServer)
    server.step_count = 50
    server.shutdown_requested = False
    server.task_started_at = None
    server._stop_reason_detail = ""
    server._get_step_limit = lambda: 50
    server._current_task_type_name = lambda: "Localization"
    statuses = []
    server._append_summary = lambda status, **_kwargs: statuses.append(status)
    return server, statuses


def test_answer_on_step_50_is_evaluated_before_step_limit():
    server, statuses = _step_limit_server()
    result = {"success": True, "action": "ANSWER", "validation": {}}

    returned = server.early_stopping(result, {}, ("127.0.0.1", 1))

    assert returned is result
    assert statuses == ["ANSWER"]


def test_nonanswer_on_step_50_stops_at_limit():
    server, statuses = _step_limit_server()
    result = {"success": True, "action": "W"}

    returned = server.early_stopping(result, {}, ("127.0.0.1", 1))

    assert returned["action"] == "ANSWER"
    assert returned["thought"] == "Terminating due to step limit reached."
    assert statuses == ["STEP_LIMIT"]
