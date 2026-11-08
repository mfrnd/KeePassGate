"""The command line, which does nothing the library cannot.

Every command here is a few lines over `pykeepassgate.api`, deliberately: if the command line could
do something a program could not, the library would be the wrong shape. So this parses arguments,
calls one function, and prints what came back.

**It never prints a credential.** The invitation is read from an argument or from standard
input, and what is printed afterwards is what the credentials are, not what they hold. A
protected field is a different thing: `show` and `field` print one when the gate answers with
it, which is when the rule granted `P`. When it did not, `show` prints `(withheld)` and `field`
fails, since what `field` prints is taken by a script for the value.

Exit codes are the point of a command line: `0` worked; `2` nothing was sent, because the parser
refused an argument or a command could not read the input it was given; `1` failed after that,
which is the gate refusing or out of reach and is also whatever the library refuses before it
dials, nothing paired, a key that will not open, an invitation that will not read and a name the
gate would refuse among them; and `130` Ctrl+C, as a shell has it, with the line `interrupted`
rather than a traceback. A reader that stops reading, as `head` does, ends a command with `1` and
nothing more. A script can branch on that without reading English.

**What a command prints is chosen with `-o`/`--output`**, on every command: `plain`, lines for a
person, and `json`, one document for a program such as `jq`. A choice rather than a `--json`
switch, so that another format is one more choice. Both say the same things under the same rules:
a value the rule withholds is named as withheld and never printed as empty, and what the gate did
not say is left out rather than printed as nothing. Only what a command found is formatted; a
failure is a sentence on standard error and an exit code either way, and is printed beside what
was found only where something was: `renew` that kept the certificate before, `revoke` whose
files would not all go, and `status` answered that the gate is not serving.

**Every standard stream is UTF-8**, whatever the code page, as Python 3.15 makes them by default:
a listing written to a file or a pipe on Windows was encoded as cp1252 and died part way through
at the first name beyond it, and a value piped in was decoded as cp1252 and stored as other text.
"""

from __future__ import annotations

import argparse
import ctypes
import errno
import getpass
import importlib
import io
import json
import math
import os
import sys
import unicodedata
from collections.abc import Callable, Iterable
from datetime import UTC, datetime, timedelta
from pathlib import Path
from typing import Literal, TypedDict

from . import api, compatibility, pairing, refs, transport
from .client import IDENTITY_PATH, STATUS_PATH, _segment
from .credentials import IDENTITY_FILE, write_owner_only
from .errors import GateError, ProtocolError, StorageError
from .model import TITLE, URL

#: Everything went wrong that this program knows how to be wrong about.
FAILED = 1

#: What a shell reports for a program ended by Ctrl+C: 128 and the number of SIGINT.
INTERRUPTED = 130

#: Where `pair` dials without `--port`: the library's own default, so the two cannot differ.
DEFAULT_PORT = api.DEFAULT_PORT

#: What `-o`/`--output` chooses between, the default first: lines for a person, as every command
#: has always printed, and one JSON document for a program.
OUTPUTS = ("plain", "json")


def main(argv: list[str] | None = None) -> int:
    """Run one command and return what the shell should see."""
    _utf8_streams()
    parser = _parser()
    arguments = parser.parse_args(argv)

    try:
        code = int(arguments.run(arguments))
        # Flushed inside the `try`, so that a reader that has gone is met where it is answered,
        # rather than at exit, where the interpreter's own flush reports an exception it ignored.
        sys.stdout.flush()
        return code
    except KeyboardInterrupt:
        # Ctrl+C, which is the way out of the minutes a pairing or a renewal waits on the operator,
        # and of a prompt: a line and the shell's number for an interrupt, 128 and SIGINT's 2,
        # rather than a traceback. Whatever the library was doing has run its own cleanup by now.
        print("interrupted", file=sys.stderr)
        return INTERRUPTED
    except _Refused as refusal:
        # Found inside a reader a command calls, where returning 2 is not possible, and the same
        # answer a command gives an argument it cannot use: nothing has been sent.
        print(str(refusal), file=sys.stderr)
        return 2
    except GateError as error:
        # The exception hierarchy already says what happened in a sentence a person can act on, so
        # the command line adds nothing to it beyond the class name for anybody grepping. That
        # covers a profile nothing has paired under, which is `NotPaired`.
        print(f"{type(error).__name__}: {error}", file=sys.stderr)
        return FAILED
    except OSError as error:
        if not _reader_gone(error):
            raise

        # A reader that stopped reading, `entries | head -1`. What is left goes to the null device,
        # as Python's documentation on SIGPIPE has it, because the interpreter flushes standard
        # output again at exit and would meet the same closed pipe.
        # The platform's names for signals and errors: cspell:ignore SIGPIPE EINVAL
        _silenced()
        return FAILED


def _reader_gone(error: OSError) -> bool:
    """Whether an error is standard output's reader having gone, as `head` goes once it has read.

    A broken pipe, or on Windows an invalid argument, which is what a write to a pipe whose reader
    has closed raises there, measured on Python 3.14. Asked again of standard output itself, which
    still holds what it could not write, so that the same error raised by anything else is not
    taken for it: a flush that works is a reader still there.
    """
    if not isinstance(error, BrokenPipeError) and error.errno != errno.EINVAL:
        return False

    try:
        sys.stdout.flush()
    except OSError as again:
        return isinstance(again, BrokenPipeError) or again.errno == errno.EINVAL

    return False


def _silenced() -> None:
    """Point standard output at the null device, for a reader that has closed its end."""
    try:
        descriptor = sys.stdout.fileno()
    except (OSError, ValueError):
        # A stream that is no file, as a test's capture is, has no descriptor to point elsewhere,
        # and nothing at exit flushes it into a pipe.
        return

    null = os.open(os.devnull, os.O_WRONLY)
    try:
        os.dup2(null, descriptor)
    finally:
        os.close(null)


class _Refused(Exception):
    """What a command cannot do as given, found where it cannot return exit code 2 itself."""


#: The sentence each help text that reads standard input ends on: `_secret` reads to
#: the end of input, since what comes after the line is refused rather than left unread, so a
#: producer that sends its line and holds the pipe open holds the command with it.
_PIPED = (
    "piped in, it is one line and is read to the end of input, so whatever sends it must "
    "close the pipe"
)

#: What `_secret` refuses input that ended before a line with.
_NO_LINE = (
    "input ended before a line was read, so there is no value to send: an empty value is an "
    "empty line"
)


def _utf8_streams() -> None:
    """Read and write UTF-8 on every standard stream, whatever the locale's code page.

    On Windows a stream that is not a console takes the code page, cp1252 here, so plain output
    written to a file or a pipe raised `UnicodeEncodeError` part way through a listing at the
    first name it cannot encode, after the rows before it were written, and a value piped in was
    decoded as cp1252: `ä` sent as UTF-8 arrived as two other letters, and was stored so. A
    console was never the problem, since Python reads and writes a console in UTF-16. Python
    3.15 makes UTF-8 the default for all three, PEP 686, and this makes the same choice now.

    **Input is strict**, so a value piped in that is not UTF-8 is refused by `_secret` rather than
    stored as something else, and a byte order mark at its start is dropped, which is what
    Windows PowerShell puts there when told to send UTF-8. **Output never fails**: a character
    UTF-8 cannot encode, which only a lone surrogate is, is written as its escape. A stream that
    is not a text wrapper over a file, as a test's capture may be, is left as it is.
    """
    # Python's own name for the handler: cspell:ignore backslashreplace
    for stream, encoding, errors in (
        (sys.stdin, "utf-8-sig", "strict"),
        (sys.stdout, "utf-8", "backslashreplace"),
        (sys.stderr, "utf-8", "backslashreplace"),
    ):
        if isinstance(stream, io.TextIOWrapper):
            stream.reconfigure(encoding=encoding, errors=errors)


def _parser() -> argparse.ArgumentParser:
    parser = argparse.ArgumentParser(
        prog="pykeepassgate",
        description="The reference client for KeePassGate.",
    )
    # A client and a plugin are a pair by their major and minor versions, and `status` names the
    # gate's, so the command line names its own half of the comparison too.
    parser.add_argument(
        "--version", action="version", version=f"pykeepassgate {compatibility.CLIENT_VERSION}"
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
            f"asked for at a terminal, showing a * for each character; {_PIPED}."
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
            "Prints what the rules grant each profile this client holds there, in the notation "
            "the KPG tab shows a rule in: CRUSDLAP, with a dot for each verb or switch the "
            "profile is not granted there. A switch reaches only the verbs on its own line, and "
            "a switch beside S or D alone is left out, since removing asks none. D is the node's "
            "own rule: removing the node asks the group above it for D as well. A node this "
            "client may do nothing at is refused as a node that is not there."
        ),
    )
    node = access.add_mutually_exclusive_group(required=True)
    node.add_argument("--group", type=_addressed, help="a group identifier, or root")
    node.add_argument("--entry", type=_addressed, help="an entry identifier")
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

    groups.add_argument(
        "--under", type=_addressed, help="a group identifier, or root, to list one level below"
    )
    _shared(groups)
    _dialed(groups)
    groups.set_defaults(run=_groups)

    group = commands.add_parser(
        "group",
        help="read one group, its notes included",
        description="Needs `read` on the group, where listing it needs `list` on the one above.",
    )
    group.add_argument("uuid", type=_addressed, help="the group's identifier, or root")
    _read_conditioned(group)
    _shared(group)
    _dialed(group)
    group.set_defaults(run=_group_read)

    add_group = commands.add_parser("add-group", help="create a group inside another")
    add_group.add_argument("--name", required=True, help="what to call it")
    add_group.add_argument(
        "--parent", type=_addressed, default=api.ROOT, help="where to put it, by identifier or root"
    )
    add_group.add_argument("--notes", help="the notes")
    _shared(add_group)
    _dialed(add_group)
    add_group.set_defaults(run=_add_group)

    update_group = commands.add_parser(
        "update-group", help="rename a group, or change its notes"
    )
    update_group.add_argument("uuid", type=_addressed, help="the group's identifier, or root")
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
    remove_group.add_argument("uuid", type=_addressed, help="the group's identifier")
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
        type=_addressed,
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
            "Redirect it or read it directly: this and field are the two commands that put a "
            "password on a terminal."
        ),
    )

    show.add_argument("uuid", type=_addressed, help="the entry's identifier")
    show.add_argument(
        "--resolve",
        action="store_true",
        help=(
            "follow field references and placeholders as KeePass would, reading each entry a "
            "reference names under this client's rules, and fail rather than print a value that "
            "cannot be given as KeePass would give it, but for %%NAME%% and a placeholder another "
            "plugin adds, which print as stored. Without it, values print as stored."
        ),
    )
    _read_conditioned(show)
    _shared(show)
    _dialed(show)
    show.set_defaults(run=_show)

    add = commands.add_parser("add", help="create an entry in a group")
    add.add_argument(
        "--group",
        type=_addressed,
        default=api.ROOT,
        help="where to put it, by identifier or root",
    )
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
            f"terminal it is asked for, showing a * for each character; {_PIPED}"
        ),
    )

    _shared(add)
    _dialed(add)
    add.set_defaults(run=_add)

    update = commands.add_parser("update", help="change an entry's standard fields")
    update.add_argument("uuid", type=_addressed, help="the entry's identifier")
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
            f"a terminal it is asked for, showing a * for each character; {_PIPED}"
        ),
    )
    _conditioned(update)
    _shared(update)
    _dialed(update)
    update.set_defaults(run=_update)

    remove = commands.add_parser("remove", help="delete an entry")
    remove.add_argument("uuid", type=_addressed, help="the entry's identifier")
    _conditioned(remove)
    _shared(remove)
    _dialed(remove)
    remove.set_defaults(run=_remove)

    fields = commands.add_parser(
        "fields", help="list the names of an entry's strings, and which are protected"
    )
    fields.add_argument("uuid", type=_addressed, help="the entry's identifier")
    _read_conditioned(fields)
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
    field.add_argument("uuid", type=_addressed, help="the entry's identifier")
    field.add_argument("name", type=_addressed, help="the string's name, such as Password")
    _read_conditioned(field)
    _shared(field)
    _dialed(field)
    field.set_defaults(run=_field)

    add_field = commands.add_parser("add-field", help="add a string the entry does not have")
    add_field.add_argument("uuid", type=_addressed, help="the entry's identifier")
    add_field.add_argument("name", type=_addressed, help="what to call it")
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
    update_field.add_argument("uuid", type=_addressed, help="the entry's identifier")
    update_field.add_argument("name", type=_addressed, help="the string's name")
    _valued(update_field)
    _conditioned(update_field)
    _shared(update_field)
    _dialed(update_field)
    update_field.set_defaults(run=_update_field)

    strip_field = commands.add_parser(
        "strip-field",
        help="take a string off an entry; the five standard ones are emptied with update instead",
    )
    strip_field.add_argument("uuid", type=_addressed, help="the entry's identifier")
    strip_field.add_argument("name", type=_addressed, help="the string's name")
    _conditioned(strip_field)
    _shared(strip_field)
    _dialed(strip_field)
    strip_field.set_defaults(run=_strip_field)

    files = commands.add_parser("attachments", help="list an entry's attachments")
    files.add_argument("uuid", type=_addressed, help="the entry's identifier")
    _read_conditioned(files)
    _shared(files)
    _dialed(files)
    files.set_defaults(run=_attachments)

    fetch = commands.add_parser("fetch", help="write one attachment to a file")
    fetch.add_argument("uuid", type=_addressed, help="the entry's identifier")
    fetch.add_argument("name", type=_addressed, help="the attachment's name")
    fetch.add_argument("--to", type=Path, required=True, help="where to write it")
    _read_conditioned(fetch)
    _shared(fetch)
    _dialed(fetch)
    fetch.set_defaults(run=_fetch)

    # Named as `fetch` is, the entry and the attachment's name first, and the file after.
    add_attachment = commands.add_parser(
        "add-attachment", help="add an attachment the entry does not have, from a file"
    )
    add_attachment.add_argument("uuid", type=_addressed, help="the entry's identifier")
    add_attachment.add_argument("name", type=_addressed, help="what to call it")
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
    update_attachment.add_argument("uuid", type=_addressed, help="the entry's identifier")
    update_attachment.add_argument("name", type=_addressed, help="the attachment's name")
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
    strip_attachment.add_argument("uuid", type=_addressed, help="the entry's identifier")
    strip_attachment.add_argument("name", type=_addressed, help="the attachment's name")
    _conditioned(strip_attachment)
    _shared(strip_attachment)
    _dialed(strip_attachment)
    strip_attachment.set_defaults(run=_strip_attachment)

    # One command for every `head` the document declares, as one library method covers them: a
    # kind of node to ask about, named as the read command of it is, and the same arguments.
    head = commands.add_parser(
        "head",
        help="ask whether a route answers, and print the tag it answers with, reading nothing",
        description=(
            "Asks the route a read command asks, with HEAD, so nothing is read. Prints the tag "
            "of the group or entry the route is about, as -o json prints it from a read, or "
            "nothing where the gate gives none. With --if-match it asks whether that tag is "
            "still current: refused with exit 1 when it is not."
        ),
    )
    kinds = head.add_subparsers(dest="kind", required=True)
    for kind, summary in HEADS.items():
        asked = kinds.add_parser(kind, help=summary)
        if kind in ("group", "entry", "fields", "field", "attachments", "attachment"):
            asked.add_argument("uuid", type=_addressed, help="the node's identifier")
            if kind in ("field", "attachment"):
                asked.add_argument("name", type=_addressed, help="the string's or the file's name")
            _read_conditioned(asked)
        elif kind == "groups":
            asked.add_argument("--under", type=_addressed, help="a group, to ask one level below")
        elif kind == "entries":
            asked.add_argument("--group", type=_addressed, help="a group, for its own entries")
        elif kind == "access":
            node = asked.add_mutually_exclusive_group(required=True)
            node.add_argument("--group", type=_addressed, help="a group identifier, or root")
            node.add_argument("--entry", type=_addressed, help="an entry identifier")
        _shared(asked)
        _dialed(asked)
        _formatted(asked)
        asked.set_defaults(run=_head)

    # Every command, by construction rather than by remembering it on each, so a command added
    # later cannot be the one a program has no way to read. `head` is the command made of others,
    # each of which carries the option itself: on both, the default of the command asked would
    # overwrite what was given to the command around it.
    for command in commands.choices.values():
        if command is not head:
            _formatted(command)

    return parser


#: What `head` asks about, by the read command each kind is named after.
HEADS = {
    "status": "the status route",
    "whoami": "who the gate admits this client as",
    "groups": "the groups listing, or one level under a group",
    "group": "one group",
    "entries": "the entries listing, or a group's own entries",
    "entry": "one entry",
    "fields": "the names of an entry's strings",
    "field": "one of an entry's strings",
    "attachments": "the names of an entry's attachments",
    "attachment": "one of an entry's attachments",
    "access": "what this client may do at a group or an entry",
}


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

    `lines` is the plain form, each through `_printable` here, so that no caller has to remember
    which of its values another writer could have chosen: a name, an identifier, a subject or a
    directory alike, from the gate, from a file or from the command line. Every caller's own text
    holds nothing it escapes, so a line is printed as written unless a value in it needed it.
    `document` is the JSON form, which needs no such care: the encoder escapes every character
    that would break a line, and `ensure_ascii` keeps the document readable on a console whose
    code page holds less than the database does.
    """
    if arguments.output == "json":
        print(json.dumps(document, indent=2))
        return

    for line in lines:
        print(_printable(line))


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
            f"showing a * for each character; {_PIPED}"
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
            "somebody changed it since or when the gate gives this client no tags, and with "
            "exit 2 before anything is sent when it is no tag the gate could match, without its "
            "quotes or weak. Left off, the write goes ahead whatever changed"
        ),
    )


def _read_conditioned(parser: argparse.ArgumentParser) -> None:
    """The tag a read is made under, sent as the library sends it, as `If-Match`."""
    parser.add_argument(
        "--if-match",
        metavar="ETAG",
        type=_condition,
        help=(
            "read only while the node still holds what this tag names, as -o json printed it, "
            "quotes and all; refused with exit 1, and nothing read, when somebody changed it "
            "since or when the gate gives this client no tags"
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
            "which stored identity to use, by a name of ASCII letters, digits, hyphens and "
            "underscores, since a client can pair with more than one database; a path is "
            "--directory"
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
        type=_host,
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
            "not the default for what is being done. The request of pair and of renew waits on "
            "the operator for six minutes or this, whichever is longer"
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


def _addressed(text: str) -> str:
    """A node's identifier, or a field's or an attachment's name, refused here if it names none.

    Empty is no identifier: `groups --under ""`, from a variable that was not set, listed the
    whole database, and `entries --group ""` asked for `/groups//entries`. Nor is text UTF-8
    cannot encode, which argv that was not UTF-8 brings on POSIX as a lone surrogate, and which
    left `main` as a traceback. Both are arguments that cannot work, exit 2 before anything is
    dialed, as the library refuses them with `ProtocolError`.
    """
    if not text:
        raise argparse.ArgumentTypeError("an identifier or a name is never empty")

    try:
        text.encode("utf-8")
    except UnicodeEncodeError:
        raise argparse.ArgumentTypeError(
            f"{text!r} is text UTF-8 cannot encode, so it names nothing the gate holds"
        ) from None

    return text


def _host(text: str) -> str:
    """Where to dial, refused here as an argument rather than as a traceback out of the dial.

    A label over 63 characters or a label holding a lone surrogate, which argv from a name that was
    not UTF-8 brings on POSIX, left the dial as `UnicodeError`, which is no `GateError`.
    """
    try:
        return transport.host_limit(text)
    except ValueError as error:
        raise argparse.ArgumentTypeError(str(error)) from None


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

    # Moved to UTC here as well as in the library, whose `ValueError` would otherwise leave `main`
    # as a traceback: `9999-12-31T23:00:00-05:00` is a moment in the year 10000 there.
    try:
        when.astimezone(UTC)
    except OverflowError:
        raise argparse.ArgumentTypeError(
            f"{text!r} falls outside the years 1 to 9999 in UTC, which is where it is sent"
        ) from None

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

    Piped in, it is the line as it was sent, less its line ending, a line feed or a carriage return
    and a line feed alike. Python reads standard input with universal newlines on Windows alone,
    so elsewhere a line a Windows program wrote kept its carriage return, and was stored with it.
    At a terminal a bare read would print nothing, so the command would look hung, and would echo
    what was typed onto the screen, into scrollback and into any recording of the session, which
    is what reading it rather than taking it as an argument is meant to avoid. `getpass` asks on
    the terminal itself, so the prompt reaches neither of the two streams a script reads.

    A `*` stands in for each character because a value is pasted as often as typed, and a prompt
    that stays blank after a paste looks as if the paste did not land. That says how long the
    value is, which is the price of it. The password a client's own key is kept under is the
    exception, and is asked for through `_typed` with nothing shown.

    **Input that ends before a line is no value**, and is refused rather than sent as an empty
    value: a producer that printed nothing, a closed standard input and `< /dev/null` end so, and
    so does the end of input typed at the prompt. An empty value is an empty line, which a pipe
    sends as a line break and a person as Enter on its own.

    **And input holding more than a line is refused**, rather than cut to its first line: a key
    in PEM piped in was stored as its `BEGIN` line, reported done, and found useless only when
    something tried it. A value over several lines is a program's to send, which takes any
    string. Asked by reading a character past the line, so a producer that never stops is
    refused at once rather than read for good.
    """
    if _console():
        try:
            return getpass.getpass(prompt, echo_char="*")
        except EOFError:
            raise _Refused(_NO_LINE) from None

    if sys.stdin is None:
        # A process started with no standard input at all, as `pythonw` and a detached process
        # are, which is input that ended before it began.
        raise _Refused(_NO_LINE)

    try:
        line = sys.stdin.readline()
        more = sys.stdin.read(1) if line.endswith("\n") else ""
    except UnicodeDecodeError:
        raise _Refused(
            "standard input is not UTF-8, so what it holds would be stored as other text; "
            "send it as UTF-8"
        ) from None

    if not line:
        raise _Refused(_NO_LINE)

    if more:
        raise _Refused(
            "standard input holds more than a line, and a value read from it is a line: only the "
            "first would be sent, so nothing was"
        )

    return line.removesuffix("\n").removesuffix("\r")


def _console() -> bool:
    """Whether standard input is a terminal a person can type at, which `getpass` then asks on.

    `isatty` alone says too much on Windows, where it is true for every character device, NUL
    among them: a program that runs a command with its input from NUL, as automation does, would
    get past every refusal for having no terminal into `getpass`, which reads the console rather
    than standard input and so waits for good, or spins with no console at all. So there the
    descriptor is asked whether it is a console. A stream with no descriptor of its own is taken
    at its word, as every stream is elsewhere, and no standard input at all is no terminal.
    """
    if sys.stdin is None or not sys.stdin.isatty():
        return False

    if os.name != "nt":
        return True

    try:
        descriptor = sys.stdin.fileno()
    except (AttributeError, OSError, ValueError):
        return True

    return _windows_console(descriptor)


def _terminal() -> bool:
    """Whether `getpass` has a terminal a person can type at, whatever standard input is.

    `getpass` does not read standard input: on POSIX it opens `/dev/tty`, the terminal the process
    belongs to, and on Windows it reads the console. So a value piped into a command can sit
    beside a key password typed at the terminal, and asking standard input refused the key
    password whenever the value was piped, though a person was there to type it. What is asked is
    whether the terminal `getpass` uses is there.

    **Input from the null device is no terminal**, whatever is there: automation points a
    command's input at it, and `getpass` would then wait for good on a console nobody is at. A
    stream with no descriptor of its own is taken at its word, as `_console` takes it.
    """
    stdin = sys.stdin
    if stdin is not None:
        try:
            descriptor = stdin.fileno()
        except (AttributeError, OSError, ValueError):
            return bool(stdin.isatty())

        if _null_device(descriptor):
            return False

    return _getpass_terminal()


def _null_device(descriptor: int) -> bool:
    """Whether a descriptor is the null device, or on Windows any character device but a console.

    On Windows `isatty` is true for every character device, NUL among them, and only a console
    answers `GetConsoleMode`. Elsewhere the device is compared with `os.devnull` itself.
    """
    if os.name == "nt":
        return os.isatty(descriptor) and not _windows_console(descriptor)

    try:
        return os.path.samestat(os.fstat(descriptor), Path(os.devnull).stat())
    except OSError:
        return False


def _getpass_terminal() -> bool:
    """Whether the terminal `getpass` asks on opens: `/dev/tty`, or on Windows a console."""
    # The platforms' own names: cspell:ignore pythonw fstat CONIN RDWR NOCTTY
    windows = os.name == "nt"
    if windows:
        name, flags = "CONIN$", os.O_RDWR
    else:
        # Looked up for the reason `_windows_console` looks its names up: Windows has no
        # `O_NOCTTY`, and a type checker told it runs there calls the name undefined.
        name, flags = "/dev/tty", os.O_RDWR | os.__dict__["O_NOCTTY"]

    try:
        descriptor = os.open(name, flags)
    except OSError:
        return False

    try:
        return not windows or _windows_console(descriptor)
    finally:
        os.close(descriptor)


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
        # Input ended at the prompt with nothing typed: an empty key password, which opens no
        # key and protects none, and every prompt asking through this refuses an empty key
        # password.
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
    list shows, and not from standard input, which may be carrying something else, a value piped
    in for the command among them, since `getpass` asks the terminal and not standard input. A
    program passes `key_password=` instead.
    """
    where = arguments.directory or api.default_directory(arguments.profile)
    identity = where / IDENTITY_FILE

    def ask() -> str:
        if not _terminal():
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
    # that cannot be done as given, with nothing spent. The terminal, not standard input, which
    # may be bringing the invitation.
    if arguments.key_password and not _terminal():
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
    serving = "serving" if status.ok else "not serving"
    _emit(
        arguments,
        {
            "ok": status.ok,
            "database": status.database,
            "version": status.version,
            "revision": status.revision,
        },
        [f"gate {status.version} {serving} database {status.database}"],
    )
    if status.ok:
        return 0

    # What the gate answered is printed, since it is what was found, and the failure is the
    # sentence and the exit code beside it, as with every other failure.
    print("the gate answered that it is not serving this database: ok is false", file=sys.stderr)
    return FAILED


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
    elif isinstance(unrevoked, StorageError):
        # Refused while the client that would present it was built, so the gate was asked
        # nothing and whether it still admits the certificate is not known here.
        print(
            f"the certificate this renewed from could not be presented to give it up: "
            f"{unrevoked}. It is kept in {renewal.retired}, and `pykeepassgate revoke "
            f"--directory {renewal.retired}` gives it up and removes it, as the next renewal "
            "tries to by itself",
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
        already = True
        said = f"the gate already refuses {named}, so there was nothing left to revoke"
    else:
        already = False
        said = f"revoked {named}"

    # The certificate is given up whatever happens to the files, so a file that will not go is
    # reported after the revocation rather than instead of it, as `renew` reports what it kept. A
    # person or a script would otherwise read a revocation that landed as refused.
    left: StorageError | None = None
    try:
        api.forget(where)
    except StorageError as refused:
        left = refused

    document: dict[str, object] = {
        "subject": client.credentials.subject,
        "database": client.credentials.database,
        "admissionKey": key,
        "alreadyRefused": already,
    }
    lines = [said, f"admission key: {key}"]
    if left is None:
        document["removed"] = str(where)
        lines.append(f"removed what was stored in {where}")

    _emit(arguments, document, lines)
    if left is None:
        return 0

    print(
        f"the certificate is given up, and what was stored for it could not all be removed from "
        f"{where}: {left}. Nothing can use it now, since the gate refuses the certificate: remove "
        "that directory",
        file=sys.stderr,
    )
    return FAILED


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
        "profilesUnreadable": found.profiles_unreadable,
    }
    lines = [
        f"subject:          {found.subject}",
        f"client key:       {found.client_key}",
        f"admission key:    {found.admission_key}",
        f"expires:          {_moment(found.expires)}",
        f"profiles:         {_listed(found.profiles)}",
        f"removed profiles: {_listed(found.removed_profiles)}",
    ]
    # Only when it is so: it is why a person reading this finds every profile removed, and a line
    # for a list that reads would be a line nobody acts on.
    if found.profiles_unreadable:
        lines.append(
            "profile list:     will not read, so every profile is removed until the operator "
            "repairs it"
        )

    _emit(arguments, document, lines)
    return 0


def _listed(names: tuple[str, ...]) -> str:
    """Names on one line, separated by commas, or the word for none."""
    return ", ".join(names) or "none"


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
        [f"{mask}  {profile}" for profile, mask in found.grants.items()],
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
    found = client.subgroups(arguments.under) if arguments.under is not None else client.groups()

    _emit(
        arguments,
        [_group(group) for group in found],
        [f"{group.uuid}  {group.name}" for group in found],
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
    found = client.group(arguments.uuid, if_match=arguments.if_match)

    # The root has nothing above it, which is said rather than printed as a blank. The notes are
    # escaped as an entry's are in `show`, so notes over several lines print on one.
    lines = [
        f"uuid:   {found.uuid}",
        f"name:   {found.name}",
        f"parent: {found.parent or 'none'}",
        f"notes:  {found.notes}",
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
        title = "(withheld)" if TITLE in entry.withheld else entry.title
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
    entry = client.entry(arguments.uuid, if_match=arguments.if_match)
    # `GateClient.entry` never answers without the strings: `Entry.read` refuses such an answer.
    # Narrowed here the same way, as `fetch` narrows an attachment's bytes, because a fallback to
    # none would print an entry holding nothing, which is the guess refused there.
    strings = entry.fields
    if strings is None:
        raise ProtocolError("the gate sent an entry with no strings")

    # A withheld value is not a value, and reading one raises, so it is asked about by name rather
    # than read. Every other one is worked out before anything is printed, so a value `--resolve`
    # cannot give fails the command with nothing printed rather than half an entry. Resolved
    # together, so that every entry they name is read once and the entry itself not again, and
    # protected wherever a protected value went in, a password a URL borrows among them.
    protected = list(entry.protected)
    if arguments.resolve:
        resolved = refs.resolve_entry(client, entry)
        values = {name: found.value for name, found in resolved.items()}
        protected += [
            name for name, found in resolved.items() if found.protected and name not in protected
        ]
    else:
        values = {name: strings[name] for name in strings if name not in entry.withheld}

    lines = [f"uuid:  {entry.uuid}", f"group: {entry.group}"]
    document: dict[str, object] = {"uuid": entry.uuid, "group": entry.group}
    _said(document, "created", entry.created)
    _said(document, "modified", entry.modified)

    # Every read carries the entry's expiry, `None` for never, since `Entry.read` refuses a read
    # without it.
    lines.append(f"expires: {_shown(entry.expires, entry.expired)}")
    document["expires"] = None if entry.expires is None else _moment(entry.expires)
    document["expired"] = entry.expired

    for name in sorted(strings):
        guarded = " (protected)" if name in protected else ""
        # A value is escaped as a name is, since one line per field is what this output promises:
        # a line feed in the notes would otherwise print a `Password:` row of its own, above the
        # real one, written by whoever may change the notes and not the password.
        shown = "(withheld)" if name in entry.withheld else values[name]
        lines.append(f"{name}: {shown}{guarded}")

    # The values as they are, which `json` escapes itself, and a withheld one only by its name.
    document["fields"] = {name: values[name] for name in sorted(values)}
    document["protected"] = protected
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
        password = _secret("Password for the new entry: ")

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
        password = _secret("New password: ")

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
    found = client.fields(arguments.uuid, if_match=arguments.if_match)

    # Each with the entry's tag when the gate gave one, since a field the listing named is taken
    # off under that tag without being read first.
    _emit(
        arguments,
        [
            _left({"name": field.name, "protected": field.protected}, field.read_etag)
            for field in found
        ],
        [
            field.name + (" (protected)" if field.protected else "")
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
    found = client.field(arguments.uuid, arguments.name, if_match=arguments.if_match)

    # Read before anything is printed: a value the rule withholds raises `Withheld`, which leaves
    # through `main` as exit 1. Printing `(withheld)` as `show` does would hand a script that
    # reads one value a word it would take for the value, and set somewhere as a password.
    value = found.value

    document: dict[str, object] = {"name": found.name, "protected": found.protected, "value": value}
    _left(document, found.read_etag)
    _emit(arguments, document, [value])
    return 0


def _value(arguments: argparse.Namespace, prompt: str) -> str:
    """A field's value, as argued, or read from standard input as a password is."""
    if arguments.value is not None:
        return str(arguments.value)

    return _secret(prompt)


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
        [f"added {arguments.name} to {arguments.uuid}"],
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
        [f"updated {arguments.name} on {arguments.uuid}"],
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
        [f"stripped {arguments.name} from {arguments.uuid}"],
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

    found = client.attachments(arguments.uuid, if_match=arguments.if_match)

    _emit(
        arguments,
        [
            _left({"name": attachment.name, "size": attachment.size}, attachment.read_etag)
            for attachment in found
        ],
        [f"{attachment.size:>10}  {attachment.name}" for attachment in found],
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
    attachment = client.attachment(
        arguments.uuid, arguments.name, if_match=arguments.if_match
    )

    # `GateClient.attachment` never answers without the bytes: `Attachment.read` refuses such an
    # answer rather than guess at one. Refused here the same way, as a `ProtocolError` rather than
    # the `AttributeError` asking a listed attachment for its bytes raises, because a fallback to
    # no bytes would write an empty file and report it written, which is the guess refused there.
    if not attachment.is_read:
        raise ProtocolError("the gate sent an attachment with no content")

    content = attachment.content

    # Owner read and write only, never through a link at the name, and a file already there
    # narrowed before the content goes in, as the identity's own files are written: what a
    # database holds is as often a key as anything, and `write_bytes` left it readable by every
    # account under the usual umask.
    try:
        write_owner_only(arguments.to, content)
    except StorageError as error:
        # The gate has answered by now. What failed is the place the file was to go: a
        # directory, a file that may not be replaced, a folder that is not there, a link. That is
        # an argument that cannot be done as given, which is what exit code 2 means, where the
        # exception reaching `main` would read as a failure of the gate's. The sentence names the
        # path.
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


def _head(arguments: argparse.Namespace) -> int:
    """Ask a route with HEAD, which reads nothing, and print the tag it answered with."""
    client = api.GateClient.open(
        arguments.directory,
        arguments.profile,
        host=arguments.host,
        key_password=_key_password(arguments),
        **_dialing(arguments),
    )
    tag = client.head(_headed(arguments), if_match=getattr(arguments, "if_match", None))

    # The tag alone, as `field` prints a value alone, for a script to read, and nothing where
    # the gate gave none: absent is not empty, and `-o json` leaves `etag` out the same way.
    _emit(arguments, _left({}, tag), [] if tag is None else [tag])
    return 0


def _headed(arguments: argparse.Namespace) -> str:
    """The path the read command of this kind asks, which `head` asks with HEAD instead."""
    kind = arguments.kind
    if kind == "status":
        return STATUS_PATH
    if kind == "whoami":
        return IDENTITY_PATH
    if kind == "groups":
        under = arguments.under
        return "/groups" if under is None else f"/groups/{_segment(under)}/groups"
    if kind == "entries":
        group = arguments.group
        return "/entries" if group is None else f"/groups/{_segment(group)}/entries"
    if kind == "access":
        if arguments.entry is not None:
            return f"/entries/{_segment(arguments.entry)}/access"
        return f"/groups/{_segment(arguments.group)}/access"
    if kind == "group":
        return f"/groups/{_segment(arguments.uuid)}"

    entry = f"/entries/{_segment(arguments.uuid)}"
    below = {"entry": "", "fields": "/fields", "attachments": "/attachments"}
    if kind in below:
        return entry + below[kind]

    plural = "fields" if kind == "field" else "attachments"
    return f"{entry}/{plural}/{_segment(arguments.name)}"


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
        f"added {arguments.name} to {arguments.uuid}",
    )


def _update_attachment(arguments: argparse.Namespace) -> int:
    return _attaching(
        arguments,
        lambda client, content: client.update_attachment(
            arguments.uuid, arguments.name, content, if_match=arguments.if_match
        ),
        f"updated {arguments.name} on {arguments.uuid}",
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
        [f"stripped {arguments.name} from {arguments.uuid}"],
    )
    return 0


if __name__ == "__main__":  # pragma: no cover, the module entry point
    raise SystemExit(main())
