# Evaluation Protocol

1. Install Python dependencies with `uv sync` from `python/`.
2. Open the Unity project from `unity/`.
3. Run tasks through `python/run_task_sequence.sh`, a model wrapper under `python/models/`, or `uv run python -m cityarena run`.
4. Evaluate each run from `outputs/<run_id>/results.csv` and `outputs/<run_id>/run_metadata.json`.

Public summaries must not include API keys, prompt bodies, image base64 payloads, or absolute filesystem paths.

Use `evaluation_score` as the official per-task score. Object Count uses mean
relative accuracy (MRA) over thresholds `{0.50, 0.55, ..., 0.95}`; all other
task families use binary scores. A run score is the mean of
`evaluation_score`, with missing and non-`ANSWER` tasks contributing zero.

`scored_success` and `is_correct` are retained as binary diagnostics.
`is_correct` can be true for final-position checks on non-`ANSWER`
terminations, but `evaluation_score` remains zero because the model did not
explicitly complete the task.

Use `--save-debug-artifacts` only for local debugging. Debug artifacts are not part of the public benchmark result schema.
