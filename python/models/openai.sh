#!/usr/bin/env bash
set -euo pipefail

SCRIPT_DIR="$(cd -- "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
RUNNER="${SCRIPT_DIR}/../run_task_sequence.sh"

export MODEL_NAME="${MODEL_NAME:-${LLM_MODEL:-}}"
export EXPERIMENT_ID="${EXPERIMENT_ID:-openai-${MODEL_NAME}}"
export PROVIDER="openai"
export EFFECTIVE_API_KEY="${OPENAI_API_KEY:-}"

# Required: OPENAI_API_KEY
# export OPENAI_API_KEY=""

if [[ -z "${MODEL_NAME}" ]]; then
  echo "[openai] ERROR: MODEL_NAME (or LLM_MODEL) is required." >&2
  exit 1
fi

if [[ -z "${EFFECTIVE_API_KEY}" ]]; then
  echo "[openai] ERROR: OPENAI_API_KEY is not set for model (${MODEL_NAME})." >&2
  echo "Export OPENAI_API_KEY before running (e.g., export OPENAI_API_KEY=...)." >&2
  exit 1
fi

exec "${RUNNER}" "$@"
