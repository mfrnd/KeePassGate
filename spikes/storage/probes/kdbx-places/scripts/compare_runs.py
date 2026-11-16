r"""Holds a fresh run's output to the recorded one, line for line.

    compare_runs.py <recorded dir> <out dir>

What a program stamps at the moment it runs is replaced before comparing, in both: what a save draws fresh, a time on the
day of either run, the text time KeePassXC writes into `_LAST_MODIFIED`, the random slug
KeePassXC 2.7.12 and 2.8.0 add, identifiers of the entries an edit adds, and seconds. Prints
every file that differs with its differing lines, then the cells of `tables.md` that differ, then
the counts.
"""
from __future__ import annotations

import re
import sys
from pathlib import Path

STAMPS = [
    (re.compile(r"\b20\d\d-\d\d-\d\dT\d\d:\d\d:\d\dZ"), lambda m: m.group(0) if m.group(0)[:10] in FIXED else "<now>"),
    # In the language of the machine: `Mon` or `Mo`.
    (re.compile(r"\b\w{2,3}\.? \w{3}\.? +\d+ \d\d:\d\d:\d\d \d{4}( GMT)?"), "<now>"),
    # What every save draws fresh, in the dumps of the bases.
    (re.compile(r"^((?:H\.MasterSeed|H\.EncryptionIV|H\.kdf\[S\]|I\.InnerRandomStreamKey)\t)(?:Bytes:)?<\d+ bytes sha256:[0-9a-f]{16}>"),
     lambda m: m.group(1) + "<fresh>"),
    (re.compile(r"<\d+ chars sha256:[0-9a-f]{16} starts [0-9a-f]{24}>"), "<slug>"),
    (re.compile(r"\b[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}\b"),
     lambda m: m.group(0) if m.group(0).startswith("4b50472d-") else "<uuid>"),
    (re.compile(r"\d+\.\d s\b"), "<s>"),
]
FIXED = {"2025-12-31", "2026-01-01", "2026-02-01", "2026-03-01", "2031-01-01", "2032-01-01", "2033-01-01",
         "2031-06-01", "2032-06-01", "2033-06-01", "2035-01-01"}


def norm(line: str) -> str:
    for rx, rep in STAMPS:
        line = rx.sub(rep, line)
    return line


def lines(p: Path) -> list[str]:
    return [norm(x) for x in p.read_text(encoding="utf-8").splitlines()]


def main() -> None:
    rec, out = Path(sys.argv[1]), Path(sys.argv[2])
    files = sorted({p.relative_to(rec) for p in rec.rglob("*.txt")} | {p.relative_to(out) for p in out.rglob("*.txt")})
    same = differ = missing = 0
    for rel in files:
        if str(rel) in ("log.txt",) or rel.parts[0] == "logs":
            continue
        a, b = rec / rel, out / rel
        if not a.exists() or not b.exists():
            missing += 1
            print(f"ONLY IN {'out' if not a.exists() else 'recorded'}: {rel}")
            continue
        la, lb = lines(a), lines(b)
        if la == lb:
            same += 1
            continue
        differ += 1
        print(f"DIFFERS: {rel}")
        sa, sb = set(la), set(lb)
        for x in [x for x in la if x not in sb][:8]:
            print(f"  recorded: {x}")
        for x in [x for x in lb if x not in sa][:8]:
            print(f"  this run: {x}")
    ta, tb = rec / "tables.md", out / "tables.md"
    cells = cell_diffs = 0
    if ta.exists() and tb.exists():
        # Rows repeat across the tables, so they are compared in order rather than by key.
        la = [r for r in lines(ta) if r.startswith("| ")]
        lb = [r for r in lines(tb) if r.startswith("| ")]
        for x, y in zip(la, lb):
            xs, ys = x.split(" | "), y.split(" | ")
            for i, (c, d) in enumerate(zip(xs, ys)):
                cells += 1
                if c != d:
                    cell_diffs += 1
                    print(f"CELL {xs[0]} column {i}: recorded {c!r}, this run {d!r}")
    print(f"files: {same} the same, {differ} differ, {missing} in one run only; "
          f"table cells: {cells} compared, {cell_diffs} differ")


if __name__ == "__main__":
    sys.stdout.reconfigure(encoding="utf-8", newline="\n")
    main()
