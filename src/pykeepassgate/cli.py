"""The command line, which does nothing the library cannot.

Every command here is a few lines over `pykeepassgate.api`, deliberately: if the command line could
do something a program could not, the library would be the wrong shape. So this parses arguments,
calls one function, and prints what came back.

**It never prints a credential.** The invitation is read from an argument or from standard
input, and what is printed afterwards is what the credentials are, not what they hold. A
protected field is a different thing: `show` prints one when the gate answers with it, which
is when the rule granted `P`, and prints `(withheld)` when it did not.

Exit codes are the point of a command line: `0` worked, `1` the gate refused or could not be
reached, `2` the arguments were wrong. A script can branch on that without reading English.
"""

from __future__ import annotations

import argparse
import sys
from pathlib import Path

from . import api
from .errors import GateError

#: Everything went wrong that this program knows how to be wrong about.
FAILED = 1

#: Where `pair` dials without `--port`: the library's own default, so the two cannot differ.
DEFAULT_PORT = api.DEFAULT_PORT


def main(argv: list[str] | None = None) -> int:
    """Run one command and return what the shell should see."""
    parser = _parser()
    arguments = parser.parse_args(argv)

    try:
        return int(arguments.run(arguments))
    except GateError as error:
        # The exception hierarchy already says what happened in a sentence a person can act on, so
        # the command line adds nothing to it beyond the class name for anybody grepping.
        print(f"{type(error).__name__}: {error}", file=sys.stderr)
        return FAILED
    except FileNotFoundError as error:
        print(str(error), file=sys.stderr)
        return FAILED


def _parser() -> argparse.ArgumentParser:
    parser = argparse.ArgumentParser(
        prog="pykeepassgate",
        description="The reference client for KeePassGate.",
    )

    commands = parser.add_subparsers(dest="command", required=True)

    pairing = commands.add_parser(
        "pair",
        help="become a client of a database, using an invitation from the KPG tab",
        description=(
            "Pairs with a KeePass over an invitation. The operator has to approve it on screen, "
            "so this waits, and what it stores is what they approved rather than what was asked."
        ),
    )

    pairing.add_argument(
        "--port",
        type=int,
        default=DEFAULT_PORT,
        help=f"the port the gate is listening on, {DEFAULT_PORT} unless its operator chose another",
    )
    pairing.add_argument("--name", required=True, help="what this client should be called")
    pairing.add_argument(
        "--profile-name",
        action="append",
        default=[],
        dest="profiles",
        metavar="PROFILE",
        help="a profile to ask for; repeat for more, and the operator may take any of them away",
    )

    pairing.add_argument(
        "--invitation",
        help="the invitation from the tab. Omit it and it is read from standard input instead.",
    )

    _shared(pairing)
    pairing.set_defaults(run=_pair)

    status = commands.add_parser(
        "status",
        help="ask the gate whether it is listening, unlocked, and still admitting this client",
    )

    _shared(status)
    status.set_defaults(run=_status)

    who = commands.add_parser("whoami", help="say what this client is, without connecting")
    _shared(who)
    who.set_defaults(run=_whoami)

    groups = commands.add_parser(
        "groups",
        help="list the groups this client may see",
        description=(
            "Anchored at the root, so it needs `list` there. Name a group to walk one level "
            "instead, which is a different question and a different grant."
        ),
    )

    groups.add_argument("--under", help="a group identifier, or root, to list one level below")
    _shared(groups)
    groups.set_defaults(run=_groups)

    entries = commands.add_parser(
        "entries",
        help="list the entries this client may see",
    )

    entries.add_argument(
        "--group",
        help="a group identifier, or root, for that group's own entries",
    )
    _shared(entries)
    entries.set_defaults(run=_entries)

    show = commands.add_parser(
        "show",
        help="read one entry, secrets included",
        description=(
            "Prints every string the entry holds. A protected one arrives only when the rule "
            "grants `protected`. Redirect it or read it directly: this is the one command "
            "that puts a password on a terminal."
        ),
    )

    show.add_argument("uuid", help="the entry's identifier")
    _shared(show)
    show.set_defaults(run=_show)

    add = commands.add_parser("add", help="create an entry in a group")
    add.add_argument("--group", default=api.ROOT, help="where to put it, by identifier or root")
    add.add_argument("--title", help="what to call it")
    add.add_argument("--username", help="the user name")
    add.add_argument("--url", help="the address")
    add.add_argument("--notes", help="the notes")
    add.add_argument(
        "--password",
        action="store_true",
        help="read the password from standard input, so it stays out of the process list",
    )

    _shared(add)
    add.set_defaults(run=_add)

    remove = commands.add_parser("remove", help="delete an entry")
    remove.add_argument("uuid", help="the entry's identifier")
    _shared(remove)
    remove.set_defaults(run=_remove)

    files = commands.add_parser("attachments", help="list an entry's attachments")
    files.add_argument("uuid", help="the entry's identifier")
    _shared(files)
    files.set_defaults(run=_attachments)

    fetch = commands.add_parser("fetch", help="write one attachment to a file")
    fetch.add_argument("uuid", help="the entry's identifier")
    fetch.add_argument("name", help="the attachment's name")
    fetch.add_argument("--to", type=Path, required=True, help="where to write it")
    _shared(fetch)
    fetch.set_defaults(run=_fetch)

    return parser


def _shared(parser: argparse.ArgumentParser) -> None:
    # One slot, so saying it twice is an error rather than a silent precedence. Both name
    # the directory an identity lives in. `--profile` chooses the usual place for a name and
    # `--directory` gives one outright, and `pair --directory D --profile work` used to store
    # where `status --profile work` would not look. Exit code 2, which the module docstring
    # already reserves for arguments that are wrong.
    where = parser.add_mutually_exclusive_group()
    where.add_argument(
        "--profile",
        default="default",
        help="which stored identity to use, since a client can pair with more than one database",
    )

    where.add_argument(
        "--directory",
        type=Path,
        help="where the identity is stored, if not the usual place for this platform",
    )

    parser.add_argument(
        "--timeout",
        type=float,
        default=None,
        help="seconds to wait for the gate, if not the default for what is being done",
    )


def _waiting(arguments: argparse.Namespace) -> dict[str, float]:
    """What `--timeout` asked for, or nothing so each call keeps its own default.

    Named rather than written out at each call because it was written out at one: every command
    took the flag and only pairing read it, so `--timeout` on a read was accepted, ignored, and
    reported nowhere.
    """
    return {} if arguments.timeout is None else {"timeout": arguments.timeout}


def _pair(arguments: argparse.Namespace) -> int:
    invitation = arguments.invitation
    if not invitation:
        # Read rather than argued, so the token does not end up in a shell history or in the
        # process list, where anything running as this user can read it.
        invitation = sys.stdin.readline().strip()

    # A pairing waits on a person, so it keeps its own longer default unless the caller
    # says otherwise: see `transport.PAIRING_TIMEOUT`.
    waiting = _waiting(arguments)

    credentials = api.pair(
        port=arguments.port,
        invitation=invitation,
        name=arguments.name,
        profiles=list(arguments.profiles),
        directory=arguments.directory,
        profile=arguments.profile,
        **waiting,
    )

    print(f"paired as {credentials.subject} with database {credentials.database}")
    print(f"profiles approved: {', '.join(credentials.profiles) or 'none'}")
    print(f"stored in {credentials.directory}")
    return 0


def _status(arguments: argparse.Namespace) -> int:
    client = api.GateClient.open(
        arguments.directory, arguments.profile, **_waiting(arguments)
    )
    status = client.status()
    print(f"gate {status.version} serving database {status.database}")
    return 0 if status.ok else FAILED


def _whoami(arguments: argparse.Namespace) -> int:
    where = arguments.directory or api.default_directory(arguments.profile)
    credentials = api.load(where)
    print(f"subject:  {credentials.subject}")
    print(f"database: {credentials.database}")
    print(f"profiles: {', '.join(credentials.profiles) or 'none'}")
    print(f"expires:  {credentials.expires}")
    print(f"port:     {credentials.port}")
    print(f"stored:   {credentials.directory}")
    return 0


def _groups(arguments: argparse.Namespace) -> int:
    client = api.GateClient.open(
        arguments.directory, arguments.profile, **_waiting(arguments)
    )
    found = client.subgroups(arguments.under) if arguments.under else client.groups()

    for group in found:
        print(f"{group.uuid}  {group.name}")

    return 0


def _entries(arguments: argparse.Namespace) -> int:
    client = api.GateClient.open(
        arguments.directory, arguments.profile, **_waiting(arguments)
    )

    for entry in client.entries(arguments.group):
        # A withheld title is not a title. Saying so beats printing the word "None".
        print(f"{entry.uuid}  {entry.title if entry.title is not None else '(withheld)'}")

    return 0


def _show(arguments: argparse.Namespace) -> int:
    client = api.GateClient.open(
        arguments.directory, arguments.profile, **_waiting(arguments)
    )
    entry = client.entry(arguments.uuid)

    print(f"uuid:  {entry.uuid}")
    print(f"group: {entry.group}")

    for name, value in sorted((entry.fields or {}).items()):
        guarded = " (protected)" if name in entry.protected else ""
        # A withheld value is not a value. Printing it as one would put the word "None" where a
        # password goes, which reads exactly like a password somebody chose badly.
        shown = "(withheld)" if value is None else value
        print(f"{name}: {shown}{guarded}")

    return 0


def _add(arguments: argparse.Namespace) -> int:
    client = api.GateClient.open(
        arguments.directory, arguments.profile, **_waiting(arguments)
    )

    password = None
    if arguments.password:
        # Read rather than argued, for the same reason the invitation is: an argument is in the
        # process list, where anything running as this user can read it.
        password = sys.stdin.readline().rstrip("\n")

    uuid = client.create_entry(
        group=arguments.group,
        title=arguments.title,
        username=arguments.username,
        password=password,
        url=arguments.url,
        notes=arguments.notes,
    )

    print(uuid)
    return 0


def _remove(arguments: argparse.Namespace) -> int:
    client = api.GateClient.open(
        arguments.directory, arguments.profile, **_waiting(arguments)
    )
    client.delete_entry(arguments.uuid)
    print(f"removed {arguments.uuid}")
    return 0


def _attachments(arguments: argparse.Namespace) -> int:
    client = api.GateClient.open(
        arguments.directory, arguments.profile, **_waiting(arguments)
    )

    for attachment in client.attachments(arguments.uuid):
        print(f"{attachment.size:>10}  {attachment.name}")

    return 0


def _fetch(arguments: argparse.Namespace) -> int:
    client = api.GateClient.open(
        arguments.directory, arguments.profile, **_waiting(arguments)
    )
    attachment = client.attachment(arguments.uuid, arguments.name)

    arguments.to.write_bytes(attachment.content or b"")
    print(f"wrote {len(attachment.content or b'')} bytes to {arguments.to}")
    return 0


if __name__ == "__main__":  # pragma: no cover, the module entry point
    raise SystemExit(main())
