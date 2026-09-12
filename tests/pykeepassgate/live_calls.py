"""The calls the end to end tier makes through the library, for what the command line leaves out.

`LiveReferenceClientTests` runs PyKeePassGate against a real KeePass, through `uv` from the
repository, and most of what it asks goes through the command line. Reading one field through the
field route and changing a password have no command of their own, so this asks them through
`pykeepassgate.api`, as any program built on the client would: it prints what came back, or the
refusal under its class's name as the command line prints one, and exits 0 or 1.

It is here rather than in the package because nothing but that fixture needs it, and here rather
than written into the fixture because a program in a string is a program nothing checks: this one
is held to the same style and types as the client, and `test_live_calls.py` runs it.
"""

from __future__ import annotations

import argparse
import sys
from pathlib import Path

from pykeepassgate import api


def main(argv: list[str] | None = None) -> int:
    """Make one call for the identity stored in `--directory`, and say what it came to."""
    parser = argparse.ArgumentParser(prog="live_calls")
    parser.add_argument("--directory", type=Path, required=True)
    calls = parser.add_subparsers(dest="call", required=True)

    field = calls.add_parser("field", help="print one string, read through the field route")
    field.add_argument("uuid")
    field.add_argument("name")

    password = calls.add_parser(
        "password", help="set an entry's password to the line on standard input"
    )
    password.add_argument("uuid")

    arguments = parser.parse_args(argv)

    try:
        client = api.GateClient.open(arguments.directory)
        if arguments.call == "field":
            print(client.field(arguments.uuid, arguments.name).value)
        else:
            client.update_entry(arguments.uuid, password=sys.stdin.readline().rstrip("\n"))
            print("updated")
    except api.GateError as error:
        print(f"{type(error).__name__}: {error}", file=sys.stderr)
        return 1

    return 0


if __name__ == "__main__":
    raise SystemExit(main())
