"""The tag a read hands back, and the condition a write can be made under.

Against the stand-in gate, which knows no node and holds no tag: each test says what the gate
answers, as the plugin would, and asserts what the client sends and makes of it. What a tag is,
when one matches and which client is handed one is the plugin's, and its own tiers hold it to that.
"""

from __future__ import annotations

import json
from collections.abc import Callable
from pathlib import Path
from typing import Any

import pytest

from pykeepassgate import credentials as credentials_module
from pykeepassgate.client import GateClient
from pykeepassgate.errors import PreconditionFailed, ProtocolError, RouteRefused
from pykeepassgate.model import Entry, Group
from pykeepassgate.testing import FakeGate
from tagged import NEXT, TAG, UUID, tagging

GROUP: dict[str, Any] = {
    "uuid": UUID,
    "name": "Invented",
    "parent": None,
    "created": "2026-01-02T03:04:05Z",
    "modified": "2026-01-02T03:04:05Z",
    "notes": "",
}

ENTRY: dict[str, Any] = {
    "uuid": UUID,
    "group": "9f86" * 8,
    "created": "2026-01-02T03:04:05Z",
    "modified": "2026-01-02T03:04:05Z",
    "expires": None,
    "fields": {"Title": "an invented service"},
    "protected": [],
}


@pytest.fixture
def paired(tmp_path: Path, bundle: dict[str, Any], gate: FakeGate) -> GateClient:
    """A client that has been through pairing with this gate."""
    stored = credentials_module.save(tmp_path / "kept", gate.port, bundle)
    return GateClient(stored, connection_timeout=10.0)


@pytest.mark.parametrize(
    ("read", "body"),
    [
        (lambda client: client.group(UUID), GROUP),
        (lambda client: client.entry(UUID), ENTRY),
        (
            lambda client: client.field(UUID, "Title"),
            {"name": "Title", "value": "x", "protected": False},
        ),
        (
            lambda client: client.attachment(UUID, "notes.txt"),
            {"name": "notes.txt", "size": 1, "content": "eA=="},
        ),
    ],
    ids=["group", "entry", "field", "attachment"],
)
def test_a_read_keeps_the_tag_it_was_answered_with_exactly(
    gate: FakeGate,
    paired: GateClient,
    read: Callable[[GateClient], Any],
    body: dict[str, Any],
) -> None:
    conditions = tagging(gate, body, TAG)

    found = read(paired)

    assert found.etag == TAG, "quotes and all, since it goes back as it came"
    assert conditions == [None], "and a read asks nothing unless told to"


@pytest.mark.parametrize(
    ("listing", "body"),
    [
        (
            lambda client: client.fields(UUID),
            {"fields": [{"name": "Title", "protected": False}, {"name": "PIN", "protected": True}]},
        ),
        (
            lambda client: client.attachments(UUID),
            {"attachments": [{"name": "a.txt", "size": 1}, {"name": "b.txt", "size": 2}]},
        ),
    ],
    ids=["fields", "attachments"],
)
def test_an_entry_s_listings_hand_out_its_tag_with_each_name(
    gate: FakeGate,
    paired: GateClient,
    listing: Callable[[GateClient], list[Any]],
    body: dict[str, Any],
) -> None:
    # Both are about one entry, so the gate tags them, and a client that may list and strip takes
    # a name off under that tag without reading the value first.
    tagging(gate, body, TAG)

    listed = listing(paired)

    assert [item.etag for item in listed] == [TAG, TAG]


@pytest.mark.parametrize(
    ("listing", "body"),
    [
        (
            lambda client: client.fields(UUID),
            {"fields": [{"name": "Title", "protected": False}]},
        ),
        (
            lambda client: client.attachments(UUID),
            {"attachments": [{"name": "a.txt", "size": 1}]},
        ),
    ],
    ids=["fields", "attachments"],
)
def test_a_listing_the_gate_gave_no_tag_for_has_none_and_says_why(
    gate: FakeGate,
    paired: GateClient,
    listing: Callable[[GateClient], list[Any]],
    body: dict[str, Any],
) -> None:
    # What a client holding `list` without `read` meets: the listings of an entry's fields and
    # attachments are the answers the gate gives about an entry without a tag. Reading an entry
    # or a group takes `read`, and every client holding `read` is handed a tag.
    tagging(gate, body, None)

    (listed,) = listing(paired)

    assert not hasattr(listed, "etag")
    with pytest.raises(AttributeError, match=r"gave no tag.*every client holding `read`"):
        _ = listed.etag


def in_parts(
    gate: FakeGate, first: str | None, later: str | None, changed: bool = False
) -> list[str | None]:
    """Serve a field listing in a first part and a later part, each under the tag given for it.

    `changed` is an entry that changed between the parts, which the gate answers `412` to a
    later part asked for under a condition. What comes back fills with the `If-Match` of every
    request answered, in order.
    """
    conditions: list[str | None] = []

    def answer(path: str, sent: str, client: bytes | None) -> tuple[int, str]:
        conditions.append(gate.if_match())
        asked_later = path.endswith("?from=1")
        if asked_later and changed and gate.if_match() is not None:
            return 412, '{"error":"precondition failed"}'

        tag = later if asked_later else first
        if tag is not None:
            gate.tag(tag)

        if asked_later:
            return 200, json.dumps({"fields": [{"name": "PIN", "protected": True}]})

        return 200, json.dumps({"fields": [{"name": "Title", "protected": False}], "next": 1})

    gate.handler = answer
    return conditions


@pytest.mark.parametrize("given", [None, "*", TAG], ids=["no condition", "any", "the tag"])
def test_the_later_parts_of_a_field_listing_are_asked_for_under_the_tag_of_the_first(
    gate: FakeGate, paired: GateClient, given: str | None
) -> None:
    # The parts are separate requests, so the gate is what holds them to the entry as the first
    # part found it, whatever the caller's own condition was.
    conditions = in_parts(gate, TAG, TAG)

    listed = paired.fields(UUID, if_match=given)

    assert conditions == [given, TAG]
    assert [(field.name, field.etag) for field in listed] == [("Title", TAG), ("PIN", TAG)]


def test_an_entry_that_changes_while_its_fields_are_listed_is_a_condition_that_did_not_hold(
    gate: FakeGate, paired: GateClient
) -> None:
    # With no `if_match` given: the names of several moments would read as a listing of the
    # entry, with a name missing or listed twice, so the refusal is what comes back.
    conditions = in_parts(gate, TAG, NEXT, changed=True)

    with pytest.raises(PreconditionFailed):
        paired.fields(UUID)

    assert conditions == [None, TAG]
    assert len(gate.requests) == 2, "and the listing is not begun again"


def test_a_field_listing_the_gate_gave_no_tag_for_is_asked_for_part_by_part_with_no_condition(
    gate: FakeGate, paired: GateClient
) -> None:
    # A client holding `list` without `read`: nothing holds its parts to a moment.
    conditions = in_parts(gate, None, None)

    listed = paired.fields(UUID)

    assert conditions == [None, None]
    assert [field.name for field in listed] == ["Title", "PIN"]
    assert not any(hasattr(field, "etag") for field in listed)


def test_a_later_part_answered_with_another_tag_than_it_was_asked_under_is_refused(
    gate: FakeGate, paired: GateClient
) -> None:
    # The gate serves a part under `If-Match` only while the entry holds that tag, so a part
    # served with another tag, or with none, is an answer outside the contract.
    in_parts(gate, TAG, NEXT)

    with pytest.raises(ProtocolError, match="another tag"):
        paired.fields(UUID)

    in_parts(gate, TAG, None)

    with pytest.raises(ProtocolError, match="another tag"):
        paired.fields(UUID)


def test_a_read_that_withholds_a_value_is_tagged_all_the_same(
    gate: FakeGate, paired: GateClient
) -> None:
    # A client holding `read` without `protected`: the value comes back withheld, and the tag
    # is of what this client may read, handed out as to any reader.
    withheld = dict(ENTRY, fields={"Title": "an invented service", "Password": None})
    withheld["protected"] = ["Password"]
    tagging(gate, withheld, TAG)

    entry = paired.entry(UUID)

    assert entry.etag == TAG


def test_a_listed_node_has_no_tag_and_says_where_to_get_one(
    gate: FakeGate, paired: GateClient
) -> None:
    # A listing answers about a collection, and the gate tags none.
    tagging(gate, {"entries": [{"uuid": UUID, "group": "9f86" * 8, "title": "x", "url": ""}]}, None)

    (listed,) = paired.entries()

    with pytest.raises(AttributeError, match=r"GateClient\.entry"):
        _ = listed.etag


def test_two_reads_of_one_node_are_equal_whatever_tag_each_came_with() -> None:
    # A tag says which version a write may be made against, not what the node holds, and a read
    # without one is what a client the gate hands none to gets.
    assert Group.read(GROUP, TAG) == Group.read(GROUP, None)
    assert Entry.read(ENTRY, TAG) == Entry.read(ENTRY, NEXT)
    assert hash(Group.read(GROUP, TAG)) == hash(Group.read(GROUP, NEXT))


@pytest.mark.parametrize(
    ("write", "returns"),
    [
        (lambda client, tag: client.update_group(UUID, name="x", if_match=tag), True),
        (lambda client, tag: client.delete_group(UUID, if_match=tag), False),
        (lambda client, tag: client.update_entry(UUID, title="x", if_match=tag), True),
        (lambda client, tag: client.delete_entry(UUID, if_match=tag), False),
        (lambda client, tag: client.create_field(UUID, "Account", "x", if_match=tag), True),
        (lambda client, tag: client.update_field(UUID, "Account", "x", if_match=tag), True),
        (lambda client, tag: client.strip_field(UUID, "Account", if_match=tag), True),
        (lambda client, tag: client.create_attachment(UUID, "a.txt", b"x", if_match=tag), True),
        (lambda client, tag: client.update_attachment(UUID, "a.txt", b"x", if_match=tag), True),
        (lambda client, tag: client.strip_attachment(UUID, "a.txt", if_match=tag), True),
    ],
    ids=[
        "update_group", "delete_group", "update_entry", "delete_entry", "create_field",
        "update_field", "strip_field", "create_attachment", "update_attachment",
        "strip_attachment",
    ],
)
def test_a_write_sends_its_condition_as_given_and_hands_back_the_tag_it_left(
    gate: FakeGate,
    paired: GateClient,
    write: Callable[[GateClient, str | None], str | None],
    returns: bool,
) -> None:
    # A group or an entry removed leaves nothing to tag; a field or an attachment taken off
    # leaves the entry, whose tag comes back.
    conditions = tagging(gate, {"ok": True}, NEXT if returns else None)

    left = write(paired, TAG)
    write(paired, None)

    assert conditions == [TAG, None], "exactly as given, and nothing when not"
    assert left == (NEXT if returns else None)


def test_a_write_s_tag_is_read_from_its_body_and_never_from_a_header(
    gate: FakeGate, paired: GateClient
) -> None:
    # The plugin puts it in the body, since an `ETag` on the answer to a `PUT` would say the body
    # sent was stored as sent. A header here is something else's, and is not taken for it.
    def answer(path: str, sent: str, client: bytes | None) -> tuple[int, str]:
        gate.tag(TAG)
        return 200, '{"ok":true}'

    gate.handler = answer

    assert paired.update_entry(UUID, title="x") is None


def test_a_write_answered_with_a_tag_that_is_not_text_is_refused(
    gate: FakeGate, paired: GateClient
) -> None:
    gate.handler = lambda path, sent, client: (200, json.dumps({"ok": True, "etag": 7}))

    with pytest.raises(ProtocolError, match="not text"):
        paired.update_entry(UUID, title="x")


def test_a_condition_that_does_not_hold_is_its_own_refusal_and_is_not_asked_again(
    gate: FakeGate, paired: GateClient
) -> None:
    tagging(gate, {"error": "precondition failed"}, None, status=412)

    with pytest.raises(PreconditionFailed) as raised:
        paired.update_entry(UUID, title="x", if_match=TAG)

    assert isinstance(raised.value, RouteRefused), "so a caller catching every refusal still does"
    assert raised.value.status == 412
    assert raised.value.error == "precondition failed"
    assert len(gate.requests) == 1


def test_the_generic_calls_carry_a_condition_too(gate: FakeGate, paired: GateClient) -> None:
    conditions = tagging(gate, GROUP, TAG)

    paired.get(f"/groups/{UUID}", if_match="*")
    assert paired.head(f"/groups/{UUID}", if_match=TAG) == TAG, "a head answers with the tag"
    paired.send("PUT", f"/groups/{UUID}", {"Notes": "x"}, if_match=f"{TAG},\t{NEXT}")

    assert conditions == ["*", TAG, f"{TAG},\t{NEXT}"], "a tab between tags is a list's own space"


def test_a_head_whose_condition_does_not_hold_is_refused_with_no_word(
    gate: FakeGate, paired: GateClient
) -> None:
    # A head carries no body, so the refusal is the status alone.
    tagging(gate, {"error": "precondition failed"}, None, status=412)

    with pytest.raises(PreconditionFailed) as raised:
        paired.head(f"/entries/{UUID}", if_match=TAG)

    assert raised.value.error == ""


@pytest.mark.parametrize("tag", ["", "  ", "\t", '"a"\r\nX-Invented: 1', '"\x00"', '"é"'])
def test_a_condition_that_could_not_be_one_header_line_is_refused_before_anything_is_dialed(
    gate: FakeGate, paired: GateClient, tag: str
) -> None:
    # A tag is whatever came back from somewhere, and a line break in it would be a second
    # header, or the whole head, written by whoever chose the tag. Refused before a connection
    # is made, which the gate would see as a name in its handshake.
    tagging(gate, {"ok": True}, None)

    with pytest.raises(ProtocolError, match="not a tag"):
        paired.update_entry(UUID, title="x", if_match=tag)

    assert gate.names == [], "nothing was dialed"
    assert gate.requests == []


@pytest.mark.parametrize(
    "tag",
    [
        TAG.strip('"'),
        f"W/{TAG}",
        f"W/{TAG}, W/{NEXT}",
        f"{TAG} {NEXT}",
        f"{TAG}x",
        '"a"b"',
        '"a',
        '"a b"',
        ",",
    ],
    ids=[
        "no quotes",
        "weak",
        "weak alone",
        "no comma",
        "after the quote",
        "a quote inside",
        "no closing quote",
        "a space inside",
        "only a comma",
    ],
)
def test_a_condition_the_gate_could_not_match_is_refused_before_anything_is_dialed(
    gate: FakeGate, paired: GateClient, tag: str
) -> None:
    # The gate answers each with the `412` a node that changed gets, so a tag pasted without its
    # quotes was refused as a node that moved on, read again, and sent again the same way.
    tagging(gate, {"ok": True}, None)

    with pytest.raises(ProtocolError, match="in quotes"):
        paired.update_entry(UUID, title="x", if_match=tag)

    assert gate.names == [], "nothing was dialed"


@pytest.mark.parametrize(
    "tag",
    ['"a,b"', f" , {TAG} ,", f"W/{TAG}, {NEXT}", '"!"', " * "],
    ids=["a comma inside", "empty elements", "weak beside strong", "one character", "any"],
)
def test_a_condition_the_gate_can_read_is_sent_as_given(
    gate: FakeGate, paired: GateClient, tag: str
) -> None:
    conditions = tagging(gate, {"ok": True}, None)

    paired.update_entry(UUID, title="x", if_match=tag)

    assert conditions == [tag.strip(" \t")], "sent exactly, as the gate trims what it reads"
