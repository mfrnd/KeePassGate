"""Nothing this client prints of what it read carries a value it read.

A repr is what a log line, a traceback, a debugger and an agent's transcript carry, so every model
object, every response and every exception prints what it is and none of what it holds. Names are
printed, because they say what an object is; the values behind them are not, and neither are an
attachment's bytes or the body of an answer, which is where all of those arrive.
"""

from __future__ import annotations

import base64
import json
from pathlib import Path
from typing import Any

import pytest

from conftest import Authority, FakeGate, Issued
from pykeepassgate import credentials as credentials_module
from pykeepassgate import transport
from pykeepassgate.client import GateClient
from pykeepassgate.errors import (
    GateError,
    GateUnavailable,
    ProtocolError,
    RouteRefused,
    TransportError,
    Withheld,
)

#: A secret nobody would choose, put wherever a value can be and looked for in everything printed.
SENTINEL = "not-a-real-secret-sentinel"

UUID = "7b1c" * 8


@pytest.fixture
def paired(tmp_path: Path, bundle: dict[str, Any], gate: FakeGate) -> GateClient:
    """A client that has been through pairing with this gate."""
    stored = credentials_module.save(tmp_path / "kept", gate.port, bundle)
    return GateClient(stored, connection_timeout=10.0)


def answering(gate: FakeGate, body: dict[str, Any] | str, status: int = 200) -> None:
    """Answer every request with this, whatever it asks for."""
    text = body if isinstance(body, str) else json.dumps(body)
    gate.handler = lambda path, sent, client: (status, text)


def refusal(call: Any) -> GateError:
    """What a call raised, which is printed as everything else here is."""
    with pytest.raises(GateError) as raised:
        call()

    return raised.value


def test_no_object_this_client_reads_or_raises_prints_a_value(
    gate: FakeGate, paired: GateClient, authority: Authority, client_certificate: Issued
) -> None:
    printed: list[object] = []

    # A read entry holding the secret in every string it can, standard and custom, beside a value
    # that is withheld, and the parts of it a caller can ask for on their own.
    answering(
        gate,
        {
            "uuid": UUID,
            "group": UUID,
            "fields": {
                "Title": SENTINEL,
                "UserName": SENTINEL,
                "Password": SENTINEL,
                "URL": SENTINEL,
                "Notes": SENTINEL,
                "Invented": SENTINEL,
                "Deploy key": None,
            },
            "protected": ["Password", "Deploy key"],
            "expires": None,
        },
    )
    entry = paired.entry(UUID)
    printed += [entry, entry.fields, entry.custom()]
    printed.append(refusal(lambda: entry.custom()["Deploy key"]))

    # A listed entry, whose title and URL are values as much as a read entry's are.
    answering(gate, {"entries": [{"uuid": UUID, "group": UUID, "title": SENTINEL, "url": None}]})
    listed = paired.entries()[0]
    printed += [listed, refusal(lambda: listed.url)]

    # A field read on its own route, and one withheld there.
    answering(gate, {"name": "Password", "protected": True, "value": SENTINEL})
    printed.append(paired.field(UUID, "Password"))
    answering(gate, {"name": "Password", "protected": True, "value": None})
    withheld = paired.field(UUID, "Password")
    printed += [withheld, refusal(lambda: withheld.value)]

    # An attachment, whose bytes are the secret, and a group, whose notes are.
    content = b"invented key material: " + SENTINEL.encode()
    answering(
        gate,
        {"name": "key.pem", "size": len(content), "content": base64.b64encode(content).decode()},
    )
    printed.append(paired.attachment(UUID, "key.pem"))
    answering(gate, {"uuid": UUID, "name": "Invented", "parent": None, "notes": SENTINEL})
    printed.append(paired.group(UUID))

    # Every answer arrives as a response first, over a real connection and parsed from bytes.
    answering(gate, {"value": SENTINEL})
    context = transport.paired_context(authority.pem, client_certificate.pem)
    with transport.connect(gate.port, context, "abc.def.kpg", connection_timeout=10.0) as opened:
        printed.append(opened.send("GET", "/entries/x/fields/Password"))
    body = json.dumps({"value": SENTINEL}).encode()
    printed.append(
        transport._parse(b"HTTP/1.1 200 OK\r\nContent-Length: %d\r\n\r\n%s" % (len(body), body))
    )

    # Refusals whose answers carry the secret beside the word: a route refused, a `503` with a
    # word nobody knows, an answer that will not read, and one cut short of its own length.
    answering(gate, {"error": "conflict", "value": SENTINEL}, status=409)
    printed.append(refusal(lambda: paired.field(UUID, "Password")))
    answering(gate, {"error": "teapot", "value": SENTINEL}, status=503)
    printed.append(refusal(lambda: paired.field(UUID, "Password")))
    answering(
        gate,
        {
            "uuid": UUID,
            "group": UUID,
            "fields": {"Password": SENTINEL},
            "protected": [],
            "expires": SENTINEL,
        },
    )
    printed.append(refusal(lambda: paired.entry(UUID)))
    printed.append(refusal(lambda: transport.Response(200, f"[{SENTINEL!r}]").json()))
    printed.append(
        refusal(lambda: transport._parse(b"HTTP/1.1 200 OK\r\nContent-Length: 99\r\n\r\n" + body))
    )

    kinds = {type(each) for each in printed}
    assert {Withheld, RouteRefused, GateUnavailable, ProtocolError, TransportError} <= kinds
    assert transport.Response in kinds, "the responses themselves were printed"

    for each in printed:
        for shown in (repr(each), str(each)):
            assert SENTINEL not in shown, f"a {type(each).__name__} printed a value: {shown}"
