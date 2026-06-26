#!/usr/bin/env bash
set -euo pipefail

SCRIPT_DIR="$(cd -- "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
RUNNER="${SCRIPT_DIR}/../run_task_sequence.sh"

export MODEL_NAME="${MODEL_NAME:-${LLM_MODEL:-}}"
export EXPERIMENT_ID="${EXPERIMENT_ID:-azure-openai-${MODEL_NAME}}"
export PROVIDER="azure-openai"
export EFFECTIVE_API_KEY="${AZURE_OPENAI_API_KEY:-${LLM_API_KEY:-}}"
export AZURE_OPENAI_ENDPOINT="${AZURE_OPENAI_ENDPOINT:-}"
export AZURE_OPENAI_API_VERSION="${AZURE_OPENAI_API_VERSION:-${LLM_API_VERSION:-}}"
export AZURE_OPENAI_DEPLOYMENT="${AZURE_OPENAI_DEPLOYMENT:-${LLM_PRETRAINED:-}}"

if [[ -z "${MODEL_NAME}" ]]; then
  echo "[azure-openai] ERROR: MODEL_NAME (or LLM_MODEL) is required." >&2
  exit 1
fi

if [[ -z "${AZURE_OPENAI_ENDPOINT:-}" && -z "${LLM_API_BASE:-}" ]]; then
  echo "[azure-openai] ERROR: AZURE_OPENAI_ENDPOINT or LLM_API_BASE is required." >&2
  echo "Set AZURE_OPENAI_ENDPOINT (e.g., https://{resource}.openai.azure.com) or LLM_API_BASE." >&2
  exit 1
fi

if [[ -z "${AZURE_OPENAI_API_VERSION:-}" && -z "${LLM_API_VERSION:-}" ]]; then
  echo "[azure-openai] ERROR: AZURE_OPENAI_API_VERSION (or LLM_API_VERSION) is required." >&2
  exit 1
fi

if [[ -z "${AZURE_OPENAI_DEPLOYMENT:-}" && -z "${LLM_PRETRAINED:-}" ]]; then
  if [[ "${LLM_API_BASE:-}" != *"/openai/deployments/"* ]]; then
    echo "[azure-openai] ERROR: AZURE_OPENAI_DEPLOYMENT (or LLM_PRETRAINED) is required." >&2
    exit 1
  fi
fi

if [[ -z "${EFFECTIVE_API_KEY}" ]]; then
  echo "[azure-openai] ERROR: AZURE_OPENAI_API_KEY (or LLM_API_KEY) is not set." >&2
  exit 1
fi

# Map Azure env vars to the unified envs used by the runner/client when needed.
if [[ -z "${LLM_API_BASE:-}" && -n "${AZURE_OPENAI_ENDPOINT:-}" ]]; then
  export LLM_API_BASE="${AZURE_OPENAI_ENDPOINT}"
fi
if [[ -z "${LLM_API_VERSION:-}" && -n "${AZURE_OPENAI_API_VERSION:-}" ]]; then
  export LLM_API_VERSION="${AZURE_OPENAI_API_VERSION}"
fi
if [[ -z "${LLM_PRETRAINED:-}" && -n "${AZURE_OPENAI_DEPLOYMENT:-}" ]]; then
  export LLM_PRETRAINED="${AZURE_OPENAI_DEPLOYMENT}"
fi

exec "${RUNNER}" "$@"
