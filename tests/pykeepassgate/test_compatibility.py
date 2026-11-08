"""Which gates this client works with, `API.md` §411, and where it asks.

The rule on its own first, with this client's version set to whatever a case needs, and then each
place the client meets a gate's version: before a paired client's first request, in the pairing
hello, on the command line, and in the stand-in gate a program is tested against.
"""

from __future__ import annotations

import hashlib
import json
from pathlib import Path
from typing import Any

import pytest
from cryptography.hazmat.primitives import serialization

from pykeepassgate import __version__, api, cli, compatibility, transport
from pykeepassgate import credentials as credentials_module
from pykeepassgate.client import GateClient
from pykeepassgate.errors import (
    DatabaseLocked,
    GateBusy,
    IncompatibleGate,
    ProofRefused,
    ProtocolError,
)
from pykeepassgate.pairing import Pairing
from pykeepassgate.testing import Authority, FakeGate, Issued
from test_pairing import INVITATION, Gate

#: A version no build of this client is, for a gate it does not work with.
AHEAD = "99.0.0"

STATUS = {"ok": True, "database": "abcdef01", "version": __version__, "revision": ""}


@pytest.fixture
def paired(tmp_path: Path, bundle: dict[str, Any], gate: FakeGate) -> GateClient:
    """A client that has been through pairing with this gate."""
    stored = credentials_module.save(tmp_path / "kept", gate.port, bundle)
    return GateClient(stored, connection_timeout=10.0)


@pytest.fixture
def pairing_gate(gate: FakeGate, host: Issued) -> Gate:
    """A gate that speaks pairing, and a handler wired to it."""
    handler = Gate(gate, host)
    gate.handler = handler
    return handler


@pytest.fixture
def waited(monkeypatch: pytest.MonkeyPatch) -> list[float]:
    """Every delay that would have been slept, without sleeping any of them."""
    recorded: list[float] = []
    monkeypatch.setattr(transport, "pause", recorded.append)
    return recorded


@pytest.mark.parametrize(
    ("client", "gate"),
    [
        ("2.3.4", "2.3.4"),
        ("2.3.4", "2.3.0"),
        ("2.3.0", "2.3.9"),
        ("2.3.4", "2.4.0"),
        ("2.0.0", "2.11.0"),
        ("0.1.0", "0.2.0"),
    ],
    ids=[
        "the same", "an older patch", "a newer patch", "a newer minor", "minors as numbers", "0.x"
    ],
)
def test_a_gate_of_the_client_s_major_that_it_does_not_lead_is_one_it_works_with(
    client: str, gate: str, monkeypatch: pytest.MonkeyPatch
) -> None:
    # Patches are released on each side on their own, so they are not compared, and a gate ahead
    # of its client still serves everything the client relies on.
    monkeypatch.setattr(compatibility, "CLIENT_VERSION", client)

    compatibility.refuse_incompatible(gate)


@pytest.mark.parametrize(
    ("client", "gate", "reason"),
    [
        ("2.4.0", "2.3.9", "its own minor version or later"),
        ("2.11.0", "2.9.0", "its own minor version or later"),
        ("0.2.0", "0.1.5", "its own minor version or later"),
        ("2.3.4", "3.0.0", "its own major version"),
        ("2.3.4", "1.9.9", "its own major version"),
        ("0.9.0", "1.0.0", "its own major version"),
    ],
)
def test_any_other_gate_is_refused_naming_both_versions(
    client: str, gate: str, reason: str, monkeypatch: pytest.MonkeyPatch
) -> None:
    monkeypatch.setattr(compatibility, "CLIENT_VERSION", client)

    with pytest.raises(IncompatibleGate, match=reason) as refused:
        compatibility.refuse_incompatible(gate)

    assert (refused.value.client, refused.value.gate) == (client, gate)
    assert f"pykeepassgate {client}" in str(refused.value)
    assert f"KeePassGate {gate}" in str(refused.value)


def test_a_client_ahead_of_its_gate_is_told_which_client_would_do(
    monkeypatch: pytest.MonkeyPatch,
) -> None:
    monkeypatch.setattr(compatibility, "CLIENT_VERSION", "2.4.1")

    with pytest.raises(IncompatibleGate, match=r"or pykeepassgate 2\.3 installed"):
        compatibility.refuse_incompatible("2.3.7")


@pytest.mark.parametrize(
    "gate",
    [
        "unknown",
        "2.3",
        "2.3.4.5",
        "2.3.4-beta",
        "v2.3.4",
        " 2.3.4",
        "2.3.4\n",
        "٢.3.4",
        "2." + "3" * 5000 + ".4",
    ],
    ids=[
        "a word",
        "two parts",
        "four parts",
        "a suffix",
        "a prefix",
        "a space",
        "a line",
        "a digit",
        "more digits than int reads",
    ],
)
def test_a_version_that_is_not_three_whole_numbers_is_refused(
    gate: str, monkeypatch: pytest.MonkeyPatch
) -> None:
    # The plugin reports `unknown` when its assembly gives none, and an Arabic-Indic digit is a
    # digit to `\d` and to `int`, which would have compared it.
    monkeypatch.setattr(compatibility, "CLIENT_VERSION", "2.3.4")

    with pytest.raises(IncompatibleGate, match="not a version this client can compare") as refused:
        compatibility.refuse_incompatible(gate)

    assert repr(gate) in str(refused.value), "quoted, so nothing in it reads as the message"


def test_a_client_whose_own_version_does_not_read_works_with_no_gate(
    monkeypatch: pytest.MonkeyPatch,
) -> None:
    # A build cannot make such a version, and refusing is the only answer that cannot be wrong.
    monkeypatch.setattr(compatibility, "CLIENT_VERSION", "unknown")

    with pytest.raises(IncompatibleGate, match="this client's own version is not a version"):
        compatibility.refuse_incompatible("2.3.4")


def test_a_gate_that_reports_no_version_is_refused_as_reporting_none() -> None:
    with pytest.raises(IncompatibleGate, match="the gate reports no version"):
        compatibility.refuse_incompatible("")


def test_this_client_works_with_a_gate_of_its_own_version() -> None:
    compatibility.refuse_incompatible(__version__)

    assert __version__ == compatibility.CLIENT_VERSION


def test_a_paired_client_compares_before_its_first_request_and_sends_nothing_after_a_refusal(
    gate: FakeGate, paired: GateClient
) -> None:
    gate.version = AHEAD
    gate.handler = lambda path, body, client: (200, "[]")

    with pytest.raises(IncompatibleGate, match=AHEAD) as refused:
        paired.groups()

    assert refused.value.gate == AHEAD
    assert gate.requests == [], "nothing but the comparison reached the gate"
    assert len(gate.names) == 1

    with pytest.raises(IncompatibleGate):
        paired.groups()

    assert gate.requests == [], "and every request after it is refused the same way"


def test_a_client_compares_once(gate: FakeGate, paired: GateClient) -> None:
    gate.handler = lambda path, body, client: (200, "{}")

    paired.get("/gate/whoami")
    paired.get("/gate/whoami")

    assert gate.requests == [("GET", "/gate/whoami")] * 2
    assert len(gate.names) == 3, "the comparison, then the two requests"


def test_the_status_route_is_the_comparison_rather_than_preceded_by_one(
    gate: FakeGate, paired: GateClient
) -> None:
    gate.version = None
    gate.handler = lambda path, body, client: (
        (200, json.dumps(STATUS)) if path == "/gate/status" else (200, "{}")
    )

    paired.status()
    paired.get("/gate/whoami")

    assert gate.requests == [("GET", "/gate/status"), ("GET", "/gate/whoami")]


def test_the_status_route_refuses_a_gate_the_client_does_not_work_with(
    gate: FakeGate, paired: GateClient
) -> None:
    gate.version = AHEAD

    with pytest.raises(IncompatibleGate, match=AHEAD):
        paired.status()


def test_the_comparison_waits_on_the_budgets_of_the_request_it_comes_before(
    gate: FakeGate, paired: GateClient, waited: list[float]
) -> None:
    # A caller willing to wait out a lock for a request is willing to wait it out for what the
    # request needs first, and a caller that is not is told about the lock as it always was.
    locked = [1]

    def answer(path: str, body: str, client: bytes | None) -> tuple[int, str]:
        if path == "/gate/status" and locked[0]:
            locked[0] -= 1
            return 503, json.dumps({"error": "locked"})

        return 200, json.dumps(STATUS) if path == "/gate/status" else "{}"

    gate.version = None
    gate.handler = answer
    gate.retry_after = "2"

    with pytest.raises(DatabaseLocked):
        paired.get("/gate/whoami")

    locked[0] = 1
    assert paired.get("/gate/whoami", retry_locked_timeout=30.0) == {}
    assert waited == [2.0]


def test_a_renewal_gives_the_old_certificate_up_without_comparing_again(
    gate: FakeGate, paired: GateClient
) -> None:
    # The client built for the old identity reaches the gate the renewal was just answered by, so
    # comparing again would cost a connection and decide nothing.
    gate.version = AHEAD

    with pytest.raises(IncompatibleGate):
        paired.renew()

    assert gate.requests == [], "nothing renewed, and nothing given up"


def test_the_command_line_exits_with_the_refusal_naming_both_versions(
    gate: FakeGate, tmp_path: Path, bundle: dict[str, Any], capsys: pytest.CaptureFixture[str]
) -> None:
    credentials_module.save(tmp_path / "kept", gate.port, bundle)
    gate.version = AHEAD

    assert cli.main(["status", "--directory", str(tmp_path / "kept")]) == 1

    printed = capsys.readouterr()
    assert printed.out == ""
    assert f"pykeepassgate {__version__}" in printed.err
    assert f"KeePassGate {AHEAD}" in printed.err


def test_a_hello_from_a_gate_the_client_does_not_work_with_ends_the_pairing_before_the_request(
    gate: FakeGate, pairing_gate: Gate
) -> None:
    pairing_gate.version = AHEAD

    with pytest.raises(IncompatibleGate, match=AHEAD):
        Pairing(gate.port, INVITATION, connection_timeout=10.0).hello()

    assert [path for _, path in gate.requests] == ["/pair/hello"], "no operator was asked"


def test_pairing_through_the_library_keeps_nothing_from_a_gate_it_does_not_work_with(
    gate: FakeGate, pairing_gate: Gate, tmp_path: Path
) -> None:
    pairing_gate.version = AHEAD

    with pytest.raises(IncompatibleGate):
        api.pair(
            INVITATION,
            "agent-invented",
            port=gate.port,
            directory=tmp_path / "kept",
            connection_timeout=10.0,
        )

    assert [path for _, path in gate.requests] == ["/pair/hello"]
    assert list((tmp_path / "kept").iterdir()) == [], "the files opening created are taken away"


def test_a_hello_without_a_version_is_from_a_gate_older_than_this_client(
    gate: FakeGate, pairing_gate: Gate
) -> None:
    pairing_gate.version = None

    with pytest.raises(IncompatibleGate, match="the gate reports no version"):
        Pairing(gate.port, INVITATION, connection_timeout=10.0).hello()


def test_a_hello_whose_version_is_not_text_is_a_protocol_error(
    gate: FakeGate, pairing_gate: Gate
) -> None:
    pairing_gate.version = 2

    with pytest.raises(ProtocolError, match="version that is not text"):
        Pairing(gate.port, INVITATION, connection_timeout=10.0).hello()


def test_an_impostor_is_refused_on_its_proof_before_its_version_is_read(
    gate: FakeGate, pairing_gate: Gate
) -> None:
    # Until the proof verifies nothing shows the answer came from the gate, so what it reports
    # about itself is not worth an answer of its own.
    pairing_gate.server_proof = bytes(32)
    pairing_gate.version = AHEAD

    with pytest.raises(ProofRefused):
        Pairing(gate.port, INVITATION, connection_timeout=10.0).hello()


def test_the_stand_in_answers_the_status_route_itself_as_the_plugin_would(
    gate: FakeGate, paired: GateClient, authority: Authority
) -> None:
    # The database is named as the plugin names it, by the first characters of the authority's
    # digest, and the answer is not among the requests a test scripted.
    status = paired.status()
    der = authority.certificate.public_bytes(serialization.Encoding.DER)

    assert (status.ok, status.version, status.revision) == (True, __version__, "")
    assert status.database == hashlib.sha256(der).hexdigest()[:16]
    assert gate.requests == []


def test_the_stand_in_leaves_the_status_route_to_the_handler_without_a_version(
    gate: FakeGate, paired: GateClient
) -> None:
    gate.version = None
    gate.handler = lambda path, body, client: (503, json.dumps({"error": "busy"}))

    with pytest.raises(GateBusy):
        paired.status()

    assert gate.requests == [("GET", "/gate/status")]
