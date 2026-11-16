"""Compares a fresh run in `out` with the record in `recorded`, beside each other in one directory.

Written on this branch, not by any agent whose probe it checks. It takes away only what differs
between two runs of the same code by design, and prints every other difference:

- identifiers a run draws at random. A snapshot's identifier is printed as `S<n>:` and six hex
  digits, other identifiers as 8 or 32 hex digits: the copies a `CreateNewUuids` merge makes, the
  state digests, which hash the rules under their nodes' random identifiers, the nodes of the 1 MiB
  file and the user entries of the critics' databases. An identifier found in both files is kept, so
  a derived one that changed still shows, which is the case of the witnesses and the store group;
  one found in only one file is renamed `<id1>`, `<id2>` in order of first appearance. Renaming
  is one to one, so two places that held one identifier still hold one name;
- the order in which a list prints such identifiers, which follows their random bytes: the
  elements of `present [...]`, `records S[...]`, `over=[...]` and the `snapshots [...]` of a
  file's deletion records are sorted after renaming, and so
  are the snapshot lines of one file in `merges.txt` and the rules in `rules=[...]`, which the
  probe orders by their nodes' identifiers; a node the run's `names.txt` does not name, such as
  those of the 1 MiB file, becomes `<node>`;
- times in milliseconds, which the bench prints, and the clock at the moment of a run, which the
  plugin's next write, Delete Empty Groups and the critics' deletions stamp: every time the probes
  write themselves falls on 1 January 2026, so a time on any other day is the clock.

Run it with `uv run --no-project python -I -X utf8 compare.py <directory>`. The exit code is 0
when every file in `recorded` that has a fresh twin in `out` is the same after this. A fresh file
with no recorded twin is named and not compared.
"""

# cspell:ignore lineterm

import difflib
import re
import sys
from pathlib import Path

SNAP = r"S\d+:[0-9a-f]{6}"
HEX = re.compile(
    r"(?<![0-9A-Za-z:])(?:" + SNAP + r"|[0-9a-f]{8}|[0-9A-F]{32})(?![0-9A-Za-z])"
)
TIME = re.compile(r"\d+[.,]\d+ ms")
CLOCK = [
    re.compile(r"(?<=mod=)(?!01-01T)\d\d-\d\dT\d\d:\d\d:\d\d"),
    re.compile(r"(?!2026-01-01T)\d{4}-\d\d-\d\dT\d\d:\d\d:\d\dZ"),
]
LISTS = [
    re.compile(r"(?<=present \[)[^\]]*(?=\])"),
    re.compile(r"(?<=records S\[)[^\]]*(?=\])"),
    re.compile(r"(?<=over=\[)[^\]]*(?=\])"),
    re.compile(r"(?<=; snapshots \[)[^\]]*(?=\])"),
]
UNNAMED_RULES = re.compile(r"(?<=rules=\[)(.*?)(?=\]( |$|\s*AUTHORITY))")


def lines(path: Path) -> list[str]:
    text = path.read_text(encoding="utf-8-sig", errors="replace")
    out = []
    for line in text.splitlines():
        line = TIME.sub("<t> ms", line)
        for clock in CLOCK:
            line = clock.sub("<clock>", line)
        out.append(line)
    return out


def rename(text: list[str], keep: set[str]) -> list[str]:
    names: dict[str, str] = {}

    def name(match: re.Match[str]) -> str:
        token = match.group(0)
        if token in keep:
            return token
        if token not in names:
            prefix = token.split(":")[0] + ":" if ":" in token else ""
            names[token] = f"{prefix}<id{len(names) + 1}>"
        return names[token]

    return [HEX.sub(name, line) for line in text]


def sort_lists(line: str) -> str:
    for pattern in LISTS:
        line = pattern.sub(lambda m: " ".join(sorted(re.split(r"[ ,]", m.group(0)))), line)

    def rules(m: re.Match[str]) -> str:
        items = m.group(1).split("; ")
        return "; ".join(sorted(re.sub(r"^<id\d+> ", "<node> ", item) for item in items))

    return UNNAMED_RULES.sub(rules, line)


def sort_blocks(text: list[str]) -> list[str]:
    """Sorts the consecutive `  snapshot ` lines of one file's dump in `merges.txt`."""
    out: list[str] = []
    run: list[str] = []
    for line in text + [""]:
        if line.startswith("  snapshot "):
            run.append(line)
            continue
        out.extend(sorted(run))
        run = []
        out.append(line)
    return out[:-1]


def normalize(text: list[str], keep: set[str]) -> list[str]:
    return sort_blocks([sort_lists(line) for line in rename(text, keep)])


def main() -> int:
    root = Path(sys.argv[1])
    status = 0
    recorded_names = {p.name for p in (root / "recorded").iterdir()}
    for recorded in sorted((root / "recorded").iterdir()):
        fresh = root / "out" / recorded.name
        if not fresh.exists():
            print(f"{recorded.name}: recorded, and no fresh twin in out to compare")
            status = 1
            continue
        old, new = lines(recorded), lines(fresh)
        common = set(HEX.findall("\n".join(old))) & set(HEX.findall("\n".join(new)))
        diff = list(
            difflib.unified_diff(
                normalize(old, common),
                normalize(new, common),
                f"recorded/{recorded.name}",
                f"out/{recorded.name}",
                lineterm="",
            )
        )
        if diff:
            status = 1
            print("\n".join(diff))
        else:
            print(f"{recorded.name}: the same after normalizing")
    for fresh in sorted((root / "out").iterdir()):
        if fresh.name not in recorded_names:
            print(f"{fresh.name}: fresh only, no record to compare with")
    return status


if __name__ == "__main__":
    sys.exit(main())
