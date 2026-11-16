# Shortens out/summary.txt: one line per verdict with the cells abbreviated.
import re, sys
def short(c):
    c = c.strip()
    m = re.match(r"KeePassLib 2.62, (\w) merged into (\w), (\w+)", c)
    if m: return "KP>%s:%s" % (m.group(2), {"Synchronize":"Sync","OverwriteIfNewer":"IfNewer","OverwriteExisting":"Overwrite","KeepExisting":"Keep","CreateNewUuids":"NewIDs"}[m.group(3)])
    m = re.match(r"keepassxc-cli ([\d.]+) merge, (\w) merged into (\w)", c)
    if m: return "XC%s>%s" % (m.group(1), m.group(3))
    m = re.match(r"keepassxc-cli ([\d.]+) add", c)
    if m: return "XCsave%s" % m.group(1)
    m = re.match(r"chain: keepassxc-cli ([\d.]+) merged (\w) into (\w), then", c)
    if m: return "XC%s>%s+KPsync" % (m.group(1), m.group(3))
    m = re.match(r"keepassxc-cli ([\d.]+), back and forth.*\[(.*)\]", c)
    if m: return "XC%s-3x:%s" % (m.group(1), m.group(2).split("/")[1][0])
    m = re.match(r"inputs \[base/(.*)\.kdbx\]", c)
    if m: return "input:" + m.group(1)
    m = re.match(r"KeePassLib 2.62 DeleteDuplicateEntries \[dedupe/(.*)\.kdbx\]", c)
    if m: return "dedupe:" + m.group(1)
    return c
name = None; verdict = None; out = {}
for line in open(sys.argv[1], encoding="utf-8"):
    line = line.rstrip("\n")
    if line.startswith("== "): name = line[3:]; out[name] = {}
    elif line.startswith("        "): out[name][verdict].append(short(line))
    elif line.startswith("   "): verdict = line.strip(); out[name][verdict] = []
for name in out:
    print("== " + name)
    for v, cells in out[name].items():
        print("   %-70s <- (%d) %s" % (v, len(cells), ", ".join(cells)))
