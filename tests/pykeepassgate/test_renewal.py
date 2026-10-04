"""A paired client renewing its certificate against a gate that answers the way the plugin answers.

What these hold the client to is the order the design rests on: the new identity is stored whole
before anything else happens, the old certificate is then given up by presenting it, and a failure
at either end leaves something a person can act on rather than nothing, or two half identities.
"""

from __future__ import annotations

import base64
import json
import re
import time
from datetime import UTC, datetime, timedelta
from pathlib import Path
from typing import Any

import pytest
from cryptography import x509
from cryptography.hazmat.primitives import serialization
from cryptography.hazmat.primitives.asymmetric import ec
from cryptography.hazmat.primitives.serialization import pkcs12

from conftest import Authority, FakeGate, Issued
from pykeepassgate import api, cli, transport
from pykeepassgate import client as client_module
from pykeepassgate import credentials as credentials_module
from pykeepassgate.client import GateClient
from pykeepassgate.errors import (
    AuthorityUndecided,
    DatabaseLocked,
    GateBusy,
    GateUnavailable,
    NotAdmitted,
    RenewalRefused,
    RenewalRefusedForever,
    RenewalRefusedThisSession,
    RenewalTooEarly,
    RenewalUnanswered,
    RouteRefused,
    StorageError,
    TransportError,
)
from test_api_routes import disagreements, resolved
from test_credentials import directory_link
from test_reference import operations, routing_table


def bundle_for(authority: Authority, issued: Issued, expires: str) -> dict[str, Any]:
    """What the gate answers a renewal with, for this certificate: pairing's bundle."""
    loaded = serialization.load_pem_private_key(issued.pem.read_bytes(), password=None)
    assert isinstance(loaded, ec.EllipticCurvePrivateKey)

    return {
        "identity": base64.b64encode(
            pkcs12.serialize_key_and_certificates(
                name=b"agent-invented",
                key=loaded,
                cert=issued.certificate,
                cas=None,
                encryption_algorithm=serialization.NoEncryption(),
            )
        ).decode(),
        "authority": base64.b64encode(
            authority.certificate.public_bytes(serialization.Encoding.DER)
        ).decode(),
        "gateKey": base64.b64encode(bytes(range(32))).decode(),
        "subject": "CN=agent-invented",
        "database": "abcdef01",
        "expires": expires,
        "profiles": ["reader"],
    }


class Gate:
    """The plugin's half of a renewal and of a client giving a certificate up."""

    def __init__(self, renewed: dict[str, Any]) -> None:
        self.renewed = renewed
        self.renewal: tuple[int, str] = (200, json.dumps(renewed))
        self.revocation: tuple[int, str] = (200, json.dumps({"ok": True}))
        self.seen: list[tuple[str, str, bytes | None]] = []

    def __call__(self, path: str, body: str, client: bytes | None) -> tuple[int, str]:
        self.seen.append((path, body, client))

        if path == "/gate/renew":
            return self.renewal

        if path == "/gate/certificate":
            return self.revocation

        return 200, json.dumps(
            {"ok": True, "database": "abcdef01", "version": "0.1.0", "revision": "0123abcd"}
        )


@pytest.fixture
def renewed_certificate(authority: Authority) -> Issued:
    """The certificate a renewal hands over: the same name, and a key of its own."""
    return authority.issue(
        "agent-invented", x509.oid.ExtendedKeyUsageOID.CLIENT_AUTH, "renewed.pem"
    )


@pytest.fixture
def paired(tmp_path: Path, bundle: dict[str, Any], gate: FakeGate) -> GateClient:
    """A client that has been through pairing with this gate, kept where a profile would be."""
    stored = credentials_module.save(tmp_path / "profiles" / "default", gate.port, bundle)
    return GateClient(stored, connection_timeout=10.0)


@pytest.fixture
def renewing(
    gate: FakeGate, authority: Authority, renewed_certificate: Issued
) -> Gate:
    """A gate that renews into the certificate above, and takes the old one back when asked."""
    answering = Gate(bundle_for(authority, renewed_certificate, "2127-01-02T03:04:05Z"))
    gate.handler = answering
    return answering


def der(issued: Issued) -> bytes:
    return issued.certificate.public_bytes(serialization.Encoding.DER)


def test_a_renewal_stores_the_new_identity_and_gives_the_old_one_up_by_presenting_it(
    gate: FakeGate,
    paired: GateClient,
    renewing: Gate,
    client_certificate: Issued,
    renewed_certificate: Issued,
) -> None:
    where = paired.credentials.directory

    renewal = paired.renew()

    assert renewal.unrevoked is None
    assert renewal.retired is None
    assert renewal.credentials.directory == where
    assert renewal.credentials.expires == "2127-01-02T03:04:05Z"
    assert api.load(where).expires == "2127-01-02T03:04:05Z", "what load finds is the new one"

    assert [(method, path) for method, path in gate.requests] == [
        ("POST", "/gate/renew"),
        ("DELETE", "/gate/certificate"),
    ]
    assert renewing.seen[0][2] == der(client_certificate), "asked for with the old certificate"
    assert renewing.seen[1][2] == der(client_certificate), "and given up by presenting it"
    assert not where.with_name("default.retired").exists(), "nothing of the old one is left"
    assert not where.with_name("default.renewing").exists()


def test_the_client_carries_on_as_the_new_identity(
    gate: FakeGate, paired: GateClient, renewing: Gate, renewed_certificate: Issued
) -> None:
    paired.renew()
    paired.status()

    assert renewing.seen[-1][2] == der(renewed_certificate)
    assert paired.credentials.expires == "2127-01-02T03:04:05Z"


@pytest.mark.parametrize(
    ("lifetime", "sent"), [(None, {}), (timedelta(days=30), {"lifetime": "P30D"})]
)
def test_a_renewal_asks_for_the_lifetime_it_was_given(
    paired: GateClient, renewing: Gate, lifetime: timedelta | None, sent: dict[str, str]
) -> None:
    paired.renew(lifetime=lifetime)

    assert json.loads(renewing.seen[0][1]) == sent


def test_what_a_renewal_sends_is_what_the_document_declares(
    paired: GateClient, renewing: Gate
) -> None:
    # The route's own case of `test_api_routes`, kept here because a renewal is an exchange: the
    # request this checks is followed by a second one that gives the old certificate up.
    declaration = operations(routing_table())["renewCertificate"]
    schema = declaration["requestBody"]["content"]["application/json"]["schema"]

    paired.renew(lifetime=timedelta(days=30))

    sent = json.loads(renewing.seen[0][1])
    assert declaration["path"] == client_module.RENEW_PATH
    assert disagreements(sent, schema, routing_table(), "renewCertificate") == []
    assert set(sent) == set(resolved(schema, routing_table()).get("properties", {})), (
        "every member the gate accepts is one the client sends, given every argument"
    )


def test_a_lifetime_nobody_can_have_is_refused_before_anything_is_sent(
    gate: FakeGate, paired: GateClient, renewing: Gate
) -> None:
    with pytest.raises(ValueError, match="lifetime"):
        paired.renew(lifetime=timedelta(hours=36))

    assert gate.requests == []


def test_a_revocation_that_did_not_happen_is_reported_and_the_old_identity_kept(
    paired: GateClient, renewing: Gate, client_certificate: Issued
) -> None:
    # The renewal worked and this client is the new identity, so it is reported on what comes
    # back rather than raised. The old identity is kept, because giving it up takes presenting it.
    renewing.revocation = (500, json.dumps({"error": "not saved"}))
    where = paired.credentials.directory

    renewal = paired.renew()

    assert isinstance(renewal.unrevoked, RouteRefused)
    assert renewal.retired == where.with_name("default.retired")
    assert api.load(renewal.retired).expires == "2126-08-15T09:00:00Z", "the old one, whole"
    assert api.load(where).expires == "2127-01-02T03:04:05Z"


def test_a_later_renewal_will_not_start_while_an_old_identity_waits_to_be_given_up(
    gate: FakeGate, paired: GateClient, renewing: Gate, client_certificate: Issued
) -> None:
    # It tries to give the kept one up first, presenting it, and asks for nothing while the gate
    # still admits it and will not take it back.
    renewing.revocation = (500, json.dumps({"error": "not saved"}))
    paired.renew()
    asked = len(gate.requests)

    with pytest.raises(StorageError, match="Revoke it first"):
        paired.renew()

    assert gate.requests[asked:] == [("DELETE", "/gate/certificate")]
    assert renewing.seen[-1][2] == der(client_certificate)
    assert api.load(paired.credentials.directory.with_name("default.retired"))


@pytest.mark.parametrize(
    ("status", "body"), [(200, {"ok": True}), (401, {"error": "unpaired"})]
)
def test_a_later_renewal_gives_up_what_the_last_one_kept_and_goes_on(
    gate: FakeGate,
    paired: GateClient,
    renewing: Gate,
    client_certificate: Issued,
    status: int,
    body: dict[str, Any],
) -> None:
    # Taken back now, or already refused: the operator revoked it on the Clients page, or an
    # earlier `not verified` left it refused. Either way nothing can use it, so it is forgotten.
    renewing.revocation = (500, json.dumps({"error": "not saved"}))
    paired.renew()
    retired = paired.credentials.directory.with_name("default.retired")
    asked = len(gate.requests)
    renewing.revocation = (status, json.dumps(body))

    renewal = paired.renew()

    assert renewal.retired is None
    assert not retired.exists()
    assert gate.requests[asked:] == [
        ("DELETE", "/gate/certificate"),
        ("POST", "/gate/renew"),
        ("DELETE", "/gate/certificate"),
    ]
    assert renewing.seen[-3][2] == der(client_certificate), "the kept one, presented"


def test_a_kept_identity_that_has_expired_is_forgotten_without_asking(
    tmp_path: Path, gate: FakeGate, bundle: dict[str, Any], renewing: Gate
) -> None:
    # Nothing could revoke it, and the gate prunes its record on its next write anyway.
    where = tmp_path / "kept"
    stored = credentials_module.save(where, gate.port, bundle)
    retired = tmp_path / "kept.retired"
    credentials_module.save(retired, gate.port, {**bundle, "expires": "2001-02-03T04:05:06Z"})

    GateClient(stored, connection_timeout=10.0).renew()

    assert not retired.exists()
    assert [path for _, path in gate.requests] == ["/gate/renew", "/gate/certificate"]


def test_what_a_renewal_kept_is_given_up_by_the_command_it_names(
    tmp_path: Path,
    gate: FakeGate,
    bundle: dict[str, Any],
    renewing: Gate,
    capsys: pytest.CaptureFixture[str],
) -> None:
    # The remedy `renew` prints, followed, after the operator revoked the old row on the Clients
    # page: the gate refuses the kept certificate, which counts as given up, and a renewal starts.
    renewing.revocation = (500, json.dumps({"error": "not saved"}))
    where = tmp_path / "kept"
    credentials_module.save(where, gate.port, bundle)
    assert cli.main(["renew", "--directory", str(where)]) == cli.FAILED
    retired = where.with_name("kept.retired")
    renewing.revocation = (401, json.dumps({"error": "unpaired"}))

    assert cli.main(["revoke", "--directory", str(retired)]) == 0

    assert not retired.exists()
    assert "the gate already refuses" in capsys.readouterr().out
    renewing.revocation = (200, json.dumps({"ok": True}))
    assert cli.main(["renew", "--directory", str(where)]) == 0


def test_an_old_certificate_already_refused_counts_as_given_up(
    paired: GateClient, renewing: Gate
) -> None:
    renewing.revocation = (401, json.dumps({"error": "unpaired"}))

    renewal = paired.renew()

    assert renewal.unrevoked is None
    assert renewal.retired is None


def test_a_renewal_moves_the_four_files_and_leaves_everything_else_where_it_is(
    paired: GateClient, renewing: Gate
) -> None:
    # A directory given with --directory may hold a program's own files beside the identity.
    where = paired.credentials.directory
    (where / "agent.log").write_text("kept by the program", encoding="utf-8")

    renewal = paired.renew()

    assert renewal.retired is None
    assert (where / "agent.log").read_text(encoding="utf-8") == "kept by the program"
    assert api.load(where).expires == "2127-01-02T03:04:05Z"
    assert not where.with_name("default.retired").exists()
    assert not where.with_name("default.renewing").exists()


def failing(
    monkeypatch: pytest.MonkeyPatch, directory: Path, times: int, raised: BaseException
) -> None:
    """Make writing an identity into `directory` fail, the first `times` times it is asked."""
    write = credentials_module.Prepared.write
    left = [times]

    def wrapped(self: credentials_module.Prepared, contents: dict[str, bytes]) -> None:
        if self.directory == directory and left[0] > 0:
            left[0] -= 1
            raise raised
        write(self, contents)

    monkeypatch.setattr(credentials_module.Prepared, "write", wrapped)


def test_a_copy_of_the_old_identity_that_cannot_be_made_leaves_it_in_place(
    paired: GateClient, renewing: Gate, monkeypatch: pytest.MonkeyPatch
) -> None:
    where = paired.credentials.directory
    staging = where.with_name("default.renewing")
    failing(monkeypatch, where.with_name("default.retired"), 1, OSError("disk full"))

    with pytest.raises(StorageError) as refused:
        paired.renew()

    said = str(refused.value)
    assert f"whole in {staging} and the gate admits it" in said
    assert f"still in {where} and still admitted" in said
    assert f"pykeepassgate revoke --directory {staging}" in said
    assert api.load(where).expires == "2126-08-15T09:00:00Z"
    assert api.load(staging).expires == "2127-01-02T03:04:05Z"
    assert not where.with_name("default.retired").exists()


def test_an_identity_that_cannot_be_written_over_is_put_back(
    paired: GateClient, renewing: Gate, monkeypatch: pytest.MonkeyPatch
) -> None:
    where = paired.credentials.directory
    staging = where.with_name("default.renewing")
    failing(monkeypatch, where, 1, OSError("disk full"))

    with pytest.raises(StorageError, match=f"back in {re.escape(str(where))}"):
        paired.renew()

    assert api.load(where).expires == "2126-08-15T09:00:00Z"
    assert api.load(staging).expires == "2127-01-02T03:04:05Z"
    assert not where.with_name("default.retired").exists(), (
        "a copy of what is in place is no kept identity, and would be given up as one"
    )
    with pytest.raises(StorageError, match="Finish that renewal"):
        paired.renew()


def test_an_identity_that_cannot_be_put_back_either_says_where_each_one_is(
    paired: GateClient, renewing: Gate, monkeypatch: pytest.MonkeyPatch
) -> None:
    where = paired.credentials.directory
    staging = where.with_name("default.renewing")
    retired = where.with_name("default.retired")
    failing(monkeypatch, where, 2, OSError("disk full"))

    with pytest.raises(StorageError) as refused:
        paired.renew()

    said = str(refused.value)
    assert f"{where} holds part of each" in said
    assert f"whole in {retired} and still admitted" in said
    assert f"whole in {staging}" in said
    assert api.load(retired).expires == "2126-08-15T09:00:00Z"
    assert api.load(staging).expires == "2127-01-02T03:04:05Z"


def test_an_interruption_while_the_identity_is_written_over_puts_it_back(
    paired: GateClient, renewing: Gate, monkeypatch: pytest.MonkeyPatch
) -> None:
    where = paired.credentials.directory
    failing(monkeypatch, where, 1, KeyboardInterrupt())

    with pytest.raises(KeyboardInterrupt):
        paired.renew()

    assert api.load(where).expires == "2126-08-15T09:00:00Z"
    assert api.load(where.with_name("default.renewing")).expires == "2127-01-02T03:04:05Z"


@pytest.mark.parametrize("beside", ["default.renewing", "default.retired"])
def test_a_copy_of_the_identity_in_place_is_forgotten_rather_than_given_up(
    gate: FakeGate, paired: GateClient, renewing: Gate, bundle: dict[str, Any], beside: str
) -> None:
    # What a renewal that put the old identity back, or could not take its staging copy away,
    # leaves: presenting it would revoke this client, and refusing would block it for nothing.
    copy = paired.credentials.directory.with_name(beside)
    credentials_module.save(copy, gate.port, bundle)

    renewal = paired.renew()

    assert renewal.retired is None
    assert not copy.exists()
    assert [path for _, path in gate.requests] == ["/gate/renew", "/gate/certificate"]


@pytest.mark.parametrize("beside", ["default.renewing", "default.retired"])
@pytest.mark.parametrize("dangling", [False, True], ids=["to an identity", "to nothing"])
def test_a_link_beside_the_identity_is_refused_rather_than_followed(
    tmp_path: Path,
    gate: FakeGate,
    paired: GateClient,
    renewing: Gate,
    bundle: dict[str, Any],
    beside: str,
    dangling: bool,
) -> None:
    # Planted by whoever can write the parent. Followed, one at `.renewing` would have the renewed
    # identity written over another of this user's, and one at `.retired` would have that other
    # identity revoked and its files removed; one naming nothing passed the old check and spent the
    # renewal before the swap failed.
    other = tmp_path / "other"
    credentials_module.save(other, gate.port, bundle)
    held = (other / credentials_module.IDENTITY_FILE).read_bytes()
    target = other if not dangling else tmp_path / "gone"
    if dangling:
        target.mkdir()
    directory_link(paired.credentials.directory.with_name(beside), target)
    if dangling:
        target.rmdir()

    with pytest.raises(StorageError, match="is a link"):
        paired.renew()

    assert gate.requests == []
    assert (other / credentials_module.IDENTITY_FILE).read_bytes() == held
    assert api.load(paired.credentials.directory).expires == "2126-08-15T09:00:00Z"


def test_a_renewed_identity_an_earlier_renewal_left_is_never_written_over(
    gate: FakeGate, paired: GateClient, renewing: Gate, renewed_certificate: Issued
) -> None:
    # It is admitted and its key is the only one: writing over it would ask the operator to approve
    # the same program again and leave a record nobody holds the key to.
    where = paired.credentials.directory
    staging = where.with_name("default.renewing")
    credentials_module.save(staging, gate.port, renewing.renewed)

    with pytest.raises(StorageError, match="Finish that renewal") as refused:
        paired.renew()

    assert f"pykeepassgate revoke --directory {staging}" in str(refused.value)
    assert gate.requests == []
    assert api.load(staging).expires == "2127-01-02T03:04:05Z", "left exactly as it was"
    assert api.load(where).expires == "2126-08-15T09:00:00Z"


def test_what_a_renewal_that_stopped_before_its_answer_left_is_written_over(
    paired: GateClient, renewing: Gate
) -> None:
    # Files opened and never written, which is all a renewal stopped while the operator read
    # leaves: no identity, so nothing admitted is lost by writing over them.
    staging = paired.credentials.directory.with_name("default.renewing")
    staging.mkdir()
    (staging / "client.json").write_bytes(b"")

    renewal = paired.renew()

    assert renewal.credentials.expires == "2127-01-02T03:04:05Z"
    assert not staging.exists()


def test_a_directory_that_cannot_hold_the_renewed_identity_is_refused_before_the_gate_is_asked(
    gate: FakeGate, paired: GateClient, renewing: Gate
) -> None:
    # The counterpart of pairing's: once the gate has answered, the record is saved, so a
    # directory found unusable afterwards loses an identity that was approved.
    where = paired.credentials.directory
    where.with_name("default.renewing").write_bytes(b"in the way")

    with pytest.raises(StorageError, match="cannot hold credentials"):
        paired.renew()

    assert gate.requests == []
    assert api.load(where).expires == "2126-08-15T09:00:00Z"


@pytest.mark.parametrize(
    ("status", "body", "raised"),
    [
        (403, {"error": "refused"}, RenewalRefused),
        (409, {"error": "this session"}, RenewalRefusedThisSession),
        (409, {"error": "forever"}, RenewalRefusedForever),
        (409, {"error": "too early", "notBefore": "2026-10-09T22:00:00Z"}, RenewalTooEarly),
        (504, {"error": "unanswered"}, RenewalUnanswered),
    ],
)
def test_a_renewal_that_did_not_happen_changes_nothing_stored(
    paired: GateClient,
    renewing: Gate,
    status: int,
    body: dict[str, str],
    raised: type[Exception],
) -> None:
    renewing.renewal = (status, json.dumps(body))
    where = paired.credentials.directory

    with pytest.raises(raised):
        paired.renew()

    assert api.load(where).expires == "2126-08-15T09:00:00Z"
    assert not where.with_name("default.renewing").exists(), "nothing half made is left beside it"
    assert [path for path, _, _ in renewing.seen] == ["/gate/renew"], "nothing was given up"


@pytest.mark.parametrize(
    ("word", "raised"),
    [
        ("busy", GateBusy),
        ("locked", DatabaseLocked),
        ("undecided", AuthorityUndecided),
        ("teapot", GateUnavailable),
    ],
)
def test_a_renewal_refused_for_now_is_read_as_every_route_reads_a_503(
    paired: GateClient, renewing: Gate, word: str, raised: type[GateUnavailable]
) -> None:
    # A renewal's own words are its `403`, `409` and `504`. Its `503`s are the gate's, answered
    # before anybody is asked, so they are the classes every route raises and nothing is stored.
    renewing.renewal = (503, json.dumps({"error": word}))
    where = paired.credentials.directory

    with pytest.raises(raised) as refused:
        paired.renew()

    assert type(refused.value) is raised
    assert api.load(where).expires == "2126-08-15T09:00:00Z"
    assert not where.with_name("default.renewing").exists()


def test_a_renewal_waits_out_two_authorities_on_the_budget_given_for_them(
    tmp_path: Path, gate: FakeGate, bundle: dict[str, Any], renewing: Gate,
    monkeypatch: pytest.MonkeyPatch,
) -> None:
    waited: list[float] = []
    monkeypatch.setattr(transport, "pause", waited.append)
    answers = [(503, json.dumps({"error": "undecided"})), renewing.renewal]

    def answer(path: str, body: str, client: bytes | None) -> tuple[int, str]:
        if path == "/gate/renew":
            return answers.pop(0)
        return renewing(path, body, client)

    gate.handler = answer
    gate.retry_after = "2"
    stored = credentials_module.save(tmp_path / "kept", gate.port, bundle)

    renewal = GateClient(
        stored, connection_timeout=10.0, retry_undecided_timeout=30.0
    ).renew()

    assert renewal.credentials.expires == "2127-01-02T03:04:05Z"
    assert waited == [2.0]


def test_an_approval_the_gate_holds_is_asked_for_again_until_it_is_written(
    tmp_path: Path, gate: FakeGate, bundle: dict[str, Any], renewing: Gate,
    monkeypatch: pytest.MonkeyPatch,
) -> None:
    # Without any busy budget: the operator has approved, and only asking again before
    # `heldUntil` turns that approval into a certificate.
    waited: list[float] = []
    monkeypatch.setattr(transport, "pause", waited.append)
    until = (datetime.now(UTC) + timedelta(minutes=5)).strftime("%Y-%m-%dT%H:%M:%SZ")
    held = (503, json.dumps({"error": "busy", "heldUntil": until}))
    answers = [held, held, renewing.renewal]

    def answer(path: str, body: str, client: bytes | None) -> tuple[int, str]:
        if path == "/gate/renew":
            return answers.pop(0)
        return renewing(path, body, client)

    gate.handler = answer
    gate.retry_after = "2"
    stored = credentials_module.save(tmp_path / "kept", gate.port, bundle)

    renewal = GateClient(stored, connection_timeout=10.0).renew()

    assert renewal.credentials.expires == "2127-01-02T03:04:05Z"
    assert waited == [2.0, 2.0]


def test_a_certificate_that_has_expired_is_not_offered_for_renewal(
    tmp_path: Path, gate: FakeGate, bundle: dict[str, Any], renewing: Gate
) -> None:
    # The gate renews only a certificate it still admits, so asking with an expired one would be
    # refused as a stranger; nothing is asked, and the way back is named.
    stored = credentials_module.save(
        tmp_path / "kept", gate.port, {**bundle, "expires": "2001-02-03T04:05:06Z"}
    )

    with pytest.raises(NotAdmitted, match="pair again"):
        GateClient(stored, connection_timeout=10.0).renew()

    assert gate.requests == []
    assert not (tmp_path / "kept.renewing").exists()


@pytest.mark.parametrize(
    ("given", "waited"),
    [
        (None, [transport.PAIRING_CONNECTION_TIMEOUT, transport.DEFAULT_CONNECTION_TIMEOUT]),
        (10.0, [transport.PAIRING_CONNECTION_TIMEOUT, 10.0]),
        (600.0, [600.0, 600.0]),
    ],
)
def test_a_renewal_waits_as_long_as_a_pairing_and_gives_up_on_the_ordinary_timeout(
    tmp_path: Path,
    gate: FakeGate,
    bundle: dict[str, Any],
    renewing: Gate,
    monkeypatch: pytest.MonkeyPatch,
    given: float | None,
    waited: list[float],
) -> None:
    # The renewal waits on a person, and the gate gives it the six minutes a pairing gets, since
    # its own worst case is the same 305 seconds: the screen, the dialog and its grace, the turn at
    # the writer and the write. A client that hangs up first leaves an approval saved for a
    # certificate nobody holds. Giving the old one up asks nobody, so it waits the ordinary time.
    timeouts: list[float] = []
    connect = transport.connect

    def recorded(*arguments: Any, **named: Any) -> Any:
        timeouts.append(named["connection_timeout"])
        return connect(*arguments, **named)

    monkeypatch.setattr(transport, "connect", recorded)
    stored = credentials_module.save(tmp_path / "kept", gate.port, bundle)

    client = (
        GateClient(stored) if given is None else GateClient(stored, connection_timeout=given)
    )
    client.renew()

    assert timeouts == waited
    assert timeouts[0] > 120.0 + 120.0 + 15.0 + 20.0 + 30.0


def test_a_renewal_nobody_answered_is_read_after_the_ordinary_timeout_would_have_ended(
    tmp_path: Path,
    gate: FakeGate,
    bundle: dict[str, Any],
    renewing: Gate,
    monkeypatch: pytest.MonkeyPatch,
) -> None:
    # The gate sends `504 unanswered` once its dialog's two minutes are up, which is always later
    # than the ordinary timeout after the request: scaled down, so the test does not wait minutes.
    monkeypatch.setattr(transport, "PAIRING_CONNECTION_TIMEOUT", 5.0)

    def late(path: str, body: str, client: bytes | None) -> tuple[int, str]:
        if path == "/gate/renew":
            time.sleep(1.0)
            return 504, json.dumps({"error": "unanswered"})
        return renewing(path, body, client)

    gate.handler = late
    stored = credentials_module.save(tmp_path / "kept", gate.port, bundle)

    with pytest.raises(RenewalUnanswered):
        GateClient(stored, connection_timeout=0.5).renew()


def test_a_renewal_whose_answer_outlasts_its_wait_keeps_the_old_identity(
    tmp_path: Path,
    gate: FakeGate,
    bundle: dict[str, Any],
    renewing: Gate,
    monkeypatch: pytest.MonkeyPatch,
) -> None:
    monkeypatch.setattr(transport, "PAIRING_CONNECTION_TIMEOUT", 0.5)

    def never(path: str, body: str, client: bytes | None) -> tuple[int, str]:
        time.sleep(1.5)
        return renewing(path, body, client)

    gate.handler = never
    where = tmp_path / "kept"
    stored = credentials_module.save(where, gate.port, bundle)

    with pytest.raises(TransportError):
        GateClient(stored, connection_timeout=0.25).renew()

    assert api.load(where).expires == "2126-08-15T09:00:00Z"
    assert not (tmp_path / "kept.renewing").exists()


def test_too_early_says_from_when_to_ask_again(paired: GateClient, renewing: Gate) -> None:
    refused = {"error": "too early", "notBefore": "2026-10-09T22:00:00Z"}
    renewing.renewal = (409, json.dumps(refused))

    with pytest.raises(RenewalTooEarly) as caught:
        paired.renew()

    assert caught.value.not_before == datetime(2026, 10, 9, 22, 0, 0, tzinfo=UTC)
    assert "2026-10-09T22:00:00Z" in str(caught.value)


def test_the_command_line_renews_and_says_what_it_stored(
    tmp_path: Path,
    gate: FakeGate,
    bundle: dict[str, Any],
    renewing: Gate,
    capsys: pytest.CaptureFixture[str],
) -> None:
    where = tmp_path / "kept"
    credentials_module.save(where, gate.port, bundle)

    code = cli.main(["renew", "--directory", str(where), "--lifetime", "30"])

    printed = capsys.readouterr().out
    assert code == 0
    assert "renewed CN=agent-invented" in printed
    assert "expires: 2127-01-02T03:04:05Z" in printed
    assert json.loads(renewing.seen[0][1]) == {"lifetime": "P30D"}


def test_renewing_passes_the_shared_directory_choice_to_the_library(
    tmp_path: Path,
    gate: FakeGate,
    bundle: dict[str, Any],
    monkeypatch: pytest.MonkeyPatch,
) -> None:
    # It is about the identity's own directory, which a pairing may have put somewhere shared.
    asked: list[bool] = []

    def renew(
        self: GateClient, lifetime: timedelta | None = None, allow_shared_directory: bool = False
    ) -> Any:
        asked.append(allow_shared_directory)
        raise StorageError("stop here")

    monkeypatch.setattr(GateClient, "renew", renew)
    where = tmp_path / "kept"
    credentials_module.save(where, gate.port, bundle)

    cli.main(["renew", "--directory", str(where)])
    cli.main(["renew", "--directory", str(where), "--allow-shared-directory"])

    assert asked == [False, True]


def test_the_command_line_says_how_to_give_up_what_it_could_not(
    tmp_path: Path,
    gate: FakeGate,
    bundle: dict[str, Any],
    renewing: Gate,
    capsys: pytest.CaptureFixture[str],
) -> None:
    renewing.revocation = (500, json.dumps({"error": "not saved"}))
    where = tmp_path / "kept"
    credentials_module.save(where, gate.port, bundle)

    code = cli.main(["renew", "--directory", str(where)])

    captured = capsys.readouterr()
    assert code == cli.FAILED
    assert "renewed CN=agent-invented" in captured.out, "the renewal itself worked"
    assert "pykeepassgate revoke --directory" in captured.err
    assert str(where.with_name("kept.retired")) in captured.err


def test_what_was_kept_for_a_revoked_certificate_and_could_not_be_removed_is_named(
    tmp_path: Path,
    gate: FakeGate,
    bundle: dict[str, Any],
    renewing: Gate,
    monkeypatch: pytest.MonkeyPatch,
    capsys: pytest.CaptureFixture[str],
) -> None:
    # Revoked, so nothing presents it again, and what is left is files: the library says where,
    # and the command line says to remove them, with the failure's exit code.
    forget = credentials_module.forget

    def stuck(directory: Path) -> None:
        if directory.name.endswith(".retired"):
            raise StorageError(f"{directory} could not be emptied")
        forget(directory)

    monkeypatch.setattr(credentials_module, "forget", stuck)
    where = tmp_path / "kept"
    stored = credentials_module.save(where, gate.port, bundle)

    renewal = GateClient(stored, connection_timeout=10.0).renew()

    assert renewal.unrevoked is None
    assert renewal.retired == where.with_name("kept.retired")

    credentials_module.save(where.with_name("other"), gate.port, bundle)
    assert cli.main(["renew", "--directory", str(where.with_name("other"))]) == cli.FAILED
    said = capsys.readouterr().err
    assert "is revoked, and what was stored for it could not be removed from" in said
    assert "remove that directory" in said


def test_the_command_line_does_not_call_a_certificate_the_gate_refuses_admitted(
    tmp_path: Path,
    gate: FakeGate,
    bundle: dict[str, Any],
    renewing: Gate,
    capsys: pytest.CaptureFixture[str],
) -> None:
    # `not verified` is a revocation KeePass holds in memory: the certificate is refused already,
    # and only whether the file keeps that is open, which is the operator's to settle.
    renewing.revocation = (500, json.dumps({"error": "not verified"}))
    where = tmp_path / "kept"
    credentials_module.save(where, gate.port, bundle)

    code = cli.main(["renew", "--directory", str(where)])

    said = capsys.readouterr().err
    assert code == cli.FAILED
    assert "is refused, and KeePass may not have that in its file" in said
    assert "still admitted" not in said
    assert f"pykeepassgate revoke --directory {where.with_name('kept.retired')}" in said


@pytest.mark.parametrize(
    ("status", "body", "said"),
    [
        (403, {"error": "refused"}, "RenewalRefused: the operator refused this renewal, and may"),
        (409, {"error": "this session"}, "after KeePass has been restarted"),
        (409, {"error": "forever"}, "allowed renewal again on the KPG tab's clients page"),
        (409, {"error": "too early", "notBefore": "2026-10-09T22:00:00Z"}, "2026-10-09T22:00:00Z"),
        (504, {"error": "unanswered"}, "ask again"),
    ],
)
def test_the_command_line_says_what_a_refusal_means(
    tmp_path: Path,
    gate: FakeGate,
    bundle: dict[str, Any],
    renewing: Gate,
    capsys: pytest.CaptureFixture[str],
    status: int,
    body: dict[str, str],
    said: str,
) -> None:
    renewing.renewal = (status, json.dumps(body))
    where = tmp_path / "kept"
    credentials_module.save(where, gate.port, bundle)

    code = cli.main(["renew", "--directory", str(where)])

    assert code == cli.FAILED
    assert said in capsys.readouterr().err
