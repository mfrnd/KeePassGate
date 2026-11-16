"""Checks what `ground-keepass.md` section 1a and 1c claim about X2 against an X2 output.

Added on this branch. The report rests its claim that seven kinds of change behave alike on "a
script that compared every cell across the seven", and that script was not kept, so this one does
the same comparison over the probe's `X2S` lines, and then holds the table of section 1a, cell for
cell, to the lines of the copies that made a history backup before changing, as the report describes
its table. It also checks the bullets of 1a and 1c that name a cell the table does not show.

Run from the branch root, after `run.ps1`:

    uv run --no-project python -I -X utf8 spikes/storage/probes/ground-keepass/checks/x2.py

An argument names the output to check, `recorded/x2.txt` for the recorded run; the default is
`out/x2.txt`.
"""

import collections
import pathlib
import sys

FAMILY = pathlib.Path(__file__).resolve().parent.parent

# The seven kinds of change section 1a names. The probe has an eighth, `only-A-changes`, which runs
# three of the six time conditions and which the report does not count among the seven.
SEVEN = ["diff-custom-string", "diff-protected-string", "same-field", "entry-custom-data",
         "attachment", "tag", "notes"]

# Section 1a's table, as written. Each cell is the one the report prints: "A" or "B" is whose change
# the current version holds, "stale" is the B copy whose time was rewritten without a change,
# "hist X" is that X's change is in a history item, "X lost" is that it is nowhere, and
# "hist stale" is that a history item carries the stale copy's time, `+30`.
COLUMNS = [("Sync", "A"), ("Sync", "B"), ("Overwrite", "A"), ("Overwrite", "B"),
           ("Keep", "A"), ("Keep", "B")]
TABLE = {
    "A-newer": ["A, hist B", "A, hist B", "B, hist A", "A, hist B", "A, hist B", "B, A lost"],
    "B-newer": ["B, hist A", "B, hist A", "B, hist A", "A, hist B", "A, B lost", "B, hist A"],
    "equal": ["B, A lost", "A, B lost", "B, hist A", "A, hist B", "A, B lost", "B, A lost"],
    "B-time-not-moved": ["A, B lost", "A, hist B", "B, hist A", "A, B lost", "A, B lost",
                         "B, A lost"],
    "neither-time-moved": ["B, A lost", "A, B lost", "B, A lost", "A, B lost", "A, B lost",
                           "B, A lost"],
    "B-stale-time-rewritten": ["stale, hist A", "stale, hist A", "stale, hist A",
                               "A, hist stale", "A", "stale, hist A"],
}
# What the table's emphasis adds to a cell: `(older)` and `(+0)`, the time the result carries.
TIMES = {("A-newer", "Overwrite", "A"): "+10", ("B-newer", "Overwrite", "B"): "+10",
         ("B-time-not-moved", "Overwrite", "A"): "+0"}


def parse(path: pathlib.Path):
    summary = collections.defaultdict(dict)
    detail = collections.defaultdict(list)
    for line in path.read_text(encoding="utf-8").splitlines():
        parts = [p.strip() for p in line.split(" | ")]
        if parts[0] == "X2S":
            variant, backup, timing, into, method = parts[1:6]
            entries = []
            for cell in " | ".join(parts[6:]).split(" ; "):
                fields = dict(f.split("=", 1) for f in cell.split())
                entries.append(fields)
            summary[(backup, timing, into.split()[1], method)][variant] = entries
        elif parts[0] == "X2":
            variant, backup, timing, into, method = parts[1:6]
            detail[(variant, backup, timing, into.split()[1], method)].append(parts[6])
    return summary, detail


def holds(entry, expected: str, history_text: str) -> bool:
    cur, hist = entry["cur"], entry["hist"]
    terms = [t.strip() for t in expected.split(",")]
    first = terms[0]
    if first == "stale":
        if cur != "-":
            return False
    elif cur != first:
        return False
    for term in terms[1:]:
        if term == "hist stale":
            if "@+30" not in history_text:
                return False
        elif term.startswith("hist "):
            if term[5:] not in hist:
                return False
        elif term.endswith(" lost"):
            who = term[0]
            if who in cur or who in hist:
                return False
        else:
            raise ValueError(term)
    return True


def main() -> int:
    path = pathlib.Path(sys.argv[1]) if len(sys.argv) > 1 else FAMILY / "out" / "x2.txt"
    summary, detail = parse(path)
    failures = 0

    def report(ok: bool, text: str) -> None:
        nonlocal failures
        failures += 0 if ok else 1
        print(("yes  " if ok else "NO   ") + text)

    cells = sorted(summary)
    alike = [k for k in cells if len({repr(summary[k].get(v)) for v in SEVEN}) == 1
             and all(v in summary[k] for v in SEVEN)]
    report(len(cells) == 120 and len(alike) == 120,
           f"cells {len(cells)}, of which the seven kinds give identical lines in {len(alike)}")

    for timing, row in TABLE.items():
        for (method, into), expected in zip(COLUMNS, row):
            methods = ["Sync", "IfNewer"] if method == "Sync" else [method]
            for m in methods:
                for variant in SEVEN:
                    entries = summary[("backup", timing, into, m)][variant]
                    history = detail[(variant, "backup", timing, into, m)][0]
                    history = history[history.index(" hist["):]
                    ok = len(entries) == 1 and holds(entries[0], expected, history)
                    want = TIMES.get((timing, method, into))
                    if want is not None:
                        ok = ok and entries[0]["lm"] == want
                    if not ok or variant == SEVEN[0]:
                        report(ok, f"1a {timing}, {m} into {into}: '{expected}'"
                               + ("" if variant == SEVEN[0] else f" ({variant})")
                               + f" | {entries}")

    for timing in TABLE:
        for into in "AB":
            for variant in SEVEN:
                entries = summary[("backup", timing, into, "NewUuids")][variant]
                sides = sorted(e["cur"] for e in entries)
                own = all(not any(side in e["hist"] for side in "AB" if side != e["cur"])
                          for e in entries)
                expected = ["-", "A"] if timing == "B-stale-time-rewritten" else ["A", "B"]
                ok = len(entries) == 2 and sides == expected and own
                if not ok or variant == SEVEN[0]:
                    report(ok, f"1a NewUuids {timing} into {into}: two entries, one per side, "
                               f"each with only its own history | {entries}")

    for m in ["Sync", "IfNewer"]:
        entries = summary[("nobackup", "B-time-not-moved", "A", m)]["diff-custom-string"]
        report(entries[0]["cur"] == "A" and "B" in entries[0]["hist"],
               f"1a without the backup, B-time-not-moved, {m} into A keeps B in history"
               f" | {entries}")

    for into, loser in [("A", "A"), ("B", "B")]:
        with_backup = summary[("backup", "neither-time-moved", into, "Overwrite")][SEVEN[0]][0]
        without = summary[("nobackup", "neither-time-moved", into, "Overwrite")][SEVEN[0]][0]
        report(loser not in with_backup["hist"] and loser in without["hist"],
               f"1c Overwrite into {into}, neither time moved: {loser} lost with the backup, in "
               f"history without it | {with_backup} | {without}")

    print(f"{failures} check(s) failed")
    return 1 if failures else 0


if __name__ == "__main__":
    sys.exit(main())
