"""Compares a fresh run in `out` with the recorded output in `recorded`, line by line.

Added on this branch, to put what `RUN.md` states about each file in a form anybody can run again.
Two things are masked before lines are compared, because they are expected to differ in every run:
the moment `T0` on each file's first line, which is the time the probe started, and a 32 digit
group identifier, which KeePassLib draws at random and X10 prints. Everything else is compared as
written, line endings aside. Every line that still differs is printed, recorded and fresh.

Run from the branch root, after `run.ps1`:

    uv run --no-project python -I -X utf8 spikes/storage/probes/ground-keepass/checks/compare.py

An argument names another directory to compare with `recorded` in place of `out`.
"""

import pathlib
import re
import sys

FAMILY = pathlib.Path(__file__).resolve().parent.parent
T0 = re.compile(r"T0 \d{4}-\d\d-\d\dT\d\d:\d\d:\d\d\.\d{7}Z")
UUID = re.compile(r"\b[0-9A-F]{32}\b")


def masked(path: pathlib.Path) -> list[str]:
    text = path.read_text(encoding="utf-8")
    return [UUID.sub("<uuid>", T0.sub("T0 <time>", line)) for line in text.splitlines()]


def main() -> int:
    fresh_dir = pathlib.Path(sys.argv[1]) if len(sys.argv) > 1 else FAMILY / "out"
    recorded_dir = FAMILY / "recorded"
    differing_files = 0
    for recorded in sorted(recorded_dir.glob("x*.txt"), key=lambda p: int(p.stem[1:])):
        fresh = fresh_dir / recorded.name
        if not fresh.exists():
            print(f"{recorded.name}: no fresh output")
            differing_files += 1
            continue
        a, b = masked(recorded), masked(fresh)
        pairs = [(i, x, y) for i, (x, y) in enumerate(zip(a, b), 1) if x != y]
        extra = abs(len(a) - len(b))
        verdict = "identical" if not pairs and not extra else "differs"
        print(f"{recorded.name}: {len(a)} lines recorded, {len(b)} fresh, "
              f"{len(pairs)} differing, {extra} unmatched: {verdict}")
        for i, x, y in pairs:
            print(f"  line {i}\n    recorded: {x}\n    fresh:    {y}")
        if verdict != "identical":
            differing_files += 1
    print(f"{differing_files} file(s) differ")
    return 1 if differing_files else 0


if __name__ == "__main__":
    sys.exit(main())
