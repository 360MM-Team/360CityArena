from __future__ import annotations

from pathlib import Path


PACKAGE_ROOT = Path(__file__).resolve().parent
PYTHON_ROOT = PACKAGE_ROOT.parents[1]
REPO_ROOT = PACKAGE_ROOT.parents[2]
BENCHMARK_ROOT = REPO_ROOT / "benchmark"
UNITY_ROOT = REPO_ROOT / "unity"
DEFAULT_OUTPUT_ROOT = REPO_ROOT / "outputs"


def resolve_repo_path(path: str | Path) -> Path:
    candidate = Path(path)
    if candidate.is_absolute():
        return candidate
    return REPO_ROOT / candidate
