# Python Benchmark Runner

This directory contains the `cityarena` Python package, MLLM clients, JSON-lines agent server, and sequence runner used by 360CityArena.

## Setup

```bash
cd python
uv sync
```

The project uses `uv` and `pyproject.toml`. Do not commit local virtual environments or generated outputs.

## Package Layout

- `src/cityarena/tasks/catalog.py`: task dataclasses, CSV loading, answer validation.
- `src/cityarena/runner/`: agent server, sequence runner, Unity controller.
- `src/cityarena/models/llm_client.py`: provider clients and OpenAI-compatible adapter.
- `src/cityarena/prompts/system.py`: system and reflection prompts.
- `models/`: provider-specific shell wrappers.

## Task Catalog

Tasks are loaded from `../benchmark/tasks/*.csv`. Referenced images live under `../benchmark/assets/`.

Useful checks:

```bash
uv run python -c "from cityarena.tasks import catalog; print(len(catalog.TASKS))"
PYTHONPATH=src uv run python -m cityarena run --help
```

## Running

One task:

```bash
OPENAI_API_KEY=... MODEL_NAME=gpt-5 ./models/openai.sh --task-id 5001
```

All tasks:

```bash
OPENAI_API_KEY=... MODEL_NAME=gpt-5 ./models/openai.sh --all
```

Direct runner invocation:

```bash
PYTHONPATH=src uv run python -m cityarena run \
  --all \
  --model gpt-5 \
  --provider openai \
  --experiment-id openai-gpt-5-public-run \
  --output-root ../outputs
```

Relative `--output-root` values are resolved from the repository root. The default output directory is `../outputs/`.
`--experiment-id` is recorded as metadata; the default `run_id` is timestamped so separate executions do not share a result directory. Use `--run-id` only when you need a specific directory name, and use `--append-results` only for intentional appends/resumes.

## Output Policy

Default outputs are intentionally lightweight:

- `outputs/<run_id>/results.csv`
- `outputs/<run_id>/run_metadata.json`

Use `--save-debug-artifacts` only when debugging. With that flag, the runner can also save per-task `steps.jsonl`, logs, images, `memo.txt`, and `context.jsonl` under `outputs/<run_id>/debug/`.

Existing `Experiment/`, `PythonLogs/`, and `Logs/` contents are not part of the public benchmark release.

## Logging

Default `INFO` logs are intended for normal benchmark operation: run start/end,
task start/end, warnings/errors, and one progress line every 10 Unity `act`
requests. Per-step observations and LLM request timing are available with
`--log-level DEBUG`; full per-step details are written to `steps.jsonl` only when
`--save-debug-artifacts` is enabled.

Use `PROGRESS_LOG_INTERVAL=0` or `--progress-log-interval 0` to disable periodic
progress lines. External HTTP client request logs are suppressed below `WARNING`
by default.

Unity console logs are also quiet by default for benchmark runs. Keep
`PythonAIInputClient.verboseLog` and `logLocationInfo` disabled during normal
runs; `verboseLog` prints per-action input injection, wait-loop, response, and
map-capture details and is intended only for simulator debugging.

## Model Configuration

Common environment variables:

- `MODEL_NAME`: provider model name used by wrapper scripts.
- `LLM_MODEL`: fallback model name for direct runner invocation.
- `PROVIDER` / `LLM_PROVIDER`: provider key such as `openai`, `azure-openai`, `anthropic`, `gemini`, or `openai-compatible`.
- `LLM_MAX_TOKENS`: maximum output tokens requested from the model.
- `LLM_TEMPERATURE`: optional sampling temperature.
- `LLM_VALIDATION_MODEL`: required when running task types that use LLM-based validation.
- `LLM_VALIDATION_PROVIDER`: provider for `LLM_VALIDATION_MODEL`.
- `LLM_TIMEOUT`: API timeout in seconds.
- `LLM_API_BASE`: OpenAI-compatible endpoint base URL.
- `LLM_API_KEY`: API key for OpenAI-compatible endpoints.
- `LLM_PRETRAINED`: model/deployment identifier for OpenAI-compatible endpoints.
- `LLM_EXTRA_HEADERS`: JSON object for additional HTTP headers.

Provider-specific keys:

- OpenAI: `OPENAI_API_KEY`
- Anthropic: `ANTHROPIC_API_KEY`
- Gemini: `GOOGLE_API_KEY`, `GEMINI_API_KEY`, or `GOOGLE_GENAI_API_KEY`
- Azure OpenAI: `AZURE_OPENAI_API_KEY`, `AZURE_OPENAI_ENDPOINT`, `AZURE_OPENAI_DEPLOYMENT`, `AZURE_OPENAI_API_VERSION`

Azure example:

```bash
export AZURE_OPENAI_ENDPOINT="https://{resource}.openai.azure.com"
export AZURE_OPENAI_DEPLOYMENT="gpt-5"
export AZURE_OPENAI_API_VERSION="2025-01-01-preview"
export AZURE_OPENAI_API_KEY="..."
./models/azure-openai.sh --task-id 5001
```

## Checks

```bash
PYTHONPATH=src uv run python -m cityarena run --help
PYTHONPATH=src uv run python -c "from cityarena.tasks import catalog; print(len(catalog.TASKS))"
```
