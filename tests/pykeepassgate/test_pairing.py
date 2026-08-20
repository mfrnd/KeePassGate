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
import inspect
import json
import secrets
from pathlib import Path
from typing import Any

import pytest
from cryptography.x509.oid import ExtendedKeyUsageOID

from conftest import Authority, FakeGate, Issued
from pykeepassgate import api, proofs, sni, transport
from pykeepassgate import pairing as pairing_module
from pykeepassgate import token as token_module
from pykeepassgate.errors import (
    DatabaseLocked,
    GateBusy,
    PairingError,
    PairingRefused,
    PairingUnanswered,
    ProofRefused,
    ProtocolError,
    RouteRefused,
)
from pykeepassgate.pairing import Pairing
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
            proofs.asked(body["name"], body["profiles"]),
        )

        if not proofs.matches(expected, base64.b64decode(body["proof"])):
            return 401, json.dumps({"error": "proof"})

        self.asked = body
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
    exchange = Pairing(gate.port, INVITATION, timeout=10.0).hello()

    assert exchange.challenge == pairing_gate.challenge
    assert exchange.binding == pairing_gate.binding
    assert exchange.nonce == pairing_gate.nonce


def test_the_name_the_hello_arrives_on_is_built_from_the_token(
    gate: FakeGate, pairing_gate: Gate
) -> None:
    Pairing(gate.port, INVITATION, timeout=10.0).hello()

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
        Pairing(gate.port, INVITATION, timeout=10.0).hello()

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
        Pairing(gate.port, INVITATION, timeout=10.0).hello()


def test_an_answer_that_cannot_prove_the_token_stops_the_exchange(
    gate: FakeGate, pairing_gate: Gate
) -> None:
    # An unpaired client has no anchor, so this is the only thing standing between it and whatever
    # accepted the connection.
    pairing_gate.server_proof = bytes(32)

    with pytest.raises(ProofRefused, match="not the right KeePass"):
        Pairing(gate.port, INVITATION, timeout=10.0).hello()


def test_a_request_follows_its_own_hello_and_comes_back_with_a_bundle(
    gate: FakeGate, pairing_gate: Gate
) -> None:
    pairing = Pairing(gate.port, INVITATION, timeout=10.0)
    exchange = pairing.hello()
    bundle = pairing.request(exchange, "agent-invented", ["reader", "writer"])

    assert pairing_gate.asked == {
        "name": "agent-invented",
        "profiles": ["reader", "writer"],
        "proof": pairing_gate.asked["proof"] if pairing_gate.asked else "",
    }

    assert bundle["subject"] == "CN=agent-invented"
    assert bundle["profiles"] == ["reader"], "what was approved, not what was asked for"


def test_the_request_goes_out_on_a_new_connection_with_a_new_name(
    gate: FakeGate, pairing_gate: Gate
) -> None:
    # The gate serves one request per connection, so the exchange spans two, and the challenge is
    # what carries it across.
    pairing = Pairing(gate.port, INVITATION, timeout=10.0)
    pairing.request(pairing.hello(), "agent-invented", [])

    assert len(gate.names) == 2
    assert gate.names[0] != gate.names[1]


def test_a_request_with_no_hello_behind_it_cannot_be_built(gate: FakeGate) -> None:
    # Not a protocol refusal but a programming one: there is nothing to carry across.
    pairing = Pairing(gate.port, INVITATION, timeout=10.0)
    with pytest.raises(AttributeError):
        pairing.request(None, "agent-invented", [])  # type: ignore[arg-type]


@pytest.mark.parametrize(
    ("status", "error", "expected", "why"),
    [
        (503, "locked", DatabaseLocked, "wait for the operator to unlock it"),
        (503, "busy", GateBusy, "a group dialog is open, so retry"),
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
    pairing = Pairing(gate.port, INVITATION, timeout=10.0)
    exchange = pairing.hello()
    pairing_gate.refuse_with = (status, error)

    with pytest.raises(expected):
        pairing.request(exchange, "agent-invented", []), why


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
        Pairing(gate.port, INVITATION, timeout=10.0).hello()


def test_something_that_is_not_a_token_never_reaches_the_socket() -> None:
    from pykeepassgate.errors import MalformedToken

    with pytest.raises(MalformedToken):
        Pairing(1, "not a token", timeout=1.0)


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
    exchange = Pairing(gate.port, INVITATION, timeout=10.0).hello()

    impostor = Authority(tmp_path / "impostor")
    gate.serve_with(impostor.issue("host", ExtendedKeyUsageOID.SERVER_AUTH, "impostor.pem"))

    with pytest.raises(ProofRefused, match="different certificate"):
        Pairing(gate.port, INVITATION, timeout=10.0).request(
            exchange, "agent-invented", ["reader"]
        )


def test_a_request_that_reaches_its_own_hello_is_served(gate: FakeGate, pairing_gate: Gate) -> None:
    # The other side of the same check: nothing changed, so nothing is refused.
    pairing = Pairing(gate.port, INVITATION, timeout=10.0)
    exchange = pairing.hello()

    handed = pairing.request(exchange, "agent-invented", ["reader"])
    assert handed["subject"] == "CN=agent-invented"


def test_a_pairing_waits_longer_than_the_gate_may_take(gate: FakeGate) -> None:
    # Not the ordinary timeout. The plugin gives this one exchange five minutes because its own
    # worst case is 185 seconds, and a client that hangs up first throws away the answer to a
    # pairing whose invitation is already spent and whose record is already written.
    assert Pairing(gate.port, INVITATION)._timeout == transport.PAIRING_TIMEOUT


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
    pairing = Pairing(gate.port, INVITATION, timeout=10.0)
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
    pairing = Pairing(gate.port, INVITATION, timeout=10.0)
    exchange = pairing.hello()

    assert pairing.request(exchange, "Agent One_1.2-3", ["read-only", "write_2"])


def test_pairing_through_the_library_waits_as_long_as_pairing_needs(
    gate: FakeGate, host: Issued, tmp_path: Path, bundle: dict[str, Any]
) -> None:
    # `api.pair` is the path the command line takes, so a default of `DEFAULT_TIMEOUT` here would
    # defeat `Pairing`'s own longer one everywhere it actually matters. A real bundle, so this
    # goes all the way through storing what came back.
    gate.handler = Gate(gate, host, bundle=bundle)

    stored = api.pair(
        port=gate.port,
        invitation=INVITATION,
        name="agent-invented",
        profiles=["reader"],
        directory=tmp_path / "kept",
    )

    assert stored.subject
    assert inspect.signature(api.pair).parameters["timeout"].default == transport.PAIRING_TIMEOUT
