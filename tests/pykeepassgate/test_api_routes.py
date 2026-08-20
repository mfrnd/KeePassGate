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
from pathlib import Path
from typing import Any
from urllib.parse import quote

import pytest
import yaml

from conftest import FakeGate
from pykeepassgate import credentials as credentials_module
from pykeepassgate.client import ROOT, GateClient
from test_reference import DOCUMENT, operations

UUID = "7b1c" * 8
OTHER = "9f86" * 8


@pytest.fixture
def paired(tmp_path: Path, bundle: dict[str, Any], gate: FakeGate) -> GateClient:
    """A client that has been through pairing with this gate."""
    stored = credentials_module.save(tmp_path / "kept", gate.port, bundle)
    return GateClient(stored, timeout=10.0)


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
                "Invented": "a custom one",
            },
            "protected": ["Password"],
        },
    )

    entry = paired.entry(UUID)

    assert entry.is_read
    assert entry.username == "agent-fictitious"
    assert entry.password == "not-a-real-password"
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

    assert entry.fields is not None
    assert entry.fields["Password"] is None
    assert entry.username == "agent-fictitious"

    with pytest.raises(AttributeError, match="may not read protected values"):
        _ = entry.password


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


def test_a_withheld_title_is_none_rather_than_the_word(
    gate: FakeGate, paired: GateClient
) -> None:
    """A listing withholds a protected title, and `None` is what that looks like."""
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

    assert listed.title is None
    assert listed.url is None


def test_a_withheld_field_value_stays_none(gate: FakeGate, paired: GateClient) -> None:
    """Reading one protected field without the switch answers `null`, not the word "None"."""
    answering(gate, {"name": "Password", "protected": True, "value": None})

    read = paired.field(UUID, "Password")

    assert read.value is None
    assert not read.is_read, "a value that was refused was not read"


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
        lambda c: c.create_entry(AWKWARD, **STANDARD),
        {"uuid": AWKWARD},
        STANDARD_SENT,
    ),
    (
        "updateEntry",
        lambda c: c.update_entry(AWKWARD, **STANDARD),
        {"uuid": AWKWARD},
        STANDARD_SENT,
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

#: One answer every reader above can take, since what is asserted here is the question.
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
    if not DOCUMENT.exists():  # pragma: no cover, only when the tree is not the repository
        pytest.skip(f"no routing table at {DOCUMENT}")

    parsed: dict[str, Any] = yaml.safe_load(DOCUMENT.read_text("utf-8"))
    return parsed


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
        return 200, json.dumps(ANY_ANSWER)

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
