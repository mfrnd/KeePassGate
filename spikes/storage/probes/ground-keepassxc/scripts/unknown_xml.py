"""Puts elements no KDBX reader knows into a database, or reports which of them are still there.

make <in.kdbx> <out.kdbx>: copies the database, adding KpgUnknown* elements at six places.
check <file.kdbx>...: prints, per file, which of those elements a reader finds.
"""
import sys

from lxml import etree
from pykeepass import PyKeePass

PASSWORD = "probe-only"
NAMES = ["KpgUnknownMeta", "KpgUnknownRoot", "KpgUnknownGroup", "KpgUnknownEntry",
         "KpgUnknownString", "KpgUnknownItem", "KpgUnknownTimes", "KpgUnknownHistoryEntry"]


def make(src: str, dst: str) -> None:
    kp = PyKeePass(src, password=PASSWORD)
    root = kp.tree.getroot()
    meta = root.find("Meta")
    etree.SubElement(meta, "KpgUnknownMeta").text = "m"
    etree.SubElement(root.find("Root"), "KpgUnknownRoot").text = "r"
    kpg = [g for g in root.iter("Group") if g.findtext("Name") == "KPG"][0]
    etree.SubElement(kpg, "KpgUnknownGroup").text = "g"
    entries = [e for e in root.iter("Entry")
               if any(s.findtext("Key") == "Title" and s.findtext("Value") == "cd-both" for s in e.findall("String"))
               and e.getparent().tag == "Group"]
    entry = entries[0]
    etree.SubElement(entry, "KpgUnknownEntry").text = "e"
    etree.SubElement(entry.find("String"), "KpgUnknownString").text = "s"
    etree.SubElement(entry.find("CustomData").find("Item"), "KpgUnknownItem").text = "i"
    etree.SubElement(entry.find("Times"), "KpgUnknownTimes").text = "t"
    hist = entry.find("History")
    if hist is not None and len(hist):
        etree.SubElement(hist[0], "KpgUnknownHistoryEntry").text = "h"
    kp.save(dst)


def check(path: str) -> None:
    kp = PyKeePass(path, password=PASSWORD)
    root = kp.tree.getroot()
    found = [n for n in NAMES if root.find(".//" + n) is not None]
    missing = [n for n in NAMES if n not in found]
    print(f"{path.split(chr(92))[-1]}: kept={found} dropped={missing}")


if __name__ == "__main__":
    if sys.argv[1] == "make":
        make(sys.argv[2], sys.argv[3])
    else:
        for f in sys.argv[2:]:
            check(f)
