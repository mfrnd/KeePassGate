"""Classify `pykeepass`'s public surface against what the gate serves.

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

HERE = pathlib.Path(__file__).parent


def public_surface() -> dict[str, str]:
    """Every public member of `PyKeePass`, and whether it is a property or callable."""
    kinds = {}
    for name in dir(pykeepass.PyKeePass):
        if name.startswith("_"):
            continue
        attr = inspect.getattr_static(pykeepass.PyKeePass, name)
        kinds[name] = "property" if isinstance(attr, property) else "method"
    return kinds


def main() -> int:
    table = json.loads((HERE / "surface.json").read_text(encoding="utf-8"))
    classified = table["members"]
    actual = public_surface()

    missing = sorted(set(actual) - set(classified))
    stale = sorted(set(classified) - set(actual))

    counts = collections.Counter(verdict for verdict, _ in classified.values())

    print(f"measured against: {table['measured_against']}")
    print(f"public members:   {len(actual)}")
    for verdict in ("served", "needs-routes", "refused"):
        print(f"  {verdict:<13} {counts[verdict]}")

    if missing:
        print("\nunclassified, so the table is out of date:")
        for name in missing:
            print(f"  {name} ({actual[name]})")

    if stale:
        print("\nclassified but no longer present:")
        for name in stale:
            print(f"  {name}")

    return 1 if missing or stale else 0


if __name__ == "__main__":
    sys.exit(main())
