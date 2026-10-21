"""A stand-in for the gate, for testing a program that uses this client without a KeePass.

**A scripted TLS stand-in, and not a model of the rules.** What is here is a certificate
authority, a host certificate under it, and a TLS 1.3 server that answers one request per
connection and then closes, which is what the plugin's transport does. It answers whatever a test
tells it to answer, nonsense included, because most of what a program has to get right is what it
does with a refusal. It does not know what a grant means, which rule a request falls under, what
a database holds or how a pairing is approved: a program tested against it has been tested
against its own script, and the plugin's own tests are where the rules are held.

So the parts worth testing against it are the ones a script can say: that a program sends what it
means to, and does what it should with each answer and each refusal this client raises for one.

**One refusal it makes on its own, and differently from the plugin.** A client certificate that is
offered is verified in the handshake, chain and dates, since `ssl` cannot ask for one without: an
expired, not yet valid or foreign one ends the handshake with an alert, which the client raises as
`TransportError`, where the plugin completes the handshake and answers `401`, which it raises as
`NotAdmitted`. A program testing what it does with `NotAdmitted` scripts the `401` with a current
certificate.

    authority = Authority(directory)
    host = authority.issue("host", ExtendedKeyUsageOID.SERVER_AUTH, "host.pem")
    with FakeGate(host, authority.pem) as gate:
        gate.handler = lambda path, body, client: (503, '{"error":"busy"}')
        ...  # credentials naming gate.port, an `authority.pem` and a client certificate

Nothing here imports a test framework, so it serves any: the client's own tests build it through
pytest fixtures, in `tests/pykeepassgate/conftest.py`, and nothing else here depends on that.
The private keys it writes are unencrypted, in the directory it is given, which is a test's own.
"""

from __future__ import annotations

import contextlib
import datetime as dt
import json
import socket
import ssl
import threading
import time
from collections.abc import Callable
from dataclasses import dataclass
from http import HTTPStatus
from pathlib import Path
from types import TracebackType

from cryptography import x509
from cryptography.hazmat.primitives import hashes, serialization
from cryptography.hazmat.primitives.asymmetric import ec
from cryptography.x509.oid import NameOID

#: What the plugin's authority uses, so that anything measured here is measured against the same
#: shape: ECDSA on P-256, signed with SHA-256, which is also why the channel binding is SHA-256.
CURVE = ec.SECP256R1()

#: Answers a request: the path, the body, and the client certificate if one was offered, with the
#: status and the body to answer it with. The method is asked of the gate, with `FakeGate.method`,
#: so that one path answered differently for `GET` and `DELETE` needs no counting of calls.
Handler = Callable[[str, str, bytes | None], tuple[int, str]]


@dataclass(frozen=True)
class Issued:
    """A certificate and its key, written where `ssl` can read them."""

    certificate: x509.Certificate
    pem: Path

    @property
    def der(self) -> bytes:
        """The bytes the channel binding is computed over."""
        return self.certificate.public_bytes(serialization.Encoding.DER)


class Authority:
    """One database's certificate authority, near enough for a client to be tested against."""

    def __init__(self, directory: Path, lifetime: dt.timedelta = dt.timedelta(days=1)) -> None:
        """Mint the authority and write it where a trust store can load it.

        A day unless a test asks for longer, which is all most tests need of it.
        """
        directory.mkdir(parents=True, exist_ok=True)
        self._directory = directory
        self._key = ec.generate_private_key(CURVE)

        subject = x509.Name([x509.NameAttribute(NameOID.COMMON_NAME, "KeePassGate Authority")])
        now = dt.datetime.now(dt.UTC)

        self.certificate = (
            x509.CertificateBuilder()
            .subject_name(subject)
            .issuer_name(subject)
            .public_key(self._key.public_key())
            .serial_number(x509.random_serial_number())
            .not_valid_before(now - dt.timedelta(minutes=5))
            .not_valid_after(now + lifetime)
            .add_extension(x509.BasicConstraints(ca=True, path_length=0), critical=True)
            .sign(self._key, hashes.SHA256())
        )

        self.pem = directory / "authority.pem"
        self.pem.write_bytes(self.certificate.public_bytes(serialization.Encoding.PEM))

    def issue(
        self,
        name: str,
        purpose: x509.ObjectIdentifier,
        filename: str,
        uri: str | None = None,
        valid: tuple[dt.timedelta, dt.timedelta] | None = None,
    ) -> Issued:
        """Issue a leaf under this authority and write the certificate and key as one PEM.

        A `uri` goes into the subject alternative name, which is where the plugin puts the one
        marker a leaf of this authority ever carries: the responder's. `valid` is when the leaf
        starts and stops being valid, each counted from now, for a test that needs one outside
        its dates; every other leaf is valid from five minutes ago for twelve hours.
        """
        key = ec.generate_private_key(CURVE)
        now = dt.datetime.now(dt.UTC)
        start, end = valid or (dt.timedelta(minutes=-5), dt.timedelta(hours=12))

        builder = (
            x509.CertificateBuilder()
            .subject_name(x509.Name([x509.NameAttribute(NameOID.COMMON_NAME, name)]))
            .issuer_name(self.certificate.subject)
            .public_key(key.public_key())
            .serial_number(x509.random_serial_number())
            .not_valid_before(now + start)
            .not_valid_after(now + end)
            .add_extension(x509.BasicConstraints(ca=False, path_length=None), critical=True)
            .add_extension(x509.ExtendedKeyUsage([purpose]), critical=False)
        )

        if uri is not None:
            builder = builder.add_extension(
                x509.SubjectAlternativeName([x509.UniformResourceIdentifier(uri)]), critical=False
            )

        certificate = builder.sign(self._key, hashes.SHA256())

        path = self._directory / filename
        path.write_bytes(
            certificate.public_bytes(serialization.Encoding.PEM)
            + key.private_bytes(
                encoding=serialization.Encoding.PEM,
                format=serialization.PrivateFormat.PKCS8,
                encryption_algorithm=serialization.NoEncryption(),
            )
        )

        return Issued(certificate=certificate, pem=path)


def held(until: dt.datetime) -> tuple[int, str]:
    """What the plugin answers a pairing or a renewal whose approval it holds until `until`.

    `503 busy` with `heldUntil`, in UTC to the second: the operator approved and KeePass was too
    busy to write it, and the same request asked again before then is written without the dialog.
    A handler returns it, as it would any answer, to test what a program does while
    `pair` or `renew` asks again by itself, and with the `GateBusy` that comes once it runs out.
    """
    moment = until.astimezone(dt.UTC).strftime("%Y-%m-%dT%H:%M:%SZ")
    return 503, json.dumps({"error": "busy", "heldUntil": moment}, separators=(",", ":"))


def _no_route(_path: str, _body: str, _client: bytes | None) -> tuple[int, str]:
    """What a gate nobody has scripted answers: a route it does not have."""
    return 404, '{"error":"no such route"}'


class FakeGate:
    """A TLS 1.3 server on loopback that answers one request per connection and then closes."""

    def __init__(self, host_certificate: Issued, authority: Path) -> None:
        """Serve with this certificate, and ask for a client one without insisting on it."""
        self.host_certificate = host_certificate
        self.names: list[str | None] = []
        self.requests: list[tuple[str, str]] = []

        #: Bytes to cut off the end of the answer, so a test can produce what a budget expiring
        #: mid-write produces: a complete head, a `Content-Length`, and a body short of it.
        self.truncate = 0

        #: What to put in `Content-Length` instead of the real one. The empty string writes no
        #: header at all, which is what an answer to a HEAD does: RFC 9110 section 9.3.2 permits
        #: the missing length, and this gate has to be able to produce one.
        self.declare: str | None = None

        #: What to put in `Retry-After` on a `503`, which is where the gate sends it and the only
        #: place: `2` unless a test says otherwise, as the plugin writes it on every one, and
        #: `None` for a `503` without it, which an older gate or a proxy may send. Set as text
        #: rather than a number so that a test can be a peer sending the date form, or something
        #: that is not a delay at all.
        self.retry_after: str | None = "2"

        #: Held open after the answer is written, so a test can be a peer that framed correctly
        #: and did not close. A client that reads to the close waits out its whole timeout here
        #: and then throws away an answer it already had.
        self.linger = 0.0

        #: What answers each request, which is all this gate knows about any route.
        self.handler: Handler = _no_route

        #: How long a peer may stay quiet, in seconds, before the thread answering it gives up, so
        #: one that goes quiet mid-request ends its thread rather than parking it.
        self.quiet = 10.0

        self._authority = authority
        self._context = self._serving(host_certificate)
        self._listener = socket.socket(socket.AF_INET, socket.SOCK_STREAM)
        self._listener.setsockopt(socket.SOL_SOCKET, socket.SO_REUSEADDR, 1)
        self._listener.bind(("127.0.0.1", 0))
        self._listener.listen(8)
        self.port: int = self._listener.getsockname()[1]

        # The request each answering thread is in the middle of, which `method` reads.
        self._answering = threading.local()

        self._stop = threading.Event()
        self._thread = threading.Thread(target=self._serve, daemon=True)
        self._thread.start()

    def __enter__(self) -> FakeGate:
        """Hand back the gate, which is already listening."""
        return self

    def __exit__(
        self,
        kind: type[BaseException] | None,
        error: BaseException | None,
        trace: TracebackType | None,
    ) -> None:
        """Stop it, however the block ended."""
        self.close()

    def close(self) -> None:
        """Stop accepting and let the thread end."""
        self._stop.set()

        # Woken by a connection of its own before the socket goes. Closing a listening socket does
        # not wake a thread blocked in `accept` on Linux, so every teardown there waited out the
        # whole join below; a connection wakes it on every platform, and it then sees the flag.
        with contextlib.suppress(OSError):
            socket.create_connection(("127.0.0.1", self.port), timeout=1).close()

        self._listener.close()
        self._thread.join(timeout=5)

    def serve_with(self, host_certificate: Issued) -> None:
        """Answer every connection from now on with a different certificate.

        What a client meets when the KeePass it was pairing with exits between two connections
        and something else takes the port, or when a database locks and answers with the
        certificate a locked one has.
        """
        self.host_certificate = host_certificate
        self._context = self._serving(host_certificate)

    def named(self) -> str:
        """The name the connection being served arrived on.

        Asked of the gate rather than read out of a list a handler holds its own alias of. The
        two disagreed about whether an entry can be `None`, which is what the `type: ignore` on
        each assignment was saying.
        """
        name = self.names[-1] if self.names else None
        if name is None:
            raise AssertionError("the connection carried no name")

        return name

    def method(self) -> str:
        """The method of the request being answered, asked from inside the handler answering it.

        Kept per answering thread rather than read off the end of `requests`, so a handler gets
        its own request's method however many connections are being answered at once.
        """
        method: str | None = getattr(self._answering, "method", None)
        if method is None:
            raise AssertionError("no request is being answered on this thread")

        return method

    def _serve(self) -> None:
        while not self._stop.is_set():
            try:
                raw, _ = self._listener.accept()
            except OSError:
                return

            if self._stop.is_set():
                # The connection `close` made to wake this thread, which nobody answers.
                raw.close()
                return

            # Bounded, so a peer that goes quiet mid-request ends its thread rather than parking
            # it. The resulting `socket.timeout` is an `OSError` and `_answer` already catches it.
            raw.settimeout(self.quiet)
            threading.Thread(target=self._answer, args=(raw,), daemon=True).start()

    def _serving(self, host_certificate: Issued) -> ssl.SSLContext:
        context = ssl.SSLContext(ssl.PROTOCOL_TLS_SERVER)
        context.load_cert_chain(certfile=str(host_certificate.pem))

        # Optional rather than required, so that both halves of the protocol can be served by one
        # gate: pairing arrives without a certificate, and everything after it arrives with one.
        # One that is offered is verified, chain and dates, because `ssl` offers no way to ask for
        # a certificate without: an expired, not yet valid or foreign one is refused here with an
        # alert, where the plugin completes the handshake and answers `401`.
        context.verify_mode = ssl.CERT_OPTIONAL
        context.load_verify_locations(cafile=str(self._authority))
        context.minimum_version = ssl.TLSVersion.TLSv1_3
        context.maximum_version = ssl.TLSVersion.TLSv1_3

        # The name the client chose reaches a server through the SNI callback and nowhere else:
        # `SSLSocket.server_hostname` is the client's own copy and is `None` on this side.
        context.sni_callback = self._remember
        return context

    def _remember(
        self, _wrapped: ssl.SSLObject, name: str | None, _context: ssl.SSLContext
    ) -> None:
        self.names.append(name)

    def _answer(self, raw: socket.socket) -> None:
        try:
            with self._context.wrap_socket(raw, server_side=True) as wrapped:
                head, body = _read_request(wrapped)
                if head is None:
                    return

                method, path = head
                self.requests.append((method, path))
                client = wrapped.getpeercert(binary_form=True)
                self._answering.method = method
                try:
                    status, answer = self.handler(path, body, client)
                finally:
                    self._answering.method = None

                # What the gate does for a HEAD: no content, and no length rather than a wrong
                # one. RFC 9112 section 6.3 and RFC 9110 section 8.6. Modeled here because a
                # client that only ever met a gate which answered HEAD like GET would not be
                # tested against the gate it has.
                retry_after = self.retry_after if status == 503 else None
                if method == "HEAD":
                    written = _response(status, "", "", retry_after)
                else:
                    written = _response(status, answer, self.declare, retry_after)
                if self.truncate:
                    written = written[: len(written) - self.truncate]

                wrapped.sendall(written)
                if self.linger:
                    time.sleep(self.linger)
        except (OSError, ssl.SSLError):
            return


def _read_request(wrapped: ssl.SSLSocket) -> tuple[tuple[str, str] | None, str]:
    buffer = bytearray()
    while b"\r\n\r\n" not in buffer:
        chunk = wrapped.recv(4096)
        if not chunk:
            return None, ""

        buffer += chunk

    head, rest = bytes(buffer).split(b"\r\n\r\n", 1)
    lines = head.decode("ascii", "replace").split("\r\n")
    parts = lines[0].split(" ")
    if len(parts) < 2:
        return None, ""

    length = 0
    for line in lines[1:]:
        if line.lower().startswith("content-length:"):
            try:
                length = int(line.split(":", 1)[1].strip())
            except ValueError:
                # Answered rather than raised. A `ValueError` here escapes the thread, where the
                # client sees only a connection closed without a word and a test says nothing
                # about why. `_answer` treats a head of `None` as nothing to serve.
                return None, ""

    while len(rest) < length:
        chunk = wrapped.recv(4096)
        if not chunk:
            break

        rest += chunk

    return (parts[0], parts[1]), rest.decode("utf-8", "replace")


def _response(
    status: int, body: str, declare: str | None = None, retry_after: str | None = None
) -> bytes:
    """The head the plugin's `HttpResponse.Write` writes, and the body.

    The reason phrase is RFC 9110's, and the two headers that keep an answer out of a cache and a
    browser's guessing are there as the plugin sends them. The one header a script cannot give is
    `Allow`, which the plugin puts on every `405` and this does not.
    """
    payload = body.encode("utf-8")
    length = str(len(payload)) if declare is None else declare
    try:
        phrase = HTTPStatus(status).phrase
    except ValueError:
        phrase = "Unknown"

    head = (
        f"HTTP/1.1 {status} {phrase}\r\n"
        "Content-Type: application/json; charset=utf-8\r\n"
        + (f"Content-Length: {length}\r\n" if length else "")
        + "Cache-Control: no-store\r\n"
        "X-Content-Type-Options: nosniff\r\n"
        + (f"Retry-After: {retry_after}\r\n" if retry_after is not None else "")
        + "Connection: close\r\n"
        "\r\n"
    )

    return head.encode("ascii") + payload
