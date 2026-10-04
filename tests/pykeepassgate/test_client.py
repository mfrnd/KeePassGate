"""A paired client against a gate, and what every refusal means to whoever called it."""

from __future__ import annotations

import dataclasses
import json
import math
import re
import socket
from datetime import UTC, datetime, timedelta
from pathlib import Path
from typing import Any, cast

import pytest

from conftest import FakeGate, Issued
from pykeepassgate import credentials as credentials_module
from pykeepassgate import sni, transport
from pykeepassgate.client import GateClient
from pykeepassgate.errors import (
    AuthorityUndecided,
    DatabaseLocked,
    GateBusy,
    GateUnavailable,
    NotAdmitted,
    ProtocolError,
    RouteRefused,
    StorageError,
    TransportError,
)


@pytest.fixture
def paired(tmp_path: Path, bundle: dict[str, Any], gate: FakeGate) -> GateClient:
    """A client that has been through pairing with this gate."""
    stored = credentials_module.save(tmp_path / "kept", gate.port, bundle)
    return GateClient(stored, connection_timeout=10.0)


def test_the_status_route_says_what_the_gate_and_the_database_are(
    gate: FakeGate, paired: GateClient
) -> None:
    gate.handler = lambda path, body, client: (
        200,
        json.dumps(
            {"ok": True, "database": "abcdef01", "version": "0.1.0", "revision": "0123abcd"}
        ),
    )

    status = paired.status()

    assert (status.ok, status.database, status.version, status.revision) == (
        True, "abcdef01", "0.1.0", "0123abcd"
    )
    assert gate.requests == [("GET", "/gate/status")]


def test_a_build_carrying_no_commit_is_read_as_an_empty_revision(
    gate: FakeGate, paired: GateClient
) -> None:
    # Empty is an answer, and the member is still required: a gate leaving it out is answering in
    # a form the document does not declare.
    gate.handler = lambda path, body, client: (
        200,
        json.dumps({"ok": True, "database": "abcdef01", "version": "0.1.0", "revision": ""}),
    )

    assert paired.status().revision == ""


def test_a_client_revokes_itself_with_a_request_that_names_nothing(
    gate: FakeGate, paired: GateClient
) -> None:
    # No body: the certificate on the connection is what is revoked, so there is nothing to name
    # and no way to name somebody else.
    bodies: list[str] = []

    def answer(path: str, body: str, client: bytes | None) -> tuple[int, str]:
        bodies.append(body)
        return 200, json.dumps({"ok": True})

    gate.handler = answer

    paired.revoke_certificate()

    assert gate.requests == [("DELETE", "/gate/certificate")]
    assert bodies == [""]


@pytest.mark.parametrize(
    ("status", "error", "raised"),
    [
        (401, "unpaired", NotAdmitted),
        (500, "not saved", RouteRefused),
        (500, "not verified", RouteRefused),
        (500, "internal error", RouteRefused),
        (503, "busy", GateBusy),
    ],
)
def test_a_revocation_that_did_not_happen_says_what_was_left_behind(
    gate: FakeGate, paired: GateClient, status: int, error: str, raised: type[Exception]
) -> None:
    # The word is what tells `not saved`, which kept nothing and may be asked again, from `not
    # verified`, which is already refusing this certificate in KeePass's memory.
    gate.handler = lambda path, body, client: (status, json.dumps({"error": error}))

    with pytest.raises(raised) as caught:
        paired.revoke_certificate()

    if isinstance(caught.value, RouteRefused):
        assert (caught.value.status, caught.value.error) == (status, error)


def test_a_busy_gate_is_waited_out_before_a_revocation_is_given_up(
    gate: FakeGate, tmp_path: Path, bundle: dict[str, Any]
) -> None:
    # A `503` is answered before anything is written, so asking again cannot revoke twice.
    answers = [(503, json.dumps({"error": "busy"})), (200, json.dumps({"ok": True}))]
    gate.retry_after = "0"
    gate.handler = lambda path, body, client: answers.pop(0)
    stored = credentials_module.save(tmp_path / "kept", gate.port, bundle)
    client = GateClient(stored, connection_timeout=10.0, retry_busy_timeout=5.0)

    client.revoke_certificate()

    assert gate.requests == [("DELETE", "/gate/certificate")] * 2


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
        (503, "undecided", AuthorityUndecided),
        (503, "teapot", GateUnavailable),
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

    assert type(raised.value) is expected, "the class that names the word, and no other"
    if isinstance(raised.value, RouteRefused):
        assert (raised.value.status, raised.value.error) == (status, error)


@pytest.mark.parametrize("error", ["locked", "busy", "undecided", "teapot"])
def test_a_503_keeps_the_word_and_the_delay_the_gate_gave(
    gate: FakeGate, paired: GateClient, error: str
) -> None:
    # The word is what a caller catching the base reads, which a word this client does not know
    # needs most of all: it is the whole of what that refusal says.
    gate.handler = lambda path, body, client: (503, json.dumps({"error": error}))
    gate.retry_after = "2"

    with pytest.raises(GateUnavailable) as raised:
        paired.status()

    assert (raised.value.error, raised.value.retry_after) == (error, 2.0)


@pytest.mark.parametrize("body", ["{}", "", "not json at all"], ids=["no word", "empty", "text"])
def test_a_503_that_names_no_word_is_the_family_itself(
    gate: FakeGate, paired: GateClient, body: str
) -> None:
    gate.handler = lambda path, sent, client: (503, body)

    with pytest.raises(GateUnavailable) as raised:
        paired.status()

    assert type(raised.value) is GateUnavailable
    assert raised.value.error == ""
    assert "named no word" in str(raised.value)


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

    with pytest.raises(DatabaseLocked, match="responder identity") as refused:
        paired.entry("abc")

    assert gate.requests == [], "nothing reached the gate at all"
    assert refused.value.error == "locked", "the same word a `503 locked` carries"


def test_a_revoked_client_and_one_that_never_paired_look_the_same(
    gate: FakeGate, paired: GateClient
) -> None:
    # The record is read from the live database on every connection and never cached, so revocation
    # is immediate and, from here, indistinguishable from never having been admitted. That is the
    # point rather than an omission.
    gate.handler = lambda path, body, client: (401, json.dumps({"error": "unpaired"}))

    with pytest.raises(NotAdmitted, match="never admitted, it was revoked, or it has expired"):
        paired.status()


def test_every_setting_is_passed_by_name(tmp_path: Path, bundle: dict[str, Any]) -> None:
    # A setting added in the middle once changed what the fourth argument meant, with nothing to
    # say so; keyword only, a setting added later cannot.
    stored = credentials_module.save(tmp_path / "kept", 45678, bundle)

    with pytest.raises(TypeError):
        cast(Any, GateClient)(stored, 10.0)
    with pytest.raises(TypeError):
        cast(Any, GateClient.open)(tmp_path / "kept", "default", "127.0.0.1")


def test_a_certificate_that_has_expired_is_named_as_expired_before_anything_is_dialed(
    tmp_path: Path, bundle: dict[str, Any], gate: FakeGate
) -> None:
    # The gate refuses one as it refuses a stranger, so the `401` would read as a missing record
    # while the record is there. Pairing stored when it expires, which says it by name.
    stored = credentials_module.save(
        tmp_path / "kept", gate.port, {**bundle, "expires": "2001-02-03T04:05:06Z"}
    )

    expired = r"expired at 2001-02-03T04:05:06Z.*pair again"
    with pytest.raises(NotAdmitted, match=expired) as refused:
        GateClient(stored, connection_timeout=10.0).status()

    assert gate.names == [], "nothing was dialed to learn it"
    # The same refusal is met whatever was asked, so it says what to do for giving the identity up
    # as well as for going on with it.
    assert "`revoke` does, takes it away" in str(refused.value)


def test_a_certificate_expired_only_by_a_clock_ahead_of_the_gate_s_is_still_offered(
    tmp_path: Path, bundle: dict[str, Any], gate: FakeGate
) -> None:
    # The gate judges by its own clock, and serves, and renews, a certificate it holds current: a
    # minute past by this machine's clock may be a minute before by the gate's.
    status = {"ok": True, "database": "abcdef01", "version": "0.1.0", "revision": ""}
    gate.handler = lambda path, body, client: (200, json.dumps(status))
    moment = datetime.now(UTC) - timedelta(minutes=1)
    stored = credentials_module.save(
        tmp_path / "kept", gate.port, {**bundle, "expires": f"{moment:%Y-%m-%dT%H:%M:%SZ}"}
    )

    assert stored.expired, "whoami still says so"
    assert GateClient(stored, connection_timeout=10.0).status().ok
    assert gate.names != [], "the gate was asked"


@pytest.mark.parametrize("expires", ["", "whenever", "2001-02-03"], ids=["none", "a word", "a day"])
def test_an_expiry_this_client_cannot_read_leaves_the_gate_to_say(
    expires: str, tmp_path: Path, bundle: dict[str, Any], gate: FakeGate
) -> None:
    # Refusing on a guess would refuse for nothing: the gate decides, as it always did.
    stored = credentials_module.save(tmp_path / "kept", gate.port, {**bundle, "expires": expires})
    gate.handler = lambda path, body, client: (200, "{}")

    GateClient(stored, connection_timeout=10.0).get("/gate/status")

    assert gate.requests == [("GET", "/gate/status")]


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
    client = GateClient(stored, connection_timeout=10.0)
    gate.handler = lambda path, body, certificate: (
        200,
        json.dumps(
            {"ok": True, "database": "abcdef01", "version": "0.1.0", "revision": "0123abcd"}
        ),
    )

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

    assert GateClient.open(tmp_path / "kept", connection_timeout=3.5)._connection_timeout == 3.5


def test_a_client_dials_loopback_unless_it_is_told_otherwise(
    tmp_path: Path, bundle: dict[str, Any], gate: FakeGate
) -> None:
    stored = credentials_module.save(tmp_path / "kept", gate.port, bundle)

    assert GateClient(stored).host == transport.LOOPBACK


def test_a_client_dials_the_host_it_was_given(
    tmp_path: Path, bundle: dict[str, Any], gate: FakeGate
) -> None:
    # The gate binds to loopback, so a tunnel is what puts it anywhere else, and the tunnel's own
    # end is an address like any other. `localhost` resolves to the same socket here, which is what
    # makes this provable without one: the string travels the whole way down or the connection is
    # made to the default instead and this passes for the wrong reason. So the name is different
    # from the default and the request still arrives.
    stored = credentials_module.save(tmp_path / "kept", gate.port, bundle)
    client = GateClient(stored, connection_timeout=10.0, host="localhost")
    gate.handler = lambda path, body, client: (200, "{}")

    client.get("/gate/status")

    assert client.host == "localhost"
    assert gate.requests == [("GET", "/gate/status")]


def test_a_client_that_dials_nowhere_says_where_it_tried(
    tmp_path: Path, bundle: dict[str, Any], gate: FakeGate
) -> None:
    stored = credentials_module.save(tmp_path / "kept", gate.port, bundle)
    client = GateClient(stored, connection_timeout=10.0, host="127.0.0.2")

    with pytest.raises(TransportError, match=re.escape("127.0.0.2")):
        client.status()


class Dialing:
    """A dial that reaches this gate whatever host it is asked for, as a relay would."""

    def __init__(self, gate: FakeGate) -> None:
        self.gate = gate
        self.asked: list[tuple[str, int, float]] = []

    def __call__(self, host: str, port: int, timeout: float) -> socket.socket:
        self.asked.append((host, port, timeout))
        return socket.create_connection((transport.LOOPBACK, self.gate.port), timeout=timeout)


def test_a_client_connects_through_the_dial_it_was_given(
    tmp_path: Path, bundle: dict[str, Any], gate: FakeGate
) -> None:
    # The host is the dial's to read, so it need not be anything this machine resolves.
    stored = credentials_module.save(tmp_path / "kept", gate.port, bundle)
    gate.handler = lambda path, body, client: (200, "{}")
    dialing = Dialing(gate)

    client = GateClient(stored, connection_timeout=10.0, host="gate.invalid", dial=dialing)
    client.get("/gate/status")
    GateClient.open(stored.directory, host="gate.invalid", dial=dialing).get("/gate/status")

    assert dialing.asked == [("gate.invalid", gate.port, 10.0), ("gate.invalid", gate.port, 120.0)]
    assert gate.requests == [("GET", "/gate/status")] * 2


def test_a_client_dials_with_the_standard_library_unless_told_otherwise(
    tmp_path: Path, bundle: dict[str, Any], gate: FakeGate
) -> None:
    stored = credentials_module.save(tmp_path / "kept", gate.port, bundle)

    assert GateClient(stored)._dial is transport.create_connection
    assert transport.create_connection.__module__ == transport.__name__


def test_a_dial_that_could_not_be_called_is_refused_before_anything_is_dialed(
    tmp_path: Path, bundle: dict[str, Any], gate: FakeGate
) -> None:
    stored = credentials_module.save(tmp_path / "kept", gate.port, bundle)
    given = cast(Any, ("127.0.0.1", gate.port))

    with pytest.raises(ValueError, match="dial"):
        GateClient(stored, dial=given)

    with pytest.raises(ValueError, match="dial"):
        GateClient.open(stored.directory, dial=given)

    assert gate.names == []


def test_a_client_opened_from_disk_takes_a_host_too(
    tmp_path: Path, bundle: dict[str, Any], gate: FakeGate
) -> None:
    credentials_module.save(tmp_path / "kept", gate.port, bundle)

    client = GateClient.open(tmp_path / "kept", host="localhost")

    assert client.host == "localhost", "the identity says which gate, the caller says where it is"


def test_an_answer_over_the_client_s_cap_fails_whole(
    tmp_path: Path, bundle: dict[str, Any], gate: FakeGate
) -> None:
    # The cap a client was built with is the one its connections read under, rather than the
    # module's default: the answer here is far under 4 MiB and over the number given.
    stored = credentials_module.save(tmp_path / "kept", gate.port, bundle)
    gate.handler = lambda path, body, client: (200, json.dumps({"groups": ["x" * 4096]}))

    # Not a connection that failed, which is what `TransportError` says and what a caller retries:
    # the same answer would meet the same cap. The message names what raises it.
    with pytest.raises(ProtocolError, match=r"over 1024 bytes.*max_response_bytes"):
        GateClient(stored, connection_timeout=10.0, max_response_bytes=1024).groups()


def test_a_cap_raised_past_an_answer_takes_it(
    tmp_path: Path, bundle: dict[str, Any], gate: FakeGate
) -> None:
    # What a caller with a database too large for the default does, since the gate does not page.
    credentials_module.save(tmp_path / "kept", gate.port, bundle)
    gate.handler = lambda path, body, client: (200, json.dumps({"ok": True, "pad": "x" * 4096}))

    client = GateClient.open(
        tmp_path / "kept", connection_timeout=10.0, max_response_bytes=8192
    )

    assert client.get("/gate/status")["ok"] is True


@pytest.mark.parametrize("cap", [0, -1])
def test_a_cap_nothing_fits_under_is_refused_before_anything_is_dialed(
    cap: int, tmp_path: Path, bundle: dict[str, Any], gate: FakeGate
) -> None:
    stored = credentials_module.save(tmp_path / "kept", gate.port, bundle)

    with pytest.raises(ValueError, match="positive number of bytes"):
        GateClient(stored, max_response_bytes=cap)

    assert gate.requests == []


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


class TestWaitingOutARefusal:
    """The three retry budgets, which are `None` unless a caller says otherwise."""

    @pytest.fixture
    def waited(self, monkeypatch: pytest.MonkeyPatch) -> list[float]:
        """Every delay that would have been slept, without sleeping any of them."""
        recorded: list[float] = []
        monkeypatch.setattr(transport, "pause", recorded.append)
        return recorded

    @staticmethod
    def _refusing(gate: FakeGate, times: int, error: str = "busy") -> None:
        """Refuse that many requests with that `503`, then answer the next one."""
        left = [times]

        def answer(path: str, body: str, client: bytes | None) -> tuple[int, str]:
            if left[0] > 0:
                left[0] -= 1
                return 503, json.dumps({"error": error})

            return 200, json.dumps(
                {"ok": True, "database": "abcdef01", "version": "0.1.0", "revision": "0123abcd"}
            )

        gate.handler = answer
        gate.retry_after = "2"

    @staticmethod
    def _client(
        stored: credentials_module.Credentials,
        retry_busy_timeout: float | None = None,
        retry_locked_timeout: float | None = None,
        retry_undecided_timeout: float | None = None,
    ) -> GateClient:
        return GateClient(
            stored,
            connection_timeout=10.0,
            retry_busy_timeout=retry_busy_timeout,
            retry_locked_timeout=retry_locked_timeout,
            retry_undecided_timeout=retry_undecided_timeout,
        )

    def test_a_client_with_no_budget_raises_the_refusal_exactly_as_it_always_did(
        self, gate: FakeGate, paired: GateClient, waited: list[float]
    ) -> None:
        self._refusing(gate, 1)

        with pytest.raises(GateBusy):
            paired.status()

        assert len(gate.requests) == 1, "it asked once and reported what it was told"
        assert waited == []

    def test_a_busy_budget_asks_again_until_the_gate_stops_refusing(
        self, tmp_path: Path, bundle: dict[str, Any], gate: FakeGate, waited: list[float]
    ) -> None:
        stored = credentials_module.save(tmp_path / "kept", gate.port, bundle)
        self._refusing(gate, 2)

        assert self._client(stored, retry_busy_timeout=30.0).status().ok
        assert len(gate.requests) == 3
        assert waited == [2.0, 2.0], "it waited what the gate asked for, twice"

    def test_the_delay_is_the_gate_s_rather_than_one_this_client_invented(
        self, tmp_path: Path, bundle: dict[str, Any], gate: FakeGate, waited: list[float]
    ) -> None:
        stored = credentials_module.save(tmp_path / "kept", gate.port, bundle)
        self._refusing(gate, 1)
        gate.retry_after = "5"

        assert self._client(stored, retry_busy_timeout=30.0).status().ok
        assert waited == [5.0]

    def test_a_retry_is_a_new_connection_under_a_new_name(
        self, tmp_path: Path, bundle: dict[str, Any], gate: FakeGate, waited: list[float]
    ) -> None:
        # The gate serves one request per connection, so a retry has to be a new one; the name is
        # minted per connection, so an observer watching a client wait out a busy KeePass sees
        # three different names rather than one repeated.
        stored = credentials_module.save(tmp_path / "kept", gate.port, bundle)
        self._refusing(gate, 2)

        assert self._client(stored, retry_busy_timeout=30.0).status().ok
        assert len(gate.names) == 3
        assert len(set(gate.names)) == 3

    def test_a_busy_budget_does_not_wait_out_a_lock(
        self, tmp_path: Path, bundle: dict[str, Any], gate: FakeGate, waited: list[float]
    ) -> None:
        # The point of there being two. A lock waits for a person to come back to their desk and
        # can arrive on its own; a busy KeePass is in the middle of something and will finish.
        stored = credentials_module.save(tmp_path / "kept", gate.port, bundle)
        self._refusing(gate, 1, "locked")

        with pytest.raises(DatabaseLocked):
            self._client(stored, retry_busy_timeout=30.0).status()

        assert len(gate.requests) == 1
        assert waited == []

    def test_a_locked_budget_waits_out_a_lock_and_not_a_busy_gate(
        self, tmp_path: Path, bundle: dict[str, Any], gate: FakeGate, waited: list[float]
    ) -> None:
        stored = credentials_module.save(tmp_path / "kept", gate.port, bundle)
        self._refusing(gate, 1, "locked")

        assert self._client(stored, retry_locked_timeout=30.0).status().ok
        assert waited == [2.0]

        self._refusing(gate, 1, "busy")
        with pytest.raises(GateBusy):
            self._client(stored, retry_locked_timeout=30.0).status()

    def test_an_undecided_database_is_waited_out_on_its_own_budget(
        self, tmp_path: Path, bundle: dict[str, Any], gate: FakeGate, waited: list[float]
    ) -> None:
        # Not on the busy one, which used to be spent on it: `undecided` waits for a person to
        # choose between two authorities, and may end with this client's not kept.
        stored = credentials_module.save(tmp_path / "kept", gate.port, bundle)
        self._refusing(gate, 1, "undecided")

        with pytest.raises(AuthorityUndecided):
            self._client(stored, retry_busy_timeout=30.0, retry_locked_timeout=30.0).status()
        assert waited == []

        self._refusing(gate, 1, "undecided")
        assert self._client(stored, retry_undecided_timeout=30.0).status().ok
        assert waited == [2.0]

    def test_a_word_no_budget_names_is_never_waited_out(
        self, tmp_path: Path, bundle: dict[str, Any], gate: FakeGate, waited: list[float]
    ) -> None:
        # Nothing here knows what ends it, so no budget a caller gave was given for it.
        stored = credentials_module.save(tmp_path / "kept", gate.port, bundle)
        self._refusing(gate, 1, "teapot")
        everything = self._client(
            stored,
            retry_busy_timeout=300.0,
            retry_locked_timeout=300.0,
            retry_undecided_timeout=300.0,
        )

        with pytest.raises(GateUnavailable) as refused:
            everything.status()

        assert refused.value.error == "teapot"
        assert len(gate.requests) == 1
        assert waited == []

    def test_a_locked_budget_waits_out_the_identity_a_locked_database_answers_with(
        self, tmp_path: Path, bundle: dict[str, Any], gate: FakeGate, responder: Issued,
        waited: list[float],
    ) -> None:
        # The safest refusal there is to repeat: nothing was sent, because a responder is refused
        # before the request. Nothing says when to come back either, so the fallback covers it.
        stored = credentials_module.save(tmp_path / "kept", gate.port, bundle)
        gate.serve_with(responder)

        with pytest.raises(DatabaseLocked):
            self._client(stored, retry_locked_timeout=5.0).status()

        assert waited == [transport.FALLBACK_RETRY_AFTER, transport.FALLBACK_RETRY_AFTER]

    def test_a_refusal_waiting_cannot_fix_is_not_sent_round_a_loop(
        self, tmp_path: Path, bundle: dict[str, Any], gate: FakeGate, waited: list[float]
    ) -> None:
        stored = credentials_module.save(tmp_path / "kept", gate.port, bundle)
        gate.handler = lambda path, body, client: (500, json.dumps({"error": "internal error"}))
        gate.retry_after = "2"

        with pytest.raises(RouteRefused):
            self._client(stored, retry_busy_timeout=30.0, retry_locked_timeout=30.0).status()

        assert len(gate.requests) == 1
        assert waited == []

    def test_one_call_can_ask_for_a_budget_the_client_was_not_built_with(
        self, gate: FakeGate, paired: GateClient, waited: list[float]
    ) -> None:
        self._refusing(gate, 1)

        assert paired.get("/gate/status", retry_busy_timeout=30.0)["ok"] is True
        assert waited == [2.0]

    def test_one_call_can_take_away_a_budget_the_client_was_built_with(
        self, tmp_path: Path, bundle: dict[str, Any], gate: FakeGate, waited: list[float]
    ) -> None:
        # Which is why "not given" cannot be `None`: `None` is a decision, and a call has to be
        # able to make it against a client that was built to wait.
        stored = credentials_module.save(tmp_path / "kept", gate.port, bundle)
        self._refusing(gate, 1)
        client = self._client(stored, retry_busy_timeout=30.0)

        with pytest.raises(GateBusy):
            client.get("/gate/status", retry_busy_timeout=None)

        assert waited == []


@pytest.mark.parametrize(
    ("setting", "value"),
    [
        ("connection_timeout", 0.0),
        ("connection_timeout", math.nan),
        ("connection_timeout", -1.0),
        ("retry_busy_timeout", math.nan),
        ("retry_locked_timeout", -1.0),
        ("retry_locked_timeout", math.inf),
        ("retry_undecided_timeout", math.nan),
        ("max_response_bytes", 1.5),
    ],
)
def test_a_client_given_a_setting_that_cannot_work_is_refused_before_anything_is_dialed(
    setting: str, value: float, tmp_path: Path, bundle: dict[str, Any], gate: FakeGate
) -> None:
    # The command line refuses each of these as an argument, and a program has to meet the same
    # refusal: a NaN budget used to wait out a `503` forever, and a bad socket timeout left the
    # first call as a `ValueError` or an `OverflowError` from inside `socket`.
    stored = credentials_module.save(tmp_path / "kept", gate.port, bundle)
    given: dict[str, Any] = {setting: value}

    with pytest.raises(ValueError, match=setting):
        GateClient(stored, **given)

    with pytest.raises(ValueError, match=setting):
        GateClient.open(tmp_path / "kept", **given)

    assert gate.names == []


def test_a_budget_given_for_one_call_is_held_to_the_same_rule(
    gate: FakeGate, paired: GateClient
) -> None:
    with pytest.raises(ValueError, match="retry_busy_timeout"):
        paired.get("/gate/status", retry_busy_timeout=math.nan)

    with pytest.raises(ValueError, match="retry_locked_timeout"):
        paired.head("/gate/status", retry_locked_timeout=-1.0)

    with pytest.raises(ValueError, match="retry_undecided_timeout"):
        paired.send("GET", "/gate/status", retry_undecided_timeout=math.inf)

    assert gate.names == []


def test_credentials_naming_a_port_tcp_does_not_have_make_no_client(
    tmp_path: Path, bundle: dict[str, Any], gate: FakeGate
) -> None:
    # `load` refuses such a file; this is a `Credentials` a program built for itself.
    stored = credentials_module.save(tmp_path / "kept", gate.port, bundle)

    with pytest.raises(ValueError, match="port"):
        GateClient(dataclasses.replace(stored, port=70000))


def test_an_identity_cut_short_opens_no_client_and_says_which_file(
    tmp_path: Path, bundle: dict[str, Any], gate: FakeGate
) -> None:
    # `client.json` reads and names its port, so `load` finds nothing wrong: the key beside it is
    # read first by `ssl`, as the client is built, and used to leave as an `SSLError`.
    stored = credentials_module.save(tmp_path / "kept", gate.port, bundle)
    stored.identity.write_bytes(stored.identity.read_bytes()[:300])

    with pytest.raises(StorageError, match=r"identity\.pem"):
        GateClient.open(tmp_path / "kept")


@pytest.mark.parametrize(
    "answer",
    [
        {"ok": "false", "database": "abcdef01", "version": "0.1.0", "revision": "0123abcd"},
        {"ok": True, "database": None, "version": "0.1.0", "revision": "0123abcd"},
        {"ok": True, "database": "abcdef01", "revision": "0123abcd"},
        {"ok": True, "database": "abcdef01", "version": "0.1.0"},
        {"ok": True, "database": "abcdef01", "version": "0.1.0", "revision": None},
    ],
    ids=["ok as text", "a null database", "no version", "no revision", "a null revision"],
)
def test_a_status_this_client_cannot_read_is_a_protocol_error(
    answer: dict[str, Any], gate: FakeGate, paired: GateClient
) -> None:
    # `bool` read the text "false" as true, so a script reading the exit code was told the gate
    # was serving, and `str` made a `null` database the word "None".
    gate.handler = lambda path, body, client: (200, json.dumps(answer))

    with pytest.raises(ProtocolError, match="status"):
        paired.status()
