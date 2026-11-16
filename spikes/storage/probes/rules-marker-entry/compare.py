"""Compare every recorded output of this family with the fresh one a run wrote beside it.

Written for this branch and measuring nothing: it reads `recorded` and `out` under `design`,
`survival` and `security-and-fit`, masks what a run cannot repeat, and prints what is left of each
difference. What it masks, and why each is noise:

- identifiers of 32 hex digits, and their first 8 digits where a probe prints only those: every
  probe makes its groups with random identifiers, and a rule entry's identifier is derived from its
  group's, so both change on every run;
- a number of milliseconds or microseconds: a timing, which `RUN.md` compares by hand;
- the first line of `dump-A.txt`, which names the file the dump read, by its full path.

It also checks the one claim that is an equality of files: the dump of copy A after a plain
KeePassXC save equals the dump of copy A before it, for each release, in the record and in the run.

Usage, from the branch root: uv run --no-project python -I -X utf8 <this file>
"""

import difflib
import pathlib
import re
import sys

FAMILY = pathlib.Path(__file__).resolve().parent
PAPERS = ("design", "survival", "security-and-fit")
RELEASES = ("2.4.2", "2.6.6", "2.7.12", "2.8.0")
TIMING = re.compile(r"[0-9]+[,.][0-9]+ (ms|microseconds)")


def read(path: pathlib.Path) -> list[str]:
    return path.read_text(encoding="utf-8-sig").splitlines()


def mask(lines: list[str], name: str) -> list[str]:
    out = []
    for i, line in enumerate(lines):
        if name == "dump-A.txt" and i == 0 and line.startswith("# "):
            line = "# <the file>"
        line = re.sub(r"\b[0-9A-F]{32}\b", "<id>", line)
        line = re.sub(r"\b[0-9A-Fa-f]{8}\b", "<id8>", line)
        line = TIMING.sub(r"<t> \1", line)
        out.append(line)
    return out


def main() -> int:
    missing = 0
    for paper in PAPERS:
        recorded_dir = FAMILY / paper / "recorded"
        fresh_dir = FAMILY / paper / "out"
        for recorded in sorted(recorded_dir.iterdir()):
            label = paper + "/" + recorded.name
            fresh = fresh_dir / recorded.name
            if not fresh.exists():
                print(label + ": no fresh output, the probe has not been run")
                missing += 1
                continue
            a, b = read(recorded), read(fresh)
            if a == b:
                print(label + ": identical")
                continue
            ma, mb = mask(a, recorded.name), mask(b, recorded.name)
            if ma == mb:
                print(label + ": the same once identifiers, timings and paths are masked")
            else:
                print(label + ": DIFFERS once identifiers, timings and paths are masked")
                for d in difflib.unified_diff(ma, mb, "recorded", "fresh", lineterm="", n=0):
                    print("    " + d)
            timings = [(x, y) for x, y in zip(a, b) if TIMING.search(x) or TIMING.search(y)]
            for x, y in timings:
                print("    recorded: " + x)
                print("    fresh:    " + y)
    for where in ("recorded", "out"):
        body = FAMILY / "design" / where / "dump-A-body.txt"
        if not body.exists():
            continue
        for v in RELEASES:
            saved = FAMILY / "design" / where / ("dump-xcsave-" + v + ".txt")
            same = saved.exists() and read(saved) == read(body)
            print("design/" + where + ": KeePassXC " + v + " save left every KPG entry of A "
                  + ("as it was" if same else "CHANGED"))
    return 1 if missing else 0


if __name__ == "__main__":
    sys.exit(main())
