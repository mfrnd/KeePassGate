"""Reads the raw XML of each database and prints, for the entries named, every history item's time
and the names of its custom string fields, so that no KeePassLib load-time maintenance is involved."""
import sys

from pykeepass import PyKeePass

STANDARD = {"Title", "UserName", "Password", "URL", "Notes"}


def fields(e):
    return sorted(s.findtext("Key") for s in e.findall("String") if s.findtext("Key") not in STANDARD)


def title(e):
    t = [s.findtext("Value") for s in e.findall("String") if s.findtext("Key") == "Title"]
    return t[0] if t else "?"


names = sys.argv[1].split(",")
for f in sys.argv[2:]:
    kp = PyKeePass(f, password="probe-only")
    for e in kp.tree.getroot().iter("Entry"):
        if e.getparent().tag != "Group" or title(e) not in names:
            continue
        h = e.find("History")
        items = [] if h is None else list(h)
        times = [i.find("Times").findtext("LastModificationTime") for i in items]
        bins = [len(i.findall("Binary")) for i in items]
        print(f"{f.split(chr(92))[-2]}/{f.split(chr(92))[-1]} {title(e)}: current={fields(e)} history={len(items)}")
        for i, t, b in zip(items, times, bins):
            print(f"    {t} {fields(i)} binaries={b}")
