"""Sets up the range measurement in a probe checkout: four edits to Cases.cs, and the comments on them.

The edits put a hunk at the file's first lines, one in the middle, one at its last line, and one that
only removes a line. Each comment covers three lines either side of the line it is about, cut to the
file and to the hunk holding it, which is the rule the review tool will follow. `outside` is one
range that reaches two lines past its hunk, to see what GitHub answers.

    python setup.py <probe checkout>
"""
import json
import re
import subprocess
import sys
from pathlib import Path

HUNK = re.compile(r"^@@ -(\d+)(?:,(\d+))? \+(\d+)(?:,(\d+))? @@")
REACH = 3

root = Path(sys.argv[1])
cases = root / "src/Probe/Cases.cs"
old = cases.read_text(encoding="utf-8").split("\n")[:-1]
removed_at = 102
assert old[removed_at - 1].strip().startswith("// c14"), old[removed_at - 1]
new = old[:1] + ["// range probe: near the first line"] + old[1:60] + [
    "        // range probe: in the middle"] + old[60:removed_at - 1] + old[removed_at:] + [
    "// range probe: at the end of the file"]
cases.write_text("\n".join(new) + "\n", encoding="utf-8", newline="\n")

diff = subprocess.run(["git", "diff", "--unified=3", "--no-color", "HEAD", "--", "src/Probe/Cases.cs"],
                      cwd=root, capture_output=True, text=True, check=True).stdout
right: list[tuple[int, int]] = []
left: list[tuple[int, int]] = []
for line in diff.splitlines():
    if (hunk := HUNK.match(line)) is not None:
        old_start, old_count = int(hunk[1]), int(hunk[2] or "1")
        new_start, new_count = int(hunk[3]), int(hunk[4] or "1")
        left.append((old_start, old_start + old_count - 1))
        right.append((new_start, new_start + new_count - 1))
print("hunks, new side:", right, " old side:", left, " lengths:", len(new), len(old))


def span(line: int, hunks: list[tuple[int, int]], length: int) -> tuple[int, int]:
    for first, last in hunks:
        if first <= line <= last:
            return max(1, line - REACH, first), min(length, line + REACH, last)
    raise SystemExit(f"line {line} is in no hunk")


def comment(what: str, line: int, side: str, label: str) -> dict[str, object]:
    hunks, length = (right, len(new)) if side == "RIGHT" else (left, len(old))
    first, last = span(line, hunks, length)
    made: dict[str, object] = {"path": "src/Probe/Cases.cs", "line": last, "side": side,
                               "body": f"Range probe, {label}: {what}, lines {first} to {last} "
                                       f"around {line}.\n\n<!-- range-probe: {label}:{what} -->"}
    if first != last:
        made.update(start_line=first, start_side=side)
    return made


top = new.index("// range probe: near the first line") + 1
middle = new.index("        // range probe: in the middle") + 1
end = len(new)
sets: dict[str, list[dict[str, object]]] = {}
for label in ("first review", "second review"):
    sets[label] = [comment("near the first line", top, "RIGHT", label),
                   comment("in the middle", middle, "RIGHT", label),
                   comment("at the end of the file", end, "RIGHT", label),
                   comment("a removed line", removed_at, "LEFT", label)]
outside = comment("reaching outside its hunk", middle, "RIGHT", "outside")
hunk_first = next(first for first, last in right if first <= middle <= last)
outside["start_line"] = hunk_first - 2
outside["start_side"] = "RIGHT"
sets["outside"] = [outside]
(root / "measure/comments.json").write_text(json.dumps(sets, indent=1) + "\n", encoding="utf-8",
                                              newline="\n")
for label, made in sets.items():
    for c in made:
        print(label, "|", c["side"], c.get("start_line"), "to", c["line"])
