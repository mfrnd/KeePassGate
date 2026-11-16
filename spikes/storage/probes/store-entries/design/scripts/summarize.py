"""Condenses out/merges.txt: one line per cell, naming what the store holds afterwards."""
import re
import sys

path = sys.argv[1]
cells = []
current = None
for line in open(path, encoding="utf-8-sig"):
    line = line.rstrip("\n")
    if line.startswith("## "):
        current = {"name": line[3:], "version": "", "groups": [], "entries": [], "deleted": [], "strings": set()}
        cells.append(current)
        continue
    if current is None:
        continue
    m = re.match(r"# (\S+) kdbx (\S+)", line)
    if m:
        current["version"] = m.group(2)
        continue
    m = re.match(r"\s+deleted (\w+)", line)
    if m:
        current["deleted"].append(m.group(1))
        continue
    m = re.match(r"\s+G '([^']*)' u=(\w+) .* search=(\w+) autotype=(\w+)", line)
    if m:
        group = m.group(1) + ":" + m.group(2)
        if m.group(1).endswith("KeePassGate"):
            current["groups"].append(group + ":search=" + m.group(3) + ":autotype=" + m.group(4))
        current["where"] = group
        continue
    m = re.match(r"\s+E '(KPG [^']*)' u=(\w+) mod=\S+ hist=(\d+)\S* :: (.*)", line)
    if m:
        title = m.group(1)[4:]
        strings = re.sub(r"\(\d+:\w+\)", "", m.group(4))
        current["strings"].add(strings)
        current["entries"].append(title + "@" + current["where"].split(":")[1][:4] + ("/h" + m.group(3) if m.group(3) != "0" else ""))

for cell in cells:
    if cell["name"] in ("signatures", "inputs"):
        continue
    print("## " + cell["name"] + " | kdbx " + cell["version"])
    print("   groups: " + "; ".join(cell["groups"]))
    print("   entries: " + ", ".join(sorted(cell["entries"])))
    print("   deletion records: " + (", ".join(sorted(cell["deleted"])) or "none"))
    print("   string sets: " + " || ".join(sorted(cell["strings"])))
