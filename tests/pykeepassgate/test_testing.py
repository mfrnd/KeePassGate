"""The stand-in gate the package ships, held to what its documentation promises a program.

Every other test here uses it; these are about it: that it needs no test framework, that a gate
nobody scripted answers as one with no routes, that a block ends it, and that the arrangement the
documentation shows really is enough to point a client at it.
"""

from __future__ import annotations

import datetime as dt
import json
import socket
import ssl
import subprocess
import sys
from pathlib import Path

import pytest
from cryptography.x509.oid import ExtendedKeyUsageOID

from pykeepassgate import api
from pykeepassgate.errors import GateBusy, RouteRefused
from pykeepassgate.testing import Authority, FakeGate, held


def test_it_imports_no_test_framework() -> None:
    # A program tests with whatever it tests with, so the module may need nothing of pytest's.
    # A fresh interpreter, because this one has pytest loaded whatever the module imports.
    ended = subprocess.run(
        [
            sys.executable,
            "-c",
            "import sys, pykeepassgate.testing; print('pytest' in sys.modules)",
        ],
        capture_output=True,
        text=True,
        check=False,
    )

    assert ended.returncode == 0, ended.stderr
    assert ended.stdout.strip() == "False"


def test_what_the_documentation_shows_is_enough_to_point_a_client_at_it(tmp_path: Path) -> None:
    # The example in `docs/pykeepassgate.md`, run: an authority, a host and a client under it,
    # a gate key, and credentials naming the gate's port.
    authority = Authority(tmp_path)
    host = authority.issue("host", ExtendedKeyUsageOID.SERVER_AUTH, "host.pem")
    authority.issue("agent-invented", ExtendedKeyUsageOID.CLIENT_AUTH, "identity.pem")
    (tmp_path / "gate.key").write_bytes(bytes(32))

    with FakeGate(host, authority.pem) as gate:
        gate.handler = lambda path, body, client: (503, '{"error":"busy"}')
        credentials = api.Credentials(tmp_path, gate.port, "CN=agent-invented", "", (), "")
        client = api.GateClient(credentials, connection_timeout=10.0)

        with pytest.raises(GateBusy):
            client.status()

    assert gate.requests == [("GET", "/gate/status")]


def test_a_held_approval_is_answered_as_the_plugin_answers_it() -> None:
    # The body `PairingHandler.HeldBody` writes, for a handler to hand to `pair` or `renew`.
    status, body = held(dt.datetime(2026, 10, 14, 11, 6, tzinfo=dt.timezone(dt.timedelta(hours=2))))

    assert status == 503
    assert json.loads(body) == {"error": "busy", "heldUntil": "2026-10-14T09:06:00Z"}


def test_a_gate_nobody_scripted_answers_that_it_has_no_such_route(
    gate: FakeGate, tmp_path: Path, bundle: dict[str, object]
) -> None:
    stored = api.save(tmp_path / "kept", gate.port, dict(bundle))

    with pytest.raises(RouteRefused) as refused:
        api.GateClient(stored, connection_timeout=10.0).status()

    assert (refused.value.status, refused.value.error) == (404, "no such route")


def test_a_gate_used_as_a_block_stops_listening_when_the_block_ends(
    tmp_path: Path,
) -> None:
    authority = Authority(tmp_path)
    host = authority.issue("host", ExtendedKeyUsageOID.SERVER_AUTH, "host.pem")

    with FakeGate(host, authority.pem) as gate:
        port = gate.port

    # Refused or timed out, as the platform has it: either way nothing is listening there.
    with pytest.raises(OSError):
        socket.create_connection(("127.0.0.1", port), timeout=5).close()


def test_a_connection_that_named_nothing_is_said_to_have_named_nothing(gate: FakeGate) -> None:
    with pytest.raises(AssertionError, match="carried no name"):
        gate.named()


def answered(gate: FakeGate, authority: Authority) -> bytes:
    """The whole answer to one request, as the bytes the stand-in wrote, head and all."""
    # Not `create_default_context`, whose strict checks want extensions the stand-in's authority
    # leaves out, as the plugin's does: the client's own context is built the same way.
    context = ssl.SSLContext(ssl.PROTOCOL_TLS_CLIENT)
    context.load_verify_locations(cafile=str(authority.pem))
    context.check_hostname = False
    with socket.create_connection(("127.0.0.1", gate.port), timeout=10) as raw, context.wrap_socket(
        raw, server_hostname="abc.def.kpg"
    ) as wrapped:
        wrapped.sendall(b"GET /gate/status HTTP/1.1\r\nHost: abc.def.kpg\r\n\r\n")
        received = b""
        while chunk := wrapped.recv(4096):
            received += chunk

    return received


def test_an_answer_carries_the_head_the_plugin_writes(
    gate: FakeGate, authority: Authority
) -> None:
    # `HttpResponse.Write`: the reason phrase, the charset, and the two headers that keep an
    # answer out of a cache and out of a browser's guessing.
    gate.handler = lambda path, body, client: (404, '{"error":"no such route"}')

    head = answered(gate, authority).split(b"\r\n\r\n")[0].decode("ascii").split("\r\n")

    assert head[0] == "HTTP/1.1 404 Not Found"
    assert "Content-Type: application/json; charset=utf-8" in head
    assert "Cache-Control: no-store" in head
    assert "X-Content-Type-Options: nosniff" in head
    assert not any(line.startswith("Retry-After") for line in head), "only a 503 carries it"


@pytest.mark.parametrize(("given", "sent"), [("2", "Retry-After: 2"), (None, None)])
def test_a_503_carries_the_retry_after_the_plugin_sends_unless_told_otherwise(
    gate: FakeGate, authority: Authority, given: str | None, sent: str | None
) -> None:
    # The plugin sends `Retry-After: 2` on every `503`, so a program reading it is tested against
    # what it will meet; `None` is a `503` from something older, or a proxy, that sends none.
    gate.handler = lambda path, body, client: (503, '{"error":"busy"}')
    if given is None:
        gate.retry_after = None

    head = answered(gate, authority).split(b"\r\n\r\n")[0].decode("ascii").split("\r\n")

    assert head[0] == "HTTP/1.1 503 Service Unavailable"
    assert [line for line in head if line.startswith("Retry-After")] == ([sent] if sent else [])


def test_a_handler_can_ask_which_method_it_is_answering(
    gate: FakeGate, tmp_path: Path, bundle: dict[str, object]
) -> None:
    # One path, two methods, answered apart without counting calls.
    uuid = "A1000000000000000000000000000001"
    asked: list[str] = []

    def entry(path: str, body: str, client: bytes | None) -> tuple[int, str]:
        asked.append(gate.method())
        if gate.method() == "DELETE":
            return 404, '{"error":"no such route"}'
        return 200, json.dumps(
            {"uuid": uuid, "group": "C1" * 16, "fields": {}, "protected": [], "expires": None}
        )

    gate.handler = entry
    client = api.GateClient(
        api.save(tmp_path / "kept", gate.port, dict(bundle)), connection_timeout=10.0
    )

    assert client.entry(uuid).uuid == uuid
    with pytest.raises(RouteRefused):
        client.delete_entry(uuid)

    assert asked == ["GET", "DELETE"]


def test_the_method_is_asked_only_while_a_request_is_answered(gate: FakeGate) -> None:
    with pytest.raises(AssertionError, match="no request is being answered"):
        gate.method()


def test_a_peer_that_goes_quiet_is_let_go(gate: FakeGate) -> None:
    # Bounded, so a quiet peer ends the thread answering it rather than parking it for good: the
    # gate closes its side, which the peer reads as the end of the stream.
    gate.quiet = 0.5

    with socket.create_connection(("127.0.0.1", gate.port), timeout=10) as raw:
        try:
            ended = raw.recv(1)
        except ConnectionResetError:
            ended = b""

    assert ended == b""


def test_a_client_offering_only_tls_12_is_refused(gate: FakeGate, authority: Authority) -> None:
    # A TLS 1.3 server, as the plugin's transport is, and not one that also speaks something
    # older to a client that asks for it: the client itself never offers less, so only a client
    # that does can show it.
    context = ssl.SSLContext(ssl.PROTOCOL_TLS_CLIENT)
    context.load_verify_locations(cafile=str(authority.pem))
    context.check_hostname = False
    context.maximum_version = ssl.TLSVersion.TLSv1_2

    with socket.create_connection(("127.0.0.1", gate.port), timeout=10) as raw, pytest.raises(
        ssl.SSLError
    ):
        context.wrap_socket(raw, server_hostname="abc.def.kpg")
