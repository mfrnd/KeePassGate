"""Measurement 2's counts as one table: every finding of StoreLab's analysis, by program.

    uv run --no-project python -I -X utf8 spikes\\storage\\probes\\source-harness\\store_table.py

Reads out\\store-<program>-summary.txt for kplib, k2a and kpdx and writes out\\store-table.md: the
cases of each kind, then one row per finding with its count for each program ("-" where the
program has none).
"""
import sys
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parent))
import common as c  # noqa: E402

PROGRAMS = ("kplib", "k2a", "kpdx")
NAMES = {"kplib": "KeePassLib 2.62", "k2a": "Keepass2Android", "kpdx": "KeePassDX"}


def read(p):
    cases, findings = {}, {}
    f = c.OUT / ("store-%s-summary.txt" % p)
    if not f.exists():
        return cases, findings
    for line in f.read_text(encoding="utf-8").splitlines():
        parts = line.split("\t")
        if parts[0] == "cases":
            cases[parts[1]] = int(parts[2])
        elif parts[0] == "finding":
            findings[parts[1]] = int(parts[2])
    return cases, findings


def main():
    data = {p: read(p) for p in PROGRAMS}
    kinds = sorted({k for p in PROGRAMS for k in data[p][0]})
    rows = sorted({k for p in PROGRAMS for k in data[p][1]})
    head = "| | " + " | ".join(NAMES[p] for p in PROGRAMS) + " |"
    sep = "| --- |" + " --- |" * len(PROGRAMS)
    md = ["# The store's records: findings by program", "", head, sep]
    for k in kinds:
        md.append("| cases: %s | " % k + " | ".join(str(data[p][0].get(k, "-")) for p in PROGRAMS) + " |")
    for r in rows:
        md.append("| %s | " % r + " | ".join(str(data[p][1].get(r, "-")) for p in PROGRAMS) + " |")
    text = "\n".join(md) + "\n"
    (c.OUT / "store-table.md").write_text(text, encoding="utf-8")
    sys.stdout.write(text)


if __name__ == "__main__":
    main()
