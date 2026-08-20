"""A paired client against a gate, and what every refusal means to whoever called it."""

from __future__ import annotations

import json
from pathlib import Path
from typing import Any

import pytest

from conftest import FakeGate, Issued
from pykeepassgate import credentials as credentials_module
from pykeepassgate import sni
from pykeepassgate.client import GateClient
from pykeepassgate.errors import DatabaseLocked, GateBusy, NotAdmitted, RouteRefused


@pytest.fixture
def paired(tmp_path: Path, bundle: dict[str, Any], gate: FakeGate) -> GateClient:
    """A client that has been through pairing with this gate."""
    stored = credentials_module.save(tmp_path / "kept", gate.port, bundle)
    return GateClient(stored, timeout=10.0)


def test_the_status_route_says_what_the_gate_and_the_database_are(
    gate: FakeGate, paired: GateClient
) -> None:
    gate.handler = lambda path, body, client: (
        200,
        json.dumps({"ok": True, "database": "abcdef01", "version": "0.1.0"}),
    )

    status = paired.status()

    assert (status.ok, status.database, status.version) == (True, "abcdef01", "0.1.0")
    assert gate.requests == [("GET", "/gate/status")]


def test_every_request_is_named_from_the_gate_key(gate: FakeGate, paired: GateClient) -> None:
    gate.handler = lambda path, body, client: (200, "{}")

    paired.get("/gate/status")
    paired.get("/gate/status")

    assert len(gate.names) == 2
    assert gate.names[0] != gate.names[1], "a passive observer would see the same name twice"

    for name in gate.names:
        assert name is not None
        nonce = bytes.fromhex(name.split(".")[0])
        assert name == sni.name_for(bytes(range(32)), nonce)


def test_the_client_certificate_is_offered_on_every_request(
    gate: FakeGate, paired: GateClient
) -> None:
    seen: list[bytes | None] = []

    def answer(path: str, body: str, client: bytes | None) -> tuple[int, str]:
        seen.append(client)
        return 200, "{}"

    gate.handler = answer
    paired.get("/gate/status")

    assert seen and seen[0], "the gate would have refused this as unpaired"


def test_a_body_is_sent_when_there_is_one(gate: FakeGate, paired: GateClient) -> None:
    seen: list[str] = []

    def answer(path: str, body: str, client: bytes | None) -> tuple[int, str]:
        seen.append(body)
        return 200, "{}"

    gate.handler = answer
    paired.send("POST", "/somewhere", {"asked": "for"})

    assert json.loads(seen[0]) == {"asked": "for"}


def test_an_empty_answer_is_an_empty_object_rather_than_a_failure(
    gate: FakeGate, paired: GateClient
) -> None:
    gate.handler = lambda path, body, client: (200, "")
    assert paired.get("/gate/status") == {}


@pytest.mark.parametrize(
    ("status", "error", "expected"),
    [
        (503, "locked", DatabaseLocked),
        (503, "busy", GateBusy),
        (401, "unpaired", NotAdmitted),
        (404, "no such route", RouteRefused),
        (405, "method not allowed", RouteRefused),
        (400, "malformed", RouteRefused),
        (409, "conflict", RouteRefused),
        (413, "this attachment is 600000 bytes", RouteRefused),
        (500, "internal error", RouteRefused),
    ],
)
def test_every_refusal_becomes_something_a_caller_can_act_on(
    gate: FakeGate,
    paired: GateClient,
    status: int,
    error: str,
    expected: type[Exception],
) -> None:
    # The four at the end are what the client's documentation tells a caller not to wait out:
    # `400` and `409` are about the request, `413` about what was asked for, and `500` is the
    # gate failing. A mapping that sent one of them to `GateBusy` would tell a caller to retry.
    gate.handler = lambda path, body, client: (status, json.dumps({"error": error}))

    with pytest.raises(expected) as raised:
        paired.status()

    if isinstance(raised.value, RouteRefused):
        assert (raised.value.status, raised.value.error) == (status, error)


def test_nothing_is_asked_of_the_identity_a_locked_database_answers_with(
    gate: FakeGate, paired: GateClient, responder: Issued
) -> None:
    # `CA.md` 208 is what the marker is for. A responder chains to the same authority, so the
    # pin admits it and an impostor holding its key could answer a route with a fabricated
    # password that an agent would then act on. The refusal comes before the request, so this
    # costs the caller a `DatabaseLocked` rather than a secret somebody made up.
    gate.serve_with(responder)
    gate.handler = lambda path, body, client: (
        200,
        json.dumps({"uuid": "abc", "title": "invented", "password": "fabricated"}),
    )

    with pytest.raises(DatabaseLocked, match="responder identity"):
        paired.entry("abc")

    assert gate.requests == [], "nothing reached the gate at all"


def test_a_revoked_client_and_one_that_never_paired_look_the_same(
    gate: FakeGate, paired: GateClient
) -> None:
    # The record is read from the live database on every connection and never cached, so revocation
    # is immediate and, from here, indistinguishable from never having been admitted. That is the
    # point rather than an omission.
    gate.handler = lambda path, body, client: (401, json.dumps({"error": "unpaired"}))

    with pytest.raises(NotAdmitted, match="no admission record"):
        paired.status()


def test_a_refusal_carries_the_status_and_the_word_the_gate_used(
    gate: FakeGate, paired: GateClient
) -> None:
    gate.handler = lambda path, body, client: (418, json.dumps({"error": "teapot"}))

    with pytest.raises(RouteRefused) as raised:
        paired.status()

    assert (raised.value.status, raised.value.error) == (418, "teapot")


def test_a_client_is_opened_from_where_pairing_put_it(
    tmp_path: Path, bundle: dict[str, Any], gate: FakeGate
) -> None:
    credentials_module.save(tmp_path / "kept", gate.port, bundle)
    client = GateClient.open(tmp_path / "kept")

    assert client.credentials.subject == "CN=agent-invented"
    assert client.credentials.port == gate.port


def test_a_client_is_one_identity_for_as_long_as_it_lives(
    tmp_path: Path, bundle: dict[str, Any], gate: FakeGate
) -> None:
    # The TLS context is built once from the anchor and the certificate. `gate_key` used to be
    # read from its file on every request, so a re-pair underneath a live client left it naming
    # connections from the new key while still presenting the old certificate.
    stored = credentials_module.save(tmp_path / "kept", gate.port, bundle)
    paired_with = stored.gate_key
    client = GateClient(stored, timeout=10.0)
    gate.handler = lambda path, body, certificate: (200, json.dumps({"ok": True}))

    client.status()

    repaired = bytes(range(32, 64))
    (stored.directory / credentials_module.GATE_KEY_FILE).write_bytes(repaired)
    client.status()

    name = gate.names[-1]
    assert name is not None
    nonce = bytes.fromhex(name.split(".")[0])
    assert name == sni.name_for(paired_with, nonce), "still the identity it was built with"
    assert name != sni.name_for(repaired, nonce)


def test_opening_a_client_can_say_how_long_to_wait(
    tmp_path: Path, bundle: dict[str, Any], gate: FakeGate
) -> None:
    # A setting nothing can reach is a setting nobody has: `open` is how a caller is meant to
    # build one, and it used to take the default and offer no way past it.
    credentials_module.save(tmp_path / "kept", gate.port, bundle)

    assert GateClient.open(tmp_path / "kept", timeout=3.5)._timeout == 3.5


def test_a_head_asks_whether_a_route_answers_without_taking_the_answer(
    paired: GateClient, gate: FakeGate
) -> None:
    # The gate omits `Content-Length` on a HEAD rather than sending a wrong one, so this is also
    # the one request this client reads to the close. Reaching the next line is the answer.
    gate.handler = lambda path, body, certificate: (200, json.dumps({"ok": True}))

    paired.head("/gate/status")

    assert gate.requests == [("HEAD", "/gate/status")]


def test_a_refused_head_carries_its_status_and_no_word(paired: GateClient, gate: FakeGate) -> None:
    # The gate writes its word into a body and an answer to a HEAD has none, so what arrives is
    # the status alone. The handler names a word to show that it goes nowhere.
    gate.handler = lambda path, body, certificate: (404, '{"error":"no such route"}')

    with pytest.raises(RouteRefused) as refused:
        paired.head("/gate/status")

    assert refused.value.status == 404
    assert refused.value.error == ""
    assert str(refused.value) == "the gate answered 404"


def test_a_head_this_client_may_not_reach_is_refused_like_any_other(
    paired: GateClient, gate: FakeGate
) -> None:
    gate.handler = lambda path, body, certificate: (401, '{"error":"unpaired"}')

    with pytest.raises(NotAdmitted):
        paired.head("/gate/status")
