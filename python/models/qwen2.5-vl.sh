#!/usr/bin/env bash
set -euo pipefail

SCRIPT_DIR="$(cd -- "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
RUNNER="${SCRIPT_DIR}/../run_task_sequence.sh"

export MODEL_NAME="${MODEL_NAME:-${LLM_MODEL:-}}"
export EXPERIMENT_ID="${EXPERIMENT_ID:-openai-compatible-${MODEL_NAME}}"
export PROVIDER="openai-compatible"
export LLM_API_BASE="${LLM_API_BASE:-}"
export LLM_API_KEY="${LLM_API_KEY:-}"
export LLM_PRETRAINED="${LLM_PRETRAINED:-}"
export EFFECTIVE_API_KEY="${LLM_API_KEY}"

if [[ -z "${MODEL_NAME}" ]]; then
  echo "[qwen2.5-vl] ERROR: MODEL_NAME (or LLM_MODEL) is required." >&2
  exit 1
fi

if [[ -z "${EFFECTIVE_API_KEY}" ]]; then
  echo "[qwen2.5-vl] ERROR: LLM_API_KEY is not set for model (${MODEL_NAME})." >&2
  echo "Export LLM_API_KEY before running (例: export LLM_API_KEY=EMPTY)." >&2
  exit 1
fi

if [[ -z "${LLM_API_BASE}" ]]; then
  echo "[qwen2.5-vl] ERROR: LLM_API_BASE must be set for model (${MODEL_NAME})." >&2
  echo "Export LLM_API_BASE (例: http://127.0.0.1:8000/v1) before running." >&2
  exit 1
fi

if [[ -z "${LLM_PRETRAINED}" ]]; then
  echo "[qwen2.5-vl] ERROR: LLM_PRETRAINED must be set for model (${MODEL_NAME})." >&2
  echo "Export LLM_PRETRAINED (例: Qwen/Qwen2.5-VL-7B-Instruct) before running." >&2
  exit 1
fi

exec "${RUNNER}" "$@"
