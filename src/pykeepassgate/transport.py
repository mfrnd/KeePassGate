"""One TLS connection, one request, then closed. The client half of what the gate serves.

The gate answers one request per connection and never offers keep-alive, so this does not pretend
otherwise: a connection is opened, a request is written, an answer is read to the end, and the
socket is closed. That is also why the name is rebuilt every time.

**Why this is written out rather than handed to a library.** The name in the ClientHello rotates
per connection and is not a host name anybody resolves, so the connection has to be made to an
address and the name given separately. `http.client` derives the name it sends from the host it
dials, which is exactly the coupling this protocol breaks. What is left to write is a status line
and a header block.

**Why the server certificate is exposed before the body is written.** During pairing the request
body carries a proof over ``tls-server-end-point``, which is a hash of the certificate the server
is serving with. So the handshake has to have happened, and its certificate has to be readable,
while the body is still being built.

**Why one identity is refused before a request reaches it.** A locked database answers with a
responder certificate, and `CA.md` 208 marks that certificate so a client can refuse to send real
requests over it. Refusing has to happen before the write rather than after the answer, because a
request body carries whatever it was going to store.
"""

from __future__ import annotations

import contextlib
import json
import socket
import ssl
from collections.abc import Iterator
from contextlib import contextmanager
from dataclasses import dataclass
from pathlib import Path
from typing import Any

from cryptography import x509

from .errors import DatabaseLocked, ProtocolError, TransportError

#: Loopback, because that is the only place the gate binds. Reaching it from elsewhere is a
#: tunnel's job and not this client's business.
LOOPBACK = "127.0.0.1"

#: How much of an answer to read before giving up on it, so that no answer can make this client
#: hold as much memory as it likes. An attachment is not what decides it: the gate hands out at
#: most 512 KiB of one (`VaultHandlers.MaxAttachmentBytes`), under 700 KiB once it is base64 inside
#: a JSON answer. What can outgrow it is a listing, since the API does not page, and one that does
#: fails here rather than being truncated.
MAX_RESPONSE_BYTES = 4 * 1024 * 1024

#: What marks the certificate a locked database answers with, and the whole of what tells one
#: apart: `AuthorityIssuer.ResponderClaim` on the plugin's side, written into the subject
#: alternative name as a URI. A responder is minted at unlock and again at the lock it answers, and
#: held only in memory, and it can say nothing but that the database is locked, so a request is
#: never sent over one.
RESPONDER_CLAIM = "kpg://host/responder"

#: How long to wait, in seconds, for anything that is not a pairing.
DEFAULT_TIMEOUT = 120.0

#: What a pairing gets instead, which is the one exchange that waits on a person. It has to outlast
#: the plugin's own sum of `PairingOptions.ApprovalTimeout` two minutes, plus
#: `PairingApprovalPrompt.Grace` fifteen seconds, plus `DatabaseWriter.LockTimeout` twenty and
#: `WriteTimeout` thirty. That sum is 185 seconds, and is why `GateOptions.PairingBudget` is five
#: minutes. Hanging up early cancels nothing: the invitation is spent and the record is written and
#: saved regardless, and only the answer is lost, leaving a record for a certificate nobody holds.
PAIRING_TIMEOUT = 300.0


@dataclass(frozen=True)
class Response:
    """What came back from one request."""

    status: int
    body: str

    def json(self) -> dict[str, Any]:
        """The body as an object, or a refusal if it is not one."""
        try:
            parsed = json.loads(self.body)
        except ValueError as error:
            raise ProtocolError(
                f"the gate answered {self.status} with something that is not JSON"
            ) from error

        if not isinstance(parsed, dict):
            raise ProtocolError(f"the gate answered {self.status} with JSON that is not an object")

        return parsed

    def error(self) -> str:
        """The one word the gate refuses with, or an empty string if it did not give one."""
        try:
            named = self.json().get("error")
        except ProtocolError:
            return ""

        return named if isinstance(named, str) else ""


def unpaired_context() -> ssl.SSLContext:
    """The context for a client that has no anchor yet, which is every client while it pairs.

    Nothing is verified here and nothing can be: the authority is what pairing is for. The server's
    proof over the certificate in use is what decides, and it is checked after the handshake rather
    than during it. Refusing the certificate here instead would refuse every honest server too.
    """
    context = ssl.SSLContext(ssl.PROTOCOL_TLS_CLIENT)
    context.check_hostname = False
    context.verify_mode = ssl.CERT_NONE
    _pin_to_tls13(context)
    return context


def paired_context(authority: Path, identity: Path) -> ssl.SSLContext:
    """The context for a client that has been through pairing.

    The authority handed over at pairing is the only anchor loaded, so a certificate that does not
    chain to it fails the handshake rather than being reported afterwards. That is the difference
    from the plugin's own side of this, where the platform's chain builder had to be told to pin
    and then checked by hand: here the trust store holds one certificate and nothing else.

    Host name checking is off because the name rotates by design and is not a name anybody
    resolves. What replaces it is the anchor: only the authority that issued this client's own
    certificate can produce a host certificate this will accept.
    """
    context = ssl.SSLContext(ssl.PROTOCOL_TLS_CLIENT)
    context.check_hostname = False
    context.verify_mode = ssl.CERT_REQUIRED
    context.load_verify_locations(cafile=str(authority))
    context.load_cert_chain(certfile=str(identity))
    _pin_to_tls13(context)
    return context


class Connection:
    """One open connection. Not reusable, because the gate does not reuse them either."""

    def __init__(self, wrapped: ssl.SSLSocket) -> None:
        """Take a socket that has already shaken hands.

        Keeps the certificate it was served with, which the proofs are computed over, and reads
        off it whether the identity that answered is the one a locked database has.
        """
        self._socket = wrapped
        der = wrapped.getpeercert(binary_form=True)
        if not der:
            raise TransportError("the server sent no certificate")

        self.server_certificate = der
        self.responder = _responder(der)

    def send(self, method: str, path: str, body: str = "") -> Response:
        """Write one request and read the whole answer, unless a responder answered the handshake.

        Nothing is written to a responder. What a locked database answers is `503 locked` on
        every path and method, decided before it reads anything, so there is no request this
        could carry that would be answered; and an impostor that came by the responder key while
        the database was locked reads nothing but could answer with a fabricated secret. Waiting
        for the answer to decide that has already handed over the request, which on a write is
        the value being stored.
        """
        if self.responder:
            raise DatabaseLocked(
                "the connection was served with the responder identity, which only a locked "
                "database has, so no request was sent over it"
            )

        _check(method, path)
        payload = body.encode("utf-8")
        head = (
            f"{method} {path} HTTP/1.1\r\n"
            # The name this connection was made under, which is what the gate resolved it by. It is
            # sent because HTTP/1.1 requires a `Host`, not because the gate reads it.
            f"Host: {self._socket.server_hostname}\r\n"
            "Content-Type: application/json\r\n"
            f"Content-Length: {len(payload)}\r\n"
            "\r\n"
        )

        try:
            self._socket.sendall(head.encode("ascii") + payload)
            raw = self._read_all()
        except (OSError, ssl.SSLError) as error:
            raise TransportError(f"the connection failed: {error}") from error

        return _parse(raw)

    def close(self) -> None:
        """Close the socket, whatever state it is in."""
        # Suppressed: a socket that is already broken is what this method is for.
        with contextlib.suppress(OSError):
            self._socket.close()

    def _read_all(self) -> bytes:
        """Read until the answer is whole, which is what it said rather than when it ends.

        The gate frames every response and closes after it, so reading to the close gives the
        right answer for one that completed. It gives the wrong one for everything else: a
        response cut short reads as a complete one, and a peer that frames correctly and does
        not close costs the whole timeout and then has its answer thrown away. Neither peer
        has to be the gate, because during pairing this client has no anchor and verifies
        nothing.
        """
        buffer = bytearray()
        declared: int | None = None
        body_at = -1

        while True:
            chunk = self._socket.recv(65536)
            if not chunk:
                # Nothing declared a length, or the peer stopped early. `_parse` says which.
                return bytes(buffer)

            buffer += chunk

            # Checked after the append rather than before it. Before, the loop enters `recv` with
            # the buffer already at the cap, so a peer feeding it gets one whole chunk past the
            # number this is here to enforce.
            if len(buffer) > MAX_RESPONSE_BYTES:
                raise TransportError(f"the answer is over {MAX_RESPONSE_BYTES} bytes")

            if body_at < 0:
                blank = buffer.find(b"\r\n\r\n")
                if blank < 0:
                    continue

                body_at = blank + 4
                declared = _declared_length(bytes(buffer[:blank]).decode("ascii", "replace"))

            if declared is not None and len(buffer) - body_at >= declared:
                return bytes(buffer)


@contextmanager
def connect(
    port: int,
    context: ssl.SSLContext,
    server_name: str,
    host: str = LOOPBACK,
    timeout: float = DEFAULT_TIMEOUT,
) -> Iterator[Connection]:
    """Open one connection, hand it over, and close it however this ends."""
    try:
        raw = socket.create_connection((host, port), timeout=timeout)
    except OSError as error:
        raise TransportError(f"nothing is listening on {host}:{port}: {error}") from error

    try:
        wrapped = context.wrap_socket(raw, server_hostname=server_name)
    except (OSError, ssl.SSLError) as error:
        raw.close()
        # The reason is the TLS library's, and it stays in the message because the causes want
        # different things done. Against a paired context: a certificate that does not chain to
        # the authority pinned at pairing, which is an authority recreated since or something else
        # on the port; one that has expired, the gate's host certificate or its authority, which
        # only recreating the authority mends, and then this client pairs again; or a peer that
        # will not speak TLS 1.3. The gate also drops a connection before any TLS when no open
        # database answers to the name it carries, and that ends the handshake here as well.
        raise TransportError(f"the handshake failed: {error}") from error

    connection = Connection(wrapped)
    try:
        yield connection
    finally:
        connection.close()


def _responder(der: bytes) -> bool:
    """Whether this certificate is the one a locked database answers with.

    Read off the marker rather than off the subject name, because the subject is a convention and
    `CA.md` 208 asks for something a client can decide on. The marker cannot be put on a certificate
    or taken off one either: what the authority signed is what arrives, and a paired client accepts
    nothing the authority did not sign.

    An encoding this cannot read is not a responder, which is the direction that keeps an honest
    connection working: a certificate the authority issued is one this reads, and one it did not
    issue never gets past the pin.
    """
    try:
        certificate = x509.load_der_x509_certificate(der)
        names = certificate.extensions.get_extension_for_class(x509.SubjectAlternativeName)
    # Everything reading a certificate can refuse with: bytes that are not one, no subject
    # alternative name at all, two of them, and a name of a kind this library will not decode.
    except (
        ValueError,
        x509.ExtensionNotFound,
        x509.DuplicateExtension,
        x509.UnsupportedGeneralNameType,
    ):
        return False

    return RESPONDER_CLAIM in names.value.get_values_for_type(x509.UniformResourceIdentifier)


def _pin_to_tls13(context: ssl.SSLContext) -> None:
    """Nothing older is offered, so nothing older can be negotiated.

    The gate serves TLS 1.3 only. Pinning both ends of the range rather than the minimum means a
    downgrade is a failed handshake here rather than a connection this client did not intend to
    make.
    """
    context.minimum_version = ssl.TLSVersion.TLSv1_3
    context.maximum_version = ssl.TLSVersion.TLSv1_3


def _check(method: str, path: str) -> None:
    """Refuse anything that could not be a request line, before it reaches the wire.

    The gate reads a head as US-ASCII and refuses a byte that is not, so a path this client cannot
    spell is refused here rather than sent. Two things go wrong otherwise, and neither is the
    caller's fault to discover on the wire. A carriage return or a newline in a path
    replaces the whole head the gate parses, headers and all, with whatever the caller wrote;
    and a character outside ASCII raises `UnicodeEncodeError` out of `encode`, which is a
    `ValueError` and so is not a `GateError` at all.

    `send` is the documented way to reach a route this client has no method for, so its arguments
    are caller data, and the threat model says a caller can be prompt-injected.
    """
    if not method.isascii() or not method.isalpha():
        raise ProtocolError(f"that is not a method: {method!r}")

    if not path.startswith("/") or not path.isascii():
        raise ProtocolError(f"that is not a path this client will send: {path!r}")

    if any(character in path for character in " \r\n\t\x00"):
        raise ProtocolError(f"that path holds something a request line cannot: {path!r}")


def _declared_length(head: str) -> int | None:
    """What the answer said its body would be, if it said."""
    for line in head.split("\r\n")[1:]:
        name, _, value = line.partition(":")
        if name.strip().lower() != "content-length":
            continue

        try:
            return int(value.strip())
        except ValueError:
            raise ProtocolError(f"that is not a content length: {value.strip()!r}") from None

    return None


def _parse(raw: bytes) -> Response:
    """Read the status line and the body, and nothing else.

    The gate sends a `Content-Length` and closes, so the body is what follows the blank line. There
    is no chunked encoding to handle because the gate refuses to produce one.

    **The length is read rather than assumed.** Believing the close instead makes a truncated
    answer indistinguishable from a complete one, and an answer whose body never arrived would
    become an empty object. A caller cannot tell that from a gate that said so.
    """
    if not raw:
        raise TransportError("the connection was closed without an answer")

    blank = raw.find(b"\r\n\r\n")
    if blank < 0:
        raise ProtocolError("the answer has no header block")

    line = raw[: raw.find(b"\r\n")].decode("ascii", "replace")
    parts = line.split(" ", 2)
    if len(parts) < 2 or not parts[0].startswith("HTTP/"):
        raise ProtocolError(f"that is not a status line: {line!r}")

    try:
        status = int(parts[1])
    except ValueError as error:
        raise ProtocolError(f"that is not a status: {parts[1]!r}") from error

    body = raw[blank + 4 :]
    declared = _declared_length(raw[:blank].decode("ascii", "replace"))
    if declared is not None and len(body) < declared:
        raise TransportError(
            f"the answer stopped {declared - len(body)} bytes short of its own content length"
        )

    return Response(status=status, body=body.decode("utf-8", "replace"))
