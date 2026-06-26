#!/usr/bin/env bash
set -euo pipefail
shopt -s extglob

# Determine important paths
SCRIPT_DIR="$(cd -- "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
PROJECT_ROOT="$(cd -- "${SCRIPT_DIR}/.." && pwd)"
#
# Unity configuration (override via env if needed)
UNITY_EDITOR_PATH="${UNITY_EDITOR_PATH:-/Applications/Unity/Hub/Editor/6000.3.4f1/Unity.app/Contents/MacOS/Unity}"
UNITY_PROJECT_PATH="${UNITY_PROJECT_PATH:-${PROJECT_ROOT}/unity}"
UNITY_URL="${UNITY_URL:-http://127.0.0.1:5005}"

# Runner / agent configuration (override via env if needed)
MODEL_NAME="${MODEL_NAME:-${LLM_MODEL:-}}"
PROVIDER="${PROVIDER:-${LLM_PROVIDER:-}}"
EFFECTIVE_API_KEY="${EFFECTIVE_API_KEY:-${OPENAI_API_KEY:-${LLM_API_KEY:-}}}"
MAX_CONSECUTIVE_ERRORS="${MAX_CONSECUTIVE_ERRORS:-3}"

TASK_ARGS=("$@")

# Optional extra args
MODEL_ARGS=()
if [[ -n "${MODEL_NAME:-}" ]]; then
  MODEL_ARGS+=(--model "${MODEL_NAME}")
fi
if [[ -n "${PROVIDER:-}" ]]; then
  MODEL_ARGS+=(--provider "${PROVIDER}")
fi
EXTRA_ARGS=()
if [[ -n "${EXPERIMENT_ID:-}" ]]; then
  EXTRA_ARGS+=(--experiment-id "${EXPERIMENT_ID}")
fi
if [[ -n "${RUNNER_LOG_FILE:-}" ]]; then
  EXTRA_ARGS+=(--runner-log-file "${RUNNER_LOG_FILE}")
fi
if [[ -n "${OUTPUT_ROOT:-}" ]]; then
  EXTRA_ARGS+=(--output-root "${OUTPUT_ROOT}")
fi
if [[ -n "${RUN_ID:-}" ]]; then
  EXTRA_ARGS+=(--run-id "${RUN_ID}")
fi
if [[ "${SAVE_DEBUG_ARTIFACTS:-0}" == "1" || "${SAVE_DEBUG_ARTIFACTS:-0}" == "true" || "${SAVE_DEBUG_ARTIFACTS:-0}" == "TRUE" ]]; then
  EXTRA_ARGS+=(--save-debug-artifacts)
fi
if [[ "${APPEND_RESULTS:-0}" == "1" || "${APPEND_RESULTS:-0}" == "true" || "${APPEND_RESULTS:-0}" == "TRUE" ]]; then
  EXTRA_ARGS+=(--append-results)
fi
if [[ -n "${LLM_API_BASE:-}" ]]; then
  EXTRA_ARGS+=(--api-base "${LLM_API_BASE}")
fi
if [[ -n "${LLM_EXTRA_HEADERS:-}" ]]; then
  EXTRA_ARGS+=(--extra-headers "${LLM_EXTRA_HEADERS}")
fi
if [[ -n "${LLM_PRETRAINED:-}" ]]; then
  EXTRA_ARGS+=(--pretrained "${LLM_PRETRAINED}")
fi
if [[ -n "${LLM_MAX_TOKENS:-}" ]]; then
  EXTRA_ARGS+=(--max-tokens "${LLM_MAX_TOKENS}")
fi
if [[ -n "${LLM_TEMPERATURE:-}" ]]; then
  EXTRA_ARGS+=(--temperature "${LLM_TEMPERATURE}")
fi
if [[ -n "${PROGRESS_LOG_INTERVAL:-}" ]]; then
  EXTRA_ARGS+=(--progress-log-interval "${PROGRESS_LOG_INTERVAL}")
fi
if [[ -n "${LLM_VALIDATION_MODEL:-}" ]]; then
  EXTRA_ARGS+=(--validation-model "${LLM_VALIDATION_MODEL}")
fi
if [[ -n "${LLM_VALIDATION_PROVIDER:-}" ]]; then
  EXTRA_ARGS+=(--validation-provider "${LLM_VALIDATION_PROVIDER}")
fi
if [[ -n "${LLM_VALIDATION_PRETRAINED:-}" ]]; then
  EXTRA_ARGS+=(--validation-pretrained "${LLM_VALIDATION_PRETRAINED}")
fi

# If TASK_TYPES env provided (comma-separated), expand into multiple --task-type
if [[ -n "${TASK_TYPES:-}" ]]; then
  IFS=',' read -r -a _types_arr <<< "${TASK_TYPES}"
  for t in "${_types_arr[@]}"; do
    t_trimmed="${t##+([[:space:]])}"
    t_trimmed="${t_trimmed%%+([[:space:]])}"
    if [[ -n "${t_trimmed}" ]]; then
      EXTRA_ARGS+=(--task-type "${t_trimmed}")
    fi
  done
fi

echo "[run_task_sequence] Using Unity editor: ${UNITY_EDITOR_PATH}"
echo "[run_task_sequence] Using Unity project: ${UNITY_PROJECT_PATH}"
echo "[run_task_sequence] Target tasks: ${TASK_ARGS[*]}"

if [[ -n "${EXPERIMENT_ID:-}" ]]; then
  echo "[run_task_sequence] Experiment ID: ${EXPERIMENT_ID}"
fi
if [[ -n "${RUNNER_LOG_FILE:-}" ]]; then
  echo "[run_task_sequence] Runner log file: ${RUNNER_LOG_FILE}"
fi
if [[ -n "${OUTPUT_ROOT:-}" ]]; then
  echo "[run_task_sequence] Output root: ${OUTPUT_ROOT}"
fi
if [[ -n "${RUN_ID:-}" ]]; then
  echo "[run_task_sequence] Run ID: ${RUN_ID}"
fi
if [[ "${SAVE_DEBUG_ARTIFACTS:-0}" == "1" || "${SAVE_DEBUG_ARTIFACTS:-0}" == "true" || "${SAVE_DEBUG_ARTIFACTS:-0}" == "TRUE" ]]; then
  echo "[run_task_sequence] Debug artifact saving enabled"
fi
if [[ "${APPEND_RESULTS:-0}" == "1" || "${APPEND_RESULTS:-0}" == "true" || "${APPEND_RESULTS:-0}" == "TRUE" ]]; then
  echo "[run_task_sequence] Existing results append enabled"
fi
if [[ -n "${TASK_TYPES:-}" ]]; then
  echo "[run_task_sequence] Task types: ${TASK_TYPES}"
fi
if [[ -n "${MODEL_NAME:-}" ]]; then
  echo "[run_task_sequence] Model: ${MODEL_NAME}"
fi
if [[ -n "${PROVIDER:-}" ]]; then
  echo "[run_task_sequence] Provider: ${PROVIDER}"
fi

PYTHONPATH="${SCRIPT_DIR}/src${PYTHONPATH:+:${PYTHONPATH}}" uv run python -m cityarena run \
  --manage-unity \
  --unity-url "${UNITY_URL}" \
  --unity-path "${UNITY_EDITOR_PATH}" \
  --unity-project-path "${UNITY_PROJECT_PATH}" \
  --api-key "${EFFECTIVE_API_KEY}" \
  --restart-delay "${RESTART_DELAY:-3}" \
  --server-ready-timeout "${SERVER_READY_TIMEOUT:-10}" \
  --post-stop-delay "${POST_STOP_DELAY:-1.5}" \
  "${MODEL_ARGS[@]}" \
  "${EXTRA_ARGS[@]}" \
  "${TASK_ARGS[@]}"
