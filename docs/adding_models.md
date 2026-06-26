# Adding Models

Model wrappers live under `python/models/`. A wrapper should:

1. Set `MODEL_NAME`.
2. Set `PROVIDER` to the explicit provider key used by the runner.
3. Set provider-specific API environment variables.
4. Set `EFFECTIVE_API_KEY` for the runner.
5. Exec `python/run_task_sequence.sh` with the original arguments.

OpenAI-compatible endpoints should set `LLM_API_BASE`, `LLM_API_KEY`, and `LLM_PRETRAINED`.

Supported provider keys are `openai`, `azure-openai`, `anthropic`, `gemini`, and `openai-compatible`. The runner does not infer providers from model names; this keeps run metadata comparable across providers and deployments.

If a task type uses LLM-based validation, configure the judge separately with `LLM_VALIDATION_MODEL`, `LLM_VALIDATION_PROVIDER`, and, when needed, `LLM_VALIDATION_PRETRAINED`.

Do not hard-code secrets or benchmark model names in wrapper scripts. Use environment variables or local shell configuration, and fail fast when required model/provider settings are missing.
