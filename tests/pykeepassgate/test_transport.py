"""One connection, one request, and what happens when any of that fails.

Half of these drive a real TLS 1.3 server, because the parts of this worth doubting are the parts
`ssl` does: whether the rotating name reaches the server as SNI, whether the certificate is readable
before the body is written, and whether loading one authority really is a pin.
"""

from __future__ import annotations

import datetime as dt
import math
import socket
import ssl
import time
from collections.abc import Callable
from pathlib import Path
from typing import Any, cast

import pytest
from cryptography import x509

from conftest import RESPONDER_CLAIM, Authority, FakeGate, Issued
from pykeepassgate import transport
from pykeepassgate.errors import (
    AuthorityUndecided,
    DatabaseLocked,
    GateBusy,
    GateUnavailable,
    ProtocolError,
    StorageError,
    TransportError,
)

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


class TestWhatA503Is:
    """One place reads a `503`'s word, and hands back the class that names it."""

    @staticmethod
    def _read(body: str) -> GateUnavailable:
        return transport.unavailable(
            transport.Response(503, body, retry_after=2.0),
            busy="said busy",
            locked="said locked",
            undecided="said undecided",
        )

    @pytest.mark.parametrize(
        ("word", "kind"),
        [("busy", GateBusy), ("locked", DatabaseLocked), ("undecided", AuthorityUndecided)],
    )
    def test_each_word_the_gate_sends_is_a_class_of_its_own(
        self, word: str, kind: type[GateUnavailable]
    ) -> None:
        refused = self._read('{"error":"' + word + '"}')

        assert type(refused) is kind
        assert (str(refused), refused.error, refused.retry_after) == (f"said {word}", word, 2.0)

    def test_a_word_this_client_does_not_know_is_the_family_itself(self) -> None:
        # Kept, because it is the whole of what such a refusal says.
        refused = self._read('{"error":"teapot"}')

        assert type(refused) is GateUnavailable
        assert (refused.error, refused.retry_after) == ("teapot", 2.0)
        assert "'teapot'" in str(refused)

    def test_a_503_naming_no_word_is_the_family_itself(self) -> None:
        refused = self._read("{}")

        assert type(refused) is GateUnavailable
        assert refused.error == ""


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
            1, context, "x.y.kpg", connection_timeout=2.0
        ):
            pass  # pragma: no cover, the connect is what raises

    def test_the_name_this_client_chose_is_what_the_gate_sees(
        self, gate: FakeGate, answers: Answers
    ) -> None:
        gate.handler = answers(200, {"ok": True})
        context = transport.unpaired_context()

        with transport.connect(
            gate.port, context, "abc.def.kpg", connection_timeout=10.0
        ) as connection:
            connection.send("GET", "/gate/status")

        assert gate.names == ["abc.def.kpg"]

    def test_the_certificate_is_readable_before_the_body_is_written(
        self, gate: FakeGate, host: Issued, answers: Answers
    ) -> None:
        # The whole reason this is not `http.client`: the pairing body is a proof over these bytes.
        gate.handler = answers(200, {"ok": True})
        context = transport.unpaired_context()

        with transport.connect(
            gate.port, context, "abc.def.kpg", connection_timeout=10.0
        ) as connection:
            assert connection.server_certificate == host.der

    def test_a_request_and_its_answer_survive_the_round_trip(
        self, gate: FakeGate, answers: Answers
    ) -> None:
        gate.handler = answers(201, {"read": "it"})
        context = transport.unpaired_context()

        with transport.connect(
            gate.port, context, "abc.def.kpg", connection_timeout=10.0
        ) as connection:
            reply = connection.send("POST", "/pair/hello", '{"proof":"x"}')

        assert (reply.status, reply.json()) == (201, {"read": "it"})
        assert gate.requests == [("POST", "/pair/hello")]

    def test_nothing_older_than_tls_13_is_offered(self, gate: FakeGate, answers: Answers) -> None:
        gate.handler = answers(200, {"ok": True})
        context = transport.unpaired_context()

        with transport.connect(
            gate.port, context, "abc.def.kpg", connection_timeout=10.0
        ) as connection:
            assert connection._socket.version() == "TLSv1.3"


class TestDialing:
    """What makes the socket, which is the standard library's unless a caller gives another."""

    def test_the_dial_is_handed_the_host_the_port_and_the_timeout(
        self, gate: FakeGate, answers: Answers
    ) -> None:
        # The host need not be anything this machine resolves: it is the dial's to read. This one
        # reaches the gate whatever it was asked, which is what a relay or a proxy does.
        gate.handler = answers(200, {"ok": True})
        asked: list[tuple[str, int, float]] = []

        def dial(host: str, port: int, timeout: float) -> socket.socket:
            asked.append((host, port, timeout))
            return socket.create_connection((transport.LOOPBACK, gate.port), timeout=timeout)

        with transport.connect(
            gate.port,
            transport.unpaired_context(),
            "abc.def.kpg",
            host="gate.invalid",
            connection_timeout=7.0,
            dial=dial,
        ) as connection:
            assert connection.send("GET", "/gate/status").status == 200

        assert asked == [("gate.invalid", gate.port, 7.0)]

    def test_a_socket_dialed_blocking_is_held_to_the_timeout_all_the_same(
        self, gate: FakeGate, answers: Answers
    ) -> None:
        # The timeout bounds the handshake and each read, whatever made the socket, so a dial
        # that hands back a blocking one cannot take that bound away.
        gate.handler = answers(200, {"ok": True})

        def blocking(host: str, port: int, timeout: float) -> socket.socket:
            made = socket.create_connection((host, port))
            made.settimeout(None)
            return made

        with transport.connect(
            gate.port, transport.unpaired_context(), "abc.def.kpg", connection_timeout=9.0,
            dial=blocking,
        ) as connection:
            assert connection._socket.gettimeout() == 9.0

    def test_a_dial_that_fails_is_a_transport_error_saying_where(self) -> None:
        def refusing(host: str, port: int, timeout: float) -> socket.socket:
            raise ConnectionRefusedError("refused by an invented relay")

        with pytest.raises(TransportError, match=r"gate\.invalid:45678.*invented relay"), (
            transport.connect(
                45678, transport.unpaired_context(), "x.y.kpg", host="gate.invalid",
                dial=refusing,
            )
        ):
            pass  # pragma: no cover, the dial is what raises

    def test_a_dial_that_hands_back_a_closed_socket_is_a_transport_error(self) -> None:
        # Setting a closed socket's timeout raises a bare `OSError`, measured on Windows as
        # `WinError 10038`, which is no `GateError`. No handshake was tried, so the message says
        # what was wrong rather than sending the reader to the gate's certificates.
        def closed(host: str, port: int, timeout: float) -> socket.socket:
            made = socket.socket()
            made.close()
            return made

        with pytest.raises(
            TransportError, match="the socket dial handed back could not be used"
        ), transport.connect(45678, transport.unpaired_context(), "x.y.kpg", dial=closed):
            pass  # pragma: no cover, the timeout is what raises

    def test_a_dial_that_hands_back_no_socket_is_a_programming_error(self) -> None:
        def wrong(host: str, port: int, timeout: float) -> socket.socket:
            return cast(socket.socket, "a socket, in a manner of speaking")

        with pytest.raises(TypeError, match="dial returned an object of type str"), (
            transport.connect(45678, transport.unpaired_context(), "x.y.kpg", dial=wrong)
        ):
            pass  # pragma: no cover, the dial is what raises

    def test_what_a_dial_handed_back_by_mistake_is_closed(self) -> None:
        # Something socket-like from another library, which owns a descriptor of its own.
        closed: list[bool] = []

        class Wrapper:
            def close(self) -> None:
                closed.append(True)

        def wrong(host: str, port: int, timeout: float) -> socket.socket:
            return cast(socket.socket, Wrapper())

        with pytest.raises(TypeError, match="object of type Wrapper"), transport.connect(
            45678, transport.unpaired_context(), "x.y.kpg", dial=wrong
        ):
            pass  # pragma: no cover, the dial is what raises

        assert closed == [True]

    def test_a_dial_that_is_not_callable_is_refused_before_anything(self) -> None:
        with pytest.raises(ValueError, match="dial is a callable"):
            transport.dial_limit(cast(Any, "127.0.0.1"))

        assert transport.dial_limit(transport.create_connection) is transport.create_connection


class TestPinning:
    def test_a_paired_client_reaches_a_gate_serving_under_its_own_authority(
        self, gate: FakeGate, authority: Authority, client_certificate: Issued, answers: Answers
    ) -> None:
        gate.handler = answers(200, {"ok": True})
        context = transport.paired_context(authority.pem, client_certificate.pem)

        with transport.connect(
            gate.port, context, "abc.def.kpg", connection_timeout=10.0
        ) as connection:
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
            gate.port, context, "abc.def.kpg", connection_timeout=10.0
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

        with transport.connect(
            gate.port, context, "nothing.like.it.kpg", connection_timeout=10.0
        ) as open_:
            assert open_.send("GET", "/gate/status").status == 200

    def test_a_client_certificate_of_the_same_authority_is_not_a_host(
        self, gate: FakeGate, authority: Authority, client_certificate: Issued
    ) -> None:
        # `CA.md` §215: the authority that signs the host signs every client too, so without the
        # `serverAuth` check any client could answer as the gate to every other. The check is
        # OpenSSL's, made for any context built for a client, and this is what would notice it
        # going. Matched on the reason, so that no other failure passes for it.
        gate.serve_with(client_certificate)
        context = transport.paired_context(authority.pem, client_certificate.pem)

        with pytest.raises(TransportError, match=r"handshake failed.*purpose"), transport.connect(
            gate.port, context, "abc.def.kpg", connection_timeout=10.0
        ):
            pass  # pragma: no cover, the handshake is what raises

    @pytest.mark.parametrize(
        ("valid", "reason"),
        [
            ((dt.timedelta(hours=-3), dt.timedelta(hours=-1)), "expired"),
            ((dt.timedelta(hours=1), dt.timedelta(hours=3)), "not yet valid"),
        ],
        ids=["expired", "not yet valid"],
    )
    def test_a_host_certificate_outside_its_dates_is_refused(
        self,
        valid: tuple[dt.timedelta, dt.timedelta],
        reason: str,
        gate: FakeGate,
        authority: Authority,
        client_certificate: Issued,
    ) -> None:
        # The dates are the one bound on how long a host or responder key that leaked, from an
        # old copy of the database or out of memory, can answer as the gate: the client pins the
        # authority and nothing else, and no host certificate is ever revoked. OpenSSL makes the
        # check, and nothing written here would notice it going, a verification written by hand
        # to tell expiry from a foreign authority least of all.
        dated = authority.issue(
            "host", x509.oid.ExtendedKeyUsageOID.SERVER_AUTH, "dated.pem", valid=valid
        )
        gate.serve_with(dated)
        context = transport.paired_context(authority.pem, client_certificate.pem)

        with pytest.raises(TransportError, match=f"handshake failed.*{reason}"), transport.connect(
            gate.port, context, "abc.def.kpg", connection_timeout=10.0
        ):
            pass  # pragma: no cover, the handshake is what raises

    def test_an_authority_and_a_host_good_for_a_hundred_years_are_accepted(
        self, tmp_path: Path, answers: Answers
    ) -> None:
        # A hundred years is the end the operator named for an authority and its host if the end
        # of year 9999 could not be read back, and on .NET Framework it cannot:
        # `LongLivedCertificateTests` on the plugin's side. OpenSSL holds the host to its dates
        # here, so this is what would notice it refusing so late a one, and `cryptography` has to
        # read the end exactly, in a zone ahead of UTC as well.
        century = dt.timedelta(days=36524)
        lasting = Authority(tmp_path / "century", lifetime=century)
        host = lasting.issue(
            "host",
            x509.oid.ExtendedKeyUsageOID.SERVER_AUTH,
            "host.pem",
            valid=(dt.timedelta(minutes=-5), century),
        )
        client = lasting.issue(
            "agent-invented", x509.oid.ExtendedKeyUsageOID.CLIENT_AUTH, "client.pem"
        )

        gate = FakeGate(host, lasting.pem)
        try:
            gate.handler = answers(200, {"ok": True})
            context = transport.paired_context(lasting.pem, client.pem)

            with transport.connect(
                gate.port, context, "abc.def.kpg", connection_timeout=10.0
            ) as connection:
                assert connection.send("GET", "/gate/status").status == 200
        finally:
            gate.close()

        end = host.certificate.not_valid_after_utc
        assert end.year - dt.datetime.now(dt.UTC).year in (99, 100)
        assert end.astimezone(dt.timezone(dt.timedelta(hours=14))) == end


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
            gate.port, context, "abc.def.kpg", connection_timeout=10.0
        ) as connection, pytest.raises(DatabaseLocked, match="responder identity") as refused:
            connection.send("PUT", "/entries/abc/fields/Password", '{"value":"secret"}')

        assert gate.requests == [], "nothing reached the gate at all"
        assert refused.value.error == "locked", "the word a `503 locked` carries, read off nothing"


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
            gate.port, context, "abc.def.kpg", connection_timeout=10.0
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
            gate.port, context, "abc.def.kpg", connection_timeout=10.0
        ) as connection, pytest.raises(ProtocolError, match="not a path"):
            connection.send("GET", beyond)

    def test_a_method_that_is_not_one_is_refused(
        self, gate: FakeGate, authority: Authority, client_certificate: Issued, answers: Answers
    ) -> None:
        gate.handler = answers(200, {"ok": True})
        context = transport.paired_context(authority.pem, client_certificate.pem)

        with transport.connect(
            gate.port, context, "abc.def.kpg", connection_timeout=10.0
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
            gate.port, context, "abc.def.kpg", connection_timeout=10.0
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

        with transport.connect(
            gate.port, context, "abc.def.kpg", connection_timeout=10.0
        ) as connection:
            assert connection.send("GET", "/gate/status").body == '{"ok": true}'

    def test_a_length_that_is_not_a_number_is_refused(
        self, gate: FakeGate, authority: Authority, client_certificate: Issued
    ) -> None:
        gate.declare = "nonsense"
        gate.handler = lambda path, body, client: (200, '{"ok": true}')
        context = transport.paired_context(authority.pem, client_certificate.pem)

        with transport.connect(
            gate.port, context, "abc.def.kpg", connection_timeout=10.0
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
        with transport.connect(
            gate.port, context, "abc.def.kpg", connection_timeout=10.0
        ) as connection:
            assert connection.send("GET", "/gate/status").body == '{"ok": true}'

        assert time.monotonic() - started < 3.0, "it did not wait for a close it did not need"

    def test_an_answer_over_the_cap_it_was_given_fails_whole(
        self, gate: FakeGate, authority: Authority, client_certificate: Issued
    ) -> None:
        # Refused rather than cut short: a listing missing its tail would read as a complete one.
        gate.handler = lambda path, body, client: (200, '{"pad": "' + "x" * 4096 + '"}')
        context = transport.paired_context(authority.pem, client_certificate.pem)

        with transport.connect(
            gate.port, context, "abc.def.kpg", connection_timeout=10.0, max_response_bytes=1024
        ) as connection, pytest.raises(ProtocolError, match="over 1024 bytes"):
            connection.send("GET", "/gate/status")

    def test_an_answer_under_the_cap_it_was_given_is_read(
        self, gate: FakeGate, authority: Authority, client_certificate: Issued
    ) -> None:
        body = '{"pad": "' + "x" * 4096 + '"}'
        gate.handler = lambda path, sent, client: (200, body)
        context = transport.paired_context(authority.pem, client_certificate.pem)

        with transport.connect(
            gate.port, context, "abc.def.kpg", connection_timeout=10.0, max_response_bytes=8192
        ) as connection:
            assert connection.send("GET", "/gate/status").body == body

    @pytest.mark.parametrize("cap", [0, -1])
    def test_a_cap_nothing_fits_under_is_refused_before_dialing(
        self, cap: int, gate: FakeGate, authority: Authority, client_certificate: Issued
    ) -> None:
        context = transport.paired_context(authority.pem, client_certificate.pem)

        with pytest.raises(ValueError, match="positive number of bytes"), transport.connect(
            gate.port, context, "abc.def.kpg", max_response_bytes=cap
        ):
            pass

        assert gate.requests == []

    def test_the_default_cap_is_the_module_s_own(self) -> None:
        # The name a caller compares against, as `DEFAULT_CONNECTION_TIMEOUT` is for the socket.
        assert transport.MAX_RESPONSE_BYTES == 4 * 1024 * 1024
        default = transport.MAX_RESPONSE_BYTES
        assert transport.response_limit(default) == default

    def test_the_pairing_timeout_outlasts_what_the_plugin_may_take(self) -> None:
        # `GateOptionsTests` sums the same five waits on the other side: the screen, the dialog
        # and its grace, the turn at the writer and the write. 305 seconds, and the gate gives
        # itself 360; a client that hangs up first loses the answer to a pairing the gate has
        # already spent the invitation and written the record for.
        assert transport.PAIRING_CONNECTION_TIMEOUT > 120.0 + 120.0 + 15.0 + 20.0 + 30.0
        assert transport.PAIRING_CONNECTION_TIMEOUT > transport.DEFAULT_CONNECTION_TIMEOUT


class TestWhenToComeBack:
    """`Retry-After`, which the gate sends on every `503` and on nothing else."""

    @staticmethod
    def _answer(head: str) -> transport.Response:
        raw = f"HTTP/1.1 503 .\r\n{head}Content-Length: 17\r\n\r\n".encode() + b'{"error":"busy"}\n'
        return transport._parse(raw)

    def test_the_seconds_the_gate_asked_for_are_read_off_the_answer(self) -> None:
        assert self._answer("Retry-After: 2\r\n").retry_after == 2.0

    def test_the_field_name_is_read_the_way_every_other_one_is(self) -> None:
        # RFC 9110 section 5.1: a field name is case-insensitive. The gate writes `Retry-After`,
        # and a client that only matched that spelling would be pinning its own gate's spelling.
        assert self._answer("retry-after:   7 \r\n").retry_after == 7.0

    def test_an_answer_that_did_not_say_says_nothing(self) -> None:
        assert self._answer("").retry_after is None

    def test_the_date_form_is_the_same_as_having_said_nothing(self) -> None:
        # Also allowed by section 10.2.3, and deliberately not read: a date is a clock the two
        # sides would have to agree about. Unreadable narrows what a client knows rather than
        # stopping it, so this falls back rather than refusing the answer.
        assert self._answer("Retry-After: Wed, 23 Sep 2026 22:48:07 GMT\r\n").retry_after is None

    def test_a_delay_that_is_not_a_delay_says_nothing_either(self) -> None:
        assert self._answer("Retry-After: soon\r\n").retry_after is None
        assert self._answer("Retry-After: -5\r\n").retry_after is None

    def test_a_refusal_the_certificate_gave_carries_no_delay(
        self, gate: FakeGate, responder: Issued, authority: Authority, client_certificate: Issued
    ) -> None:
        # There is no answer to have read one off: the request was never sent. The fallback is
        # what covers it, which is the same two seconds the gate would have asked for.
        gate.serve_with(responder)
        context = transport.paired_context(authority.pem, client_certificate.pem)

        with transport.connect(
            gate.port, context, "abc.def.kpg", connection_timeout=10.0
        ) as connection, pytest.raises(DatabaseLocked) as refused:
            connection.send("GET", "/gate/status")

        assert refused.value.retry_after is None


class TestABudget:
    """What a caller is prepared to spend waiting, and what that buys."""

    @pytest.fixture
    def waited(self, monkeypatch: pytest.MonkeyPatch) -> list[float]:
        """Every delay that would have been slept, without sleeping any of them."""
        recorded: list[float] = []
        monkeypatch.setattr(transport, "pause", recorded.append)
        return recorded

    def test_no_budget_waits_for_nothing(self, waited: list[float]) -> None:
        # The default everywhere, and the whole of what `None` means: the refusal is raised
        # exactly as it was before any of this existed.
        assert transport.Budget(None).wait(2.0) is False
        assert waited == []

    def test_what_the_gate_asked_for_is_what_gets_waited(self, waited: list[float]) -> None:
        assert transport.Budget(10.0).wait(3.0) is True
        assert waited == [3.0]

    def test_a_budget_is_spent_rather_than_reset_by_each_refusal(self, waited: list[float]) -> None:
        budget = transport.Budget(5.0)

        assert budget.wait(2.0) is True
        assert budget.wait(2.0) is True
        assert budget.wait(2.0) is False, "four of the five are gone and a third two will not fit"
        assert waited == [2.0, 2.0]

    def test_a_delay_exactly_what_is_left_is_waited(self, waited: list[float]) -> None:
        # Only a delay longer than what is left is refused, so a budget of one `Retry-After`
        # is one wait: `retry_busy_timeout=2` against the gate's two seconds retries once.
        assert transport.Budget(2.0).wait(2.0) is True
        assert waited == [2.0]

    def test_a_delay_longer_than_what_is_left_is_not_shortened(self, waited: list[float]) -> None:
        # Waiting less than the gate asked for and asking again is a request it has already
        # called too early. So the refusal is raised now rather than a shorter wait later.
        assert transport.Budget(10.0).wait(30.0) is False
        assert waited == []

    def test_a_refusal_that_said_nothing_waits_the_fallback(self, waited: list[float]) -> None:
        assert transport.Budget(10.0).wait(None) is True
        assert waited == [transport.FALLBACK_RETRY_AFTER]

    def test_come_back_now_is_not_something_a_client_can_act_on(self, waited: list[float]) -> None:
        # A zero is a legal delay and a loop at whatever rate the network allows, against
        # something that has just said it is busy. Read as having said nothing.
        assert transport.Budget(10.0).wait(0.0) is True
        assert waited == [transport.FALLBACK_RETRY_AFTER]


class TestTheThreeBudgets:
    """Which refusal spends which, because a lock, a busy KeePass and two authorities end apart."""

    @pytest.fixture
    def waited(self, monkeypatch: pytest.MonkeyPatch) -> list[float]:
        recorded: list[float] = []
        monkeypatch.setattr(transport, "pause", recorded.append)
        return recorded

    def test_a_busy_gate_spends_the_busy_budget(self, waited: list[float]) -> None:
        retry = transport.Retry(busy=10.0)

        assert retry.waited(GateBusy("busy", 3.0)) is True
        assert retry.waited(DatabaseLocked("locked", 3.0)) is False
        assert retry.waited(AuthorityUndecided("undecided", 3.0)) is False
        assert waited == [3.0]

    def test_a_locked_database_spends_the_locked_one(self, waited: list[float]) -> None:
        retry = transport.Retry(locked=10.0)

        assert retry.waited(DatabaseLocked("locked", 4.0)) is True
        assert retry.waited(GateBusy("busy", 4.0)) is False
        assert retry.waited(AuthorityUndecided("undecided", 4.0)) is False
        assert waited == [4.0]

    def test_an_undecided_database_spends_the_undecided_one(self, waited: list[float]) -> None:
        retry = transport.Retry(undecided=10.0)

        assert retry.waited(AuthorityUndecided("undecided", 5.0)) is True
        assert retry.waited(GateBusy("busy", 5.0)) is False
        assert retry.waited(DatabaseLocked("locked", 5.0)) is False
        assert waited == [5.0]

    def test_none_is_spent_by_default(self, waited: list[float]) -> None:
        retry = transport.Retry()

        assert retry.waited(GateBusy("busy", 1.0)) is False
        assert retry.waited(DatabaseLocked("locked", 1.0)) is False
        assert retry.waited(AuthorityUndecided("undecided", 1.0)) is False
        assert waited == []

    def test_a_word_none_of_them_names_spends_nothing(self, waited: list[float]) -> None:
        retry = transport.Retry(busy=10.0, locked=10.0, undecided=10.0)

        assert retry.waited(GateUnavailable("teapot", "teapot", 1.0)) is False
        assert retry.waited(GateUnavailable("no word")) is False
        assert waited == []


class TestASettingThatCannotWork:
    """What the command line refuses as an argument, the library refuses as a `ValueError`.

    Each names the setting, and each is asked where the setting is given, so that nothing is
    dialed on it and nothing fails later from inside `socket` with an error that is not this
    library's.
    """

    @pytest.mark.parametrize("port", [0, -1, 65536, 70000, True, 45678.0])
    def test_a_port_tcp_does_not_have(self, port: Any) -> None:
        with pytest.raises(ValueError, match="port"):
            transport.port_number(port)

    @pytest.mark.parametrize("seconds", [0, 0.0, -1.0, math.nan, math.inf, -math.inf, True])
    def test_a_socket_timeout_that_bounds_no_wait(self, seconds: Any) -> None:
        # Zero dialed non-blocking and read as nothing listening; the rest left `socket` as a
        # `ValueError` or an `OverflowError` from inside the call.
        with pytest.raises(ValueError, match="connection_timeout"):
            transport.connection_limit(seconds)

    @pytest.mark.parametrize("seconds", [-1.0, math.nan, math.inf, -math.inf, True])
    def test_a_budget_that_cannot_be_spent(self, seconds: Any) -> None:
        # NaN compares false with every delay, so a NaN budget was never spent and a `503` was
        # asked again every two seconds for as long as the gate answered one.
        with pytest.raises(ValueError, match="retry_busy_timeout"):
            transport.Retry(busy=seconds)
        with pytest.raises(ValueError, match="retry_locked_timeout"):
            transport.Retry(locked=seconds)
        with pytest.raises(ValueError, match="retry_undecided_timeout"):
            transport.Retry(undecided=seconds)

    @pytest.mark.parametrize("cap", [1.5, math.nan, True])
    def test_a_cap_that_is_not_a_whole_number_of_bytes(self, cap: Any) -> None:
        # A NaN cap compares false with every length, so it capped nothing at all.
        with pytest.raises(ValueError, match="max_response_bytes"):
            transport.response_limit(cap)

    def test_the_edges_of_every_setting_are_taken(self) -> None:
        # The other side of the refusals: the same edges the command line accepts.
        assert transport.port_number(1) == 1
        assert transport.port_number(65535) == 65535
        assert transport.connection_limit(0.5) == 0.5
        assert transport.retry_limit("retry_busy_timeout", None) is None
        assert transport.retry_limit("retry_busy_timeout", 0.0) == 0.0
        assert transport.retry_limit("retry_locked_timeout", 3600.0) == 3600.0
        assert transport.retry_limit("retry_undecided_timeout", 86400.0) == 86400.0
        assert transport.response_limit(1) == 1

    def test_nothing_is_dialed_on_a_setting_that_cannot_work(
        self, gate: FakeGate, authority: Authority, client_certificate: Issued
    ) -> None:
        context = transport.paired_context(authority.pem, client_certificate.pem)

        with pytest.raises(ValueError, match="connection_timeout"), transport.connect(
            gate.port, context, "abc.def.kpg", connection_timeout=math.nan
        ):
            pass  # pragma: no cover, the connect is what raises

        with pytest.raises(ValueError, match="port"), transport.connect(
            70000, context, "abc.def.kpg", connection_timeout=10.0
        ):
            pass  # pragma: no cover, the connect is what raises

        assert gate.names == [], "no handshake was begun"


class TestTheFilesAPairedClientLoads:
    """The anchor and the identity, which `ssl` reads, and nothing reads before it."""

    @staticmethod
    def _spoiled(source: Path, how: str, where: Path) -> Path:
        """A copy of a PEM file spoiled one way, or no file at all."""
        written = source.read_bytes()
        end = b"-----END CERTIFICATE-----\n"

        if how == "cut short":
            where.write_bytes(written[:300])
        elif how == "without its key":
            where.write_bytes(written[: written.index(end) + len(end)])
        elif how == "not a PEM":
            where.write_bytes(b"not a certificate at all\n")

        return where

    @pytest.mark.parametrize("how", ["cut short", "without its key", "not a PEM", "missing"])
    def test_an_identity_that_will_not_load_is_a_storage_error_naming_it(
        self, how: str, authority: Authority, client_certificate: Issued, tmp_path: Path
    ) -> None:
        # A re-pair writes in place, so a disk that fills part way through leaves this file cut
        # short, and an `SSLError` out of here is an `OSError` rather than anything a caller
        # holding this library's one root would catch.
        identity = self._spoiled(client_certificate.pem, how, tmp_path / "identity.pem")

        with pytest.raises(StorageError, match=r"identity\.pem"):
            transport.paired_context(authority.pem, identity)

    @pytest.mark.parametrize("how", ["cut short", "not a PEM", "missing"])
    def test_an_authority_that_will_not_load_is_a_storage_error_naming_it(
        self, how: str, authority: Authority, client_certificate: Issued, tmp_path: Path
    ) -> None:
        anchor = self._spoiled(authority.pem, how, tmp_path / "anchor.pem")

        with pytest.raises(StorageError, match=r"anchor\.pem"):
            transport.paired_context(anchor, client_certificate.pem)
