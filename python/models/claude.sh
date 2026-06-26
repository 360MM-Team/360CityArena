#!/usr/bin/env bash
set -euo pipefail

SCRIPT_DIR="$(cd -- "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
RUNNER="${SCRIPT_DIR}/../run_task_sequence.sh"

export MODEL_NAME="${MODEL_NAME:-${LLM_MODEL:-}}"
export EXPERIMENT_ID="${EXPERIMENT_ID:-anthropic-${MODEL_NAME}}"
export PROVIDER="anthropic"
export EFFECTIVE_API_KEY="${ANTHROPIC_API_KEY:-}"

# Required: ANTHROPIC_API_KEY
# export ANTHROPIC_API_KEY=""

if [[ -z "${MODEL_NAME}" ]]; then
  echo "[claude] ERROR: MODEL_NAME (or LLM_MODEL) is required." >&2
  exit 1
fi

if [[ -z "${EFFECTIVE_API_KEY}" ]]; then
  echo "[claude] ERROR: ANTHROPIC_API_KEY is not set for model (${MODEL_NAME})." >&2
  echo "Export ANTHROPIC_API_KEY before running (e.g., export ANTHROPIC_API_KEY=...)." >&2
  exit 1
fi

exec "${RUNNER}" "$@"
