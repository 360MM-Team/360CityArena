from __future__ import annotations

import sys

from cityarena.runner import sequence_runner


def main(argv: list[str] | None = None) -> int:
    args = list(sys.argv[1:] if argv is None else argv)
    if args and args[0] in {"-h", "--help"}:
        print("usage: python -m cityarena run [runner args]")
        print()
        print("Commands:")
        print("  run    Execute 360CityArena benchmark tasks")
        return 0
    if args and args[0] == "run":
        args = args[1:]
    elif args and not args[0].startswith("-"):
        raise SystemExit(f"Unknown command: {args[0]}")
    return sequence_runner.main(args)
