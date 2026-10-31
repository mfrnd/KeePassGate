"""Sets up the files measurement on a probe branch taken from `main`, and the reviews posted on it.

One change of each kind a pull request can make to a file: a rename with one line changed, a rename
with none, a copy of a file nothing else changes, new files of a thousand, five thousand and
twenty-five thousand lines, names with a space, an accent and a quote, a deletion, and a last line
with no newline. `measure/each.json` is one review for each place a thread might go, and one with
two ranges that overlap.

    python setup.py <probe checkout> <folder holding measure_each.py>
"""
import json
import subprocess
import sys
from pathlib import Path

root, scripts = Path(sys.argv[1]), Path(sys.argv[2])


def git(*arguments: str, given: str | None = None) -> str:
    return subprocess.run(["git", *arguments], cwd=root, check=True, capture_output=True,
                          text=True, encoding="utf-8", input=given).stdout


def write(name: str, text: str) -> None:
    (root / name).parent.mkdir(parents=True, exist_ok=True)
    (root / name).write_text(text, encoding="utf-8", newline="\n")


git("mv", "probe_py/cases.py", "probe_py/renamed_cases.py")
renamed = (root / "probe_py/renamed_cases.py").read_text(encoding="utf-8").split("\n")
assert renamed[19].startswith("# p4"), renamed[19]
renamed[19] = "# p4: a rule id that is not the one that fires, in a file renamed with this line changed."
write("probe_py/renamed_cases.py", "\n".join(renamed))
git("mv", "README.md", "README-renamed.md")
(root / "src/Probe/Copy.cs").write_bytes((root / "src/Probe/Cases.cs").read_bytes())
for count in (1000, 5000, 25000):
    write(f"measure/lines-{count}.txt", "".join(f"line {n}\n" for n in range(1, count + 1)))
small = "".join(f"// line {n}\n" for n in range(1, 11))
write("measure/a b.cs", small)
write("measure/ü.cs", small)
blob = git("hash-object", "-w", "--stdin", given=small).strip()
# Git for Windows refuses a name NTFS cannot hold, even in the index, unless told not to.
git("-c", "core.protectNTFS=false", "update-index", "--add", "--cacheinfo",
    f"100644,{blob},measure/q\"t.cs")
git("rm", "-q", "tools/filter_sarif.py")
write("measure/noeol.cs", "".join(f"// line {n}\n" for n in range(1, 6)) + "// line 6")
git("rm", "-q", ".github/workflows/codeql.yml", ".github/workflows/old.yml",
    ".github/workflows/suppressions.yml")
(root / "measure/measure_each.py").write_bytes((scripts / "measure_each.py").read_bytes())
write(".github/workflows/measure.yml", (scripts / "measure.yml").read_text(encoding="utf-8"))


def one(label: str, path: str, line: int, side: str = "RIGHT", start: int | None = None) -> dict:
    comment: dict[str, object] = {"path": path, "line": line, "side": side,
                                  "body": f"Files probe: {label}.\n\n<!-- files-probe: {label} -->"}
    if start is not None:
        comment.update(start_line=start, start_side=side)
    return comment


entries = [
    ("renamed, its changed line", [one("renamed, its changed line", "probe_py/renamed_cases.py", 20)]),
    ("renamed, a range in its hunk",
     [one("renamed, a range in its hunk", "probe_py/renamed_cases.py", 23, start=17)]),
    ("renamed, a line outside its hunk",
     [one("renamed, a line outside its hunk", "probe_py/renamed_cases.py", 40)]),
    ("renamed, the old side by its new name",
     [one("renamed, the old side by its new name", "probe_py/renamed_cases.py", 20, "LEFT")]),
    ("renamed, the old side by its old name",
     [one("renamed, the old side by its old name", "probe_py/cases.py", 20, "LEFT")]),
    ("renamed without changes", [one("renamed without changes", "README-renamed.md", 1)]),
    ("copied", [one("copied", "src/Probe/Copy.cs", 10)]),
    ("1000 lines, the last", [one("1000 lines, the last", "measure/lines-1000.txt", 1000)]),
    ("5000 lines, the last", [one("5000 lines, the last", "measure/lines-5000.txt", 5000)]),
    ("25000 lines, the last", [one("25000 lines, the last", "measure/lines-25000.txt", 25000)]),
    ("25000 lines, the first", [one("25000 lines, the first", "measure/lines-25000.txt", 1)]),
    ("a space in the name", [one("a space in the name", "measure/a b.cs", 5)]),
    ("an accent in the name", [one("an accent in the name", "measure/ü.cs", 5)]),
    ("a quote in the name", [one("a quote in the name", 'measure/q"t.cs', 5)]),
    ("deleted, the old side", [one("deleted, the old side", "tools/filter_sarif.py", 10, "LEFT")]),
    ("no newline, the last line", [one("no newline, the last line", "measure/noeol.cs", 6)]),
    ("two ranges that overlap", [one("two ranges that overlap, first", "measure/a b.cs", 6, start=1),
                                 one("two ranges that overlap, second", "measure/a b.cs", 8,
                                     start=3)]),
]
write("measure/each.json", json.dumps([{"label": label, "comments": comments}
                                       for label, comments in entries], indent=1,
                                      ensure_ascii=False) + "\n")
print(len(entries), "reviews,", sum(len(c) for _, c in entries), "comments")
