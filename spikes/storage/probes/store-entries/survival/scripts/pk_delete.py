"""pykeepass deletions over invented databases (password "test").

pk_delete.py entry <in> <out> <title>   delete the entry with that title and save
pk_delete.py group <in> <out> <name>    delete the group with that name and save
"""
import sys

import pykeepass
from pykeepass import PyKeePass


def deleted_count(kp: PyKeePass) -> int:
    node = kp.tree.find("Root/DeletedObjects")
    return 0 if node is None else len(node)


def main() -> None:
    action, src, dst, name = sys.argv[1:5]
    kp = PyKeePass(src, password="test")
    before = deleted_count(kp)
    if action == "entry":
        e = kp.find_entries(title=name, first=True)
        kp.delete_entry(e)
    else:
        g = kp.find_groups(name=name, first=True)
        kp.delete_group(g)
    kp.save(dst)
    after = deleted_count(PyKeePass(dst, password="test"))
    print("pykeepass", pykeepass.__version__, action, repr(name), "deleted; deletion records", before, "->", after)


main()
