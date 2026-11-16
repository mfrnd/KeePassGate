"""Probe for the critique of option store-entries, key store-entries-security-and-fit.

Question: when pykeepass deletes an entry, does the saved file carry a DeletedObjects record
naming it? Invented content only, a throwaway password, files in the work directory given.
"""

import shutil
import sys
from pathlib import Path

from pykeepass import create_database, PyKeePass

work = Path(sys.argv[1])
out = Path(sys.argv[2])
password = "invented-throwaway"
lines = []

a = work / "A.kdbx"
kp = create_database(str(a), password=password)
store = kp.add_group(kp.root_group, "KeePassGate")
rev = kp.add_entry(store, "KPG revoked client 9b1c2d3e", "", "")
rev.set_custom_property("KPG.Kind", "revoked")
rev.set_custom_property("KPG.Data", '{"v":"1","certificate":"9b1c"}')
kp.save()
uuid_hex = rev.uuid.hex.upper()
shutil.copyfile(a, work / "A-before-delete.kdbx")

kp = PyKeePass(str(a), password=password)
found = kp.find_entries(title="KPG revoked client 9b1c2d3e", first=True)
lines.append(f"before delete: entry {uuid_hex} present={found is not None}")
kp.delete_entry(found)
kp.save()

kp = PyKeePass(str(a), password=password)
deleted = kp.tree.findall(".//DeletedObjects/DeletedObject")
lines.append(f"after delete and save: entry present={kp.find_entries(title='KPG revoked client 9b1c2d3e', first=True) is not None}")
lines.append(f"DeletedObject elements in the file: {len(deleted)}")
lines.append(f"pykeepass version: {__import__('pykeepass').__version__ if hasattr(__import__('pykeepass'), '__version__') else 'unknown'}")
out.write_text("\n".join(lines) + "\n", encoding="utf-8")
