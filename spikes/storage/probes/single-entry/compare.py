"""Compares a fresh run in `out` with the record in `recorded`, beside each other in one directory.

Written on this branch, not by any agent whose probe it checks. It takes away only what differs
between two runs of the same code by design, and prints every other difference:

- the order of the labels inside a merged version's name, `M(A1+B1)` or `M(B1+A1)`: the step
  sorts the heads by their version identifiers, which `Lib.ps1` draws at random, so the labels are
  compared as a sorted list;
- the identifier the survival probe's acknowledgment draws, `ack.<16 hex digits>`;
- the order of the names after `version missing:`, which follows a hash table keyed by those
  random identifiers, compared as a sorted list;
- the identifiers KeePassLib draws for the invented group and entry, 32 hex digits, and the
  8-digit prefixes of them that the security probe prints: renamed `<a>`, `<b>` in the order they
  first appear in the file, a prefix taking the name of the identifier it begins, so that which
  node is which is still compared;
- how long something took, in milliseconds;
- the size of a database file in bytes, which moves with the random identifiers and salts inside
  it, and which means nothing, as `design.md` section 14 has it.

Run it with `uv run --no-project python -I -X utf8 compare.py <directory>`, where the directory is
`design`, `survival` or `security-and-fit`, relative to this file. The exit code is 0 when every
file in `recorded` that has a fresh twin in `out` is the same after this. A file only one side has
is named, not compared.
"""

import difflib
import re
import sys
from pathlib import Path

MERGED = re.compile(r"M\(([^()]*)\)")
ACK = re.compile(r"\back\.[0-9a-f]{16}\b")
MISSING = re.compile(r"(version missing: )([^;\r\n]+)")
NODE = re.compile(r"(?<![0-9A-Za-z])(?:[0-9A-F]{32}|[0-9A-F]{8})(?![0-9A-Za-z])")
TOOK = re.compile(r"\d+(?:[.,]\d+)? ms")
BYTES = re.compile(r"\d+ bytes")
# The two fixed identifiers `Lib.ps1` derives, which a run must reproduce exactly.
FIXED = {"FE4040A9E0FAE884FA8BECAEEE9FBB46", "8AB03BFAEDE536C6078045089CEB0601"}


def merged(match: re.Match[str]) -> str:
    return "M(" + "+".join(sorted(match.group(1).split("+"))) + ")"


def missing(match: re.Match[str]) -> str:
    return match.group(1) + ",".join(sorted(match.group(2).strip().split(",")))


def normalize(lines: list[str]) -> list[str]:
    names: dict[str, str] = {}

    def node(match: re.Match[str]) -> str:
        token = match.group(0)
        if token in FIXED:
            return token
        if len(token) == 8:
            for full, name in names.items():
                if full.startswith(token):
                    return name
        if token not in names:
            names[token] = f"<{chr(ord('a') + len(names))}>"
        return names[token]

    result = []
    for line in lines:
        line = MERGED.sub(merged, line)
        line = ACK.sub("ack.<id>", line)
        line = MISSING.sub(missing, line)
        line = NODE.sub(node, line)
        line = TOOK.sub("<t> ms", line)
        line = BYTES.sub("<n> bytes", line)
        result.append(line)
    return result


def main() -> int:
    if len(sys.argv) != 2:
        print("usage: compare.py design|survival|security-and-fit")
        return 2
    here = Path(__file__).resolve().parent / sys.argv[1]
    recorded, fresh = here / "recorded", here / "out"
    status = 0
    for record in sorted(recorded.glob("*.txt")):
        twin = fresh / record.name
        if not twin.exists():
            print(f"{record.name}: no fresh twin in out")
            continue
        old = normalize(record.read_text(encoding="utf-8").splitlines())
        new = normalize(twin.read_text(encoding="utf-8").splitlines())
        if old == new:
            print(f"{record.name}: the same after normalizing")
            continue
        status = 1
        print(f"{record.name}: differs")
        for line in difflib.unified_diff(old, new, "recorded", "out", lineterm="", n=0):
            print("  " + line)
    for twin in sorted(fresh.glob("*.txt")):
        if not (recorded / twin.name).exists():
            print(f"{twin.name}: only in out")
    return status


if __name__ == "__main__":
    sys.exit(main())
