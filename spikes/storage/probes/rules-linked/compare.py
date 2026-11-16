"""Compares every recorded output of this family with the fresh one a run wrote beside it.

Written for this branch and measuring nothing: it reads `recorded` and `out` under `design`,
`survival` and `security-and-fit`, masks the one thing a run cannot repeat, and prints every other
difference as a unified diff. What it masks:

- the moment of the run, an ISO 8601 time in UTC, which the first line of `keepass.txt`, of each
  `xc-*.txt`, of `survival.txt` and of `probe.txt` prints. Every other time the probes print is one
  they set themselves, on 1 January 2026, and is compared;
- in `survival/signatures.txt`, the path of the KeePass.exe whose signature was checked: the record
  names the copy the agent loaded from its own `bin`, a run names the pinned `.keepass` file, which
  has the same SHA-256. The paths of `keepassxc-cli.exe` are compared.

Line endings are not compared: the design's probe writes some lines with LF inside lines it ends
with CRLF, and the repository normalizes the endings of what it stores.

It also checks the one claim that is an equality of files: the four `xc-*.txt` of the design are
the same line for line once the release's name is taken out, in the record and in the run.

Usage, from the branch root: uv run --no-project python -I -X utf8 <this file>
The exit code is 0 when nothing differs.
"""

import difflib
import pathlib
import re
import sys

FAMILY = pathlib.Path(__file__).resolve().parent
PAPERS = ("design", "survival", "security-and-fit")
RELEASES = ("2.4.2", "2.6.6", "2.7.12", "2.8.0")
MOMENT = re.compile(r"\d{4}-\d\d-\d\dT\d\d:\d\d:\d\d(?:\.\d+)?Z")
KEEPASS = re.compile(r"^\S+\\KeePass\.exe ")


def read(path: pathlib.Path) -> list[str]:
    lines = [MOMENT.sub("<the run>", line) for line in path.read_text(encoding="utf-8").splitlines()]
    if path.parent.parent.name == "survival" and path.name == "signatures.txt":
        lines = [KEEPASS.sub("<KeePass.exe> ", line) for line in lines]
    return lines


def compare() -> int:
    status = 0
    for paper in PAPERS:
        for recorded in sorted((FAMILY / paper / "recorded").iterdir()):
            fresh = FAMILY / paper / "out" / recorded.name
            name = f"{paper}/{recorded.name}"
            if not fresh.exists():
                print(f"{name}: no fresh output in out to compare")
                status = 1
                continue
            diff = list(
                difflib.unified_diff(
                    read(recorded), read(fresh), f"{paper}/recorded/{recorded.name}",
                    f"{paper}/out/{recorded.name}", lineterm="",
                )
            )
            if diff:
                status = 1
                print("\n".join(diff))
            else:
                print(f"{name}: the same, once masked")
    return status


def releases_alike() -> int:
    status = 0
    for where in ("recorded", "out"):
        files = [FAMILY / "design" / where / f"xc-{release}.txt" for release in RELEASES]
        if not all(path.exists() for path in files):
            print(f"design/{where}: not every xc-*.txt is there")
            status = 1
            continue
        bodies = [
            [line.replace(release, "<release>") for line in read(path)[1:]]
            for path, release in zip(files, RELEASES)
        ]
        alike = all(body == bodies[0] for body in bodies[1:])
        print(f"design/{where}: the four xc-*.txt alike once the release is taken out: {alike}")
        if not alike:
            status = 1
    return status


sys.exit(compare() | releases_alike())
