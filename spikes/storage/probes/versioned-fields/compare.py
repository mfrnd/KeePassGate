"""Compares a fresh run in `out` with the record in `recorded`, beside each other in one directory.

Written on this branch, not by any agent whose probe it checks. It takes away only what differs
between two runs of the same code by design, and prints every other difference:

- identifiers a run draws at random (the root group, `General`, the user entry, and the groups and
  entries a `CreateNewUuids` merge, an import or a duplication makes), as runs of 8, 16 or 32
  upper case hex digits, the forms the dumps print. An identifier found in both files is kept, so a
  derived identifier that changed still shows; one found in only one file is renamed `<id1>`,
  `<id2>` in order of first appearance;
- times in milliseconds, which the walk prints, and the clock at the moment of a run, which
  `keepassxc-cli add` stamps on what it adds and on its group, and Delete Duplicate Entries on the
  deletion records it writes: every time the probes write themselves falls on 1 January, so a time
  on any other day is the clock;
- the size of a database file, which moves by whole cipher blocks between two saves of the same
  content, because every save draws new keys and the compressed stream holds bytes made from them;
- line endings, since one recorded file was assembled in a shell that wrote some lines with a bare
  line feed.

Run it with `uv run --no-project python -I -X utf8 compare.py <directory>`. The exit code is 0
when every file in `recorded` that has a fresh twin in `out` is the same after this.
"""

# cspell:ignore lineterm

import difflib
import re
import sys
from pathlib import Path

HEX = re.compile(r"(?<![0-9A-Za-z])(?:[0-9A-F]{32}|[0-9A-F]{16}|[0-9A-F]{8})(?![0-9A-Za-z])")
TIME = re.compile(r"\d+[.,]\d+ ms")
CLOCK = re.compile(r"(?<=[=@])(?!01-01T)\d\d-\d\dT\d\d:\d\d:\d\d")
BYTES = re.compile(r"(?<=file bytes )\d+")


def lines(path: Path) -> list[str]:
    text = path.read_text(encoding="utf-8-sig", errors="replace")
    return [
        BYTES.sub("<n>", CLOCK.sub("<clock>", TIME.sub("<t> ms", line)))
        for line in text.splitlines()
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
