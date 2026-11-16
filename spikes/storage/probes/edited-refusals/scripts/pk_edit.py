"""Part 3 of the history extension: the payload edits made by pykeepass, which keeps no history.

    pk_edit.py <dir>

Reads `<dir>/pykeepass.tsv`, which `Edr.exe history` writes (scenario, entry identifier, the
target's field, edit), opens each scenario's copy B, edits the entry as a program built on pykeepass
would, through `set_custom_property` and `delete_custom_property` alone, and saves. A `character`
edit changes the last character of the target's value as `ChangeOne` in `probe/Program.cs` does.
Prints the pykeepass release and, per file, what the entry's history holds afterwards.
"""

import importlib.metadata
import pathlib
import re
import sys

from pykeepass import PyKeePass


def change_one(data, field):
    m = re.search('"' + field + '":"([^"]*)"', data)
    i = m.end(1) - 1
    c = data[i]
    if "0" <= c <= "8" or "a" <= c <= "e" or "A" <= c <= "E" or "g" <= c < "z":
        n = chr(ord(c) + 1)
    else:
        n = {"9": "0", "f": "a", "F": "A"}.get(c, "y")
    return data[:i] + n + data[i + 1:]


def main(d):
    d = pathlib.Path(d)
    print("pykeepass " + importlib.metadata.version("pykeepass"))
    for line in (d / "pykeepass.tsv").read_text(encoding="utf-8").splitlines():
        scenario, uuid, field, edit = line.split("\t")
        kp = PyKeePass(str(d / scenario / "B.kdbx"), password="probe-only")
        entry = next(e for e in kp.entries if e.uuid.hex.upper() == uuid)
        if edit == "character":
            entry.set_custom_property("KPG.Data", change_one(entry.get_custom_property("KPG.Data"), field))
        elif edit == "delete-string":
            entry.delete_custom_property("KPG.Data")
        kp.save()
        print(f"{scenario}: {len(entry.history)} history items")


if __name__ == "__main__":
    main(sys.argv[1])
