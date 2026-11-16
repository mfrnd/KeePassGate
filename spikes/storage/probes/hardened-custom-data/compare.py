"""Compares a fresh run in `out` with the record in `recorded`, beside each other in one directory.

Written on this branch, not by any agent whose probe it checks. It takes away only what differs
between two runs of the same code by design, and prints every other difference:

- the full path of the branch root in front of `spikes\\storage\\probes\\`, which the record holds
  as a path relative to the branch root;
- the identifiers a run draws at random for its groups and entries, as 32 hex digits, as the
  8 digit prefix the survival probe prints, and in the lowercase forms KeePassXC prints, dashed in
  braces or bare in brackets. The probes print them only inside listings sorted by name, so they
  are renamed `<a>`, `<b>` in the order they stand in each line, and KeePassXC's forms, which it
  prints beside the path or the title of the node it names, become `<guid>`;
- the clock: the `made at` line, the time a KeePassXC save stamps on the root group and the times
  KeePassXC stamps on custom data items. Every time the probes write themselves falls on 1 to
  4 January, 1 February or 2 March 2026, so a time on any other day is the clock;
- how long a `keepassxc-cli` run took;
- the order of the changes one `keepassxc-cli merge` prints, which follows its hash tables: the
  changes of one `cli said:` line are compared as a sorted list.

Run it with `uv run --no-project python -I -X utf8 compare.py <directory>`, where the directory is
`design`, `survival` or `security-and-fit`. The exit code is 0 when every file in `recorded` that
has a fresh twin in `out` is the same after this. A file only one side has is named, not compared.
"""

# The change lines of `keepassxc-cli` 2.7.12 and 2.8.0, which answered in German here.
# cspell:ignore Geändert

import difflib
import re
import sys
from pathlib import Path

ROOT = re.compile(r"[A-Za-z]:\\(?:[^\\\"\s]+\\)+?(?=spikes\\storage\\probes\\)")
GUID = re.compile(
    r"\{[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}\}"
    r"|(?<![0-9A-Za-z])[0-9a-f]{32}(?![0-9A-Za-z])"
)
NODE = re.compile(r"(?<![0-9A-Za-z])[0-9A-F]{32}(?![0-9A-Za-z])|(?<=Rule\.)[0-9A-F]{8}(?=\.)")
ISO = re.compile(r"(\d{4}-\d\d-\d\d) \d\d:\d\d:\d\dZ")
LOCAL = re.compile(r"(?<=@)\d{1,2}[./]\d{1,2}[./]\d{4} \d{1,2}:\d\d:\d\d(?: [AP]M)?")
TOOK = re.compile(r"(exit -?\d+), \d+[.,]\d s")
CHANGE = re.compile(
    # 2.7.12 and 2.8.0, which print in the language of the machine: here German.
    r"(?:Metadata|Geändert): (?:'[^']*'(?:/'[^']*')? \[\{[^}]*\}\] )?\((?:[^()]|\([^()]*\))*\)"
    # 2.6.6, which prints in English.
    r"|(?:Removed|Adding) custom data \S+ \[.*?\]"
    r"(?= Removed custom data | Adding custom data | Successfully merged )"
    r"|(?:Synchronizing from (?:older|newer) source|Overwriting) \S+ \[[^\]]*\]"
)
PROBE_DATES = {"2026-01-01", "2026-01-02", "2026-01-03", "2026-01-04", "2026-02-01", "2026-03-02"}


def clock(match: re.Match[str]) -> str:
    return match.group(0) if match.group(1) in PROBE_DATES else "<clock>"


def nodes(line: str) -> str:
    names: dict[str, str] = {}

    def name(match: re.Match[str]) -> str:
        token = match.group(0)
        if token not in names:
            names[token] = f"<{chr(ord('a') + len(names))}>"
        return names[token]

    return NODE.sub(name, line)


def normalize(line: str) -> str:
    line = ROOT.sub("", line)
    line = GUID.sub("{<guid>}", line)
    line = ISO.sub(clock, line)
    line = LOCAL.sub("<clock>", line)
    line = TOOK.sub(r"\1, <t> s", line)
    if "cli said: " in line:
        changes = sorted(nodes(change) for change in CHANGE.findall(line))
        rest = nodes(CHANGE.sub("<change>", line))
        return rest + ("  changes: " + " ; ".join(changes) if changes else "")
    return nodes(line)


def lines(path: Path) -> list[str]:
    text = path.read_text(encoding="utf-8-sig", errors="replace")
    return [normalize(line) for line in text.splitlines()]


def main() -> int:
    root = Path(sys.argv[1])
    recorded = {p.name for p in (root / "recorded").iterdir() if p.is_file()}
    fresh = {p.name for p in (root / "out").iterdir() if p.is_file()} if (root / "out").exists() else set()
    status = 0
    for name in sorted(recorded | fresh):
        if name not in fresh:
            print(f"{name}: recorded only, no fresh twin in out to compare")
            continue
        if name not in recorded:
            print(f"{name}: fresh only, nothing recorded to compare it with")
            continue
        old = lines(root / "recorded" / name)
        new = lines(root / "out" / name)
        diff = list(difflib.unified_diff(old, new, f"recorded/{name}", f"out/{name}", lineterm=""))
        if diff:
            status = 1
            print("\n".join(diff))
        else:
            print(f"{name}: the same")
    return status


if __name__ == "__main__":
    sys.exit(main())
