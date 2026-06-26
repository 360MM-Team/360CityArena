# Results Schema

Runtime outputs are written under `outputs/<run_id>/`.

## `run_metadata.json`

| Field | Description |
| --- | --- |
| `benchmark_name` | Benchmark name, currently `360CityArena`. |
| `result_schema_version` | Result schema version. Current value is `3`. |
| `run_id` | Output directory name for this run. |
| `experiment_id` | User-provided experiment identifier, if any. |
| `model` | Model name passed to the runner. |
| `provider` | Explicit provider key passed to the runner. |
| `pretrained` | Provider-specific deployment/pretrained identifier, if any. |
| `max_tokens` | Maximum output tokens requested from the model. |
| `temperature` | Sampling temperature, if explicitly set. |
| `validation_model` | LLM judge model, if LLM-based validation is used. |
| `validation_provider` | Provider for the LLM judge model, if used. |
| `validation_pretrained` | Provider-specific judge deployment/pretrained identifier, if any. |
| `task_catalog_hash` | SHA-256 hash over public task CSV files. |
| `prompt_hash` | SHA-256 hash over prompt and validation template files. |
| `python_lock_hash` | SHA-256 hash of `python/uv.lock`, when available. |
| `unity_version` | Unity editor version recorded in `ProjectVersion.txt`, when available. |
| `git_commit` | Git commit hash for the working tree, when available. |
| `git_dirty` | Whether the working tree had uncommitted changes when metadata was written. |
| `step_limit_default` | Default step limit used by the agent server. |
| `location_epsilon` | Movement threshold for stagnation detection. |
| `location_stagnant_limit` | Consecutive unchanged-position limit for stagnation detection. |
| `goal_away_consecutive_limit` | Consecutive away-from-goal limit for map-navigation aborts. |
| `status` | `running`, `completed`, `failed`, or `interrupted`. |
| `started_at` | Local start timestamp. |
| `ended_at` | Local end timestamp, set when the runner exits. |
| `task_count` | Number of scheduled tasks. |
| `task_ids` | Scheduled task IDs in execution order. |
| `output_root` | Output root directory name. |
| `results_file` | Relative results CSV path. |
| `save_debug_artifacts` | Whether debug artifacts were enabled. |
| `append_results` | Whether appending to an existing `results.csv` was explicitly enabled. |

## `results.csv`

| Column | Description |
| --- | --- |
| `timestamp` | Local timestamp when the task summary was written. |
| `run_id` | Run identifier. |
| `experiment_id` | User-provided experiment identifier, if any. |
| `task_id` | Benchmark task ID. |
| `task_type` | Internal task type enum name. |
| `difficulty` | Normalized task difficulty. |
| `map_id` | Map reference ID, if the task uses one. |
| `landmark` | Landmark name, if the task uses one. |
| `model` | Model name passed to the runner. |
| `provider` | Explicit provider key passed to the runner. |
| `pretrained` | Provider-specific deployment/pretrained identifier, if any. |
| `max_tokens` | Maximum output tokens requested from the model. |
| `temperature` | Sampling temperature, if explicitly set. |
| `validation_model` | LLM judge model, if LLM-based validation is used. |
| `validation_provider` | Provider for the LLM judge model, if used. |
| `validation_pretrained` | Provider-specific judge deployment/pretrained identifier, if any. |
| `status` | Final task status, such as `ANSWER`, `STEP_LIMIT`, `STAGNATION`, `NO_ACTIVITY`, or `IDLE_TIMEOUT`. |
| `scored_success` | Binary success diagnostic. This is `true` only when the task ended with `ANSWER` and binary validation was correct. Object Count can receive partial `evaluation_score` credit while this remains `false`. |
| `is_correct` | Validator result for the final answer or final position when available. For non-`ANSWER` location tasks, this may report final-position correctness while `scored_success` remains `false`. |
| `validation_available` | Whether a validator result was produced. |
| `completed_by_answer` | Whether the task ended from the model's `ANSWER` action. |
| `evaluation_metric` | Metric used for the task: `exact_match`, `coordinate_match`, `fuzzy_match`, or `mean_relative_accuracy`. |
| `evaluation_score` | Official per-task score in `[0, 1]`. Object Count uses MRA; the other task families use binary `0` or `1`. Non-`ANSWER` terminations score `0`. |
| `steps` | Number of action requests processed for the task. |
| `act_requests` | Number of Unity `act` requests received, including failed model actions. |
| `elapsed_seconds` | Task elapsed time in seconds. |
| `final_action` | Last model action token observed before termination. |
| `final_position_x` | Last reported Unity x position. |
| `final_position_z` | Last reported Unity z position. |
| `final_segment_path` | Last reported Unity segment path. |
| `user_answer` | Model answer or final coordinate, when available. |
| `expected` | Ground-truth answer or target, when available. |
| `distance_to_goal` | Final Euclidean distance to target for location-validated tasks. |
| `validation_threshold` | Distance threshold used by the location validator. |
| `message` | Validator message. |
| `stop_reason_detail` | Machine-readable detail for timeout, step limit, stagnation, or away-from-goal stops. |
| `llm_request_count` | Number of model requests made for this task. |
| `llm_error_count` | Number of model API errors. |
| `parse_error_count` | Number of responses that could not be parsed as a JSON object. |
| `invalid_action_count` | Number of parsed responses with an invalid action token. |
| `request_error_count` | Number of non-LLM request/server errors. |
| `total_prompt_tokens` | Sum of provider-reported prompt/input tokens, when available. |
| `total_completion_tokens` | Sum of provider-reported completion/output tokens, when available. |
| `total_tokens` | Sum of provider-reported total tokens, when available. |
| `total_llm_latency_seconds` | Sum of measured model request latency. |
| `avg_llm_latency_seconds` | Mean measured model request latency. |
| `last_finish_reason` | Provider finish/stop reason for the last model response, when available. |
| `last_response_model` | Provider-reported response model/deployment for the last model response, when available. |
| `last_error_type` | Last machine-readable error type. |
| `last_error_message` | Last error message. |
| `artifact_dir` | Relative debug artifact directory. Empty unless `--save-debug-artifacts` is enabled. |

The CSV must not contain API keys, prompt bodies, image base64 payloads, or absolute filesystem paths.

## Debug Artifacts

When `--save-debug-artifacts` is enabled, per-task debug files are written under
`outputs/<run_id>/debug/task_<task_id>/`. These files are not part of the public
result schema.

| File | Description |
| --- | --- |
| `steps.jsonl` | One JSON object per Unity `act` request. Includes position, action, parsed response, raw model response, provider metadata, latency, and errors. It intentionally omits image base64 payloads. |
| `context.jsonl` | Lightweight assistant action/thought history. |
| `memo.txt` | Latest reflection memory. |
| `images/` | Captured camera/map images, when image saving succeeds. |
