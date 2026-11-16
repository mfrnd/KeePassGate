"""Delete, with pykeepass, the rule entry and the ruled record of the group named N.

pykeepass removes an element and records no deletion (others report, read). Invented database,
throwaway password. Usage: pk_delete.py <in.kdbx> <out.kdbx>
"""
import sys

from pykeepass import PyKeePass

src, dst = sys.argv[1], sys.argv[2]
kp = PyKeePass(src, password="probe-only")
n = kp.find_groups(name="N", first=True)
node = "group/" + n.uuid.hex.upper()
gone = []
for e in list(kp.entries):
    kind = e.get_custom_property("KPG.Kind")
    if kind == "rule" and e.get_custom_property("KPG.Node") == node:
        gone.append("rule " + e.uuid.hex[:8])
        kp.delete_entry(e)
    elif kind == "ruled" and node in (e.get_custom_property("KPG.Data") or ""):
        gone.append("ruled " + e.uuid.hex[:8])
        kp.delete_entry(e)
kp.save(dst)
print("pykeepass deleted:", ", ".join(gone))
