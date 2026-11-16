
import re, sys
from collections import Counter, defaultdict
bound = {"G00": {"list"}, "G01": {"list"}, "G02": {"read"}, "G03": {"read"}, "G04": {"read"}, "G05": {"read"},
         "G06": {"read", "list"}, "G07": {"read", "list"}, "G08": {"read"}, "G09": {"read"}, "G10": {"read"},
         "G11": {"read"}, "G12": {"list"}, "G13": {"list"}, "G14": {"list"}, "G15": {"read"}, "G16": {"read", "list"},
         "G12x": {"read"}, "G13x": {"read"}, "G14x": {"read"}}
cell = None
seen = defaultdict(Counter)
wider = []
cells = Counter()
for raw in open(sys.argv[1], encoding="utf-8-sig"):
    line = raw.rstrip("\n")
    if line.startswith("## "):
        cell = line[3:]
        continue
    merged = cell is not None and ("merge" in cell or "merged" in cell or "back and forth" in cell)
    m = re.match(r"^  (G\d\d)-\S+ .*? reader=(\S+)", line)
    key = None
    if m:
        key, value = m.group(1), m.group(2)
    else:
        m = re.match(r"^    entry X-(G\d\d) .* reader=(\S+)", line)
        if m:
            key, value = m.group(1) + "x", m.group(2)
    if key is None or not merged:
        continue
    cells[cell] += 1
    seen[key][value] += 1
    if value in ("REFUSED", "nothing", "out-of-reach"):
        continue
    if not set(value.split("+")) <= bound[key]:
        wider.append((cell, key, value))
print("results of merges read: %d cells, %d verdicts" % (len(cells), sum(cells.values())))
newids = [c for c in cells if "CreateNewUuids" in c]
print("  of which imports with new identifiers: %d cells, %d verdicts" % (len(newids), sum(cells[c] for c in newids)))
print("  merges that match identifiers: %d cells, %d verdicts, wider there: %d" % (len(cells) - len(newids), sum(cells[c] for c in cells if c not in newids), len([w for w in wider if "CreateNewUuids" not in w[0]])))
kinds = {}
for c in cells:
    k = "KeePassLib" if c.startswith("KeePassLib") else ("chain" if c.startswith("chain") else ("back and forth" if "back and forth" in c else ("then hand deletion" if "deleted for good" in c else "keepassxc-cli merge")))
    kinds.setdefault(k, [0, 0]); kinds[k][0] += 1; kinds[k][1] += cells[c]
print("  by kind:", kinds)
for key in sorted(seen):
    print("  %-5s at most %-10s got %s" % (key, "+".join(sorted(bound[key])), dict(seen[key])))
print("wider than the last decision: %d" % len(wider))
for w in wider:
    print("   ", w)
