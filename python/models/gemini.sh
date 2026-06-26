#!/usr/bin/env bash
set -euo pipefail

SCRIPT_DIR="$(cd -- "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
RUNNER="${SCRIPT_DIR}/../run_task_sequence.sh"

export MODEL_NAME="${MODEL_NAME:-${LLM_MODEL:-}}"
export EXPERIMENT_ID="${EXPERIMENT_ID:-gemini-${MODEL_NAME}}"
export PROVIDER="gemini"
export EFFECTIVE_API_KEY="${GOOGLE_API_KEY:-${GEMINI_API_KEY:-${GOOGLE_GENAI_API_KEY:-}}}"

# Required: GOOGLE_API_KEY (or GEMINI_API_KEY / GOOGLE_GENAI_API_KEY)
# export GOOGLE_API_KEY=""

if [[ -z "${MODEL_NAME}" ]]; then
  echo "[gemini] ERROR: MODEL_NAME (or LLM_MODEL) is required." >&2
  exit 1
fi

if [[ -z "${EFFECTIVE_API_KEY}" ]]; then
  echo "[gemini] ERROR: Google/Gemini API key is not set for model (${MODEL_NAME})." >&2
  echo "Export one of GOOGLE_API_KEY, GEMINI_API_KEY, or GOOGLE_GENAI_API_KEY before running." >&2
  exit 1
fi

exec "${RUNNER}" "$@"
