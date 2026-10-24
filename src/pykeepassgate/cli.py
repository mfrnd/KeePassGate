"""The command line, which does nothing the library cannot.

Every command here is a few lines over `pykeepassgate.api`, deliberately: if the command line could
do something a program could not, the library would be the wrong shape. So this parses arguments,
calls one function, and prints what came back.

**It never prints a credential.** The invitation is read from an argument or from standard
input, and what is printed afterwards is what the credentials are, not what they hold. A
protected field is a different thing: `show` and `field` print one when the gate answers with
it, which is when the rule granted `P`. When it did not, `show` prints `(withheld)` and `field`
fails, since what `field` prints is taken by a script for the value.

Exit codes are the point of a command line: `0` worked, `1` the gate refused or could not be
reached, `2` the arguments were wrong. A script can branch on that without reading English.

**What a command prints is chosen with `-o`/`--output`**, on every command: `plain`, lines for a
person, and `json`, one document for a program such as `jq`. A choice rather than a `--json`
switch, so that another format is one more choice. Both say the same things under the same rules:
a value the rule withholds is named as withheld and never printed as empty, and what the gate did
not say is left out rather than printed as nothing. Only what a command found is formatted; a
failure is a sentence on standard error and an exit code either way.
"""

from __future__ import annotations

import argparse
import ctypes
import getpass
import importlib
import json
import math
import os
import sys
import unicodedata
from collections.abc import Callable, Iterable
from datetime import datetime, timedelta
from pathlib import Path
from typing import Literal, TypedDict

from . import api, pairing, refs, transport
from .credentials import IDENTITY_FILE
from .errors import GateError, ProtocolError, StorageError
from .model import TITLE, URL

#: Everything went wrong that this program knows how to be wrong about.
FAILED = 1

#: Where `pair` dials without `--port`: the library's own default, so the two cannot differ.
DEFAULT_PORT = api.DEFAULT_PORT

#: What `-o`/`--output` chooses between, the default first: lines for a person, as every command
#: has always printed, and one JSON document for a program.
OUTPUTS = ("plain", "json")


def main(argv: list[str] | None = None) -> int:
    """Run one command and return what the shell should see."""
    parser = _parser()
    arguments = parser.parse_args(argv)

    try:
        return int(arguments.run(arguments))
    except GateError as error:
        # The exception hierarchy already says what happened in a sentence a person can act on, so
        # the command line adds nothing to it beyond the class name for anybody grepping. That
        # covers a profile nothing has paired under, which is `NotPaired`.
        print(f"{type(error).__name__}: {error}", file=sys.stderr)
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
        type=_port,
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
        "--lifetime",
        type=_days,
        metavar="DAYS",
        help=(
            "how long the certificate should last, in whole days from "
            f"{api.SHORTEST_LIFETIME.days} to {api.LONGEST_LIFETIME.days}. The operator picks, "
            "and is offered a year when this is left out."
        ),
    )

    pairing.add_argument(
        "--invitation",
        help=(
            "the invitation from the tab. Omit it and it is read from standard input instead, "
            "asked for at a terminal, showing a * for each character."
        ),
    )
    pairing.add_argument(
        "--allow-shared-directory",
        action="store_true",
        help=(
            "keep the identity in a directory another account owns or may write to, which is "
            "otherwise refused; every other check on the directory still applies"
        ),
    )
    pairing.add_argument(
        "--key-password",
        action="store_true",
        help=(
            "store the key under a password, asked for twice at a terminal without echo before "
            "anything is sent, and asked for at a terminal whenever the key is used after. It "
            "protects copies of the directory, and nothing against a process run as this user."
        ),
    )

    _shared(pairing)
    _dialed(pairing)
    pairing.set_defaults(run=_pair)

    status = commands.add_parser(
        "status",
        help="ask the gate whether it is listening, unlocked, and still admitting this client",
    )

    _shared(status)
    _dialed(status)
    status.set_defaults(run=_status)

    renew = commands.add_parser(
        "renew",
        help="ask the operator for a new certificate in place of this one",
        description=(
            "Asks the operator for a new certificate, which they approve on screen as they did "
            "the pairing, stores it in place of this one, and then gives this one up. Waits while "
            "the operator reads."
        ),
    )
    renew.add_argument(
        "--lifetime",
        type=_days,
        metavar="DAYS",
        help=(
            "how long the new certificate should last, in whole days from "
            f"{api.SHORTEST_LIFETIME.days} to {api.LONGEST_LIFETIME.days}. The operator picks, "
            "and is offered a year when this is left out."
        ),
    )
    renew.add_argument(
        "--allow-shared-directory",
        action="store_true",
        help=(
            "accept the identity's own directory where another account may reach it, as pair "
            "did; the directories a renewal makes beside it are never accepted that way"
        ),
    )

    _shared(renew)
    _dialed(renew)
    renew.set_defaults(run=_renew)

    revoke = commands.add_parser(
        "revoke",
        help="give up this client's admission, and remove what is stored for it",
        description=(
            "Revokes the certificate this client presents, so the database refuses it from the "
            "next connection on, then removes the identity stored for it, which nothing can use "
            "any more. A certificate the gate already refuses, revoked or expired, counts as "
            "revoked, and its files are removed as well. Pairing again is the way back."
        ),
    )

    _shared(revoke)
    _dialed(revoke)
    revoke.set_defaults(run=_revoke)

    # Named as the API names what it answers: `whoami` asks `GET /gate/whoami`, and `identity`
    # reads the identity this client stored, without connecting.
    identity = commands.add_parser(
        "identity", help="say what this client's stored identity is, without connecting"
    )
    _shared(identity)
    identity.set_defaults(run=_identity)

    who = commands.add_parser(
        "whoami",
        help="ask the gate who this client is, and which of its profiles count now",
        description=(
            "Asks the gate who this client is, as the database admitted it: the subject, the "
            "client key, the admission key, when the certificate expires, the profiles the rules "
            "are asked about now, and any the database no longer names, which grant nothing. "
            "identity says what is stored, without connecting."
        ),
    )
    _shared(who)
    _dialed(who)
    who.set_defaults(run=_whoami)

    access = commands.add_parser(
        "access",
        help="ask the gate what this client may do at a group or an entry",
        description=(
            "Prints one rule for each profile this client holds, as the KPG tab shows a rule: "
            "CRUSDLAP, with a dot for each verb or switch the profile is not granted there. A "
            "switch reaches only the verbs on its own line. A node this client may do nothing at "
            "is refused as one that is not there."
        ),
    )
    node = access.add_mutually_exclusive_group(required=True)
    node.add_argument("--group", help="a group identifier, or root")
    node.add_argument("--entry", help="an entry identifier")
    _shared(access)
    _dialed(access)
    access.set_defaults(run=_access)

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
    _dialed(groups)
    groups.set_defaults(run=_groups)

    group = commands.add_parser(
        "group",
        help="read one group, its notes included",
        description="Needs `read` on the group, where listing it needs `list` on the one above.",
    )
    group.add_argument("uuid", help="the group's identifier, or root")
    _shared(group)
    _dialed(group)
    group.set_defaults(run=_group_read)

    add_group = commands.add_parser("add-group", help="create a group inside another")
    add_group.add_argument("--name", required=True, help="what to call it")
    add_group.add_argument(
        "--parent", default=api.ROOT, help="where to put it, by identifier or root"
    )
    add_group.add_argument("--notes", help="the notes")
    _shared(add_group)
    _dialed(add_group)
    add_group.set_defaults(run=_add_group)

    update_group = commands.add_parser(
        "update-group", help="rename a group, or change its notes"
    )
    update_group.add_argument("uuid", help="the group's identifier, or root")
    update_group.add_argument("--name", help="what to call it from now on")
    update_group.add_argument("--notes", help="the notes")
    _conditioned(update_group)
    _shared(update_group)
    _dialed(update_group)
    update_group.set_defaults(run=_update_group)

    remove_group = commands.add_parser(
        "remove-group",
        help="delete a group, which the gate refuses while it holds anything",
    )
    remove_group.add_argument("uuid", help="the group's identifier")
    _conditioned(remove_group)
    _shared(remove_group)
    _dialed(remove_group)
    remove_group.set_defaults(run=_remove_group)

    entries = commands.add_parser(
        "entries",
        help="list the entries this client may see",
    )

    entries.add_argument(
        "--group",
        help="a group identifier, or root, for that group's own entries",
    )
    entries.add_argument(
        "--include-expired",
        action=argparse.BooleanOptionalAction,
        default=True,
        help=(
            "list the entries that have expired along with the rest, which is the default; "
            "--no-include-expired leaves them out, judged by the gate as KeePass judges one"
        ),
    )
    _shared(entries)
    _dialed(entries)
    entries.set_defaults(run=_entries)

    show = commands.add_parser(
        "show",
        help="read one entry, secrets included",
        description=(
            "Prints every string the entry holds, one to a line, with a line break inside one "
            "written as \\n. A protected one arrives only when the rule grants `protected`. "
            "Redirect it or read it directly: this is the one command that puts a password on "
            "a terminal."
        ),
    )

    show.add_argument("uuid", help="the entry's identifier")
    show.add_argument(
        "--resolve",
        action="store_true",
        help=(
            "follow field references and placeholders as KeePass would, reading each entry a "
            "reference names under this client's rules, and fail rather than print a value that "
            "cannot be given as KeePass would give it. Without it, values print as stored."
        ),
    )
    _shared(show)
    _dialed(show)
    show.set_defaults(run=_show)

    add = commands.add_parser("add", help="create an entry in a group")
    add.add_argument("--group", default=api.ROOT, help="where to put it, by identifier or root")
    add.add_argument("--title", help="what to call it")
    add.add_argument("--username", help="the user name")
    add.add_argument("--url", help="the address")
    add.add_argument("--notes", help="the notes")
    add.add_argument(
        "--expires",
        type=_expiry,
        help=(
            "when it expires: a moment with its time zone, such as 2031-02-03T04:05:06Z, or auto "
            "for whatever KeePass's options give a new entry. Left off, it does not expire."
        ),
    )
    add.add_argument(
        "--password",
        action="store_true",
        help=(
            "read the password from standard input, so it stays out of the process list; at a "
            "terminal it is asked for, showing a * for each character"
        ),
    )

    _shared(add)
    _dialed(add)
    add.set_defaults(run=_add)

    update = commands.add_parser("update", help="change an entry's standard fields")
    update.add_argument("uuid", help="the entry's identifier")
    update.add_argument("--title", help="what to call it")
    update.add_argument("--username", help="the user name")
    update.add_argument("--url", help="the address")
    update.add_argument("--notes", help="the notes")
    update.add_argument(
        "--expires",
        type=_changed_expiry,
        help=(
            "when it expires from now on: a moment with its time zone, such as "
            "2031-02-03T04:05:06Z, or never to take its expiry off. Left off, it stays as it is."
        ),
    )
    update.add_argument(
        "--password",
        action="store_true",
        help=(
            "read the new password from standard input, so it stays out of the process list; at "
            "a terminal it is asked for, showing a * for each character"
        ),
    )
    _conditioned(update)
    _shared(update)
    _dialed(update)
    update.set_defaults(run=_update)

    remove = commands.add_parser("remove", help="delete an entry")
    remove.add_argument("uuid", help="the entry's identifier")
    _conditioned(remove)
    _shared(remove)
    _dialed(remove)
    remove.set_defaults(run=_remove)

    fields = commands.add_parser(
        "fields", help="list the names of an entry's strings, and which are protected"
    )
    fields.add_argument("uuid", help="the entry's identifier")
    _shared(fields)
    _dialed(fields)
    fields.set_defaults(run=_fields)

    field = commands.add_parser(
        "field",
        help="print one of an entry's strings, a password among them",
        description=(
            "Prints the value alone, for a script to read, with a line break inside it written "
            "as \\n; -o json gives it exactly. A protected one arrives only when the rule grants "
            "`protected`, and one it withholds fails the command rather than printing anything."
        ),
    )
    field.add_argument("uuid", help="the entry's identifier")
    field.add_argument("name", help="the string's name, such as Password")
    _shared(field)
    _dialed(field)
    field.set_defaults(run=_field)

    add_field = commands.add_parser("add-field", help="add a string the entry does not have")
    add_field.add_argument("uuid", help="the entry's identifier")
    add_field.add_argument("name", help="what to call it")
    _valued(add_field)
    add_field.add_argument(
        "--protected",
        action="store_true",
        help=(
            "store it protected whatever its name; left off, the database protects it if it "
            "protects that name"
        ),
    )
    _conditioned(add_field)
    _shared(add_field)
    _dialed(add_field)
    add_field.set_defaults(run=_add_field)

    update_field = commands.add_parser(
        "update-field", help="change a string the entry already has"
    )
    update_field.add_argument("uuid", help="the entry's identifier")
    update_field.add_argument("name", help="the string's name")
    _valued(update_field)
    _conditioned(update_field)
    _shared(update_field)
    _dialed(update_field)
    update_field.set_defaults(run=_update_field)

    strip_field = commands.add_parser(
        "strip-field",
        help="take a string off an entry; the five standard ones are emptied with update instead",
    )
    strip_field.add_argument("uuid", help="the entry's identifier")
    strip_field.add_argument("name", help="the string's name")
    _conditioned(strip_field)
    _shared(strip_field)
    _dialed(strip_field)
    strip_field.set_defaults(run=_strip_field)

    files = commands.add_parser("attachments", help="list an entry's attachments")
    files.add_argument("uuid", help="the entry's identifier")
    _shared(files)
    _dialed(files)
    files.set_defaults(run=_attachments)

    fetch = commands.add_parser("fetch", help="write one attachment to a file")
    fetch.add_argument("uuid", help="the entry's identifier")
    fetch.add_argument("name", help="the attachment's name")
    fetch.add_argument("--to", type=Path, required=True, help="where to write it")
    _shared(fetch)
    _dialed(fetch)
    fetch.set_defaults(run=_fetch)

    # Named as `fetch` is, the entry and the attachment's name first, and the file after.
    add_attachment = commands.add_parser(
        "add-attachment", help="add an attachment the entry does not have, from a file"
    )
    add_attachment.add_argument("uuid", help="the entry's identifier")
    add_attachment.add_argument("name", help="what to call it")
    add_attachment.add_argument(
        "--from", dest="source", type=Path, required=True, help="the file to read it from"
    )
    _conditioned(add_attachment)
    _shared(add_attachment)
    _dialed(add_attachment)
    add_attachment.set_defaults(run=_add_attachment)

    update_attachment = commands.add_parser(
        "update-attachment", help="replace an attachment the entry already has, from a file"
    )
    update_attachment.add_argument("uuid", help="the entry's identifier")
    update_attachment.add_argument("name", help="the attachment's name")
    update_attachment.add_argument(
        "--from", dest="source", type=Path, required=True, help="the file to read it from"
    )
    _conditioned(update_attachment)
    _shared(update_attachment)
    _dialed(update_attachment)
    update_attachment.set_defaults(run=_update_attachment)

    strip_attachment = commands.add_parser(
        "strip-attachment", help="take an attachment off an entry"
    )
    strip_attachment.add_argument("uuid", help="the entry's identifier")
    strip_attachment.add_argument("name", help="the attachment's name")
    _conditioned(strip_attachment)
    _shared(strip_attachment)
    _dialed(strip_attachment)
    strip_attachment.set_defaults(run=_strip_attachment)

    # Every command, by construction rather than by remembering it on each, so a command added
    # later cannot be the one a program has no way to read.
    for command in commands.choices.values():
        _formatted(command)

    return parser


def _formatted(parser: argparse.ArgumentParser) -> None:
    """How a command prints what it found, which `_emit` reads."""
    parser.add_argument(
        "-o",
        "--output",
        choices=OUTPUTS,
        default=OUTPUTS[0],
        help=(
            "what to print: plain, lines for a person, which is the default, or json, one "
            "document for a program such as jq"
        ),
    )


def _emit(arguments: argparse.Namespace, document: object, lines: Iterable[str]) -> None:
    """What a command found, in the format `--output` chose.

    `lines` is the plain form, every name and value in it already through `_printable`, and
    `document` the JSON one, which needs no such care: the encoder escapes every character that
    would break a line, and `ensure_ascii` keeps the document readable on a console whose code
    page holds less than the database does.
    """
    if arguments.output == "json":
        print(json.dumps(document, indent=2))
        return

    for line in lines:
        print(line)


def _moment(when: datetime) -> str:
    """A moment as the gate writes one, RFC 3339 in UTC to the second."""
    return f"{when:%Y-%m-%dT%H:%M:%SZ}"


def _said(document: dict[str, object], key: str, when: datetime | None) -> None:
    """A moment the gate may not have said, set only when it did: absent is not empty."""
    if when is not None:
        document[key] = _moment(when)


def _stored(credentials: api.Credentials) -> dict[str, object]:
    """What a pairing or a renewal stored, as `-o json` prints it, and nothing it holds."""
    return {
        "subject": credentials.subject,
        "clientKey": credentials.client_key,
        "admissionKey": credentials.admission_key,
        "database": credentials.database,
        "profiles": list(credentials.profiles),
        "expires": credentials.expires,
        "directory": str(credentials.directory),
    }


def _valued(parser: argparse.ArgumentParser) -> None:
    """Where a field's value comes from: an argument, or standard input when there is none."""
    parser.add_argument(
        "--value",
        help=(
            "the value. Left off, it is read from standard input, which keeps it out of the "
            "process list and is the way to give a secret; at a terminal it is asked for, "
            "showing a * for each character"
        ),
    )


def _conditioned(parser: argparse.ArgumentParser) -> None:
    """The tag a write is made under, which `-o json` printed from a read as `etag`."""
    parser.add_argument(
        "--if-match",
        metavar="ETAG",
        type=_condition,
        help=(
            "write only while the node still holds what this tag says, as -o json printed it "
            "from a read, quotes and all; refused with exit 1, and nothing changed, when "
            "somebody changed it since or when the gate gives this client no tags. Left off, "
            "the write goes ahead whatever changed"
        ),
    )


def _condition(text: str) -> str:
    """A tag a write can be made under, refused here as an argument rather than after dialing."""
    try:
        transport.check_condition(text)
    except ProtocolError as error:
        raise argparse.ArgumentTypeError(str(error)) from None

    return text


def _left(document: dict[str, object], etag: str | None) -> dict[str, object]:
    """A document with the tag the gate gave, set only when it gave one: absent is not empty."""
    if etag is not None:
        document["etag"] = etag

    return document


def _shared(parser: argparse.ArgumentParser) -> None:
    # One slot, so saying it twice is an error rather than a silent precedence. Both name
    # the directory an identity lives in. `--profile` chooses the usual place for a name and
    # `--directory` gives one outright, and `pair --directory D --profile work` used to store
    # where `status --profile work` would not look. Exit code 2, which the module docstring
    # already reserves for arguments that are wrong.
    where = parser.add_mutually_exclusive_group()
    where.add_argument(
        "--profile",
        type=_profile,
        default="default",
        help=(
            "which stored identity to use, by name, since a client can pair with more than one "
            "database; a path is --directory"
        ),
    )

    where.add_argument(
        "--directory",
        type=Path,
        help="where the identity is stored, if not the usual place for this platform",
    )


def _dialed(parser: argparse.ArgumentParser) -> None:
    """Where to open the socket, for the commands that open one.

    Not on `identity`, which answers out of the stored identity and never connects: an option that
    cannot change what a command does is an option that misleads whoever reads `--help`.
    """
    parser.add_argument(
        "--host",
        default=api.LOOPBACK,
        help=(
            "where to dial, if the gate is not reachable on loopback. It binds to loopback, so "
            "anything else is the local end of a tunnel somebody put in front of it."
        ),
    )

    # The socket's own timeout, and the three budgets for waiting out a refusal, all belong to a
    # command that opens a socket and asks for an answer, which `identity` does not. The same rule
    # as `--host` above them.
    parser.add_argument(
        "--connection-timeout",
        type=_seconds,
        default=None,
        help=(
            "seconds any one wait on the socket may last, the dial, the handshake or a read, if "
            "not the default for what is being done"
        ),
    )

    parser.add_argument(
        "--retry-busy-timeout",
        type=_budget,
        default=None,
        help=(
            "seconds to go on waiting out a KeePass that is busy, asking again for as long as "
            "that allows. Without it a refusal is reported at once, which is the default."
        ),
    )

    parser.add_argument(
        "--retry-locked-timeout",
        type=_budget,
        default=None,
        help=(
            "the same for a locked database. Separate, because a lock waits for a person to come "
            "back and being busy does not."
        ),
    )

    parser.add_argument(
        "--retry-undecided-timeout",
        type=_budget,
        default=None,
        help=(
            "the same for a database holding two authorities, until the operator keeps one at "
            "KeePass. Separate again, because that waits for a person to choose, and the one "
            "kept may not be this client's."
        ),
    )

    # How much of one answer to read, beside the socket's timeout because it too bounds what one
    # connection may cost. The plugin does not page, so a listing grows with the database.
    parser.add_argument(
        "--max-response-bytes",
        type=_byte_count,
        default=None,
        metavar="BYTES",
        help=(
            f"the most of one answer to read, {api.MAX_RESPONSE_BYTES} unless this says "
            "otherwise. The gate does not page, so a very large database may need it raised; an "
            "answer over it fails whole rather than arriving cut short."
        ),
    )


def _byte_count(text: str) -> int:
    """A cap on an answer, refused here as an argument rather than later as a failed call."""
    try:
        value = int(text)
    except ValueError:
        raise argparse.ArgumentTypeError(f"{text!r} is not a whole number of bytes") from None

    if value <= 0:
        raise argparse.ArgumentTypeError(f"an answer is capped at a positive number, not {value}")

    return value


def _port(text: str) -> int:
    """A TCP port, refused here as an argument rather than reported later as nothing listening.

    Passed on, a number outside what TCP has would reach the socket and come back as a
    connection that failed, exit code 1, which a script cannot tell from a gate that is not there.
    """
    try:
        value = int(text)
    except ValueError:
        raise argparse.ArgumentTypeError(f"{text!r} is not a port number") from None

    if not 0 < value <= 65535:
        raise argparse.ArgumentTypeError(f"a port is from 1 to 65535, not {value}")

    return value


def _seconds(text: str) -> float:
    """How long the socket waits, which has to be a finite number of seconds above zero.

    Zero would make the socket non-blocking, so the dial would fail at once and read as nothing
    listening. A negative number, NaN or infinity is refused by `socket` itself with a
    `ValueError` or an `OverflowError`, neither of which is a `GateError`, so it would leave
    `main` as a traceback rather than as an exit code.
    """
    value = _finite(text)
    if value <= 0:
        raise argparse.ArgumentTypeError(f"a timeout is more than zero seconds, not {text}")

    return value


def _budget(text: str) -> float:
    """How long a refusal may be waited out: a finite number of seconds, where zero waits none.

    NaN compares false with every delay, so a budget of NaN would never be spent and a refusal
    would be asked again forever; a negative one would read as a decision it is not.
    """
    value = _finite(text)
    if value < 0:
        raise argparse.ArgumentTypeError(f"a budget is zero or more seconds, not {text}")

    return value


def _finite(text: str) -> float:
    """A number of seconds that is a number, before either kind of timeout asks anything more."""
    try:
        value = float(text)
    except ValueError:
        raise argparse.ArgumentTypeError(f"{text!r} is not a number of seconds") from None

    if not math.isfinite(value):
        raise argparse.ArgumentTypeError(f"{text!r} is not a finite number of seconds")

    return value


def _days(text: str) -> timedelta:
    """How long a certificate should last, refused here as an argument rather than by the library.

    The library refuses one it will not ask for with a `ValueError`, which is not a `GateError`
    and would leave `main` as a traceback. The bounds are the library's, read rather than copied.
    """
    try:
        days = int(text)
    except ValueError:
        raise argparse.ArgumentTypeError(f"{text!r} is not a whole number of days") from None

    shortest, longest = api.SHORTEST_LIFETIME.days, api.LONGEST_LIFETIME.days
    if not shortest <= days <= longest:
        raise argparse.ArgumentTypeError(
            f"a certificate lasts from {shortest} to {longest} days, not {days}"
        )

    return timedelta(days=days)


def _profile(text: str) -> str:
    """A stored identity's name, refused here as an argument rather than joined as a path.

    The library says what a profile's name is, and this asks it rather than keeping a copy of the
    rule: a separator or `..` stored the identity beside the others, and an absolute path
    somewhere else altogether, where `identity` read it back as though it were a profile.
    """
    try:
        api.default_directory(text)
    except ValueError as error:
        raise argparse.ArgumentTypeError(str(error)) from None

    return text


def _expiry(text: str) -> datetime | Literal["auto"]:
    """When a new entry expires, refused here as an argument rather than later as a failed call.

    A moment with no time zone is refused rather than taken for local time or for UTC, because
    which instant it names would be a guess, and an expiry a guess moved by an hour is wrong.
    """
    if text == "auto":
        return "auto"

    try:
        when = datetime.fromisoformat(text)
    except ValueError:
        raise argparse.ArgumentTypeError(
            f"{text!r} is neither auto nor a moment such as 2031-02-03T04:05:06Z"
        ) from None

    if when.utcoffset() is None:
        raise argparse.ArgumentTypeError(f"{text!r} names no time zone; end it with Z for UTC")

    return when


def _changed_expiry(text: str) -> datetime | Literal[False]:
    """When an entry expires from now on, or `False` for never, refused here as an argument.

    `never` is the word `show` prints for an entry that does not expire. `auto` is a new entry's
    alone, so it is refused here as the library refuses it on an update.
    """
    if text == "never":
        return False

    when = _expiry(text)
    if when == "auto":
        raise argparse.ArgumentTypeError(
            "auto is for a new entry; an update takes a moment or never"
        )

    return when


def _shown(when: datetime | None, expired: bool) -> str:
    """An expiry as `show` prints it: the moment in UTC, and whether it has passed."""
    if when is None:
        return "never"

    return f"{when:%Y-%m-%dT%H:%M:%SZ}" + (" (expired)" if expired else "")


#: What ends a line, or moves back along one, wherever it is printed: every control character,
#: and the separators Unicode keeps for lines and paragraphs, which `str.splitlines` breaks on.
_LINE_BREAKING = frozenset(("Cc", "Zl", "Zp"))


def _printable(text: str) -> str:
    """A name or a value somebody chose, kept on the one line it is printed on.

    The gate accepts a line feed, a carriage return, a tab and every C1 control in a name, and a
    read hands back whatever any writer put in the file. Printed as they are, a line feed makes a
    listing show a row that is not there and a carriage return hides one, and people and scripts
    both read this output a line at a time. So each is written as its escape, the way Python
    writes it in a literal; text holding none of them is printed exactly as it is.
    """
    return "".join(
        character.encode("unicode_escape").decode("ascii")
        if unicodedata.category(character) in _LINE_BREAKING
        else character
        for character in text
    )


class _Dialing(TypedDict, total=False):
    """What a command that dials may pass on, each key only when the command line gave it."""

    connection_timeout: float
    retry_busy_timeout: float
    retry_locked_timeout: float
    retry_undecided_timeout: float
    max_response_bytes: int


def _dialing(arguments: argparse.Namespace) -> _Dialing:
    """What the command line asked of a connection, and nothing at all for what it did not.

    Passing nothing is what lets each caller keep its own default: a pairing and a renewal
    wait on a person and get `transport.PAIRING_CONNECTION_TIMEOUT`, and everything else gets
    the ordinary one. Passing `None` through would flatten both into whatever this file decided,
    and for the three retry budgets `None` is a decision in its own right: do not retry.

    Only the commands that connect are asked, which is every command `_dialed` reaches.
    """
    given = _Dialing()

    if arguments.connection_timeout is not None:
        given["connection_timeout"] = arguments.connection_timeout
    if arguments.retry_busy_timeout is not None:
        given["retry_busy_timeout"] = arguments.retry_busy_timeout
    if arguments.retry_locked_timeout is not None:
        given["retry_locked_timeout"] = arguments.retry_locked_timeout
    if arguments.retry_undecided_timeout is not None:
        given["retry_undecided_timeout"] = arguments.retry_undecided_timeout
    if arguments.max_response_bytes is not None:
        given["max_response_bytes"] = arguments.max_response_bytes

    return given


def _secret(prompt: str) -> str:
    """One line of standard input, asked for with a `*` per character when a person is typing it.

    Piped in, it is the line as it was sent. At a terminal a bare read would print nothing, so
    the command would look hung, and would echo what was typed onto the screen, into scrollback
    and into any recording of the session, which is what reading it rather than taking it as an
    argument is meant to avoid. `getpass` asks on the terminal itself, so the prompt reaches
    neither of the two streams a script reads.

    A `*` stands in for each character because a value is pasted as often as typed, and a prompt
    that stays blank after a paste looks as if the paste did not land. That says how long the
    value is, which is the price of it. The password a client's own key is kept under is the
    exception, and is asked for through `_typed` with nothing shown.
    """
    if not _console():
        return sys.stdin.readline()

    return _typed(prompt, "*")


def _console() -> bool:
    """Whether standard input is a terminal a person can type at, which `getpass` then asks on.

    `isatty` alone says too much on Windows, where it is true for every character device, NUL
    among them: a program that runs a command with its input from NUL, as automation does, would
    get past every refusal for having no terminal into `getpass`, which reads the console rather
    than standard input and so waits for good, or spins with no console at all. So there the
    descriptor is asked whether it is a console. A stream with no descriptor of its own is taken
    at its word, as every stream is elsewhere.
    """
    if not sys.stdin.isatty():
        return False

    if os.name != "nt":
        return True

    try:
        descriptor = sys.stdin.fileno()
    except (AttributeError, OSError, ValueError):
        return True

    return _windows_console(descriptor)


def _windows_console(descriptor: int) -> bool:
    """Whether a descriptor is a console on Windows, which `GetConsoleMode` answers and NUL fails.

    Looked up rather than imported, as `os.geteuid` is in `credentials`: neither `msvcrt` nor
    `ctypes.windll` exists elsewhere, and a type checker told which platform it runs on calls the
    other one's branch unreachable or its names undefined.
    """
    # cspell:ignore osfhandle
    try:
        handle = importlib.import_module("msvcrt").get_osfhandle(descriptor)
    except OSError:
        return False

    mode = ctypes.c_ulong()
    kernel32 = ctypes.__dict__["windll"].kernel32
    return bool(kernel32.GetConsoleMode(handle, ctypes.byref(mode)))


def _typed(prompt: str, echo_char: str | None = None) -> str:
    """One line typed at the terminal, or nothing if input ended at the prompt.

    Nothing is echoed, or `echo_char` for each character when one is given, which `_secret`
    says when it is.
    """
    try:
        return getpass.getpass(prompt, echo_char=echo_char)
    except EOFError:
        # Input ended at the prompt, which a pipe would have given as an empty line.
        return ""


def _new_key_password() -> str | None:
    """A password for a key about to be stored, typed twice, or `None` with the reason printed.

    Twice, because a typing mistake here is a key nobody can open again, and nothing sent can be
    taken back once the gate has answered. Neither answer is ever printed.
    """
    first = _typed("Key password: ")
    if not first:
        print("an empty key password protects nothing, so none was taken", file=sys.stderr)
        return None

    if _typed("The same again: ") != first:
        print("the two key passwords differ, so neither was taken", file=sys.stderr)
        return None

    try:
        transport.key_password_limit(first)
    except ValueError as refused:
        print(f"{refused}, so it was not taken", file=sys.stderr)
        return None

    return first


def _key_password(arguments: argparse.Namespace) -> Callable[[], str]:
    """What opens a stored key that is encrypted: its password, asked for at a terminal.

    Handed to every command that loads the key, and called only when the key turns out to be
    encrypted, so an identity stored without a password is never asked about. There is no other
    way to give it to the command line, deliberately: never as an argument, which the process
    list shows, and not from standard input, which may be carrying something else. A program
    passes `key_password=` instead.
    """
    where = arguments.directory or api.default_directory(arguments.profile)
    identity = where / IDENTITY_FILE

    def ask() -> str:
        if not _console():
            raise StorageError(
                f"{identity} holds an encrypted key, and its password is asked for only at a "
                "terminal: a program passes key_password= instead"
            )

        typed = _typed("Key password: ")
        if not typed:
            raise StorageError(f"{identity} holds an encrypted key, and no password was typed")

        return typed

    return ask


def _pair(arguments: argparse.Namespace) -> int:
    # Before anything is read or sent: the password is asked for at a terminal and nowhere else,
    # so without one this pairing could not be what it says, and it is refused as an argument
    # that cannot be done as given, with nothing spent.
    if arguments.key_password and not _console():
        print(
            "--key-password asks for the password at a terminal, and there is none here: a "
            "program passes key_password= to api.pair instead",
            file=sys.stderr,
        )
        return 2

    # Stripped whichever way it arrives. A paste brings a space or a line break with it as often
    # as not, and the token's alphabet holds neither, so taking them off loses nothing and the
    # argument accepts what standard input does. One of nothing but white space is no invitation,
    # and is asked for as an empty one is.
    invitation = (arguments.invitation or "").strip()
    if not invitation:
        # Read rather than argued, so the token does not end up in a shell history or in the
        # process list, where anything running as this user can read it.
        invitation = _secret("Invitation: ").strip()

    key_password = None
    if arguments.key_password:
        # Everything the gate is not needed for is refused before a person is asked for a
        # password twice: an invitation that is not one, a name or profiles it would refuse.
        pairing.refuse_unusable(invitation, arguments.name, list(arguments.profiles))
        key_password = _new_key_password()
        if key_password is None:
            return 2

    credentials = api.pair(
        port=arguments.port,
        invitation=invitation,
        name=arguments.name,
        profiles=list(arguments.profiles),
        directory=arguments.directory,
        profile=arguments.profile,
        host=arguments.host,
        allow_shared_directory=arguments.allow_shared_directory,
        lifetime=arguments.lifetime,
        key_password=key_password,
        **_dialing(arguments),
    )

    _emit(
        arguments,
        _stored(credentials),
        [
            f"paired as {credentials.subject} with database {credentials.database}",
            # What the audit file names this client by on every line from now on, and what the
            # operator finds beside the approval there, so the two ends can name the same client.
            f"admission key: {credentials.admission_key}",
            f"profiles approved: {', '.join(credentials.profiles) or 'none'}",
            # What the operator picked, which is not always the lifetime that was asked for.
            f"expires: {credentials.expires}",
            f"stored in {credentials.directory}",
        ],
    )
    return 0


def _status(arguments: argparse.Namespace) -> int:
    client = api.GateClient.open(
        arguments.directory,
        arguments.profile,
        host=arguments.host,
        key_password=_key_password(arguments),
        **_dialing(arguments),
    )
    status = client.status()
    _emit(
        arguments,
        {
            "ok": status.ok,
            "database": status.database,
            "version": status.version,
            "revision": status.revision,
        },
        [f"gate {status.version} serving database {status.database}"],
    )
    return 0 if status.ok else FAILED


def _renew(arguments: argparse.Namespace) -> int:
    where = arguments.directory or api.default_directory(arguments.profile)
    client = api.GateClient.open(
        where,
        arguments.profile,
        host=arguments.host,
        key_password=_key_password(arguments),
        **_dialing(arguments),
    )
    renewal = client.renew(
        lifetime=arguments.lifetime, allow_shared_directory=arguments.allow_shared_directory
    )

    renewed = renewal.credentials
    # Where the identity it renewed from is still kept, when it is: the sentence on standard
    # error below says why, and a program reads where.
    document = _stored(renewed) | {
        "retired": None if renewal.retired is None else str(renewal.retired)
    }
    _emit(
        arguments,
        document,
        [
            f"renewed {renewed.subject} with database {renewed.database}",
            # A new certificate is a new key, which the audit's line for this renewal names as
            # issued.
            f"admission key: {renewed.admission_key}",
            f"profiles approved: {', '.join(renewed.profiles) or 'none'}",
            f"expires: {renewed.expires}",
            f"stored in {renewed.directory}",
        ],
    )

    if renewal.retired is None:
        return 0

    # The renewal itself worked, and this client is already the new identity, so this is said
    # after what was stored rather than instead of it. The exit code is the failure's, because
    # what is left behind wants doing something about: a later renewal will not start until it
    # is gone.
    unrevoked = renewal.unrevoked
    if isinstance(unrevoked, api.RouteRefused) and unrevoked.error == "not verified":
        # The gate already refuses it: KeePass holds the revocation in memory, and whether the file
        # keeps it is the operator's to settle. Not "still admitted", which it is not.
        print(
            f"the certificate this renewed from is refused, and KeePass may not have that in its "
            f"file: {unrevoked}. Whether the file keeps it is the operator's to settle. What was "
            f"stored for it is kept in {renewal.retired}, and `pykeepassgate revoke --directory "
            f"{renewal.retired}` removes it, as the next renewal does by itself",
            file=sys.stderr,
        )
    elif unrevoked is not None:
        print(
            f"the certificate this renewed from is still admitted: {unrevoked}. It is kept in "
            f"{renewal.retired}, and `pykeepassgate revoke --directory {renewal.retired}` gives "
            "it up and removes it, as the next renewal tries to by itself",
            file=sys.stderr,
        )
    else:
        print(
            f"the certificate this renewed from is revoked, and what was stored for it could not "
            f"be removed from {renewal.retired}: remove that directory",
            file=sys.stderr,
        )

    return FAILED


def _revoke(arguments: argparse.Namespace) -> int:
    # Removed once the gate has said the revocation is saved and read back, or once it refuses the
    # certificate already, which is what revoking it was for: revoked on the Clients page, left
    # refused by an earlier `not verified`, or expired, and an expired one is not dialed at all.
    # Nothing can use what is stored for a certificate the gate refuses, and the gate itself calls
    # its `401 unpaired` here what was asked for having happened either way. Any other refusal
    # leaves the files where they are: `not saved` leaves the client admitted, so they are still
    # its identity.
    where = arguments.directory or api.default_directory(arguments.profile)
    client = api.GateClient.open(
        where,
        arguments.profile,
        host=arguments.host,
        key_password=_key_password(arguments),
        **_dialing(arguments),
    )
    # Read before anything is removed, since it is read off the certificate stored there. It is
    # the key the audit's line for this revocation names.
    key = client.credentials.admission_key
    named = f"{client.credentials.subject} with database {client.credentials.database}"
    try:
        client.revoke_certificate()
    except api.NotAdmitted:
        api.forget(where)
        already = True
        said = f"the gate already refuses {named}, so there was nothing left to revoke"
    else:
        api.forget(where)
        already = False
        said = f"revoked {named}"

    _emit(
        arguments,
        {
            "subject": client.credentials.subject,
            "database": client.credentials.database,
            "admissionKey": key,
            "alreadyRefused": already,
            "removed": str(where),
        },
        [said, f"admission key: {key}", f"removed what was stored in {where}"],
    )
    return 0


def _identity(arguments: argparse.Namespace) -> int:
    where = arguments.directory or api.default_directory(arguments.profile)
    credentials = api.load(where)
    # The two things that stop every other command before it dials are said here as well, since
    # this is what is run to find out which side a problem is on: a certificate that has expired,
    # which no request can use, and a key under a password, which is asked for at a terminal.
    expired = " (expired)" if credentials.expired else ""
    if credentials.expiry is None:
        # Said rather than shown as though it were an answer: every other command leaves this to
        # the gate, which judges by the certificate itself.
        expired = " (not a moment this client can read, so the gate decides)"
    encrypted = credentials.key_encrypted
    key = "encrypted" if encrypted else "not encrypted"
    document = _stored(credentials) | {
        # `null` for a moment this client cannot read, which leaves the gate to judge, as above.
        "expired": None if credentials.expiry is None else credentials.expired,
        "keyEncrypted": encrypted,
        "port": credentials.port,
    }
    _emit(
        arguments,
        document,
        [
            f"subject:       {credentials.subject}",
            f"client key:    {credentials.client_key}",
            f"admission key: {credentials.admission_key}",
            f"database:      {credentials.database}",
            f"profiles:      {', '.join(credentials.profiles) or 'none'}",
            f"expires:       {credentials.expires}{expired}",
            f"key:           {key}",
            f"port:          {credentials.port}",
            f"stored:        {credentials.directory}",
        ],
    )
    return 0


def _whoami(arguments: argparse.Namespace) -> int:
    client = api.GateClient.open(
        arguments.directory,
        arguments.profile,
        host=arguments.host,
        key_password=_key_password(arguments),
        **_dialing(arguments),
    )
    found = client.identity()

    # Under the names the gate answers with, so a program reading this reads what the route
    # documents.
    document = {
        "subject": found.subject,
        "clientKey": found.client_key,
        "admissionKey": found.admission_key,
        "expires": _moment(found.expires),
        "profiles": list(found.profiles),
        "removedProfiles": list(found.removed_profiles),
    }
    # Escaped as every name the gate answers with is, though the plugin allows no character in a
    # subject or a profile that would need it: what is printed is what was answered.
    _emit(
        arguments,
        document,
        [
            f"subject:          {_printable(found.subject)}",
            f"client key:       {_printable(found.client_key)}",
            f"admission key:    {_printable(found.admission_key)}",
            f"expires:          {_moment(found.expires)}",
            f"profiles:         {_listed(found.profiles)}",
            f"removed profiles: {_listed(found.removed_profiles)}",
        ],
    )
    return 0


def _listed(names: tuple[str, ...]) -> str:
    """Names on one line, separated by commas, or the word for none."""
    return ", ".join(_printable(name) for name in names) or "none"


def _access(arguments: argparse.Namespace) -> int:
    client = api.GateClient.open(
        arguments.directory,
        arguments.profile,
        host=arguments.host,
        key_password=_key_password(arguments),
        **_dialing(arguments),
    )
    found = (
        client.entry_access(arguments.entry)
        if arguments.entry is not None
        else client.group_access(arguments.group)
    )

    # The mask first, since every one is eight characters, so the profiles line up after them.
    _emit(
        arguments,
        {"grants": dict(found.grants)},
        [f"{mask}  {_printable(profile)}" for profile, mask in found.grants.items()],
    )
    return 0


def _groups(arguments: argparse.Namespace) -> int:
    client = api.GateClient.open(
        arguments.directory,
        arguments.profile,
        host=arguments.host,
        key_password=_key_password(arguments),
        **_dialing(arguments),
    )
    found = client.subgroups(arguments.under) if arguments.under else client.groups()

    _emit(
        arguments,
        [_group(group) for group in found],
        [f"{group.uuid}  {_printable(group.name)}" for group in found],
    )
    return 0


def _group_read(arguments: argparse.Namespace) -> int:
    client = api.GateClient.open(
        arguments.directory,
        arguments.profile,
        host=arguments.host,
        key_password=_key_password(arguments),
        **_dialing(arguments),
    )
    found = client.group(arguments.uuid)

    # The root has nothing above it, which is said rather than printed as a blank. The notes are
    # escaped as an entry's are in `show`, so notes over several lines print on one.
    lines = [
        f"uuid:   {found.uuid}",
        f"name:   {_printable(found.name)}",
        f"parent: {found.parent or 'none'}",
        f"notes:  {_printable(found.notes)}",
    ]

    _emit(arguments, _group(found), lines)
    return 0


def _add_group(arguments: argparse.Namespace) -> int:
    client = api.GateClient.open(
        arguments.directory,
        arguments.profile,
        host=arguments.host,
        key_password=_key_password(arguments),
        **_dialing(arguments),
    )
    uuid = client.create_group(arguments.name, parent=arguments.parent, notes=arguments.notes)

    # The identifier alone, as `add` prints it, so `$(...)` is what addresses the new group.
    _emit(arguments, {"uuid": uuid}, [uuid])
    return 0


def _update_group(arguments: argparse.Namespace) -> int:
    # Refused before anything is dialed, as `update` refuses it: success for a body naming
    # nothing is one a script cannot tell from a change.
    if arguments.name is None and arguments.notes is None:
        print("update-group needs something to change: --name or --notes", file=sys.stderr)
        return 2

    client = api.GateClient.open(
        arguments.directory,
        arguments.profile,
        host=arguments.host,
        key_password=_key_password(arguments),
        **_dialing(arguments),
    )
    etag = client.update_group(
        arguments.uuid, name=arguments.name, notes=arguments.notes, if_match=arguments.if_match
    )

    _emit(arguments, _left({"uuid": arguments.uuid}, etag), [f"updated {arguments.uuid}"])
    return 0


def _remove_group(arguments: argparse.Namespace) -> int:
    client = api.GateClient.open(
        arguments.directory,
        arguments.profile,
        host=arguments.host,
        key_password=_key_password(arguments),
        **_dialing(arguments),
    )
    client.delete_group(arguments.uuid, if_match=arguments.if_match)

    _emit(arguments, {"uuid": arguments.uuid}, [f"removed {arguments.uuid}"])
    return 0


def _group(group: api.Group) -> dict[str, object]:
    """A group as `-o json` prints it: notes and a tag only when it was read, not listed."""
    document: dict[str, object] = {"uuid": group.uuid, "name": group.name, "parent": group.parent}
    _said(document, "created", group.created)
    _said(document, "modified", group.modified)
    if group.is_read:
        document["notes"] = group.notes

    _left(document, group.read_etag)
    return document


def _entries(arguments: argparse.Namespace) -> int:
    client = api.GateClient.open(
        arguments.directory,
        arguments.profile,
        host=arguments.host,
        key_password=_key_password(arguments),
        **_dialing(arguments),
    )

    found = client.entries(arguments.group, include_expired=arguments.include_expired)

    lines = []
    for entry in found:
        # A withheld title is not a title, and reading one raises. Asked first rather than
        # caught, so one withheld title does not end the listing.
        title = "(withheld)" if TITLE in entry.withheld else _printable(entry.title)
        lines.append(f"{entry.uuid}  {title}")

    _emit(arguments, [_listed_entry(entry) for entry in found], lines)
    return 0


def _listed_entry(entry: api.Entry) -> dict[str, object]:
    """A listed entry as `-o json` prints it: a withheld title or URL is named, never empty."""
    document: dict[str, object] = {"uuid": entry.uuid, "group": entry.group}
    if TITLE not in entry.withheld:
        document["title"] = entry.title
    if URL not in entry.withheld:
        document["url"] = entry.url

    _said(document, "created", entry.created)
    _said(document, "modified", entry.modified)
    document["withheld"] = list(entry.withheld)
    return document


def _show(arguments: argparse.Namespace) -> int:
    client = api.GateClient.open(
        arguments.directory,
        arguments.profile,
        host=arguments.host,
        key_password=_key_password(arguments),
        **_dialing(arguments),
    )
    entry = client.entry(arguments.uuid)
    strings = entry.fields if entry.fields is not None else api.Fields()

    # A withheld value is not a value, and reading one raises, so it is asked about by name rather
    # than read. Every other one is worked out before anything is printed, so a value `--resolve`
    # cannot give fails the command with nothing printed rather than half an entry.
    values = {
        name: refs.resolve(client, entry, strings[name]) if arguments.resolve else strings[name]
        for name in strings
        if name not in entry.withheld
    }

    lines = [f"uuid:  {entry.uuid}", f"group: {entry.group}"]
    document: dict[str, object] = {"uuid": entry.uuid, "group": entry.group}
    _said(document, "created", entry.created)
    _said(document, "modified", entry.modified)

    # `hasattr` is how the read model says whether the gate said anything at all, and a gate that
    # said nothing about an expiry has not said the entry never expires.
    if hasattr(entry, "expires"):
        lines.append(f"expires: {_shown(entry.expires, entry.expired)}")
        document["expires"] = None if entry.expires is None else _moment(entry.expires)
        document["expired"] = entry.expired

    for name in sorted(strings):
        guarded = " (protected)" if name in entry.protected else ""
        # A value is escaped as a name is, since one line per field is what this output promises:
        # a line feed in the notes would otherwise print a `Password:` row of its own, above the
        # real one, written by whoever may change the notes and not the password.
        shown = "(withheld)" if name in entry.withheld else _printable(values[name])
        lines.append(f"{_printable(name)}: {shown}{guarded}")

    # The values as they are, which `json` escapes itself, and a withheld one only by its name.
    document["fields"] = {name: values[name] for name in sorted(values)}
    document["protected"] = list(entry.protected)
    document["withheld"] = list(entry.withheld)
    _left(document, entry.read_etag)

    _emit(arguments, document, lines)
    return 0


def _add(arguments: argparse.Namespace) -> int:
    client = api.GateClient.open(
        arguments.directory,
        arguments.profile,
        host=arguments.host,
        key_password=_key_password(arguments),
        **_dialing(arguments),
    )

    password = None
    if arguments.password:
        # Read rather than argued, for the same reason the invitation is: an argument is in the
        # process list, where anything running as this user can read it.
        password = _secret("Password for the new entry: ").rstrip("\n")

    uuid = client.create_entry(
        group=arguments.group,
        title=arguments.title,
        username=arguments.username,
        password=password,
        url=arguments.url,
        notes=arguments.notes,
        expires=arguments.expires,
    )

    # The identifier alone, so `$(pykeepassgate add ...)` is what addresses the new entry.
    _emit(arguments, {"uuid": uuid}, [uuid])
    return 0


def _update(arguments: argparse.Namespace) -> int:
    # Refused before anything is dialed. The library writes nothing for a body naming nothing,
    # and a command that reports success for that is one a script cannot tell from a change.
    named = (arguments.title, arguments.username, arguments.url, arguments.notes, arguments.expires)
    if all(value is None for value in named) and not arguments.password:
        print(
            "update needs something to change: --title, --username, --url, --notes, --expires "
            "or --password",
            file=sys.stderr,
        )
        # Exit 2, which a shell reads as the arguments having been wrong.
        return 2

    client = api.GateClient.open(
        arguments.directory,
        arguments.profile,
        host=arguments.host,
        key_password=_key_password(arguments),
        **_dialing(arguments),
    )

    password = None
    if arguments.password:
        # Read rather than argued, as `add` reads one.
        password = _secret("New password: ").rstrip("\n")

    etag = client.update_entry(
        arguments.uuid,
        title=arguments.title,
        username=arguments.username,
        password=password,
        url=arguments.url,
        notes=arguments.notes,
        expires=arguments.expires,
        if_match=arguments.if_match,
    )

    _emit(arguments, _left({"uuid": arguments.uuid}, etag), [f"updated {arguments.uuid}"])
    return 0


def _remove(arguments: argparse.Namespace) -> int:
    client = api.GateClient.open(
        arguments.directory,
        arguments.profile,
        host=arguments.host,
        key_password=_key_password(arguments),
        **_dialing(arguments),
    )
    client.delete_entry(arguments.uuid, if_match=arguments.if_match)
    _emit(arguments, {"uuid": arguments.uuid}, [f"removed {arguments.uuid}"])
    return 0


def _fields(arguments: argparse.Namespace) -> int:
    client = api.GateClient.open(
        arguments.directory,
        arguments.profile,
        host=arguments.host,
        key_password=_key_password(arguments),
        **_dialing(arguments),
    )
    found = client.fields(arguments.uuid)

    # Each with the entry's tag when the gate gave one, since a field the listing named is taken
    # off under that tag without being read first.
    _emit(
        arguments,
        [
            _left({"name": field.name, "protected": field.protected}, field.read_etag)
            for field in found
        ],
        [
            _printable(field.name) + (" (protected)" if field.protected else "")
            for field in found
        ],
    )
    return 0


def _field(arguments: argparse.Namespace) -> int:
    client = api.GateClient.open(
        arguments.directory,
        arguments.profile,
        host=arguments.host,
        key_password=_key_password(arguments),
        **_dialing(arguments),
    )
    found = client.field(arguments.uuid, arguments.name)

    # Read before anything is printed: a value the rule withholds raises `Withheld`, which leaves
    # through `main` as exit 1. Printing `(withheld)` as `show` does would hand a script that
    # reads one value a word it would take for the value, and set somewhere as a password.
    value = found.value

    document: dict[str, object] = {"name": found.name, "protected": found.protected, "value": value}
    _left(document, found.read_etag)
    _emit(arguments, document, [_printable(value)])
    return 0


def _value(arguments: argparse.Namespace, prompt: str) -> str:
    """A field's value, as argued, or read from standard input as a password is."""
    if arguments.value is not None:
        return str(arguments.value)

    return _secret(prompt).rstrip("\n")


def _add_field(arguments: argparse.Namespace) -> int:
    client = api.GateClient.open(
        arguments.directory,
        arguments.profile,
        host=arguments.host,
        key_password=_key_password(arguments),
        **_dialing(arguments),
    )
    value = _value(arguments, f"Value of {arguments.name}: ")
    etag = client.create_field(
        arguments.uuid,
        arguments.name,
        value,
        protected=arguments.protected,
        if_match=arguments.if_match,
    )

    _emit(
        arguments,
        _left({"uuid": arguments.uuid, "name": arguments.name}, etag),
        [f"added {_printable(arguments.name)} to {arguments.uuid}"],
    )
    return 0


def _update_field(arguments: argparse.Namespace) -> int:
    client = api.GateClient.open(
        arguments.directory,
        arguments.profile,
        host=arguments.host,
        key_password=_key_password(arguments),
        **_dialing(arguments),
    )
    value = _value(arguments, f"New value of {arguments.name}: ")
    etag = client.update_field(arguments.uuid, arguments.name, value, if_match=arguments.if_match)

    _emit(
        arguments,
        _left({"uuid": arguments.uuid, "name": arguments.name}, etag),
        [f"updated {_printable(arguments.name)} on {arguments.uuid}"],
    )
    return 0


def _strip_field(arguments: argparse.Namespace) -> int:
    client = api.GateClient.open(
        arguments.directory,
        arguments.profile,
        host=arguments.host,
        key_password=_key_password(arguments),
        **_dialing(arguments),
    )
    etag = client.strip_field(arguments.uuid, arguments.name, if_match=arguments.if_match)

    _emit(
        arguments,
        _left({"uuid": arguments.uuid, "name": arguments.name}, etag),
        [f"stripped {_printable(arguments.name)} from {arguments.uuid}"],
    )
    return 0


def _attachments(arguments: argparse.Namespace) -> int:
    client = api.GateClient.open(
        arguments.directory,
        arguments.profile,
        host=arguments.host,
        key_password=_key_password(arguments),
        **_dialing(arguments),
    )

    found = client.attachments(arguments.uuid)

    _emit(
        arguments,
        [
            _left({"name": attachment.name, "size": attachment.size}, attachment.read_etag)
            for attachment in found
        ],
        [f"{attachment.size:>10}  {_printable(attachment.name)}" for attachment in found],
    )
    return 0


def _fetch(arguments: argparse.Namespace) -> int:
    client = api.GateClient.open(
        arguments.directory,
        arguments.profile,
        host=arguments.host,
        key_password=_key_password(arguments),
        **_dialing(arguments),
    )
    attachment = client.attachment(arguments.uuid, arguments.name)

    # `GateClient.attachment` never answers without the bytes: `Attachment.read` refuses such an
    # answer rather than guess at one. Narrowed here the same way, because a fallback to no bytes
    # would write an empty file and report it written, which is the guess refused there.
    content = attachment.content
    if content is None:
        raise ProtocolError("the gate sent an attachment with no content")

    try:
        arguments.to.write_bytes(content)
    except OSError as error:
        # The gate has answered by now. What failed is the place the file was to go: a
        # directory, a file that may not be replaced, a folder that is not there. That is an
        # argument that cannot be done as given, which is what exit code 2 says, where the
        # exception escaping `main` would read as a gate failure after a traceback. The
        # operating system's sentence names the path.
        print(str(error), file=sys.stderr)
        return 2

    document: dict[str, object] = {
        "uuid": arguments.uuid,
        "name": arguments.name,
        "size": len(content),
        "path": str(arguments.to),
    }
    _left(document, attachment.read_etag)
    _emit(arguments, document, [f"wrote {len(content)} bytes to {arguments.to}"])
    return 0


def _attaching(
    arguments: argparse.Namespace,
    write: Callable[[api.GateClient, bytes], str | None],
    done: str,
) -> int:
    """Read the file an attachment comes from, and only then dial, write it and say so.

    `done` is what the plain line says was done, as the field commands say it.

    Read first, so a file that is not there or will not read is an argument that cannot be done
    as given, exit 2 with nothing dialed, as a place `fetch` cannot write to is.
    """
    try:
        content = arguments.source.read_bytes()
    except OSError as error:
        print(str(error), file=sys.stderr)
        return 2

    client = api.GateClient.open(
        arguments.directory,
        arguments.profile,
        host=arguments.host,
        key_password=_key_password(arguments),
        **_dialing(arguments),
    )
    etag = write(client, content)

    _emit(
        arguments,
        _left({"uuid": arguments.uuid, "name": arguments.name, "size": len(content)}, etag),
        [f"{done}, {len(content)} bytes"],
    )
    return 0


def _add_attachment(arguments: argparse.Namespace) -> int:
    return _attaching(
        arguments,
        lambda client, content: client.create_attachment(
            arguments.uuid, arguments.name, content, if_match=arguments.if_match
        ),
        f"added {_printable(arguments.name)} to {arguments.uuid}",
    )


def _update_attachment(arguments: argparse.Namespace) -> int:
    return _attaching(
        arguments,
        lambda client, content: client.update_attachment(
            arguments.uuid, arguments.name, content, if_match=arguments.if_match
        ),
        f"updated {_printable(arguments.name)} on {arguments.uuid}",
    )


def _strip_attachment(arguments: argparse.Namespace) -> int:
    client = api.GateClient.open(
        arguments.directory,
        arguments.profile,
        host=arguments.host,
        key_password=_key_password(arguments),
        **_dialing(arguments),
    )
    etag = client.strip_attachment(arguments.uuid, arguments.name, if_match=arguments.if_match)

    _emit(
        arguments,
        _left({"uuid": arguments.uuid, "name": arguments.name}, etag),
        [f"stripped {_printable(arguments.name)} from {arguments.uuid}"],
    )
    return 0


if __name__ == "__main__":  # pragma: no cover, the module entry point
    raise SystemExit(main())
