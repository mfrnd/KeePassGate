"""Delete one entry by UUID with pykeepass and save in place, over an invented database.

pk_del.py <file> <password> <uuid hex>
"""
import sys
import uuid

import pykeepass
from pykeepass import PyKeePass


def records(kp: PyKeePass) -> int:
    node = kp.tree.find("Root/DeletedObjects")
    return 0 if node is None else len(node)


def main() -> None:
    path, password, hex_id = sys.argv[1:4]
    kp = PyKeePass(path, password=password)
    before = records(kp)
    entry = kp.find_entries(uuid=uuid.UUID(hex=hex_id), first=True)
    if entry is None:
        print("no such entry")
        sys.exit(1)
    kp.delete_entry(entry)
    kp.save()
    after = records(PyKeePass(path, password=password))
    print("pykeepass", pykeepass.__version__, "deleted", entry.title, "; deletion records", before, "->", after)


if __name__ == "__main__":
    main()
