"""Compares a fresh run in `out` with the record in `recorded`, beside each other in one directory.

Written on this branch, not by any agent whose probe it checks. It takes away only what differs
between two runs of the same code by design, and prints every other difference:

- identifiers a run draws at random (the root group, `General`, the user entry, the groups and
  entries a `CreateNewUuids` merge makes, the entries the import and pykeepass make), as runs of
  8 or 32 hex digits and the 4 digit prefixes the design's summary prints after `@`. An identifier
  found in both files is kept, so a derived identifier that changed still shows; one found in only
  one file is renamed `<id1>`, `<id2>` in order of first appearance;
- times in milliseconds, which the walk prints, and the clock at the moment of a run, which
  `keepassxc-cli add` stamps on what it adds and on its group: every time the probes write
  themselves falls on 1 January, so a modification time on any other day is the clock;
- the lines uv prints while it downloads and installs a package before the probe starts.

Run it with `uv run --no-project python -I -X utf8 compare.py <directory>`. The exit code is 0
when every file in `recorded` that has a fresh twin in `out` is the same after this. A recorded
file no run writes, such as a list a critic recorded by hand, is named and not compared.
"""

# cspell:ignore lineterm

import difflib
import re
import sys
from pathlib import Path

HEX = re.compile(r"(?<![0-9A-Za-z])(?:[0-9a-f]{8}|[0-9A-F]{32}|(?<=@)[0-9a-f]{4})(?![0-9A-Za-z])")
TIME = re.compile(r"\d+[.,]\d+ ms")
CLOCK = re.compile(r"(?<=mod=)(?!01-01T)\d\d-\d\dT\d\d:\d\d:\d\d")
UV = re.compile(
    r"(?:Downloading \S+ \([^)]*\)|Downloaded \S+|"
    r"(?:Installed|Prepared|Resolved|Built) \d+ packages? in [\d.]+m?s)\s*"
)


def lines(path: Path) -> list[str]:
    text = path.read_text(encoding="utf-8-sig", errors="replace")
    return [
        UV.sub("", CLOCK.sub("<clock>", TIME.sub("<t> ms", line))) for line in text.splitlines()
    ]


def rename(text: list[str], keep: set[str]) -> list[str]:
    names: dict[str, str] = {}

    def name(match: re.Match[str]) -> str:
        token = match.group(0)
        if token in keep:
            return token
        if token not in names:
            names[token] = f"<id{len(names) + 1}>"
        return names[token]

    return [HEX.sub(name, line) for line in text]


def main() -> int:
    root = Path(sys.argv[1])
    status = 0
    for recorded in sorted((root / "recorded").iterdir()):
        fresh = root / "out" / recorded.name
        if not fresh.exists():
            print(f"{recorded.name}: recorded only, no fresh twin in out to compare")
            continue
        old, new = lines(recorded), lines(fresh)
        common = set(HEX.findall("\n".join(old))) & set(HEX.findall("\n".join(new)))
        diff = list(
            difflib.unified_diff(
                rename(old, common),
                rename(new, common),
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
    return status


sys.exit(main())
