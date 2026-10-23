"""The tag a read hands back, and the condition a write can be made under.

Against the stand-in gate, which knows no node and holds no tag: each test says what the gate
answers, as the plugin would, and asserts what the client sends and makes of it. What a tag is,
when one matches and which client is handed one is the plugin's, and its own tiers hold it to that.
"""

from __future__ import annotations

import json
import pickle
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


def test_a_read_the_gate_gave_no_tag_for_has_none_and_says_why(
    gate: FakeGate, paired: GateClient
) -> None:
    # What a client meets whose rule does not read everything the tag covers.
    tagging(gate, ENTRY, None)

    entry = paired.entry(UUID)

    assert not hasattr(entry, "etag")
    with pytest.raises(AttributeError, match=r"gave no tag.*whose rule reads everything"):
        _ = entry.etag


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
    assert Group.of(GROUP, TAG) == Group.of(GROUP, None)
    assert Entry.read(ENTRY, TAG) == Entry.read(ENTRY, NEXT)
    assert hash(Group.of(GROUP, TAG)) == hash(Group.of(GROUP, NEXT))


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


def test_a_refusal_survives_being_pickled_as_what_it_was() -> None:
    # What a process pool does with an exception raised in a worker.
    for refusal in (PreconditionFailed("precondition failed"), RouteRefused(404, "no such route")):
        copied = pickle.loads(pickle.dumps(refusal))  # noqa: S301, its own bytes

        assert type(copied) is type(refusal)
        assert str(copied) == str(refusal)
        assert (copied.status, copied.error) == (refusal.status, refusal.error)


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
