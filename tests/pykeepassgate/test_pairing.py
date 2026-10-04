"""The exchange, driven against a gate that answers the way the plugin answers.

What these prove is the half that vectors cannot: that the client speaks first, that it checks the
answer before it commits to anything, that the request carries the hello's binding and nonce rather
than its own, and that each refusal becomes the exception a caller can act on.

The gate here computes with this package's own `proofs`, so a construction that was wrong in both
halves would still pass. `test_protocol.py` is what closes that, by pinning every construction
against output the plugin produced.
"""

from __future__ import annotations

import base64
import datetime as dt
import json
import math
import os
import secrets
from datetime import timedelta
from pathlib import Path
from typing import Any

import pytest
from cryptography.x509.oid import ExtendedKeyUsageOID

from conftest import Authority, FakeGate, Issued
from pykeepassgate import api, proofs, sni, transport
from pykeepassgate import pairing as pairing_module
from pykeepassgate import token as token_module
from pykeepassgate.errors import (
    AuthorityUndecided,
    DatabaseLocked,
    GateBusy,
    GateUnavailable,
    MalformedToken,
    PairingError,
    PairingRefused,
    PairingUnanswered,
    ProofRefused,
    ProtocolError,
    RouteRefused,
    StorageError,
)
from pykeepassgate.pairing import Pairing, lifetime_text
from test_api_routes import resolved
from test_reference import operations, routing_table

TOKEN = bytes(range(32))
INVITATION = token_module.encode(TOKEN)


class Gate:
    """The plugin's half of pairing, near enough to drive a client through it."""

    def __init__(
        self,
        served: FakeGate,
        host: Issued,
        token: bytes = TOKEN,
        bundle: dict[str, Any] | None = None,
    ) -> None:
        self.served = served
        self.host = host
        self.bundle = bundle
        self.token = token
        self.binding = proofs.binding(host.der)
        self.challenge: bytes | None = None
        self.nonce: bytes | None = None
        self.asked: dict[str, Any] | None = None

        #: What the operator decides. Set to a status to refuse instead of approving.
        self.refuse_with: tuple[int, str] | None = None

        #: Refusals to hand out before the real answer, one per attempt, so a test can be a gate
        #: that is busy for a while and then is not. Kept per route, because which of the two may
        #: be asked again is the whole question a retry budget raises here.
        self.hello_refusals: list[tuple[int, str | dict[str, str]]] = []
        self.request_refusals: list[tuple[int, str | dict[str, str]]] = []

        #: Swapped for something else to play an impostor that cannot prove the token.
        self.server_proof: bytes | None = None

    def __call__(self, path: str, body: str, client: bytes | None) -> tuple[int, str]:
        parsed = json.loads(body)

        if path == "/pair/hello":
            return self._hello(parsed)

        if path == "/pair/request":
            return self._request(parsed)

        return 404, json.dumps({"error": "no such route"})

    def _hello(self, body: dict[str, Any]) -> tuple[int, str]:
        if self.hello_refusals:
            status, error = self.hello_refusals.pop(0)
            return status, json.dumps(error if isinstance(error, dict) else {"error": error})

        offered = base64.b64decode(body["proof"])

        # The client speaks first, so this is the first thing that can refuse, and it refuses
        # before anything is spent and before an operator is disturbed.
        nonce = self._nonce_from_name()
        if not proofs.matches(proofs.client(self.token, self.binding, nonce), offered):
            return 401, json.dumps({"error": "proof"})

        self.challenge = secrets.token_bytes(32)
        self.nonce = nonce
        proof = self.server_proof or proofs.server(
            self.token, self.binding, nonce, self.challenge
        )

        return 200, json.dumps(
            {
                "challenge": base64.b64encode(self.challenge).decode(),
                "proof": base64.b64encode(proof).decode(),
                "expires": "2026-08-15T09:02:00Z",
            }
        )

    def _request(self, body: dict[str, Any]) -> tuple[int, str]:
        if self.challenge is None or self.nonce is None:
            return 409, json.dumps({"error": "no exchange"})

        expected = proofs.request(
            self.token,
            self.binding,
            self.nonce,
            self.challenge,
            proofs.asked(body["name"], body["profiles"], body.get("lifetime")),
        )

        if not proofs.matches(expected, base64.b64decode(body["proof"])):
            return 401, json.dumps({"error": "proof"})

        self.asked = body

        # After the proof, which is where `PairingHandler.Approve` puts every `503` it sends: the
        # challenge is spent by then and the invitation is not.
        if self.request_refusals:
            status, error = self.request_refusals.pop(0)
            return status, json.dumps(error if isinstance(error, dict) else {"error": error})

        if self.refuse_with:
            status, error = self.refuse_with
            return status, json.dumps({"error": error})

        if self.bundle is not None:
            return 200, json.dumps(self.bundle)

        return 200, json.dumps(
            {
                # Not a PKCS#12 and never parsed: these tests stop at the exchange, and the
                # ones that go on to store what came back are handed a real bundle.
                "identity": base64.b64encode(b"not a real pkcs12 and never read as one").decode(),
                "authority": base64.b64encode(self.host.der).decode(),
                "gateKey": base64.b64encode(bytes(32)).decode(),
                "subject": "CN=agent-invented",
                "database": "abcdef01",
                "expires": "2027-08-15T09:00:00Z",
                # What the operator approved, which here is one fewer than was asked for.
                "profiles": ["reader"],
            }
        )

    def _nonce_from_name(self) -> bytes:
        # The gate resolves the invitation from the name the connection arrived on, so the nonce
        # the proof is over is the one in that name rather than anything in the body.
        return bytes.fromhex(self.served.named().split(".")[0])


@pytest.fixture
def pairing_gate(gate: FakeGate, host: Issued) -> Gate:
    """A gate that speaks pairing, and a handler wired to it."""
    handler = Gate(gate, host)
    gate.handler = handler
    return handler


def test_a_hello_proves_the_token_and_checks_the_answer(
    gate: FakeGate, pairing_gate: Gate
) -> None:
    exchange = Pairing(gate.port, INVITATION, connection_timeout=10.0).hello()

    assert exchange.challenge == pairing_gate.challenge
    assert exchange.binding == pairing_gate.binding
    assert exchange.nonce == pairing_gate.nonce


def test_the_name_the_hello_arrives_on_is_built_from_the_token(
    gate: FakeGate, pairing_gate: Gate
) -> None:
    Pairing(gate.port, INVITATION, connection_timeout=10.0).hello()

    name = gate.names[-1]
    assert name is not None
    nonce = bytes.fromhex(name.split(".")[0])
    assert name == sni.name_for(TOKEN, nonce), "the gate would not have resolved this"


def test_a_hello_to_a_locked_database_spends_nothing(
    gate: FakeGate, pairing_gate: Gate, responder: Issued
) -> None:
    # Pairing needs the authority key and an admission write, and a locked database has
    # neither, so it refuses this as it refuses everything else. Reading that off the
    # certificate keeps the proof over the token off the wire, and the invitation whole.
    gate.serve_with(responder)

    with pytest.raises(DatabaseLocked, match="responder identity"):
        Pairing(gate.port, INVITATION, connection_timeout=10.0).hello()

    assert gate.requests == [], "nothing reached the gate at all"


def test_a_database_locked_between_hello_and_request_is_a_lock_rather_than_an_impostor(
    gate: FakeGate, pairing_gate: Gate, responder: Issued
) -> None:
    # KeePass locks after the hello, and the request's connection is served with the responder,
    # which is not the certificate the hello proved. Reported as `ProofRefused` that would be
    # "stop, this is not the right KeePass"; what it is, is a lock to wait out.
    pairing = Pairing(gate.port, INVITATION, 10.0)
    exchange = pairing.hello()
    gate.serve_with(responder)

    with pytest.raises(DatabaseLocked, match="responder identity"):
        pairing.request(exchange, "agent-invented", ["reader"])

    assert [path for _, path in gate.requests] == ["/pair/hello"], "the request was never sent"


def test_a_wrong_token_is_refused_at_the_first_route(gate: FakeGate, pairing_gate: Gate) -> None:
    # Nothing is spent and no dialog is raised: the client speaks first precisely so that a
    # captured name replayed at the gate dies here.
    pairing_gate.token = bytes(32)

    with pytest.raises(ProofRefused, match="not the token"):
        Pairing(gate.port, INVITATION, connection_timeout=10.0).hello()


def test_an_answer_that_cannot_prove_the_token_stops_the_exchange(
    gate: FakeGate, pairing_gate: Gate
) -> None:
    # An unpaired client has no anchor, so this is the only thing standing between it and whatever
    # accepted the connection.
    pairing_gate.server_proof = bytes(32)

    with pytest.raises(ProofRefused, match="not the right KeePass"):
        Pairing(gate.port, INVITATION, connection_timeout=10.0).hello()


def test_a_request_follows_its_own_hello_and_comes_back_with_a_bundle(
    gate: FakeGate, pairing_gate: Gate
) -> None:
    pairing = Pairing(gate.port, INVITATION, connection_timeout=10.0)
    exchange = pairing.hello()
    bundle = pairing.request(exchange, "agent-invented", ["reader", "writer"])

    assert pairing_gate.asked == {
        "name": "agent-invented",
        "profiles": ["reader", "writer"],
        "proof": pairing_gate.asked["proof"] if pairing_gate.asked else "",
    }

    assert bundle["subject"] == "CN=agent-invented"
    assert bundle["profiles"] == ["reader"], "what was approved, not what was asked for"


def test_a_lifetime_asked_for_is_sent_and_signed_for(gate: FakeGate, pairing_gate: Gate) -> None:
    # The gate checks the proof over the lifetime the body carries, so this arriving at all is
    # the two halves agreeing on what was signed; `test_protocol.py` pins the bytes.
    pairing = Pairing(gate.port, INVITATION, connection_timeout=10.0)
    exchange = pairing.hello()
    pairing.request(exchange, "agent-invented", ["reader"], timedelta(days=30))

    assert pairing_gate.asked is not None
    assert pairing_gate.asked["lifetime"] == "P30D"


@pytest.mark.parametrize(
    "lifetime",
    [
        timedelta(0),
        timedelta(days=-1),
        timedelta(days=366),
        timedelta(hours=36),
        timedelta(days=30, seconds=1),
        timedelta(days=1, microseconds=-1),
        30,
    ],
)
def test_a_lifetime_the_gate_would_refuse_never_reaches_it(
    gate: FakeGate, pairing_gate: Gate, lifetime: Any
) -> None:
    # The gate refuses it as `400 malformed` before it takes the challenge, as it refuses a name,
    # so this saves the round trip and says which rule was broken. A part of a day is refused
    # rather than rounded, because either way it rounds is a certificate nobody asked for.
    pairing = Pairing(gate.port, INVITATION, connection_timeout=10.0)
    exchange = pairing.hello()
    before = len(gate.requests)

    with pytest.raises(ValueError, match="whole number of days from 1 to 365"):
        pairing.request(exchange, "agent-invented", ["reader"], lifetime)

    assert len(gate.requests) == before, "nothing was asked of the gate at all"


@pytest.mark.parametrize(("days", "written"), [(1, "P1D"), (7, "P7D"), (365, "P365D")])
def test_every_lifetime_the_gate_allows_is_written_as_it_reads_one(days: int, written: str) -> None:
    assert lifetime_text(timedelta(days=days)) == written


def test_the_request_goes_out_on_a_new_connection_with_a_new_name(
    gate: FakeGate, pairing_gate: Gate
) -> None:
    # The gate serves one request per connection, so the exchange spans two, and the challenge is
    # what carries it across.
    pairing = Pairing(gate.port, INVITATION, connection_timeout=10.0)
    pairing.request(pairing.hello(), "agent-invented", [])

    assert len(gate.names) == 2
    assert gate.names[0] != gate.names[1]


def test_both_halves_of_the_exchange_go_to_the_host_they_were_given(
    gate: FakeGate, pairing_gate: Gate
) -> None:
    # A tunnel is what puts a gate anywhere but loopback, and it puts both connections of one
    # exchange in the same place: the challenge only carries across if the second connection
    # reaches the same KeePass as the first. `localhost` is the same socket by another name, so
    # what this proves is that the name travels rather than being dropped for the default.
    pairing = Pairing(gate.port, INVITATION, connection_timeout=10.0, host="localhost")

    bundle = pairing.request(pairing.hello(), "agent-invented", [])

    assert len(gate.names) == 2, "both halves arrived"
    assert bundle["subject"] == "CN=agent-invented"


def test_a_request_with_no_hello_behind_it_cannot_be_built(gate: FakeGate) -> None:
    # Not a protocol refusal but a programming one: there is nothing to carry across.
    pairing = Pairing(gate.port, INVITATION, connection_timeout=10.0)
    with pytest.raises(AttributeError):
        pairing.request(None, "agent-invented", [])  # type: ignore[arg-type]


@pytest.mark.parametrize(
    ("status", "error", "expected", "why"),
    [
        (503, "locked", DatabaseLocked, "wait for the operator to unlock it"),
        (503, "busy", GateBusy, "a group dialog is open, so retry"),
        (503, "undecided", AuthorityUndecided, "wait for the operator to keep an authority"),
        (503, "teapot", GateUnavailable, "a word nobody knows, which nothing waits out"),
        (504, "unanswered", PairingUnanswered, "nobody answered, and nothing is spent"),
        (403, "refused", PairingRefused, "the operator said no, and it is spent"),
        (409, "no invitation", PairingError, "it expired or somebody else spent it"),
        (409, "no exchange", PairingError, "a fresh hello, and the invitation is still good"),
        (409, "unheard of", RouteRefused, "a word this does not know is passed on as it came"),
        (500, "not recorded", RouteRefused, "the record did not land, so nothing was handed over"),
    ],
)
def test_every_refusal_becomes_the_thing_a_caller_has_to_do_about_it(
    gate: FakeGate,
    pairing_gate: Gate,
    status: int,
    error: str,
    expected: type[Exception],
    why: str,
) -> None:
    pairing = Pairing(gate.port, INVITATION, connection_timeout=10.0)
    exchange = pairing.hello()
    pairing_gate.refuse_with = (status, error)

    with pytest.raises(expected) as refused:
        pairing.request(exchange, "agent-invented", []), why

    assert type(refused.value) is expected, "the class that names the word, and no other"


def test_a_busy_gate_is_described_by_what_it_asks_about(
    gate: FakeGate, pairing_gate: Gate
) -> None:
    # The gate's check before the dialog looks at no group dialog, so a message guessing at one
    # sends whoever reads it looking for a window that cannot be the cause.
    pairing = Pairing(gate.port, INVITATION, connection_timeout=10.0)
    exchange = pairing.hello()
    pairing_gate.refuse_with = (503, "busy")

    with pytest.raises(GateBusy) as refused:
        pairing.request(exchange, "agent-invented", [])

    said = str(refused.value)
    assert "Database Maintenance" in said
    assert "saving the database would stop to ask" in said
    assert "another pairing or renewal" in said
    assert "group dialog" not in said
    assert "invitation is still good" in said


@pytest.mark.parametrize(
    ("error", "says", "never"),
    [("no exchange", "not spent", "gone"), ("no invitation", "gone", "not spent")],
)
def test_the_two_conflicts_say_opposite_things_about_the_invitation(
    gate: FakeGate, pairing_gate: Gate, error: str, says: str, never: str
) -> None:
    # `no exchange` spends the challenge and nothing else, so the same invitation starts again
    # from a hello; `no invitation` leaves nothing to start with. One sentence for both sent a
    # caller holding a good invitation to the operator for another.
    pairing = Pairing(gate.port, INVITATION, 10.0)
    exchange = pairing.hello()
    pairing_gate.refuse_with = (409, error)

    with pytest.raises(PairingError) as refused:
        pairing.request(exchange, "agent-invented", [])

    assert says in str(refused.value)
    assert never not in str(refused.value)


def test_an_answer_missing_its_challenge_is_a_protocol_error(
    gate: FakeGate, host: Issued
) -> None:
    def answer(path: str, body: str, client: bytes | None) -> tuple[int, str]:
        return 200, json.dumps({"proof": base64.b64encode(bytes(32)).decode()})

    gate.handler = answer

    with pytest.raises(ProtocolError, match="challenge"):
        Pairing(gate.port, INVITATION, connection_timeout=10.0).hello()


def test_something_that_is_not_a_token_never_reaches_the_socket() -> None:
    from pykeepassgate.errors import MalformedToken

    with pytest.raises(MalformedToken):
        Pairing(1, "not a token", connection_timeout=1.0)


def test_the_request_refuses_a_certificate_its_hello_never_proved(
    gate: FakeGate, pairing_gate: Gate, tmp_path: Path
) -> None:
    """The one thing an unpaired client can check, and it was not checking it.

    A pairing client has no trust anchor: `unpaired_context` verifies nothing, on purpose, because
    the anchor is what it is about to be handed. The hello's server proof is therefore the whole of
    what it knows, and it is a proof about *that connection's* certificate. Let the KeePass exit
    between the two connections and anything else take the port, and the request hands over its
    name and its profiles and then writes the stranger's authority and gate key as the ones to
    trust from here on. The gate makes the mirror comparison and answers `409 no exchange`.
    """
    exchange = Pairing(gate.port, INVITATION, connection_timeout=10.0).hello()

    impostor = Authority(tmp_path / "impostor")
    gate.serve_with(impostor.issue("host", ExtendedKeyUsageOID.SERVER_AUTH, "impostor.pem"))

    with pytest.raises(ProofRefused, match="different certificate"):
        Pairing(gate.port, INVITATION, connection_timeout=10.0).request(
            exchange, "agent-invented", ["reader"]
        )


def test_a_request_that_reaches_its_own_hello_is_served(gate: FakeGate, pairing_gate: Gate) -> None:
    # The other side of the same check: nothing changed, so nothing is refused.
    pairing = Pairing(gate.port, INVITATION, connection_timeout=10.0)
    exchange = pairing.hello()

    handed = pairing.request(exchange, "agent-invented", ["reader"])
    assert handed["subject"] == "CN=agent-invented"


def test_a_pairing_waits_longer_than_the_gate_may_take(gate: FakeGate) -> None:
    # Not the ordinary timeout. The plugin gives this one exchange six minutes because its own
    # worst case is 305 seconds, and a client that hangs up first throws away the answer to a
    # pairing whose invitation is already spent and whose record is already written.
    assert (
        Pairing(gate.port, INVITATION)._connection_timeout
        == transport.PAIRING_CONNECTION_TIMEOUT
    )


@pytest.mark.parametrize(
    ("name", "profiles"),
    [
        ("", ["reader"]),
        ("a" * 65, ["reader"]),
        ("agent/one", ["reader"]),
        ("agent\none", ["reader"]),
        (" agent", ["reader"]),
        ("agent ", ["reader"]),
        ("agent", [""]),
        ("agent", ["read,write"]),
        ("agent", ["read\nwrite"]),
        ("agent", ["default"]),
        ("agent", ["Default"]),
        ("agent", ["DEFAULT"]),
        ("agent", ["b" * 65]),
        ("agent", [f"profile-{number}" for number in range(33)]),
    ],
)
def test_a_name_the_gate_would_refuse_never_reaches_it(
    gate: FakeGate, pairing_gate: Gate, name: str, profiles: list[str]
) -> None:
    # `PairingHandler.Request` checks the name and the profiles before it takes the challenge,
    # so a `400 name` costs a round trip and leaves the exchange as it was. Refusing here saves
    # the round trip and names the rule that was broken. It is also what keeps `proofs.asked`
    # unambiguous: with these characters excluded, neither of its separators can appear inside a
    # part.
    pairing = Pairing(gate.port, INVITATION, connection_timeout=10.0)
    exchange = pairing.hello()
    before = len(gate.requests)

    with pytest.raises(PairingError):
        pairing.request(exchange, name, profiles)

    assert len(gate.requests) == before, "nothing was asked of the gate at all"


def test_the_profile_ceiling_is_the_routing_document_s() -> None:
    # Mirrored rather than read, as the name rules are, so held to the document here.
    declared = operations(routing_table())["pairRequest"]["requestBody"]["content"][
        "application/json"
    ]["schema"]
    profiles = resolved(declared, routing_table())["properties"]["profiles"]

    assert resolved(profiles, routing_table())["maxItems"] == pairing_module.MAX_PROFILES


def test_the_names_the_gate_does_allow_are_sent(gate: FakeGate, pairing_gate: Gate) -> None:
    # The other side of it: everything `AuthorityIssuer` accepts has to get through, or this
    # refuses pairings the gate would have served.
    pairing = Pairing(gate.port, INVITATION, connection_timeout=10.0)
    exchange = pairing.hello()

    assert pairing.request(exchange, "Agent One_1.2-3", ["read-only", "write_2"])


def spy_on_pairing(monkeypatch: pytest.MonkeyPatch) -> list[dict[str, Any]]:
    """Record what `api.pair` builds its `Pairing` with, and dial the fake gate regardless.

    The host is recorded as asked and then replaced by loopback, which is where the fake gate
    listens, so a test can name a host that is not loopback and still see what reached here.
    """
    built: list[dict[str, Any]] = []

    class Spied(Pairing):
        def __init__(self, port: int, invitation: str, **arguments: Any) -> None:
            built.append({"port": port, **arguments})
            super().__init__(port, invitation, **{**arguments, "host": transport.LOOPBACK})

    monkeypatch.setattr(api, "Pairing", Spied)
    return built


def test_pairing_through_the_library_passes_on_what_it_was_given(
    gate: FakeGate,
    host: Issued,
    tmp_path: Path,
    bundle: dict[str, Any],
    monkeypatch: pytest.MonkeyPatch,
) -> None:
    # `api.pair` is the path the command line takes, so an ordinary timeout here would defeat
    # `Pairing`'s own longer one everywhere it actually matters, and hang up on an operator who
    # is still reading. Observed on the way into `Pairing` rather than read off a signature,
    # because a default that is declared and then not passed on pins nothing. A real bundle, so
    # this goes all the way through storing what came back.
    handler = Gate(gate, host, bundle=bundle)
    gate.handler = handler
    built = spy_on_pairing(monkeypatch)

    stored = api.pair(
        port=gate.port,
        invitation=INVITATION,
        name="agent-invented",
        profiles=["reader", "writer"],
        directory=tmp_path / "kept",
        host="gate.invalid",
        lifetime=timedelta(days=7),
    )

    assert built == [
        {
            "port": gate.port,
            "connection_timeout": transport.PAIRING_CONNECTION_TIMEOUT,
            "host": "gate.invalid",
            "max_response_bytes": transport.MAX_RESPONSE_BYTES,
        }
    ]
    assert handler.asked is not None
    assert handler.asked["profiles"] == ["reader", "writer"], "the operator is shown what was asked"
    assert handler.asked["lifetime"] == "P7D"
    assert stored.directory == tmp_path / "kept"
    assert api.load(tmp_path / "kept").port == gate.port, "the port stored is the one paired on"


def test_a_pairing_with_a_cap_nothing_fits_under_is_refused_before_anything_is_touched(
    gate: FakeGate, tmp_path: Path
) -> None:
    # Before `prepare`, which opens files, and before a hello the gate would count as an attempt.
    with pytest.raises(ValueError, match="positive number of bytes"):
        api.pair(
            port=gate.port,
            invitation=INVITATION,
            name="agent-invented",
            directory=tmp_path / "kept",
            max_response_bytes=0,
        )

    assert not (tmp_path / "kept").exists()
    assert gate.requests == []


@pytest.mark.parametrize(
    ("setting", "value"),
    [
        ("port", 0),
        ("port", 70000),
        ("connection_timeout", 0.0),
        ("connection_timeout", math.nan),
        ("retry_busy_timeout", math.nan),
        ("retry_locked_timeout", -1.0),
        ("retry_undecided_timeout", math.inf),
    ],
)
def test_a_pairing_with_a_setting_that_cannot_work_is_refused_before_anything_is_touched(
    setting: str, value: float, gate: FakeGate, tmp_path: Path
) -> None:
    # The command line refuses each of these, exit 2. Taken by the library, a NaN budget waited
    # out a `503` forever, a port TCP does not have left the dial as an `OverflowError`, and
    # either could be met only after `prepare` had opened the four files.
    given: dict[str, Any] = {setting: value}

    with pytest.raises(ValueError, match=setting):
        api.pair(
            **{
                "port": gate.port,
                "invitation": INVITATION,
                "name": "agent-invented",
                "directory": tmp_path / "kept",
                **given,
            }
        )

    with pytest.raises(ValueError, match=setting):
        Pairing(**{"port": gate.port, "invitation": INVITATION, **given})

    assert not (tmp_path / "kept").exists()
    assert gate.names == []


def test_a_pairing_asking_for_a_lifetime_nobody_can_have_is_refused_before_anything_is_touched(
    gate: FakeGate, tmp_path: Path
) -> None:
    # Before `prepare` and before the hello, as every other setting is: the gate would refuse the
    # request as `400 malformed`, and only after the four files had been opened to receive it.
    with pytest.raises(ValueError, match="lifetime"):
        api.pair(
            port=gate.port,
            invitation=INVITATION,
            name="agent-invented",
            directory=tmp_path / "kept",
            lifetime=timedelta(days=400),
        )

    assert not (tmp_path / "kept").exists()
    assert gate.names == []


def test_an_invitation_pasted_with_white_space_around_it_is_the_invitation(
    gate: FakeGate, pairing_gate: Gate
) -> None:
    # What the command line strips, the library strips too, so a program handed the same paste is
    # not refused what the command line accepts. `token.decode` itself stays exact, as the
    # plugin's decoder is, and a space inside is still refused.
    pairing = Pairing(gate.port, " " + INVITATION + "\r\n", connection_timeout=10.0)

    assert pairing.hello()

    with pytest.raises(MalformedToken):
        Pairing(gate.port, INVITATION[:21] + " " + INVITATION[21:])


def test_a_port_tcp_does_not_have_is_never_stored(
    tmp_path: Path, bundle: dict[str, Any]
) -> None:
    # `load` would refuse the file afterwards, so the identity beside it would be written and
    # never read again.
    with pytest.raises(ValueError, match="port"):
        api.save(tmp_path / "kept", 70000, bundle)

    assert not (tmp_path / "kept").exists()


def test_a_pairing_under_a_profile_is_kept_under_that_profile(
    gate: FakeGate,
    host: Issued,
    tmp_path: Path,
    bundle: dict[str, Any],
    monkeypatch: pytest.MonkeyPatch,
) -> None:
    # Saving writes in place, so a profile that did not arrive would store over the identity
    # already paired with another database under the default one.
    monkeypatch.setenv("LOCALAPPDATA", str(tmp_path))
    monkeypatch.setenv("XDG_CONFIG_HOME", str(tmp_path))
    gate.handler = Gate(gate, host, bundle=bundle)

    stored = api.pair(port=gate.port, invitation=INVITATION, name="agent", profile="work")

    assert stored.directory == api.default_directory("work")
    assert api.default_directory("work").parent == tmp_path / "pykeepassgate"
    assert not api.default_directory().exists(), "nothing was kept under the default profile"


def test_a_directory_that_cannot_hold_the_identity_is_refused_before_the_gate_is_asked(
    gate: FakeGate, host: Issued, tmp_path: Path, bundle: dict[str, Any]
) -> None:
    # The gate spends the invitation and saves the admission record before it answers, so a
    # directory found wanting only once the bundle is in hand costs an identity nobody can ask
    # for again, and leaves a record for a certificate nobody holds.
    gate.handler = Gate(gate, host, bundle=bundle)
    taken = tmp_path / "taken"
    taken.write_text("x")

    with pytest.raises(StorageError, match="cannot hold credentials"):
        api.pair(
            port=gate.port,
            invitation=INVITATION,
            name="agent-invented",
            profiles=["reader"],
            directory=taken,
        )

    assert gate.requests == [], "nothing reached the gate at all"


def test_pairing_hands_the_shared_directory_choice_to_the_check(
    gate: FakeGate,
    host: Issued,
    tmp_path: Path,
    bundle: dict[str, Any],
    monkeypatch: pytest.MonkeyPatch,
) -> None:
    # Every directory reads as another account's, so `prepare` before the exchange refuses, and
    # only a flag that reaches it lets the pairing land.
    monkeypatch.setattr(os, "geteuid", lambda: 4242, raising=False)
    gate.handler = Gate(gate, host, bundle=bundle)
    where = tmp_path / "kept"
    where.mkdir()

    with pytest.raises(StorageError, match="allow-shared-directory"):
        api.pair(port=gate.port, invitation=INVITATION, name="agent", directory=where)
    assert gate.requests == []

    stored = api.pair(
        port=gate.port,
        invitation=INVITATION,
        name="agent",
        profiles=["reader"],
        directory=where,
        allow_shared_directory=True,
    )

    assert stored.directory == where


def test_what_prepare_returns_is_exported_beside_it(tmp_path: Path) -> None:
    # A caller driving `Pairing` by hand calls `api.prepare` and holds what it returns until the
    # exchange is over, so the type of that is part of the library's surface as much as the
    # function is: annotating a variable with it should not mean reaching into `credentials`.
    assert "Prepared" in api.__all__
    with api.prepare(tmp_path / "held") as prepared:
        assert isinstance(prepared, api.Prepared)


def test_a_refused_pairing_leaves_an_identity_already_there_working(
    gate: FakeGate, host: Issued, tmp_path: Path, bundle: dict[str, Any]
) -> None:
    # `prepare` holds the four files open for writing through the exchange, and opening them
    # must not truncate: until the gate hands over another, the old identity is the working one.
    stored = api.save(tmp_path / "kept", gate.port, bundle)
    before = {path.name: path.read_bytes() for path in stored.directory.iterdir()}
    refusing = Gate(gate, host, bundle=bundle)
    refusing.refuse_with = (403, "refused")
    gate.handler = refusing

    with pytest.raises(PairingRefused):
        api.pair(port=gate.port, invitation=INVITATION, name="agent", directory=stored.directory)

    assert {path.name: path.read_bytes() for path in stored.directory.iterdir()} == before
    assert api.load(stored.directory).subject == stored.subject


def test_a_refused_pairing_leaves_no_empty_identity_behind(
    gate: FakeGate, host: Issued, tmp_path: Path, bundle: dict[str, Any]
) -> None:
    refusing = Gate(gate, host, bundle=bundle)
    refusing.refuse_with = (403, "refused")
    gate.handler = refusing
    where = tmp_path / "kept"

    with pytest.raises(PairingRefused):
        api.pair(port=gate.port, invitation=INVITATION, name="agent", directory=where)

    assert list(where.iterdir()) == [], "the files opening created are taken away again"


class TestWhatPairingMayRepeat:
    """Which phase a retry budget reaches, settled against `PAIRING.md` and pinned here.

    The rule is the brief's: nothing may be re-sent once the invitation could have been spent.
    Phase 3 spends nothing and carries nothing, so it may be repeated. Phase 4 carries a challenge
    the gate spends as it reads it, and is the message whose own completion issues a certificate
    and writes a record, so it may not. What is left is the exchange, and `api.pair` repeats that.
    """

    @pytest.fixture
    def waited(self, monkeypatch: pytest.MonkeyPatch) -> list[float]:
        """Every delay that would have been slept, without sleeping any of them."""
        recorded: list[float] = []
        monkeypatch.setattr(transport, "pause", recorded.append)
        return recorded

    @staticmethod
    def _sent(gate: FakeGate, path: str) -> int:
        return len([asked for asked in gate.requests if asked[1] == path])

    def test_a_hello_is_asked_again_because_a_hello_spends_nothing(
        self, gate: FakeGate, pairing_gate: Gate, waited: list[float]
    ) -> None:
        # `PAIRING.md` 306: the token is burned by an approval or a refusal and by nothing else.
        # A hello reaches no dialog, no issuance and no write, so a refused one has cost the
        # invitee nothing and asking again is starting phase 3 rather than sending it twice.
        pairing_gate.hello_refusals = [(503, "busy"), (503, "busy")]
        gate.retry_after = "2"

        exchange = Pairing(
            gate.port, INVITATION, connection_timeout=10.0, retry_busy_timeout=30.0
        ).hello()

        assert exchange.challenge == pairing_gate.challenge
        assert self._sent(gate, "/pair/hello") == 3
        assert waited == [2.0, 2.0]

    def test_each_hello_is_a_fresh_nonce_rather_than_the_refused_one_sent_again(
        self, gate: FakeGate, pairing_gate: Gate, waited: list[float]
    ) -> None:
        # What makes the repetition safe to call safe. The name carries the nonce the proof is
        # over, so two attempts under one name would be one message sent twice; these are two
        # different phase 3s, and the challenge that comes back belongs to the second.
        pairing_gate.hello_refusals = [(503, "busy")]

        exchange = Pairing(
            gate.port, INVITATION, connection_timeout=10.0, retry_busy_timeout=30.0
        ).hello()

        assert len(gate.names) == 2
        assert len(set(gate.names)) == 2
        assert exchange.nonce == pairing_gate.nonce

    def test_a_locked_database_is_waited_out_on_its_own_budget(
        self, gate: FakeGate, pairing_gate: Gate, waited: list[float]
    ) -> None:
        pairing_gate.hello_refusals = [(503, "locked")]
        gate.retry_after = "2"

        assert Pairing(
            gate.port, INVITATION, connection_timeout=10.0, retry_locked_timeout=30.0
        ).hello()
        assert waited == [2.0]

    def test_an_undecided_database_is_waited_out_on_its_own_budget(
        self, gate: FakeGate, pairing_gate: Gate, waited: list[float]
    ) -> None:
        # And not on the busy one: two authorities wait for a person to keep one of them.
        pairing_gate.hello_refusals = [(503, "undecided")]
        gate.retry_after = "2"

        with pytest.raises(AuthorityUndecided):
            Pairing(gate.port, INVITATION, connection_timeout=10.0, retry_busy_timeout=30.0).hello()
        assert waited == []

        pairing_gate.hello_refusals = [(503, "undecided")]
        assert Pairing(
            gate.port, INVITATION, connection_timeout=10.0, retry_undecided_timeout=30.0
        ).hello()
        assert waited == [2.0]

    def test_a_word_no_budget_names_is_never_waited_out(
        self, gate: FakeGate, pairing_gate: Gate, tmp_path: Path, waited: list[float]
    ) -> None:
        pairing_gate.hello_refusals = [(503, "teapot")]

        with pytest.raises(GateUnavailable) as refused:
            api.pair(
                port=gate.port,
                invitation=INVITATION,
                name="agent-invented",
                directory=tmp_path / "kept",
                retry_busy_timeout=300.0,
                retry_locked_timeout=300.0,
                retry_undecided_timeout=300.0,
            )

        assert refused.value.error == "teapot"
        assert self._sent(gate, "/pair/hello") == 1
        assert waited == []

    def test_a_hello_with_no_budget_reports_the_refusal_as_it_always_did(
        self, gate: FakeGate, pairing_gate: Gate, waited: list[float]
    ) -> None:
        pairing_gate.hello_refusals = [(503, "busy")]

        with pytest.raises(GateBusy):
            Pairing(gate.port, INVITATION, connection_timeout=10.0).hello()

        assert self._sent(gate, "/pair/hello") == 1
        assert waited == []

    def test_a_request_is_never_sent_twice_however_long_the_budget_is(
        self, gate: FakeGate, pairing_gate: Gate, waited: list[float]
    ) -> None:
        """The one that matters. Two reasons, and either alone would be enough.

        The challenge is spent by the gate as it reads this, so a second send earns
        `409 no exchange` and has told the operator nothing. And this is the message that can end
        with an invitation spent: everything after the dialog happens whether or not this client
        is still there, so a client resending it could not know whether what it was repeating had
        already issued a certificate and written a record.
        """
        pairing_gate.request_refusals = [(503, "busy")] * 3
        gate.retry_after = "2"

        pairing = Pairing(
            gate.port,
            INVITATION,
            connection_timeout=10.0,
            retry_busy_timeout=300.0,
            retry_locked_timeout=300.0,
        )

        exchange = pairing.hello()
        with pytest.raises(GateBusy):
            pairing.request(exchange, "agent-invented", ["reader"])

        assert self._sent(gate, "/pair/request") == 1
        assert waited == []

    def test_a_request_that_met_a_lock_is_not_sent_again_either(
        self, gate: FakeGate, pairing_gate: Gate, waited: list[float]
    ) -> None:
        pairing_gate.request_refusals = [(503, "locked")] * 3

        pairing = Pairing(
            gate.port, INVITATION, connection_timeout=10.0, retry_locked_timeout=300.0
        )

        exchange = pairing.hello()
        with pytest.raises(DatabaseLocked):
            pairing.request(exchange, "agent-invented", ["reader"])

        assert self._sent(gate, "/pair/request") == 1
        assert waited == []

    def test_the_whole_exchange_is_what_gets_tried_again(
        self,
        gate: FakeGate,
        host: Issued,
        tmp_path: Path,
        bundle: dict[str, Any],
        waited: list[float],
    ) -> None:
        """`api.pair` repeats the exchange, which is what `PAIRING.md` 303 says to do.

        "Nothing is spent by that, so the invitee can simply ask again." A `503` from
        `/pair/request` is provably before the burn: `PairingHandler.Approve` answers it at the
        blocked check, at a dialog that could not be shown, and at one that never reached the
        screen, and after the burn it answers `500` instead, in as many words. So the invitation
        is still good, and what follows is a new hello with a new nonce and a new challenge
        rather than the refused request sent again.
        """
        answering = Gate(gate, host, bundle=bundle)
        answering.request_refusals = [(503, "busy")]
        gate.handler = answering
        gate.retry_after = "2"

        stored = api.pair(
            port=gate.port,
            invitation=INVITATION,
            name="agent-invented",
            profiles=["reader"],
            directory=tmp_path / "kept",
            retry_busy_timeout=30.0,
        )

        assert stored.subject
        assert self._sent(gate, "/pair/hello") == 2, "the second try began at phase 3"
        assert self._sent(gate, "/pair/request") == 2
        assert waited == [2.0]

    def test_an_approval_the_gate_holds_is_asked_for_again_without_a_busy_budget(
        self,
        gate: FakeGate,
        host: Issued,
        tmp_path: Path,
        bundle: dict[str, Any],
        waited: list[float],
    ) -> None:
        # The operator approved and KeePass was too busy to write it: the gate holds the approval
        # for exactly this request until `heldUntil`, so not asking again loses it, and a caller
        # that gave no budget for KeePass being busy did not mean to lose an approval.
        until = (dt.datetime.now(dt.UTC) + dt.timedelta(minutes=5)).strftime("%Y-%m-%dT%H:%M:%SZ")
        answering = Gate(gate, host, bundle=bundle)
        answering.request_refusals = [(503, {"error": "busy", "heldUntil": until})]
        gate.handler = answering
        gate.retry_after = "2"

        stored = api.pair(
            port=gate.port,
            invitation=INVITATION,
            name="agent-invented",
            profiles=["reader"],
            directory=tmp_path / "kept",
        )

        assert stored.subject
        assert self._sent(gate, "/pair/request") == 2, "the same request, from a fresh hello"
        assert answering.asked is not None and answering.asked["profiles"] == ["reader"]
        assert waited == [2.0]

    def test_an_approval_no_longer_held_is_raised_with_the_moment_it_ran_out(
        self, gate: FakeGate, pairing_gate: Gate, tmp_path: Path, waited: list[float]
    ) -> None:
        ran_out = {"error": "busy", "heldUntil": "2001-02-03T04:05:06Z"}
        pairing_gate.request_refusals = [(503, ran_out)]

        with pytest.raises(GateBusy) as refused:
            api.pair(
                port=gate.port,
                invitation=INVITATION,
                name="agent-invented",
                directory=tmp_path / "kept",
            )

        assert refused.value.held_until == dt.datetime(2001, 2, 3, 4, 5, 6, tzinfo=dt.UTC)
        assert waited == []

    def test_a_pairing_waits_out_two_authorities_on_the_budget_given_for_them(
        self,
        gate: FakeGate,
        host: Issued,
        tmp_path: Path,
        bundle: dict[str, Any],
        waited: list[float],
    ) -> None:
        answering = Gate(gate, host, bundle=bundle)
        answering.hello_refusals = [(503, "undecided")]
        gate.handler = answering
        gate.retry_after = "2"

        stored = api.pair(
            port=gate.port,
            invitation=INVITATION,
            name="agent-invented",
            directory=tmp_path / "kept",
            retry_undecided_timeout=30.0,
        )

        assert stored.subject
        assert waited == [2.0]

    def test_the_budget_is_held_in_one_place_rather_than_multiplied_by_two(
        self, gate: FakeGate, pairing_gate: Gate, tmp_path: Path, waited: list[float]
    ) -> None:
        # `api.pair` builds its `Pairing` without the budgets, because a refusal waited out by
        # `hello` and then waited out again by the loop around it would spend some multiple of
        # what the caller allowed, and nothing could say which. Five seconds at two a time is two
        # waits, wherever the refusal was.
        pairing_gate.hello_refusals = [(503, "busy")] * 20
        gate.retry_after = "2"

        with pytest.raises(GateBusy):
            api.pair(
                port=gate.port,
                invitation=INVITATION,
                name="agent-invented",
                directory=tmp_path / "kept",
                retry_busy_timeout=5.0,
            )

        assert waited == [2.0, 2.0], "five seconds bought two waits of two, and not four"
        assert self._sent(gate, "/pair/hello") == 3

    @pytest.mark.parametrize(
        ("status", "error", "raises"),
        [
            (504, "unanswered", PairingUnanswered),
            (403, "refused", PairingRefused),
        ],
    )
    def test_an_answer_the_operator_gave_is_never_asked_again(
        self,
        gate: FakeGate,
        pairing_gate: Gate,
        tmp_path: Path,
        waited: list[float],
        status: int,
        error: str,
        raises: type[Exception],
    ) -> None:
        # Neither is a `503`, and neither may be repeated for a reason of its own. A refusal
        # spends the invitation, so there is nothing left to ask with. An unanswered dialog
        # spends nothing, but asking again puts the same question on somebody's screen a second
        # time without them having asked for it, which is the dialog spam phase 1 exists to stop.
        pairing_gate.refuse_with = (status, error)
        gate.retry_after = "2"

        with pytest.raises(raises):
            api.pair(
                port=gate.port,
                invitation=INVITATION,
                name="agent-invented",
                directory=tmp_path / "kept",
                retry_busy_timeout=300.0,
                retry_locked_timeout=300.0,
            )

        assert self._sent(gate, "/pair/request") == 1
        assert waited == []
