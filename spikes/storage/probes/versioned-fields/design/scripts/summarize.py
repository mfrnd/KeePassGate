"""Condenses merges.txt: one line per result, the verdicts only, and groups cells that read alike."""
import re
import sys
from collections import OrderedDict

path = sys.argv[1]
cells = OrderedDict()
title = None
for line in open(path, encoding="utf-8-sig"):
    line = line.rstrip("\n")
    if line.startswith("## "):
        title = line[3:]
        cells[title] = []
    elif title is not None and line.strip():
        cells[title].append(line)


def short(lines):
    out = []
    for l in lines:
        l = l.strip()
        if l.startswith("#"):
            continue
        if l.startswith("write entries"):
            m = re.search(r"write entries (\d+) \(version fields (\d+)\), markers (\d+), copies of write entries under other identifiers (\d+), deletion records: the plugin's own (\d+), made elsewhere (\d+)(?: \(entry present again: (\d+)\))?, versions of another database (\d+)", l)
            out.append("W%s/F%s/M%s/copies%s/own-del%s/hand-del%s%s/foreign%s" % (m.group(1), m.group(2), m.group(3), m.group(4), m.group(5), m.group(6), ("(back %s)" % m.group(7)) if m.group(7) else "", m.group(8)))
        elif l.startswith("DAMAGED"):
            out.append("DAMAGED")
        elif l.startswith("CLOSED"):
            out.append("CLOSED")
        elif l.startswith("authority"):
            out.append(re.sub(r"\s+\[.*$", "", l))
        elif l.startswith("client"):
            m = re.match(r"client (\w+): (REFUSED, revoked|admitted|refused, no record that counts)", l)
            n = len(re.findall(r"'KPG", l.split("[record in")[0])) if "revocation in" in l else 0
            out.append("%s=%s%s" % (m.group(1), {"REFUSED, revoked": "REVOKED", "admitted": "admitted", "refused, no record that counts": "norecord"}[m.group(2)], ("x%d" % n) if n else ""))
        elif l.startswith("profile"):
            m = re.match(r"profile (\w+): (on the list|OFF the list)", l)
            out.append("%s=%s" % (m.group(1), "on" if m.group(2) == "on the list" else "OFF"))
        elif l.startswith("rule on"):
            m = re.match(r"rule on (.*?): (.*?)(?:  \[newest in.*)?  \(versions (\d+)\)$", l)
            out.append("rule[%s]=%s" % (m.group(1), m.group(2)))
    return " | ".join(out)


groups = OrderedDict()
for t, lines in cells.items():
    if t.startswith(("signatures", "marks", "inputs")):
        continue
    if "merged into" not in t or "chain" in t:
        print("== " + t)
        # several folds may sit under one title
        block = []
        for l in lines:
            if l.startswith("# ") and block:
                print("   " + short(block)); block = []
            block.append(l)
        if block:
            print("   " + short(block))
        continue
    m = re.match(r"(KeePassLib 2\.62|keepassxc-cli [\d.]+ merge), pair (\w+), (\S+) merged into (\S+?),? (.*)$", t)
    prog, pair, src, dst, rest = m.groups()
    who = prog.replace("keepassxc-cli ", "XC ").replace(" merge", "").replace("KeePassLib 2.62", "KP") + ("" if prog.startswith("keepassxc") else " " + rest)
    key = (pair, "%s into %s" % (src, dst), short(lines))
    groups.setdefault(key, []).append(who)

last = None
for (pair, direction, verdict), who in groups.items():
    if (pair, direction) != last:
        print("\n== pair %s, %s" % (pair, direction))
        last = (pair, direction)
    print("  [" + ", ".join(who) + "]")
    print("     " + verdict)
