# Benchmark Design

360CityArena evaluates embodied MLLM agents in a Unity-based 360-degree city environment.

The repository separates public benchmark inputs, execution code, and generated outputs:

- `benchmark/` contains versioned public task data and reference assets.
- `unity/` contains the simulator project.
- `python/src/cityarena/` contains runner, model, task catalog, and validation code.
- `outputs/` contains generated run summaries and is ignored by Git.

Task code is split by responsibility:

- `tasks/types.py` defines task data structures and task categories.
- `tasks/prompt_templates.py` contains benchmark task prompt templates.
- `tasks/validators.py` contains answer validation logic.
- `tasks/loaders.py` loads and validates public CSV task definitions.
- `tasks/catalog.py` exposes the stable task lookup API used by runners.

This structure keeps public release artifacts auditable and prevents generated logs, prompts, images, or private experiment outputs from being mixed with the benchmark definition.
