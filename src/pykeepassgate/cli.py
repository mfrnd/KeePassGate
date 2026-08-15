"""The command line, which does nothing the library cannot.

Every command here is a few lines over `pykeepassgate.api`, deliberately: if the command line could
do something a program could not, the library would be the wrong shape. So this parses arguments,
calls one function, and prints what came back.

**It never prints a credential.** The invitation is read from an argument or from standard
input, and what is printed afterwards is what the credentials are, not what they hold. A
protected field is a different thing: TODO(step 15): `show` arrives with the routes that read
one, and prints it when the gate answers with it, which is when the rule granted `P`, and
`(withheld)` when it did not.

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


def _pair(arguments: argparse.Namespace) -> int:
    invitation = arguments.invitation
    if not invitation:
        # Read rather than argued, so the token does not end up in a shell history or in the
        # process list, where anything running as this user can read it.
        invitation = sys.stdin.readline().strip()

    # A pairing waits on a person, so it keeps its own longer default unless the caller
    # says otherwise: see `transport.PAIRING_TIMEOUT`.
    waiting = (
        {} if arguments.timeout is None else {"timeout": arguments.timeout}
    )

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
    client = (
        api.GateClient.open(arguments.directory, arguments.profile)
        if arguments.timeout is None
        else api.GateClient.open(
            arguments.directory, arguments.profile, timeout=arguments.timeout
        )
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


if __name__ == "__main__":  # pragma: no cover, the module entry point
    raise SystemExit(main())
