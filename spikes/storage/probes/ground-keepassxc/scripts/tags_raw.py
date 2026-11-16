"""set <in> <out> <tags>: writes the raw Tags text of entry 'tag-A' and of group 'GTagsA'.
get <file>...: prints the raw Tags text of both."""
import sys

from pykeepass import PyKeePass


def find(kp):
    root = kp.tree.getroot()
    entry = [e for e in root.iter("Entry") if e.getparent().tag == "Group"
             and any(s.findtext("Key") == "Title" and s.findtext("Value") == "tag-A" for s in e.findall("String"))][0]
    group = [g for g in root.iter("Group") if g.findtext("Name") == "GTagsA"][0]
    return entry, group


if sys.argv[1] == "set":
    kp = PyKeePass(sys.argv[2], password="probe-only")
    entry, group = find(kp)
    entry.find("Tags").text = sys.argv[4]
    group.find("Tags").text = sys.argv[4]
    kp.save(sys.argv[3])
else:
    for f in sys.argv[2:]:
        kp = PyKeePass(f, password="probe-only")
        entry, group = find(kp)
        print(f"{f.split(chr(92))[-1]}: entry={entry.findtext('Tags')!r} group={group.findtext('Tags')!r}")
