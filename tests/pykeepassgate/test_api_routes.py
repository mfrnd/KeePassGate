"""Every route the client covers, against a gate that answers what the document says it would.

The reference test holds the client to the routing table; this holds it to what the answers mean.
Two things are worth the trouble of asserting here rather than in the plugin: which path and method
each call sends, because that is the client's own decision, and what a listed entry does when
somebody asks it for a password it never fetched.
"""

from __future__ import annotations

import base64
import json
from collections.abc import Callable
from datetime import UTC, datetime, timedelta, timezone
from pathlib import Path
from typing import Any
from urllib.parse import quote

import pytest

from conftest import FakeGate
from pykeepassgate import credentials as credentials_module
from pykeepassgate.client import ROOT, GateClient
from pykeepassgate.errors import ProtocolError, Withheld
from pykeepassgate.model import Attachment, Entry, Field, Fields, Group
from test_reference import operations, routing_table

UUID = "7b1c" * 8
OTHER = "9f86" * 8


@pytest.fixture
def paired(tmp_path: Path, bundle: dict[str, Any], gate: FakeGate) -> GateClient:
    """A client that has been through pairing with this gate."""
    stored = credentials_module.save(tmp_path / "kept", gate.port, bundle)
    return GateClient(stored, connection_timeout=10.0)


def answering(gate: FakeGate, body: dict[str, Any], status: int = 200) -> None:
    """Answer every request with this, whatever it asks for."""
    gate.handler = lambda path, sent, client: (status, json.dumps(body))


def test_groups_are_listed_read_and_walked(gate: FakeGate, paired: GateClient) -> None:
    answering(gate, {"groups": [{"uuid": UUID, "name": "Fictitious", "parent": None}]})

    listed = paired.groups()

    assert [group.uuid for group in listed] == [UUID]
    assert listed[0].is_root, "the root is the one group with nothing above it"
    assert gate.requests == [("GET", "/groups")]

    paired.subgroups(UUID)
    assert gate.requests[-1] == ("GET", f"/groups/{UUID}/groups")

    answering(gate, {"uuid": UUID, "name": "Fictitious", "parent": None, "notes": "invented"})
    read = paired.group(UUID)
    assert read.notes == "invented"
    assert read.is_read


def test_a_listed_group_has_no_notes_rather_than_empty_ones(
    gate: FakeGate, paired: GateClient
) -> None:
    # A listing is what `list` gives, and the notes need `read`: absent, so that no caller
    # takes a group it only listed for one whose notes are blank.
    answering(gate, {"groups": [{"uuid": UUID, "name": "Fictitious", "parent": None}]})

    listed = paired.groups()[0]

    assert not listed.is_read
    with pytest.raises(AttributeError, match="listed rather than read"):
        _ = listed.notes


def test_a_group_defaults_to_the_root_by_name(gate: FakeGate, paired: GateClient) -> None:
    # Which is what lets a client granted only `create` put something somewhere.
    answering(gate, {"uuid": UUID, "name": "Invented", "parent": None})

    paired.group()

    assert gate.requests == [("GET", f"/groups/{ROOT}")]


def test_a_group_is_made_changed_and_removed(gate: FakeGate, paired: GateClient) -> None:
    answering(gate, {"uuid": OTHER})

    assert paired.create_group("Invented") == OTHER
    assert gate.requests[-1] == ("POST", f"/groups/{ROOT}/groups")

    answering(gate, {"ok": True})

    paired.update_group(OTHER, name="Renamed")
    assert gate.requests[-1] == ("PUT", f"/groups/{OTHER}")

    paired.delete_group(OTHER)
    assert gate.requests[-1] == ("DELETE", f"/groups/{OTHER}")


def test_entries_are_swept_or_asked_of_one_group(gate: FakeGate, paired: GateClient) -> None:
    answering(gate, {"entries": [], "count": 0})

    paired.entries()
    assert gate.requests[-1] == ("GET", "/entries")

    paired.entries(group=UUID)
    assert gate.requests[-1] == ("GET", f"/groups/{UUID}/entries")


def test_a_listed_entry_has_no_password_because_it_was_never_asked_for(
    gate: FakeGate, paired: GateClient
) -> None:
    # Absent rather than empty, which is the rule the read model was given before any of this was
    # built. A client that read a blank string here would report a database of blank passwords.
    answering(
        gate,
        {
            "entries": [
                {"uuid": UUID, "group": OTHER, "title": "an invented service", "url": "https://invented"}
            ],
            "count": 1,
        },
    )

    listed = paired.entries()[0]

    assert listed.title == "an invented service"
    assert not listed.is_read

    with pytest.raises(AttributeError, match="listed rather than read"):
        _ = listed.password


def test_a_read_entry_carries_its_fields_and_says_which_are_protected(
    gate: FakeGate, paired: GateClient
) -> None:
    answering(
        gate,
        {
            "uuid": UUID,
            "group": OTHER,
            "created": "2026-03-04T05:06:07Z",
            "modified": "2026-03-04T05:06:07Z",
            "fields": {
                "Title": "an invented service",
                "UserName": "agent-fictitious",
                "Password": "not-a-real-password",
                "Notes": "invented notes",
                "Invented": "a custom one",
            },
            "protected": ["Password"],
        },
    )

    entry = paired.entry(UUID)

    assert entry.is_read
    assert entry.username == "agent-fictitious"
    assert entry.password == "not-a-real-password"
    assert entry.notes == "invented notes"
    assert entry.withheld == ()
    assert entry.custom() == {"Invented": "a custom one"}
    assert entry.protected == ("Password",)
    assert entry.created is not None and entry.created.year == 2026


def test_a_withheld_protected_value_stays_withheld(
    gate: FakeGate, paired: GateClient
) -> None:
    """A `null` is not the word "None", and asking for one is not answered with a stand-in."""
    answering(
        gate,
        {
            "uuid": UUID,
            "group": OTHER,
            "fields": {
                "Title": "an invented service",
                "UserName": "agent-fictitious",
                "Password": None,
            },
            "protected": ["Password"],
        },
    )

    entry = paired.entry(UUID)

    assert entry.username == "agent-fictitious"
    assert entry.withheld == ("Password",)

    # The message names the field and says what would open it, which is the operator adding
    # `P` to the rule that already lets this client read the entry rather than a new pairing.
    with pytest.raises(Withheld, match=r"'Password' is withheld.*rule that lets this client read"):
        _ = entry.password


#: A read answer holding a withheld value among each kind of string: a standard one, a custom
#: one, and readable ones of both kinds beside them.
WITHHOLDING = {
    "uuid": UUID,
    "group": OTHER,
    "fields": {
        "Title": "an invented service",
        "UserName": "agent-fictitious",
        "Password": None,
        "Invented": "a custom one",
        "Deploy key": None,
    },
    "protected": ["Password", "Deploy key"],
}

#: Every path a caller has to a withheld value, each of which has to raise rather than hand back
#: anything at all. `getattr` with a default and `hasattr` are here because they swallow an
#: `AttributeError`, which is why `Withheld` is not one.
WITHHELD_PATHS: list[tuple[str, Callable[[Entry], object]]] = [
    ("the attribute", lambda entry: entry.password),
    ("getattr with a default", lambda entry: getattr(entry, "password", "")),
    ("hasattr", lambda entry: hasattr(entry, "password")),
    ("by name", lambda entry: entry.fields["Password"] if entry.fields is not None else None),
    ("get", lambda entry: entry.fields.get("Password") if entry.fields is not None else None),
    (
        "get with a default",
        lambda entry: entry.fields.get("Password", "") if entry.fields is not None else None,
    ),
    ("values", lambda entry: list(entry.fields.values()) if entry.fields is not None else None),
    ("items", lambda entry: list(entry.fields.items()) if entry.fields is not None else None),
    ("dict", lambda entry: dict(entry.fields) if entry.fields is not None else None),
    ("a custom one by name", lambda entry: entry.custom()["Deploy key"]),
    ("a custom one by get", lambda entry: entry.custom().get("Deploy key", "")),
    ("the custom values", lambda entry: list(entry.custom().values())),
]


class TestAWithheldValueIsNeverAValue:
    """`Withheld` from every way of reading one, and the names still told truthfully."""

    @pytest.fixture
    def entry(self, gate: FakeGate, paired: GateClient) -> Entry:
        answering(gate, WITHHOLDING)
        return paired.entry(UUID)

    @pytest.mark.parametrize(
        "read", [case[1] for case in WITHHELD_PATHS], ids=[case[0] for case in WITHHELD_PATHS]
    )
    def test_every_way_of_reading_one_raises(
        self, entry: Entry, read: Callable[[Entry], object]
    ) -> None:
        with pytest.raises(Withheld):
            read(entry)

    def test_the_names_say_what_the_entry_holds_withheld_ones_included(self, entry: Entry) -> None:
        assert entry.fields is not None
        assert list(entry.fields) == ["Title", "UserName", "Password", "Invented", "Deploy key"]
        assert len(entry.fields) == 5
        assert "Password" in entry.fields
        assert "Absent" not in entry.fields
        assert entry.fields.withheld == ("Password", "Deploy key")
        assert entry.withheld == ("Password", "Deploy key")
        assert list(entry.custom()) == ["Invented", "Deploy key"]
        assert entry.custom().withheld == ("Deploy key",)

    def test_every_value_this_client_may_read_is_read_by_asking_first(self, entry: Entry) -> None:
        # What the documentation shows: ask, rather than catch, and nothing withheld is touched.
        assert entry.fields is not None
        readable = {name: entry.fields[name] for name in entry.fields if name not in entry.withheld}

        assert readable == {
            "Title": "an invented service",
            "UserName": "agent-fictitious",
            "Invented": "a custom one",
        }

    def test_a_name_the_entry_does_not_hold_is_a_key_error_as_in_any_mapping(
        self, entry: Entry
    ) -> None:
        assert entry.fields is not None
        assert entry.fields.get("Absent") is None
        assert entry.fields.get("Absent", "fallback") == "fallback"

        with pytest.raises(KeyError):
            _ = entry.fields["Absent"]

    def test_entries_holding_a_withheld_value_compare_and_hash_without_reading_it(
        self, entry: Entry
    ) -> None:
        # A mapping compares by reading its items, which here would raise out of `==`.
        again = Entry.read(WITHHOLDING)

        assert entry == again
        assert hash(entry.fields) == hash(again.fields)
        assert entry.fields != {"Title": "an invented service"}, "a dict holds no withheld value"

    def test_with_nothing_withheld_the_strings_compare_to_a_plain_mapping(self) -> None:
        assert Fields({"Title": "x", "URL": ""}) == {"URL": "", "Title": "x"}
        assert Fields({"Title": "x"}) != {"Title": "y"}
        assert Fields({"Title": "x"}) != "x", "and to nothing that is not a mapping"

    def test_the_strings_print_their_names_and_no_value(self, entry: Entry) -> None:
        # A repr is what a log line or a traceback carries, so it names what is there and which
        # of it is withheld, and shows none of what is readable either.
        printed = repr(entry.fields)

        assert "'Password'" in printed and "'Invented'" in printed
        assert "a custom one" not in printed
        assert "agent-fictitious" not in printed

    def test_a_withheld_value_is_withheld_on_its_own_route_too(
        self, gate: FakeGate, paired: GateClient
    ) -> None:
        answering(gate, {"name": "Password", "protected": True, "value": None})

        read = paired.field(UUID, "Password")

        assert read.withheld
        assert read.is_read, "it was read, and refused"
        with pytest.raises(Withheld, match="'Password' is withheld") as refused:
            _ = read.value
        assert refused.value.name == "Password"

    def test_a_listed_field_has_no_value_rather_than_a_withheld_one(
        self, gate: FakeGate, paired: GateClient
    ) -> None:
        # Never asked for, which is absence, `AttributeError`, as for a listed entry's password.
        answering(gate, {"fields": [{"name": "Password", "protected": True}]})

        listed = paired.fields(UUID)[0]

        assert not listed.withheld
        assert not listed.is_read
        with pytest.raises(AttributeError, match="listed rather than read"):
            _ = listed.value


def test_an_entry_is_made_changed_and_removed(gate: FakeGate, paired: GateClient) -> None:
    sent: list[dict[str, Any]] = []
    # A create answers `201` and everything after it `200`, which is what the document declares.
    # Answering `200` throughout is what let the client accept only that and nobody notice.
    answers = [201]

    def answer(path: str, body: str, client: bytes | None) -> tuple[int, str]:
        if body:
            sent.append(json.loads(body))
        return answers[0], json.dumps({"uuid": UUID})

    gate.handler = answer

    assert paired.create_entry(title="an invented service", password="not-a-real-password") == UUID
    answers[0] = 200
    assert gate.requests[-1] == ("POST", f"/groups/{ROOT}/entries")
    assert sent[-1] == {"Title": "an invented service", "Password": "not-a-real-password"}

    paired.update_entry(UUID, username="agent-fictitious")
    assert gate.requests[-1] == ("PUT", f"/entries/{UUID}")
    assert sent[-1] == {"UserName": "agent-fictitious"}, "only what was named is written"

    paired.delete_entry(UUID)
    assert gate.requests[-1] == ("DELETE", f"/entries/{UUID}")


def test_every_create_takes_the_201_the_document_declares(
    gate: FakeGate, paired: GateClient
) -> None:
    """`createGroup`, `createEntry`, `createField` and `createAttachment` all answer `201`."""
    gate.handler = lambda path, body, client: (201, json.dumps({"uuid": UUID}))

    assert paired.create_entry(title="an invented service") == UUID
    assert paired.create_group("an invented group") == UUID

    # These two answer nothing a caller keeps, so the assertion is that they do not raise.
    paired.create_field(UUID, "Invented", "a value")
    paired.create_attachment(UUID, "invented.txt", b"nothing real")


def test_a_title_a_listing_withholds_raises_rather_than_reading_as_none(
    gate: FakeGate, paired: GateClient
) -> None:
    """A listing withholds a protected title, and reading it says so rather than answer `None`."""
    # The message names the rule that let this client list the entry, which is the one a
    # listing answers to.
    answering(
        gate,
        {
            "entries": [
                {
                    "uuid": UUID,
                    "group": OTHER,
                    "title": None,
                    "url": None,
                    "created": "2026-03-04T05:06:07Z",
                    "modified": "2026-03-04T05:06:07Z",
                },
            ],
            "count": 1,
        },
    )

    listed = paired.entries()[0]

    assert listed.withheld == ("Title", "URL")
    with pytest.raises(Withheld, match="rule that lets this client list the entry"):
        _ = listed.title
    with pytest.raises(Withheld, match="'URL' is withheld"):
        _ = listed.url


@pytest.mark.parametrize("member", ["title", "url"])
def test_a_listing_that_does_not_say_a_title_or_a_url_is_not_read_as_either(
    gate: FakeGate, paired: GateClient, member: str
) -> None:
    # The document requires both on every listed entry, `null` when withheld. One that is missing
    # is not withheld and not empty either: it is an answer this client cannot read.
    summary = {"uuid": UUID, "group": OTHER, "title": "x", "url": ""}
    del summary[member]
    answering(gate, {"entries": [summary], "count": 1})

    with pytest.raises(ProtocolError, match=member):
        paired.entries()


def test_a_withheld_field_value_is_withheld_rather_than_none(
    gate: FakeGate, paired: GateClient
) -> None:
    """Reading one protected field without the switch answers `null`, not the word "None"."""
    answering(gate, {"name": "Password", "protected": True, "value": None})

    read = paired.field(UUID, "Password")

    assert read.withheld
    with pytest.raises(Withheld):
        _ = read.value


def test_fields_are_listed_read_added_changed_and_removed(
    gate: FakeGate, paired: GateClient
) -> None:
    answering(gate, {"fields": [{"name": "Password", "protected": True}]})

    listed = paired.fields(UUID)

    assert (listed[0].name, listed[0].protected, listed[0].is_read) == ("Password", True, False)
    assert gate.requests[-1] == ("GET", f"/entries/{UUID}/fields")

    answering(gate, {"name": "Invented", "value": "a value", "protected": False})

    read = paired.field(UUID, "Invented")
    assert (read.value, read.is_read) == ("a value", True)
    assert gate.requests[-1] == ("GET", f"/entries/{UUID}/fields/Invented")

    sent: list[dict[str, Any]] = []

    def answer(path: str, body: str, client: bytes | None) -> tuple[int, str]:
        sent.append(json.loads(body))
        return 200, json.dumps({"uuid": "Invented"})

    gate.handler = answer

    # The marker is the whole of what makes a custom name protected. Without it the gate stores
    # the value under whatever the database protects by that name, which for this one is nothing,
    # and answers 201: a secret in clear, and nothing on this side would notice.
    paired.create_field(UUID, "Invented", "a value", protected=True)
    assert gate.requests[-1] == ("POST", f"/entries/{UUID}/fields")
    assert sent[-1] == {"name": "Invented", "value": "a value", "protected": "true"}

    paired.create_field(UUID, "Invented", "a value")
    assert sent[-1] == {"name": "Invented", "value": "a value"}, "the database decides, not false"

    answering(gate, {"ok": True})
    paired.update_field(UUID, "Invented", "another")
    assert gate.requests[-1] == ("PUT", f"/entries/{UUID}/fields/Invented")

    paired.strip_field(UUID, "Invented")
    assert gate.requests[-1] == ("DELETE", f"/entries/{UUID}/fields/Invented")


def test_a_field_name_that_needs_escaping_gets_it(gate: FakeGate, paired: GateClient) -> None:
    answering(gate, {"name": "a/b", "value": "x", "protected": False})

    paired.field(UUID, "a/b")

    assert gate.requests[-1] == ("GET", f"/entries/{UUID}/fields/a%2Fb"), (
        "the separator is escaped, which is why the gate splits a path before decoding it"
    )


def test_attachments_are_listed_read_added_changed_and_removed(
    gate: FakeGate, paired: GateClient
) -> None:
    content = b"an invented attachment"
    encoded = base64.b64encode(content).decode("ascii")

    answering(gate, {"attachments": [{"name": "notes.txt", "size": len(content)}]})

    listed = paired.attachments(UUID)

    assert (listed[0].name, listed[0].size, listed[0].is_read) == ("notes.txt", len(content), False)

    answering(gate, {"name": "notes.txt", "size": len(content), "content": encoded})

    read = paired.attachment(UUID, "notes.txt")
    assert read.content == content
    assert read.is_read

    sent: list[dict[str, Any]] = []

    def answer(path: str, body: str, client: bytes | None) -> tuple[int, str]:
        if body:
            sent.append(json.loads(body))
        return 200, json.dumps({"uuid": "notes.txt"})

    gate.handler = answer

    paired.create_attachment(UUID, "notes.txt", content)
    assert sent[-1] == {"name": "notes.txt", "content": encoded}

    paired.update_attachment(UUID, "notes.txt", b"replaced")
    assert gate.requests[-1] == ("PUT", f"/entries/{UUID}/attachments/notes.txt")

    paired.strip_attachment(UUID, "notes.txt")
    assert gate.requests[-1] == ("DELETE", f"/entries/{UUID}/attachments/notes.txt")


def test_a_moment_the_gate_did_not_write_is_absent_rather_than_wrong(
    gate: FakeGate, paired: GateClient
) -> None:
    answering(
        gate,
        {"uuid": UUID, "group": OTHER, "fields": {}, "protected": [], "created": "not a time"},
    )

    entry = paired.entry(UUID)

    assert entry.created is None


#: A value no path can carry as it stands: the transport refuses the space before anything is
#: sent, and an unescaped slash routes somewhere else. One for what addresses a node and one for
#: a name inside it, so that each is seen to be escaped on its own.
AWKWARD = "an invented/node"
AWKWARD_NAME = "an invented/name"

#: The five standard strings, every one given, so the body shows every key the client can write.
STANDARD = {
    "title": "an invented service",
    "username": "agent-fictitious",
    "password": "not-a-real-password",
    "url": "https://invented.example",
    "notes": "invented notes",
}
STANDARD_SENT = {
    "Title": "an invented service",
    "UserName": "agent-fictitious",
    "Password": "not-a-real-password",
    "URL": "https://invented.example",
    "Notes": "invented notes",
}

ATTACHED = base64.b64encode(b"an invented attachment").decode("ascii")
REPLACED = base64.b64encode(b"replaced").decode("ascii")

#: A moment with nothing below the second, which is all a `.kdbx` keeps of one.
MOMENT = datetime(2031, 2, 3, 4, 5, 6, tzinfo=UTC)

#: Every call that puts a caller's value into a path or a body: the operation it is meant to be,
#: the call, what fills the path's parameters, and the body it has to send, or None for none.
#: Every optional argument is given, so a body missing a member the client can write is wrong.
WIRE: list[tuple[str, Callable[[GateClient], object], dict[str, str], dict[str, str] | None]] = [
    ("readGroup", lambda c: c.group(AWKWARD), {"uuid": AWKWARD}, None),
    ("listSubgroups", lambda c: c.subgroups(AWKWARD), {"uuid": AWKWARD}, None),
    (
        "createGroup",
        lambda c: c.create_group("Invented", parent=AWKWARD, notes="invented notes"),
        {"uuid": AWKWARD},
        {"Name": "Invented", "Notes": "invented notes"},
    ),
    (
        "updateGroup",
        lambda c: c.update_group(AWKWARD, name="Renamed", notes="invented notes"),
        {"uuid": AWKWARD},
        {"Name": "Renamed", "Notes": "invented notes"},
    ),
    ("deleteGroup", lambda c: c.delete_group(AWKWARD), {"uuid": AWKWARD}, None),
    ("listGroupEntries", lambda c: c.entries(group=AWKWARD), {"uuid": AWKWARD}, None),
    ("readEntry", lambda c: c.entry(AWKWARD), {"uuid": AWKWARD}, None),
    (
        "createEntry",
        lambda c: c.create_entry(AWKWARD, **STANDARD, expires="auto"),
        {"uuid": AWKWARD},
        {**STANDARD_SENT, "expires": "auto"},
    ),
    (
        "updateEntry",
        lambda c: c.update_entry(AWKWARD, **STANDARD, expires=MOMENT),
        {"uuid": AWKWARD},
        {**STANDARD_SENT, "expires": "2031-02-03T04:05:06Z"},
    ),
    ("deleteEntry", lambda c: c.delete_entry(AWKWARD), {"uuid": AWKWARD}, None),
    ("listFields", lambda c: c.fields(AWKWARD), {"uuid": AWKWARD}, None),
    (
        "readField",
        lambda c: c.field(AWKWARD, AWKWARD_NAME),
        {"uuid": AWKWARD, "name": AWKWARD_NAME},
        None,
    ),
    (
        "createField",
        lambda c: c.create_field(AWKWARD, AWKWARD_NAME, "a value", protected=True),
        {"uuid": AWKWARD},
        {"name": AWKWARD_NAME, "value": "a value", "protected": "true"},
    ),
    (
        "updateField",
        lambda c: c.update_field(AWKWARD, AWKWARD_NAME, "another"),
        {"uuid": AWKWARD, "name": AWKWARD_NAME},
        {"value": "another"},
    ),
    (
        "stripField",
        lambda c: c.strip_field(AWKWARD, AWKWARD_NAME),
        {"uuid": AWKWARD, "name": AWKWARD_NAME},
        None,
    ),
    ("listAttachments", lambda c: c.attachments(AWKWARD), {"uuid": AWKWARD}, None),
    (
        "readAttachment",
        lambda c: c.attachment(AWKWARD, AWKWARD_NAME),
        {"uuid": AWKWARD, "name": AWKWARD_NAME},
        None,
    ),
    (
        "createAttachment",
        lambda c: c.create_attachment(AWKWARD, AWKWARD_NAME, b"an invented attachment"),
        {"uuid": AWKWARD},
        {"name": AWKWARD_NAME, "content": ATTACHED},
    ),
    (
        "updateAttachment",
        lambda c: c.update_attachment(AWKWARD, AWKWARD_NAME, b"replaced"),
        {"uuid": AWKWARD, "name": AWKWARD_NAME},
        {"content": REPLACED},
    ),
    (
        "stripAttachment",
        lambda c: c.strip_attachment(AWKWARD, AWKWARD_NAME),
        {"uuid": AWKWARD, "name": AWKWARD_NAME},
        None,
    ),
]

#: The two reads whose answer shares a member's name with another's and not its type: a field
#: listing's `fields` is a list where an entry's is an object, and a field's `protected` is a flag
#: where an entry's is a list of names. Each is answered as the gate answers it.
OWN_ANSWER: dict[str, dict[str, Any]] = {
    "listFields": {"fields": []},
    "readField": {"name": "an invented name", "value": "x", "protected": False},
}

#: One answer every other reader above can take, since what is asserted here is the question.
ANY_ANSWER: dict[str, Any] = {
    "uuid": UUID,
    "name": "an invented name",
    "parent": None,
    "group": OTHER,
    "fields": {},
    "protected": [],
    "value": "x",
    "content": "",
    "size": 0,
    "groups": [],
    "entries": [],
    "attachments": [],
    "count": 0,
}


@pytest.fixture(scope="module")
def document() -> dict[str, Any]:
    """The routing table the plugin embeds, read from the same file it embeds."""
    return routing_table()


def resolved(schema: dict[str, Any], document: dict[str, Any]) -> dict[str, Any]:
    """A schema with its `$ref` followed, which is the one indirection the document uses."""
    while "$ref" in schema:
        target: Any = document
        for part in schema["$ref"].removeprefix("#/").split("/"):
            target = target[part]
        schema = target
    return schema


def disagreements(
    value: Any, schema: dict[str, Any], document: dict[str, Any], where: str
) -> list[str]:
    """What the gate's own body check would refuse about this value, as far as this wire goes.

    The subset `BodyCheck` implements: objects with required and undeclared members, and strings
    with their lengths. A schema using anything else fails here rather than being waved through,
    for the reason the plugin refuses one at startup.
    """
    schema = resolved(schema, document)
    kind = schema.get("type")

    if kind == "object":
        if not isinstance(value, dict):
            return [f"{where} is not an object"]
        members: dict[str, Any] = schema.get("properties", {})
        required: list[str] = schema.get("required", [])
        found = [f"{where} lacks {name}" for name in required if name not in value]
        if schema.get("additionalProperties") is False:
            found += [f"{where} carries {name}" for name in value if name not in members]
        for name, member in value.items():
            if name in members:
                found += disagreements(member, members[name], document, f"{where}.{name}")
        return found

    if kind == "string":
        if not isinstance(value, str):
            return [f"{where} is not a string"]
        if not schema.get("minLength", 0) <= len(value) <= schema.get("maxLength", len(value)):
            return [f"{where} is outside the lengths declared"]
        return []

    return [f"{where} is declared as {kind!r}, which this check does not know"]


@pytest.mark.parametrize(
    ("operation", "call", "parameters", "body"), WIRE, ids=[case[0] for case in WIRE]
)
def test_every_call_sends_what_its_operation_declares(
    gate: FakeGate,
    paired: GateClient,
    document: dict[str, Any],
    operation: str,
    call: Callable[[GateClient], object],
    parameters: dict[str, str],
    body: dict[str, str] | None,
) -> None:
    """Method, path and body, each held to the routing table rather than to this file.

    The tests above name the path they expect, which is the client agreeing with itself. This
    reads the operation out of the document the plugin serves, fills its path with values that
    have to be escaped, and holds the body to the schema the gate's own check applies.
    """
    declaration = operations(document)[operation]

    bodies: list[Any] = []

    def answer(path: str, sent: str, client: bytes | None) -> tuple[int, str]:
        bodies.append(json.loads(sent) if sent else None)
        return 200, json.dumps(OWN_ANSWER.get(operation, ANY_ANSWER))

    gate.handler = answer

    call(paired)

    path = declaration["path"]
    for name, value in parameters.items():
        path = path.replace("{" + name + "}", quote(value, safe=""))

    assert "{" not in path, f"{operation} has a parameter this case does not fill"
    assert gate.requests[-1] == (declaration["method"].upper(), path)
    assert bodies[-1] == body

    request = declaration.get("requestBody")
    if request is None:
        assert body is None, f"{operation} declares no body and the client sends one"
        return

    schema = request["content"]["application/json"]["schema"]
    assert disagreements(bodies[-1], schema, document, operation) == []
    assert set(bodies[-1]) == set(resolved(schema, document).get("properties", {})), (
        "every member the gate accepts is one the client sends, given every argument"
    )


def test_every_call_that_carries_a_value_is_held_to_the_document(
    document: dict[str, Any],
) -> None:
    """A route with a path parameter or a body cannot arrive without a case above."""
    carrying = {
        name
        for name, operation in operations(document).items()
        if operation["method"] != "head"
        and not operation["path"].startswith("/pair/")
        and ("{" in operation["path"] or "requestBody" in operation)
    }

    assert carrying == {case[0] for case in WIRE}


def test_a_read_entry_says_when_it_expires(gate: FakeGate, paired: GateClient) -> None:
    """`None` is an entry that does not expire, and a moment is when one does."""
    answering(
        gate,
        {
            "uuid": UUID,
            "group": OTHER,
            "fields": {},
            "protected": [],
            "expires": "2001-02-03T04:05:06Z",
        },
    )

    retired = paired.entry(UUID)

    assert retired.expires == datetime(2001, 2, 3, 4, 5, 6, tzinfo=UTC)
    assert retired.expired, "a moment in the past is an entry KeePass strikes through"

    answering(gate, {"uuid": UUID, "group": OTHER, "fields": {}, "protected": [], "expires": None})

    live = paired.entry(UUID)

    assert live.expires is None
    assert not live.expired


def test_an_entry_expiring_later_has_not_expired(gate: FakeGate, paired: GateClient) -> None:
    later = datetime.now(UTC) + timedelta(days=1)
    answering(
        gate,
        {
            "uuid": UUID,
            "group": OTHER,
            "fields": {},
            "protected": [],
            "expires": later.strftime("%Y-%m-%dT%H:%M:%SZ"),
        },
    )

    assert not paired.entry(UUID).expired


def test_a_listed_entry_says_nothing_about_when_it_expires(
    gate: FakeGate, paired: GateClient
) -> None:
    """Absent rather than never: a listing does not carry it, so a listed entry has none."""
    listing = {"entries": [{"uuid": UUID, "group": OTHER, "title": "x", "url": ""}], "count": 1}
    answering(gate, listing)

    listed = paired.entries()[0]

    assert not hasattr(listed, "expires")
    with pytest.raises(AttributeError, match="listed rather than read"):
        _ = listed.expired


def test_a_read_that_says_nothing_about_expiry_has_none(gate: FakeGate, paired: GateClient) -> None:
    """Not never, which is what `None` means here, and not the listing's message either."""
    answering(gate, {"uuid": UUID, "group": OTHER, "fields": {}, "protected": []})

    entry = paired.entry(UUID)

    with pytest.raises(AttributeError, match="said nothing"):
        _ = entry.expires


@pytest.mark.parametrize("said", ["not a time", "", 20310203, "2031-02-03T04:05:06+01:00"])
def test_an_expiry_this_client_cannot_read_is_refused_rather_than_read_as_never(
    gate: FakeGate, paired: GateClient, said: object
) -> None:
    """Read as never, it would report a credential the operator retired as a live one."""
    answering(gate, {"uuid": UUID, "group": OTHER, "fields": {}, "protected": [], "expires": said})

    with pytest.raises(ProtocolError):
        paired.entry(UUID)


def test_a_listing_can_leave_expired_entries_out(gate: FakeGate, paired: GateClient) -> None:
    """Kept unless asked, and asked in the one word the gate reads."""
    answering(gate, {"entries": [], "count": 0})

    paired.entries()
    assert gate.requests[-1] == ("GET", "/entries"), "the default asks nothing of the query"

    paired.entries(include_expired=False)
    assert gate.requests[-1] == ("GET", "/entries?includeExpired=false")

    paired.entries(group=UUID, include_expired=False)
    assert gate.requests[-1] == ("GET", f"/groups/{UUID}/entries?includeExpired=false")


def test_an_entry_is_made_to_expire_or_not(gate: FakeGate, paired: GateClient) -> None:
    sent: list[dict[str, Any]] = []

    def answer(path: str, body: str, client: bytes | None) -> tuple[int, str]:
        sent.append(json.loads(body))
        return 201, json.dumps({"uuid": UUID})

    gate.handler = answer

    paired.create_entry(title="x")
    assert sent[-1] == {"Title": "x"}, "no expiry is asked for unless one is given"

    paired.create_entry(title="x", expires="auto")
    assert sent[-1] == {"Title": "x", "expires": "auto"}

    paired.create_entry(title="x", expires=False)
    assert sent[-1] == {"Title": "x", "expires": ""}

    # In UTC, whatever zone the moment was given in, and to the second.
    ahead = timezone(timedelta(hours=2))
    paired.create_entry(title="x", expires=datetime(2031, 2, 3, 6, 5, 6, 789000, tzinfo=ahead))
    assert sent[-1] == {"Title": "x", "expires": "2031-02-03T04:05:06Z"}


def test_an_update_moves_an_expiry_or_takes_it_off(gate: FakeGate, paired: GateClient) -> None:
    sent: list[dict[str, Any]] = []

    def answer(path: str, body: str, client: bytes | None) -> tuple[int, str]:
        sent.append(json.loads(body))
        return 200, json.dumps({"ok": True})

    gate.handler = answer

    paired.update_entry(UUID, expires=MOMENT)
    assert sent[-1] == {"expires": "2031-02-03T04:05:06Z"}

    paired.update_entry(UUID, password="a-fresh-invented-one", expires=False)
    assert sent[-1] == {"Password": "a-fresh-invented-one", "expires": ""}

    paired.update_entry(UUID, username="agent-fictitious")
    assert sent[-1] == {"UserName": "agent-fictitious"}, "left out, the expiry stays as it is"


@pytest.mark.parametrize(
    "call",
    [
        lambda c: c.create_entry(title="x", expires=datetime(2031, 2, 3, 4, 5, 6)),
        lambda c: c.update_entry(UUID, expires=datetime(2031, 2, 3, 4, 5, 6)),
        lambda c: c.update_entry(UUID, expires="auto"),
        lambda c: c.create_entry(title="x", expires=True),
        lambda c: c.create_entry(title="x", expires="Auto"),
    ],
    ids=["a create with no zone", "an update with no zone", "auto on an update", "true", "Auto"],
)
def test_an_expiry_the_gate_would_refuse_is_never_sent(
    gate: FakeGate, paired: GateClient, call: Callable[[GateClient], object]
) -> None:
    """A moment with no time zone names no instant, and the rest the gate answers `400`."""
    answering(gate, {"uuid": UUID})

    with pytest.raises(ValueError, match="expiry"):
        call(paired)

    assert gate.requests == [], "refused before anything was dialed"


#: Answers the gate never gives, one member missing or of the wrong type each, and the call that
#: reads it. Only a plugin defect or a plugin and a client of different versions sends one.
UNREADABLE: list[tuple[str, dict[str, Any], Callable[[GateClient], object]]] = [
    ("a create with no uuid", {}, lambda c: c.create_entry(title="x")),
    ("a null uuid", {"uuid": None}, lambda c: c.create_group("x")),
    ("a uuid that is a number", {"uuid": 7}, lambda c: c.create_group("x")),
    ("names rather than groups", {"groups": ["root"]}, lambda c: c.groups()),
    ("a group with no name", {"groups": [{"uuid": UUID, "parent": None}]}, lambda c: c.subgroups()),
    ("an entry with no group", {"entries": [{"uuid": UUID, "title": "x"}]}, lambda c: c.entries()),
    (
        "fields that are a list",
        {"uuid": UUID, "group": OTHER, "fields": ["Title"], "protected": []},
        lambda c: c.entry(UUID),
    ),
    (
        "a value that is a number",
        {"uuid": UUID, "group": OTHER, "fields": {"Title": 7}, "protected": []},
        lambda c: c.entry(UUID),
    ),
    ("a field listed as unprotected by saying nothing", {"fields": [{"name": "x"}]},
     lambda c: c.fields(UUID)),
    ("a field whose value is a number", {"name": "x", "value": 7, "protected": False},
     lambda c: c.field(UUID, "x")),
    ("a field read with no value at all", {"name": "x", "protected": False},
     lambda c: c.field(UUID, "x")),
    (
        "a listed title that is a number",
        {"entries": [{"uuid": UUID, "group": OTHER, "title": 7, "url": ""}]},
        lambda c: c.entries(),
    ),
    ("a size that is a word", {"attachments": [{"name": "a.txt", "size": "big"}]},
     lambda c: c.attachments(UUID)),
    ("content cut short", {"name": "a.txt", "size": 3, "content": "abc"},
     lambda c: c.attachment(UUID, "a.txt")),
    ("content outside the alphabet", {"name": "a.txt", "size": 3, "content": "YW!Jj"},
     lambda c: c.attachment(UUID, "a.txt")),
    ("no content", {"name": "a.txt", "size": 3, "content": None},
     lambda c: c.attachment(UUID, "a.txt")),
]


@pytest.mark.parametrize(
    ("answer", "call"),
    [case[1:] for case in UNREADABLE],
    ids=[case[0] for case in UNREADABLE],
)
def test_an_answer_this_client_cannot_read_is_a_protocol_error(
    gate: FakeGate,
    paired: GateClient,
    answer: dict[str, Any],
    call: Callable[[GateClient], object],
) -> None:
    """What `errors.py` promises rather than `KeyError`, `ValueError` or `binascii.Error`.

    Each of these used to leave as one of those, outside the one root a caller catches, and end
    the command line in a traceback. A `null` uuid came back as the word "None", addressed as a
    group, and base64 read leniently dropped a character outside its alphabet and answered with
    bytes the gate never sent.
    """
    answering(gate, answer)

    with pytest.raises(ProtocolError):
        call(paired)


def test_a_moment_that_is_not_text_is_absent_rather_than_a_crash(
    gate: FakeGate, paired: GateClient
) -> None:
    # A time the gate did not write is `None`, as one it wrote wrongly already was. A number used
    # to reach `strptime` and leave as a `TypeError`.
    answering(
        gate, {"uuid": UUID, "group": OTHER, "fields": {}, "protected": [], "created": 20260304}
    )

    assert paired.entry(UUID).created is None


@pytest.mark.parametrize("kind", [Entry, Group, Field, Attachment, GateClient])
def test_nothing_here_has_a_save(kind: type) -> None:
    # The first of the read model's two rules. `pykeepass` saves the file it opened, and a method
    # that looks like that and does something else is worse than no method: a write here is one
    # of the client's named calls, which says it is talking to a gate.
    assert not hasattr(kind, "save")


def test_a_listed_entry_holds_no_custom_fields_rather_than_none(
    gate: FakeGate, paired: GateClient
) -> None:
    # The second rule: absent rather than empty. An empty dictionary here would report every
    # entry a listing returned as having no custom fields, which nobody asked.
    answering(gate, {"entries": [{"uuid": UUID, "group": OTHER, "title": "x", "url": ""}]})

    with pytest.raises(AttributeError, match="listed rather than read"):
        paired.entries()[0].custom()


def test_a_value_withheld_on_its_own_still_says_it_is_protected(
    gate: FakeGate, paired: GateClient
) -> None:
    # Withheld and protected are two answers, and both arrive as the gate said them: a protected
    # value the rule opens is protected and not withheld.
    answering(gate, {"name": "Password", "protected": True, "value": None})

    read = paired.field(UUID, "Password")

    assert (read.protected, read.withheld) == (True, True)

    answering(gate, {"name": "Password", "protected": True, "value": "not-a-real-password"})

    opened = paired.field(UUID, "Password")

    assert (opened.protected, opened.withheld, opened.value) == (
        True, False, "not-a-real-password"
    )


def test_a_title_a_read_withholds_raises_and_one_it_does_not_carry_is_empty(
    gate: FakeGate, paired: GateClient
) -> None:
    # A read carries every string the entry holds, so a title it does not carry is one the entry
    # does not have: empty, as KeePass shows it and as a listing of the same entry says. One it
    # carries as `null` is withheld, which is never empty.
    answering(
        gate,
        {
            "uuid": UUID,
            "group": OTHER,
            "created": "2026-03-04T05:06:07Z",
            "fields": {"Title": None, "URL": None},
            "protected": ["Title", "URL"],
        },
    )

    withheld = paired.entry(UUID)

    assert withheld.withheld == ("Title", "URL")
    with pytest.raises(Withheld, match="rule that lets this client read the entry"):
        _ = withheld.title
    with pytest.raises(Withheld):
        _ = withheld.url
    assert withheld.created == datetime(2026, 3, 4, 5, 6, 7, tzinfo=UTC), "in UTC, and aware"

    answering(gate, {"uuid": UUID, "group": OTHER, "fields": {}, "protected": []})

    bare = paired.entry(UUID)

    assert (bare.title, bare.url, bare.withheld) == ("", "", ())
