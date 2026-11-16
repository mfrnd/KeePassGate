"""pykeepass probe over invented databases.

pk.py resave <in> <out>              open and save unchanged
pk.py inject <in> <out>              add an unknown element to Meta, to group G and to entry E, and save
pk.py count <in>                     count the unknown elements and print what pykeepass sees
"""
import sys

import pykeepass
from lxml import etree
from pykeepass import PyKeePass


def main() -> None:
    action = sys.argv[1]
    if action == "resave":
        kp = PyKeePass(sys.argv[2], password="test")
        kp.save(sys.argv[3])
        print("pykeepass", pykeepass.__version__, "resaved", "version", kp.version)
    elif action == "inject":
        kp = PyKeePass(sys.argv[2], password="test")
        meta = kp.tree.find("Meta")
        etree.SubElement(meta, "KPGUnknown").text = "meta-x"
        g = kp.find_groups(name="G", first=True)
        etree.SubElement(g._element, "KPGUnknown").text = "group-x"
        e = kp.find_entries(title="E", first=True)
        etree.SubElement(e._element, "KPGUnknown").text = "entry-x"
        kp.save(sys.argv[3])
        print("injected 3 unknown elements")
    elif action == "count":
        kp = PyKeePass(sys.argv[2], password="test")
        n = len(kp.tree.findall(".//KPGUnknown"))
        e = kp.find_entries(title="E", first=True)
        print(sys.argv[2].split("\\")[-1], "unknown elements:", n,
              "| E custom_properties:", sorted(e.custom_properties.keys()),
              "| history:", len(e.history), "| kdbx", kp.version)


main()
