"""One connection, one request, and what happens when any of that fails.

Half of these drive a real TLS 1.3 server, because the parts of this worth doubting are the parts
`ssl` does: whether the rotating name reaches the server as SNI, whether the certificate is readable
before the body is written, and whether loading one authority really is a pin.
"""

from __future__ import annotations

import ssl
import time
from collections.abc import Callable
from pathlib import Path
from typing import Any

import pytest

from conftest import RESPONDER_CLAIM, Authority, FakeGate, Issued
from pykeepassgate import transport
from pykeepassgate.errors import DatabaseLocked, ProtocolError, TransportError

Answers = Callable[[int, "dict[str, Any] | str"], Any]


class TestResponse:
    def test_a_body_that_is_not_json_is_a_refusal_rather_than_a_crash(self) -> None:
        with pytest.raises(ProtocolError, match="not JSON"):
            transport.Response(200, "not json").json()

    def test_json_that_is_not_an_object_is_refused_too(self) -> None:
        with pytest.raises(ProtocolError, match="not an object"):
            transport.Response(200, "[1, 2]").json()

    def test_the_word_a_refusal_carries_is_readable(self) -> None:
        assert transport.Response(503, '{"error":"locked"}').error() == "locked"

    def test_a_refusal_with_no_word_reads_as_no_word(self) -> None:
        # The gate always sends one, but a client that assumed so would raise the wrong thing at
        # whatever else answered the socket.
        assert transport.Response(500, "").error() == ""
        assert transport.Response(500, '{"ok":true}').error() == ""


class TestParsing:
    def test_a_status_line_and_a_body_are_all_that_is_read(self) -> None:
        raw = b"HTTP/1.1 200 OK\r\nContent-Length: 9\r\n\r\n{\"ok\":1}\n"
        reply = transport._parse(raw)

        assert reply.status == 200
        assert reply.body == '{"ok":1}\n'

    def test_a_connection_closed_without_an_answer_says_so(self) -> None:
        with pytest.raises(TransportError, match="without an answer"):
            transport._parse(b"")

    def test_something_that_is_not_a_status_line_is_refused(self) -> None:
        with pytest.raises(ProtocolError, match="status line"):
            transport._parse(b"hello there\r\n\r\n")

    def test_a_status_that_is_not_a_number_is_refused(self) -> None:
        with pytest.raises(ProtocolError, match="not a status"):
            transport._parse(b"HTTP/1.1 fine OK\r\n\r\n")

    def test_an_answer_with_no_header_block_is_refused(self) -> None:
        with pytest.raises(ProtocolError, match="header block"):
            transport._parse(b"HTTP/1.1 200 OK\r\n")


class TestConnecting:
    def test_nothing_listening_is_a_transport_error_rather_than_an_os_error(self) -> None:
        context = transport.unpaired_context()

        # Port 1 on loopback, which nothing this test could be racing has any reason to hold.
        with pytest.raises(TransportError, match="nothing is listening"), transport.connect(
            1, context, "x.y.kpg", timeout=2.0
        ):
            pass  # pragma: no cover, the connect is what raises

    def test_the_name_this_client_chose_is_what_the_gate_sees(
        self, gate: FakeGate, answers: Answers
    ) -> None:
        gate.handler = answers(200, {"ok": True})
        context = transport.unpaired_context()

        with transport.connect(gate.port, context, "abc.def.kpg", timeout=10.0) as connection:
            connection.send("GET", "/gate/status")

        assert gate.names == ["abc.def.kpg"]

    def test_the_certificate_is_readable_before_the_body_is_written(
        self, gate: FakeGate, host: Issued, answers: Answers
    ) -> None:
        # The whole reason this is not `http.client`: the pairing body is a proof over these bytes.
        gate.handler = answers(200, {"ok": True})
        context = transport.unpaired_context()

        with transport.connect(gate.port, context, "abc.def.kpg", timeout=10.0) as connection:
            assert connection.server_certificate == host.der

    def test_a_request_and_its_answer_survive_the_round_trip(
        self, gate: FakeGate, answers: Answers
    ) -> None:
        gate.handler = answers(201, {"read": "it"})
        context = transport.unpaired_context()

        with transport.connect(gate.port, context, "abc.def.kpg", timeout=10.0) as connection:
            reply = connection.send("POST", "/pair/hello", '{"proof":"x"}')

        assert (reply.status, reply.json()) == (201, {"read": "it"})
        assert gate.requests == [("POST", "/pair/hello")]

    def test_nothing_older_than_tls_13_is_offered(self, gate: FakeGate, answers: Answers) -> None:
        gate.handler = answers(200, {"ok": True})
        context = transport.unpaired_context()

        with transport.connect(gate.port, context, "abc.def.kpg", timeout=10.0) as connection:
            assert connection._socket.version() == "TLSv1.3"


class TestPinning:
    def test_a_paired_client_reaches_a_gate_serving_under_its_own_authority(
        self, gate: FakeGate, authority: Authority, client_certificate: Issued, answers: Answers
    ) -> None:
        gate.handler = answers(200, {"ok": True})
        context = transport.paired_context(authority.pem, client_certificate.pem)

        with transport.connect(gate.port, context, "abc.def.kpg", timeout=10.0) as connection:
            assert connection.send("GET", "/gate/status").status == 200

    def test_a_certificate_from_a_foreign_authority_fails_the_handshake(
        self, gate: FakeGate, tmp_path: Path, client_certificate: Issued
    ) -> None:
        # The plugin's side of this needed the chain's last element compared byte for byte, because
        # the platform's chain builder accepts a foreign anchor from its extra store. Here the trust
        # store holds one certificate and nothing else, so the handshake is where it ends.
        stranger = Authority(tmp_path / "stranger")
        context = transport.paired_context(stranger.pem, client_certificate.pem)

        with pytest.raises(TransportError, match="handshake failed"), transport.connect(
            gate.port, context, "abc.def.kpg", timeout=10.0
        ):
            pass  # pragma: no cover, the handshake is what raises

    def test_the_rotating_name_is_not_checked_against_the_certificate(
        self, gate: FakeGate, authority: Authority, client_certificate: Issued, answers: Answers
    ) -> None:
        # The certificate says `CN=host` and the name says something else on every connection. A
        # client that checked the name would refuse every honest gate.
        gate.handler = answers(200, {"ok": True})
        context = transport.paired_context(authority.pem, client_certificate.pem)

        assert context.check_hostname is False
        assert context.verify_mode == ssl.CERT_REQUIRED

        with transport.connect(gate.port, context, "nothing.like.it.kpg", timeout=10.0) as open_:
            assert open_.send("GET", "/gate/status").status == 200


class TestTheResponder:
    """The identity a locked database answers with, and what this client will do with it."""

    def test_the_marker_is_the_one_the_plugin_writes(self) -> None:
        # Written down on both sides rather than shared, because a client reading a different
        # string reads no marker at all and finds every certificate innocent. The value comes
        # from `AuthorityIssuer.ResponderClaim`; regenerate it from there and never from here.
        assert transport.RESPONDER_CLAIM == RESPONDER_CLAIM

    def test_a_marked_certificate_is_recognized(self, responder: Issued) -> None:
        assert transport._responder(responder.der)

    def test_the_certificate_an_open_database_answers_with_is_not(self, host: Issued) -> None:
        # It carries no subject alternative name at all, which is the ordinary case and has to
        # cost nothing: a client that took this for a responder would refuse every honest gate.
        assert not transport._responder(host.der)

    def test_bytes_that_are_not_a_certificate_are_not_one(self) -> None:
        # Unreachable through a handshake, since the peer's certificate parsed to get there.
        # The answer still has to be a `bool` rather than whatever `x509` raises.
        assert not transport._responder(b"not a certificate at all")

    def test_no_request_is_sent_over_a_responder(
        self, gate: FakeGate, responder: Issued, authority: Authority, client_certificate: Issued
    ) -> None:
        # `CA.md` 208: the marker is there so a client can refuse to send real requests over an
        # identity that can only say the database is locked. Refusing after the answer would
        # have handed over the request first, and a request body is what it was going to store.
        gate.serve_with(responder)
        gate.handler = lambda path, body, client: (200, '{"password": "fabricated"}')
        context = transport.paired_context(authority.pem, client_certificate.pem)

        with transport.connect(
            gate.port, context, "abc.def.kpg", timeout=10.0
        ) as connection, pytest.raises(DatabaseLocked, match="responder identity"):
            connection.send("PUT", "/entries/abc/fields/Password", '{"value":"secret"}')

        assert gate.requests == [], "nothing reached the gate at all"


class TestWhatThisClientWillNotSend:
    """A path is caller data, and a request line it cannot be is refused here rather than sent."""

    def test_a_path_carrying_a_line_break_is_refused(
        self, gate: FakeGate, authority: Authority, client_certificate: Issued, answers: Answers
    ) -> None:
        # Otherwise the caller writes the whole head the gate parses: its own `Host`,
        # `Content-Type` and `Content-Length` land in the tail and are never read.
        gate.handler = answers(200, {"ok": True})
        context = transport.paired_context(authority.pem, client_certificate.pem)
        smuggled = "/gate/status\r\nX-Injected: yes\r\n\r\nGET /elsewhere HTTP/1.1"

        with transport.connect(
            gate.port, context, "abc.def.kpg", timeout=10.0
        ) as connection, pytest.raises(ProtocolError, match="request line cannot"):
            connection.send("GET", smuggled)

        assert gate.requests == [], "nothing reached the gate at all"

    def test_a_path_outside_ascii_is_refused_as_a_gate_error(
        self, gate: FakeGate, authority: Authority, client_certificate: Issued, answers: Answers
    ) -> None:
        # `encode("ascii")` would raise `UnicodeEncodeError`, which is a `ValueError`: outside this
        # library's one root, so a caller wrapping a session in `except GateError` would miss it.
        gate.handler = answers(200, {"ok": True})
        context = transport.paired_context(authority.pem, client_certificate.pem)

        # Built from its code point rather than typed, so this file stays ASCII and the
        # path does not.
        beyond = "/groups/" + chr(0x00FC) + "ber"

        with transport.connect(
            gate.port, context, "abc.def.kpg", timeout=10.0
        ) as connection, pytest.raises(ProtocolError, match="not a path"):
            connection.send("GET", beyond)

    def test_a_method_that_is_not_one_is_refused(
        self, gate: FakeGate, authority: Authority, client_certificate: Issued, answers: Answers
    ) -> None:
        gate.handler = answers(200, {"ok": True})
        context = transport.paired_context(authority.pem, client_certificate.pem)

        with transport.connect(
            gate.port, context, "abc.def.kpg", timeout=10.0
        ) as connection, pytest.raises(ProtocolError, match="not a method"):
            connection.send("GET /elsewhere HTTP/1.1", "/gate/status")


class TestTheAnswerIsFramedRatherThanEnded:
    """The gate sends a length. Believing the close instead cannot tell short from complete."""

    def test_a_body_shorter_than_its_own_length_is_refused(
        self, gate: FakeGate, authority: Authority, client_certificate: Issued
    ) -> None:
        # What a budget expiring mid-write looks like from here. Read to EOF, this is a complete
        # `200` with an empty body, which `GateClient.send` turns into an empty object.
        gate.truncate = 4
        gate.handler = lambda path, body, client: (200, '{"name": "something long"}')
        context = transport.paired_context(authority.pem, client_certificate.pem)

        with transport.connect(
            gate.port, context, "abc.def.kpg", timeout=10.0
        ) as connection, pytest.raises(TransportError, match="short of its own content length"):
            connection.send("GET", "/gate/status")

    def test_an_answer_that_declares_no_length_is_taken_as_it_stands(
        self, gate: FakeGate, authority: Authority, client_certificate: Issued
    ) -> None:
        # What an answer to a HEAD looks like. RFC 9110 section 9.3.2 permits the missing length,
        # and the gate serves HEAD everywhere it serves GET, so refusing this would refuse half
        # the routes there are.
        gate.declare = ""
        gate.handler = lambda path, body, client: (200, '{"ok": true}')
        context = transport.paired_context(authority.pem, client_certificate.pem)

        with transport.connect(gate.port, context, "abc.def.kpg", timeout=10.0) as connection:
            assert connection.send("GET", "/gate/status").body == '{"ok": true}'

    def test_a_length_that_is_not_a_number_is_refused(
        self, gate: FakeGate, authority: Authority, client_certificate: Issued
    ) -> None:
        gate.declare = "nonsense"
        gate.handler = lambda path, body, client: (200, '{"ok": true}')
        context = transport.paired_context(authority.pem, client_certificate.pem)

        with transport.connect(
            gate.port, context, "abc.def.kpg", timeout=10.0
        ) as connection, pytest.raises(ProtocolError, match="not a content length"):
            connection.send("GET", "/gate/status")

    def test_a_peer_that_frames_and_does_not_close_is_answered_at_once(
        self, gate: FakeGate, authority: Authority, client_certificate: Issued
    ) -> None:
        # Reading to the close would wait out the whole timeout here and then throw away an answer
        # it already held complete. The frame is what says the answer is whole.
        gate.linger = 5.0
        gate.handler = lambda path, body, client: (200, '{"ok": true}')
        context = transport.paired_context(authority.pem, client_certificate.pem)

        started = time.monotonic()
        with transport.connect(gate.port, context, "abc.def.kpg", timeout=10.0) as connection:
            assert connection.send("GET", "/gate/status").body == '{"ok": true}'

        assert time.monotonic() - started < 3.0, "it did not wait for a close it did not need"

    def test_the_pairing_timeout_outlasts_what_the_plugin_may_take(self) -> None:
        # `GateOptionsTests` sums the same four constants on the other side. 185 seconds, and the
        # gate gives itself 300; a client that hangs up first loses the answer to a pairing the
        # gate has already spent the invitation and written the record for.
        assert transport.PAIRING_TIMEOUT > 185.0
        assert transport.PAIRING_TIMEOUT > transport.DEFAULT_TIMEOUT
