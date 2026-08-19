"""A gate to talk to, without a KeePass in the room.

The C# side draws the same line: a tier of tests that cannot load KeePass by construction, and a
tier that drives the real thing. This is the first of those. What it builds is a certificate
authority, a host certificate under it, and a TLS 1.3 server that answers one request per connection
and then closes, which is exactly what the plugin's transport does.

It is not a second implementation of the protocol. It answers whatever a test tells it to answer,
including nonsense, because most of what a client has to get right is what it does with a refusal.
The one place it does compute something is the pairing exchange, and there it uses this package's
own `proofs`, so what those tests prove is that the client drives the exchange in the right order
with the right values carried across, not that the construction is the one the document describes.
The vectors taken from the plugin are what prove that.
"""

from __future__ import annotations

import base64
import contextlib
import datetime as dt
import socket
import ssl
import threading
import time
from collections.abc import Callable, Iterator
from dataclasses import dataclass
from pathlib import Path
from typing import Any

import pytest
from cryptography import x509
from cryptography.hazmat.primitives import hashes, serialization
from cryptography.hazmat.primitives.asymmetric import ec
from cryptography.hazmat.primitives.serialization import pkcs12
from cryptography.x509.oid import NameOID

#: What the plugin's authority uses, so that anything measured here is measured against the same
#: shape: ECDSA on P-256, signed with SHA-256, which is also why the channel binding is SHA-256.
CURVE = ec.SECP256R1()

#: What the plugin writes into a responder certificate, copied from `AuthorityIssuer.ResponderClaim`
#: rather than read from the client, so that the client's own constant is compared with something.
#: `CA.md` 208 is why the marker is a URI with a path: `System.Uri` normalizes `kpg://responder` to
#: `kpg://responder/` on the way in, and a client then reads back something else than was written.
RESPONDER_CLAIM = "kpg://host/responder"

#: Answers a request: the path, the body, and the client certificate if one was offered.
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
    """One database's certificate authority, near enough for the client to be tested against."""

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


class FakeGate:
    """A TLS 1.3 server that answers one request per connection and then closes."""

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

        #: What to put in `Retry-After`, which the gate sends on every `503` and on nothing else.
        #: Set as text rather than a number so that a test can be a peer sending the date form,
        #: or something that is not a delay at all.
        self.retry_after: str | None = None

        #: Held open after the answer is written, so a test can be a peer that framed correctly
        #: and did not close. A client that reads to the close waits out its whole timeout here
        #: and then throws away an answer it already had.
        self.linger = 0.0
        self.handler: Handler = lambda path, body, client: (404, '{"error":"no such route"}')

        self._authority = authority
        self._context = self._serving(host_certificate)
        self._listener = socket.socket(socket.AF_INET, socket.SOCK_STREAM)
        self._listener.setsockopt(socket.SOL_SOCKET, socket.SO_REUSEADDR, 1)
        self._listener.bind(("127.0.0.1", 0))
        self._listener.listen(8)
        self.port = self._listener.getsockname()[1]

        self._stop = threading.Event()
        self._thread = threading.Thread(target=self._serve, daemon=True)
        self._thread.start()

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
            raw.settimeout(10)
            threading.Thread(target=self._answer, args=(raw,), daemon=True).start()

    def serve_with(self, host_certificate: Issued) -> None:
        """Answer every connection from now on with a different certificate.

        What a client meets when the KeePass it was pairing with exits between two connections
        and something else takes the port.
        """
        self.host_certificate = host_certificate
        self._context = self._serving(host_certificate)

    def _serving(self, host_certificate: Issued) -> ssl.SSLContext:
        context = ssl.SSLContext(ssl.PROTOCOL_TLS_SERVER)
        context.load_cert_chain(certfile=str(host_certificate.pem))

        # Optional rather than required, so that both halves of the protocol can be served by one
        # gate: pairing arrives without a certificate, and everything after it arrives with one.
        context.verify_mode = ssl.CERT_OPTIONAL
        context.load_verify_locations(cafile=str(self._authority))
        context.minimum_version = ssl.TLSVersion.TLSv1_3
        context.maximum_version = ssl.TLSVersion.TLSv1_3

        # The name the client chose reaches a server through the SNI callback and nowhere else:
        # `SSLSocket.server_hostname` is the client's own copy and is `None` on this side.
        context.sni_callback = self._remember
        return context

    def _remember(
        self, wrapped: ssl.SSLObject, name: str | None, context: ssl.SSLContext
    ) -> None:
        self.names.append(name)

    def named(self) -> str:
        """The name the connection being served arrived on.

        Asked of the gate rather than read out of a list a handler holds its own alias
        of. The two disagreed about whether an entry can be `None`, which is what the
        `type: ignore` on each assignment was saying.
        """
        name = self.names[-1] if self.names else None
        if name is None:
            raise AssertionError("the connection carried no name")

        return name

    def _answer(self, raw: socket.socket) -> None:
        try:
            with self._context.wrap_socket(raw, server_side=True) as wrapped:
                head, body = _read_request(wrapped)
                if head is None:
                    return

                method, path = head
                self.requests.append((method, path))
                client = wrapped.getpeercert(binary_form=True)
                status, answer = self.handler(path, body, client)

                # What the gate does for a HEAD: no content, and no length rather than a wrong
                # one. RFC 9112 section 6.3 and RFC 9110 section 8.6. Modeled here because a
                # client that only ever met a gate which answered HEAD like GET would not be
                # tested against the gate it has.
                if method == "HEAD":
                    written = _response(status, "", "", self.retry_after)
                else:
                    written = _response(status, answer, self.declare, self.retry_after)
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
    payload = body.encode("utf-8")
    length = str(len(payload)) if declare is None else declare
    head = (
        f"HTTP/1.1 {status} .\r\n"
        "Content-Type: application/json\r\n"
        + (f"Content-Length: {length}\r\n" if length else "")
        + (f"Retry-After: {retry_after}\r\n" if retry_after is not None else "")
        + "Connection: close\r\n"
        "\r\n"
    )

    return head.encode("ascii") + payload


@pytest.fixture
def authority(tmp_path: Path) -> Authority:
    """A database's authority, and the two leaves under it the tests need."""
    return Authority(tmp_path)


@pytest.fixture
def host(authority: Authority) -> Issued:
    """The certificate the gate serves with."""
    return authority.issue("host", x509.oid.ExtendedKeyUsageOID.SERVER_AUTH, "host.pem")


@pytest.fixture
def responder(authority: Authority) -> Issued:
    """What a locked database answers with: the same authority, and the marker that says so."""
    return authority.issue(
        "KeePassGate responder",
        x509.oid.ExtendedKeyUsageOID.SERVER_AUTH,
        "responder.pem",
        uri=RESPONDER_CLAIM,
    )


@pytest.fixture
def client_certificate(authority: Authority) -> Issued:
    """A certificate the gate would have issued at pairing."""
    return authority.issue(
        "agent-invented", x509.oid.ExtendedKeyUsageOID.CLIENT_AUTH, "client.pem"
    )


@pytest.fixture
def gate(host: Issued, authority: Authority) -> Iterator[FakeGate]:
    """A gate that is listening, and stops when the test ends."""
    running = FakeGate(host, authority.pem)
    try:
        yield running
    finally:
        running.close()


@pytest.fixture
def bundle(authority: Authority, client_certificate: Issued, host: Issued) -> dict[str, Any]:
    """What `/pair/request` hands over, with the members the plugin sends.

    The identity is a PKCS#12 with no password, which is what the gate sends: what protects it in
    the database is the database, and what protects it on this side is the file. It is one
    `cryptography` serialized itself, though, not one .NET Framework's export wrote, so reading it
    says nothing about reading the plugin's.
    """
    loaded = serialization.load_pem_private_key(client_certificate.pem.read_bytes(), password=None)
    # The authority issues P-256 and nothing else, which is what makes this cast safe.
    assert isinstance(loaded, ec.EllipticCurvePrivateKey)

    return {
        "identity": base64.b64encode(
            pkcs12.serialize_key_and_certificates(
                name=b"agent-invented",
                key=loaded,
                cert=client_certificate.certificate,
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
        "expires": "2126-08-15T09:00:00Z",
        "profiles": ["reader"],
    }


@pytest.fixture
def answers() -> Callable[[int, dict[str, Any] | str], Handler]:
    """A handler that always answers the same thing, which is most of what a test wants."""
    import json

    def build(status: int, body: dict[str, Any] | str) -> Handler:
        text = body if isinstance(body, str) else json.dumps(body)
        return lambda path, request, client: (status, text)

    return build
