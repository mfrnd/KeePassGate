# Collapses out/merges.txt: for every scenario, which cells gave which verdict.
import re
import sys
from collections import OrderedDict, defaultdict

path = sys.argv[1]
cells = OrderedDict()
cell = None
with open(path, encoding="utf-8-sig") as f:
    for raw in f:
        line = raw.rstrip("\n")
        if line.startswith("## "):
            cell = line[3:]
            cells[cell] = defaultdict(list)
            continue
        if cell is None:
            continue
        if line.startswith("# "):
            cells[cell]["__file__"].append(line[2:])
            current_file = line[2:].split(" ")[0]
            continue
        m = re.match(r"^  (G\d\d)-\S+ (?:@'([^']*)' )?: (.*)$", line)
        if m:
            name, where, rest = m.group(1), m.group(2), m.group(3)
            if rest.startswith("GROUP ABSENT"):
                verdict = "group absent"
            else:
                binned = rest.startswith("[in the bin")
                rest2 = re.sub(r"^\[in the bin, out of reach\] ", "", rest)
                v = re.match(r"^(\S+|\{.*?\}) \(([^)]*)\)", rest2)
                verdict = v.group(1) + (" [group in the bin]" if binned else "")
                if "replaced one" in v.group(2):
                    verdict += " +old"
                if where not in ("", None):
                    verdict += " @" + where
            cells[cell][current_file + " " + name].append(verdict)
            continue
        m = re.match(r"^    entry (\S+) in '([^']*)' : own (\S+|\{.*?\}) \((.*?)\); its group (\S+); password (\S+) hist=(\d+)", line)
        if m:
            key = current_file + " " + m.group(1)[2:5] + "x"
            verdict = m.group(3) + (" +old" if "replaced one" in m.group(4) else "") + " in " + m.group(2).split("-")[0] + (" group:" + m.group(5) if m.group(5) != "SILENCE" else "") + " pw:" + m.group(6).split("-")[0]
            cells[cell][key].append(verdict)

rows = defaultdict(lambda: defaultdict(list))
for cell, data in cells.items():
    for key, verdicts in data.items():
        if key == "__file__":
            continue
        file, name = key.split(" ", 1)
        rows[name][" | ".join(verdicts)].append(cell + ("" if len(data["__file__"]) < 2 else " [" + file + "]"))

for name in sorted(rows):
    print("== " + name)
    for verdict, where in rows[name].items():
        print("   " + verdict)
        for w in where:
            print("        " + w)
