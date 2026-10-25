"""Classify `pykeepass`'s public surface against what the gate serves.

Four classes, because a program holds more than the database: `PyKeePass` itself, and the
`Entry`, `Group` and `Attachment` objects it hands back, which are where most of the work happens.

The classification lives in `surface.json` and this reads the library's own `dir()`, so a release
that adds or removes a member fails here rather than leaving the table quietly wrong. That is the
whole point: the answer to "could PyKeePassGate be a pykeepass backend" is a per member question,
and per member questions rot.

Run it with the library present and nothing else arranged:

    uv run --no-project --with pykeepass python surface.py
"""

from __future__ import annotations

import collections
import inspect
import json
import pathlib
import sys

import pykeepass
from pykeepass.attachment import Attachment
from pykeepass.entry import Entry
from pykeepass.group import Group

HERE = pathlib.Path(__file__).parent

CLASSES = {
    "PyKeePass": pykeepass.PyKeePass,
    "Entry": Entry,
    "Group": Group,
    "Attachment": Attachment,
}

VERDICTS = ("served", "served-differently", "needs-routes", "refused")


def public_surface(cls: type) -> dict[str, str]:
    """Every public member of a class, and whether it is a property or callable."""
    kinds = {}
    for name in dir(cls):
        if name.startswith("_"):
            continue
        attr = inspect.getattr_static(cls, name)
        kinds[name] = "property" if isinstance(attr, property) else "method"
    return kinds


def main() -> int:
    table = json.loads((HERE / "surface.json").read_text(encoding="utf-8"))
    print(f"measured against: {table['measured_against']}")
    failed = False

    for class_name, cls in CLASSES.items():
        classified = table["classes"][class_name]
        actual = public_surface(cls)

        missing = sorted(set(actual) - set(classified))
        stale = sorted(set(classified) - set(actual))
        unknown = sorted(name for name, (verdict, _) in classified.items() if verdict not in VERDICTS)
        counts = collections.Counter(verdict for verdict, _ in classified.values())

        print()
        print(f"{class_name}: {len(actual)} public members")
        for verdict in VERDICTS:
            print(f"  {verdict:<19} {counts[verdict]}")

        for heading, names in (
            ("unclassified, so the table is out of date", missing),
            ("classified but no longer present", stale),
            ("classified with a verdict that does not exist", unknown),
        ):
            if names:
                failed = True
                print(f"  {heading}:")
                for name in names:
                    print(f"    {name}")

    return 1 if failed else 0


if __name__ == "__main__":
    sys.exit(main())
