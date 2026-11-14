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

**The refusals it makes on its own, as the plugin makes them**: every request the plugin's reader
refuses before any route sees it, answered with the plugin's status and reason before the handler
is asked anything. Among them a head over 8 KiB, `431`; a target over 2048 characters, `414`; a
target that is no path the plugin accepts, a `..` segment or a character RFC 3986 escapes, `400`;
a version other than HTTP/1.1, `505`; a request without a `Host`, a header sent twice or a header
value holding a control character, `400`; and a body over 1 MiB, `413`. A refused `HEAD` is
answered without content, as the plugin's is. And a handler that raises is answered `500
internal error`, as the plugin answers a defect of its own, with what it raised kept in
`FakeGate.failures`.

**And the framing it gives an answer is the plugin's.** The plugin writes every answer that grows
with what a database holds as it is sent, in chunks: each listing, of groups, of a group's
subgroups or entries, of entries, and of an entry's fields or attachments, and a read of an entry
or of a single field. So a `GET` of any of those paths that a handler answers `200` goes out here
under `Transfer-Encoding: chunked`, in chunks of the size the plugin writes, whatever query the
path carries. Every other answer goes out under a `Content-Length`: a single group, an
attachment, the routes about the gate and the caller, every refusal, and the answer to any
method but `GET`. A client that only ever met answers under a length would not be tested against
the gate it has. `FakeGate.declare` puts such an answer back under a length.

**And one route it answers on its own, unless told not to**: `GET /gate/status`, which a paired
client asks before its first request to compare the gate's version with its own. It answers with
`FakeGate.version`, this client's own unless a test sets another, so a program's script need not
answer for it, and a test of what the program does with a gate it is refused sets an older version.
Set to `None`, the route goes to the handler like any other. Answered before the handler is asked,
it is no more in `FakeGate.requests` than a refusal is.

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
import hashlib
import json
import re
import socket
import ssl
import string
import threading
import time
from collections.abc import Callable, Sequence
from dataclasses import dataclass, field
from http import HTTPStatus
from pathlib import Path
from types import TracebackType

from cryptography import x509
from cryptography.hazmat.primitives import hashes, serialization
from cryptography.hazmat.primitives.asymmetric import ec
from cryptography.x509.oid import NameOID

from .client import STATUS_PATH
from .compatibility import CLIENT_VERSION

#: What the plugin's authority uses, so that anything measured here is measured against the same
#: shape: ECDSA on P-256, signed with SHA-256, which is also why the channel binding is SHA-256.
CURVE = ec.SECP256R1()

#: Answers a request: the path, the body, and the client certificate if one was offered, with the
#: status and the body to answer it with. The method is asked of the gate, with `FakeGate.method`,
#: so that one path answered differently for `GET` and `DELETE` needs no counting of calls. The
#: path is the request's target as it was sent, with its query, so a script answers a listing of
#: entries asked for with `?includeExpired=false` apart from the listing asked for without it.
Handler = Callable[[str, str, bytes | None], tuple[int, str]]

#: The plugin's `GateOptions.MaxHeadBytes`, the blank line that ends a head counted in it.
_MAX_HEAD = 8192

#: The plugin's `GateOptions.MaxBodyBytes`.
_MAX_BODY = 1048576

#: The longest target the plugin's reader takes, in characters.
_MAX_TARGET = 2048

#: What a method or a header name is made of, RFC 9110 section 5.6.2's `tchar`.
_TOKEN = frozenset(string.ascii_letters + string.digits + "!#$%&'*+-.^_`|~")

#: What stands for itself in a path segment, as the plugin's `PercentEncoding` takes it: RFC
#: 3986's `pchar` less the escape, with `/` and `?` beside them in a query.
_SEGMENT = frozenset(string.ascii_letters + string.digits + "-._~!$&'()*+,;=:@")


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


def written(etag: str | None = None, uuid: str | None = None) -> tuple[int, str]:
    """What the plugin answers a write that landed, with the tag it left when it gave one.

    `200` and `{"ok":true}`, or `201` and the `uuid` that addresses what a create made, with
    `etag` beside either when there is one: the plugin puts a write's tag in the body, quoted as
    `ETag` writes one, and only for a client holding `read` on the node, over what that client
    may read. A handler returns it, as it would any answer. A tag that is not an entity tag, as
    `FakeGate.tag` holds a tag to, is refused with `ValueError`.
    """
    document: dict[str, object] = {"ok": True} if uuid is None else {"uuid": uuid}
    if etag is not None:
        document["etag"] = _entity_tag(etag)

    return (200 if uuid is None else 201), json.dumps(document, separators=(",", ":"))


def _entity_tag(etag: str) -> str:
    """An entity tag as given, or `ValueError` where RFC 9110 section 8.8.3 would call it none.

    A strong tag only, since the plugin gives no other: a quote, visible ASCII but a quote, which
    is the section's tag characters less the bytes beyond ASCII a header cannot carry here, and a
    quote.
    """
    if (
        len(etag) < 2
        or etag[0] != '"'
        or etag[-1] != '"'
        or any(not "!" <= character <= "~" or character == '"' for character in etag[1:-1])
    ):
        raise ValueError(f"that is not a tag the gate could send: {etag!r}")

    return etag


def identified(
    subject: str,
    admission_key: str,
    expires: dt.datetime,
    profiles: Sequence[str] = (),
    removed_profiles: Sequence[str] = (),
    *,
    client_key: str | None = None,
    profiles_unreadable: bool = False,
) -> tuple[int, str]:
    """What the plugin answers `GET /gate/whoami` for a client its record holds so.

    `profiles` are the record's profiles the database still names and `removed_profiles` the
    rest; `client_key` is the admission key unless a renewal has changed the certificate since
    pairing. `profiles_unreadable` is a database whose list of profiles will not read, which the
    plugin answers by naming every profile on the record as removed and none as counted, so both
    lists given end up under `removedProfiles`. A handler returns it, as it would any answer.
    """
    counted = [] if profiles_unreadable else list(profiles)
    removed = [*profiles, *removed_profiles] if profiles_unreadable else list(removed_profiles)
    document = {
        "subject": subject,
        "clientKey": admission_key if client_key is None else client_key,
        "admissionKey": admission_key,
        "expires": expires.astimezone(dt.UTC).strftime("%Y-%m-%dT%H:%M:%SZ"),
        "profiles": counted,
        "removedProfiles": removed,
        "profilesUnreadable": profiles_unreadable,
    }
    return 200, json.dumps(document, separators=(",", ":"))


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
        #: mid-write produces: a complete head, a `Content-Length`, and a body short of it, or an
        #: answer in chunks that stops before its last chunk is whole.
        self.truncate = 0

        #: What to put in `Content-Length` instead of the real one. The empty string writes no
        #: header at all, which is what an answer to a HEAD does: RFC 9110 section 9.3.2 permits
        #: the missing length, and this gate has to be able to produce one. Set, it frames every
        #: answer by that length, an answer the plugin writes in chunks among them.
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

        #: What the handler raised, in order, each answered `500` as the plugin answers a defect of
        #: its own: a test that scripted something impossible finds out here, rather than from a
        #: connection that closed without a word.
        self.failures: list[BaseException] = []

        #: How long a peer may stay quiet, in seconds, before the thread answering it gives up, so
        #: one that goes quiet mid-request ends its thread rather than parking it.
        self.quiet = 10.0

        #: The version `GET /gate/status` is answered with, without asking the handler: this
        #: client's own, which it works with, unless a test sets another. `None` hands the route
        #: to the handler like any other, for a test that scripts what the status route answers.
        self.version: str | None = CLIENT_VERSION

        # Named as the plugin names it: the first sixteen characters of SHA-256 over the
        # authority's DER, in lowercase hex.
        anchor = x509.load_pem_x509_certificate(authority.read_bytes())
        digest = hashlib.sha256(anchor.public_bytes(serialization.Encoding.DER)).hexdigest()
        self._database = digest[:16]

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

    def if_match(self) -> str | None:
        """The `If-Match` the request being answered carried, as its value arrived, or `None`.

        Asked from inside the handler, as `method` is, so a test can answer `412` to a request
        made against a tag of a node that has changed since. Whether a tag matches is the test's
        to decide: this gate knows no node, so it holds none, and no rule either, so a test of a
        client the plugin hands no tag to answers its `If-Match` with `412` itself.
        """
        self._in_handler()
        found: str | None = getattr(self._answering, "if_match", None)
        return found

    def tag(self, etag: str) -> None:
        """Answer the read being answered with this `ETag`, exactly as given.

        What the plugin does for a read about one group or one entry, to a client holding `read`
        on it, over what that client may read. A write's tag is not a header: the plugin puts it
        in the body, as `etag`, which `written` makes. Called from inside the handler, before it
        returns, and for that answer alone.

        Refused with `ValueError` unless it is an entity tag, visible ASCII between two quotes and
        no quote inside, RFC 9110 section 8.8.3: the plugin's own are 32 hex digits, and a test may
        give any other, since a client holds a tag as it arrived. The refusal is answered as the
        plugin answers a defect, so a test sees it in `failures` rather than in a connection that
        closed without a word.
        """
        self._in_handler()
        self._answering.etag = _entity_tag(etag)

    def _in_handler(self) -> None:
        """Refuse a question about the request being answered from outside a handler."""
        self.method()

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

    def _status(self, version: str) -> str:
        """What the plugin's status route answers, from a build with this version and no commit."""
        document = {"ok": True, "database": self._database, "version": version, "revision": ""}
        return json.dumps(document, separators=(",", ":"))

    def _answer(self, raw: socket.socket) -> None:
        try:
            with self._context.wrap_socket(raw, server_side=True) as wrapped:
                read = _read_request(wrapped)
                if read is None:
                    return

                method, path, body, headers = read.method, read.path, read.body, read.headers
                if read.refused is not None:
                    # Refused as the plugin refuses it, before any route sees it, and a HEAD
                    # without content, as everywhere else. What the request still has on its way
                    # is taken after the answer, as the plugin takes it, since a socket closed over
                    # unread bytes is reset and the reset can throw the refusal away unread.
                    status, reason = read.refused
                    refusal = "" if method == "HEAD" else json.dumps({"error": reason})
                    wrapped.sendall(_response(status, refusal, "" if method == "HEAD" else None))
                    _discard(wrapped, read.unread)
                    return

                version = self.version
                if version is not None and method == "GET" and path == STATUS_PATH:
                    wrapped.sendall(_response(200, self._status(version)))
                    return

                self.requests.append((method, path))
                client = wrapped.getpeercert(binary_form=True)
                self._answering.method = method
                self._answering.if_match = headers.get("if-match")
                self._answering.etag = None
                etag: str | None = None
                try:
                    status, answer = self.handler(path, body, client)
                    etag = self._answering.etag
                except Exception as failure:
                    # A script's defect, answered rather than left to end the thread silently.
                    self.failures.append(failure)
                    status, answer = 500, '{"error":"internal error"}'
                finally:
                    self._answering.method = None
                    self._answering.if_match = None
                    self._answering.etag = None

                # What the gate does for a HEAD: no content, and no length rather than a wrong
                # one. RFC 9112 section 6.3 and RFC 9110 section 8.6. Modeled here because a
                # client that only ever met a gate which answered HEAD like GET would not be
                # tested against the gate it has.
                retry_after = self.retry_after if status == 503 else None
                word = _unavailable_word(answer) if status == 503 else None
                if method == "HEAD":
                    written = _response(status, "", "", retry_after, etag, word)
                else:
                    # In chunks where the plugin writes the answer as it is sent, for the reason
                    # a HEAD is modeled above. The query takes no part in which answer that is.
                    chunked = (
                        method == "GET"
                        and status == 200
                        and self.declare is None
                        and _SENT_IN_CHUNKS.fullmatch(path.partition("?")[0]) is not None
                    )
                    written = _response(
                        status, answer, self.declare, retry_after, etag, word, chunked
                    )
                if self.truncate:
                    written = written[: len(written) - self.truncate]

                wrapped.sendall(written)
                if self.linger:
                    time.sleep(self.linger)
        except (OSError, ssl.SSLError):
            return


@dataclass(frozen=True)
class _Read:
    """A request as it arrived, or the status and reason the plugin's reader refuses it with.

    `method` is empty where the request never got as far as naming a method, and `unread` is how
    much of the body the request announced is still on its way when it is refused.
    """

    method: str
    path: str = ""
    body: str = ""
    headers: dict[str, str] = field(default_factory=dict)
    refused: tuple[int, str] | None = None
    unread: int = 0


def _read_request(wrapped: ssl.SSLSocket) -> _Read | None:
    """The request, with every header by its name in lower case, or `None` when none arrived.

    Refused where the plugin's `HttpRequestReader` refuses it, in the same order and with the same
    status and reason, so that a program tested here is not tested against a gate that takes
    more than the plugin does. The whole head is read before it is judged, where the plugin stops
    at its cap: what the peer meets is the same answer either way.
    """
    buffer = bytearray()
    while b"\r\n\r\n" not in buffer:
        chunk = wrapped.recv(4096)
        if not chunk:
            return None

        buffer += chunk

    head, rest = bytes(buffer).split(b"\r\n\r\n", 1)
    lines = head.decode("latin-1").split("\r\n")
    first = lines[0].split(" ")
    named = first[0] if len(first) > 1 and _is_token(first[0]) else ""
    unread = max(_announced(lines[1:]) - len(rest), 0)

    def refuse(status: int, reason: str, method: str = named) -> _Read:
        return _Read(method, refused=(status, reason), unread=unread)

    if len(head) + 4 > _MAX_HEAD:
        return refuse(431, f"the head is over {_MAX_HEAD} bytes")

    if not head.isascii():
        return refuse(400, "the head is not ascii")

    if len(first) != 3:
        return refuse(400, "the request line is not three parts", "")

    method, target, version = first
    if not _is_token(method):
        return refuse(400, "the method is not a token", "")

    if not target.startswith("/"):
        return refuse(400, "the target is not an absolute path")

    if len(target) > _MAX_TARGET:
        return refuse(414, f"the target is over {_MAX_TARGET} characters")

    if version != "HTTP/1.1":
        return refuse(505, "only HTTP/1.1 is accepted")

    if not _usable_target(target):
        return refuse(400, "the target is not a path this server accepts")

    headers: dict[str, str] = {}
    for line in lines[1:]:
        if line[:1] in (" ", "\t"):
            return refuse(400, "folded headers are not accepted")

        name, colon, value = line.partition(":")
        if not colon or not name:
            return refuse(400, "a header line has no name")

        if not _is_token(name):
            return refuse(400, "a header name is not a token")

        value = value.strip(" \t")
        if any(
            character != "\t" and (character < " " or character == "\x7f") for character in value
        ):
            return refuse(400, "a header value holds a control character")

        name = name.lower()
        if name == "transfer-encoding":
            return refuse(501, "transfer encodings are not accepted")

        if name in headers:
            return refuse(400, "a header is repeated")

        headers[name] = value

    if "host" not in headers:
        return refuse(400, "a request needs a host header")

    length = 0
    if "content-length" in headers:
        given = headers["content-length"]
        if not given.isascii() or not given.isdigit() or int(given) >= 2**63:
            return refuse(400, "the content length is not a number")

        length = int(given)
        if length > _MAX_BODY:
            return refuse(413, f"the body is over {_MAX_BODY} bytes")
    elif method in ("POST", "PUT", "PATCH"):
        return refuse(411, "a body needs a content length")

    while len(rest) < length:
        chunk = wrapped.recv(4096)
        if not chunk:
            return _Read(method, refused=(400, "the body ended early"))

        rest += chunk

    try:
        body = rest[:length].decode("utf-8")
    except UnicodeDecodeError:
        return _Read(method, refused=(400, "the body is not utf-8"))

    return _Read(method, target, body, headers)


def _is_token(text: str) -> bool:
    """Whether a method or a header name is RFC 9110's `token`, one character or more."""
    return bool(text) and all(character in _TOKEN for character in text)


def _usable_target(target: str) -> bool:
    """Whether the plugin's reader takes a target as a path and a query it can split.

    Each segment of the path, and each name and value of the query, decoded as `PercentEncoding`
    decodes them: a character that does not stand for itself, an escape that is not two hex digits
    or stands for a control, and what is not UTF-8 once decoded, all refused, and so is a segment
    of `.` or `..`, a query name that is empty, and a query name given twice.
    """
    path, _, query = target.partition("?")
    segments = [_decoded(raw, in_query=False) for raw in path[1:].split("/")]
    if any(segment is None or segment in (".", "..") for segment in segments):
        return False

    names: set[str] = set()
    for pair in query.split("&") if query else []:
        if not pair:
            continue

        raw_name, _, raw_value = pair.partition("=")
        name = _decoded(raw_name, in_query=True)
        if not name or _decoded(raw_value, in_query=True) is None or name in names:
            return False

        names.add(name)

    return True


def _decoded(text: str, *, in_query: bool) -> str | None:
    """A segment or a query part with its escapes decoded, or `None` where the plugin refuses it."""
    octets = bytearray()
    at = 0
    while at < len(text):
        character = text[at]
        if character == "+" and in_query:
            octets += b" "
        elif character != "%":
            if character not in _SEGMENT and not (in_query and character in "/?"):
                return None

            octets += character.encode("ascii")
        else:
            escape = text[at + 1 : at + 3]
            if len(escape) != 2 or any(digit not in string.hexdigits for digit in escape):
                return None

            value = int(escape, 16)
            if value < 0x20 or value == 0x7F:
                return None

            octets.append(value)
            at += 2

        at += 1

    try:
        decoded = octets.decode("utf-8")
    except UnicodeDecodeError:
        return None

    # `char.IsControl`, which is the C0 and C1 controls and DEL, what the plugin refuses.
    if any(character < " " or "\x7f" <= character <= "\x9f" for character in decoded):
        return None

    return decoded


def _announced(lines: list[str]) -> int:
    """How much body a head announced, read as leniently as the plugin's `AnnouncedBy` reads it.

    The largest `Content-Length` that is digits alone, from every header line whether or not the
    line is refused, and nothing for a request that sent `Expect`, which is waiting to be asked
    for its body rather than sending it.
    """
    length = 0
    for line in lines:
        name, colon, value = line.partition(":")
        value = value.strip(" \t")
        if not colon:
            continue

        if name.lower() == "expect" and value:
            return 0

        if name.lower() == "content-length" and value.isascii() and value.isdigit():
            length = max(length, int(value))

    return length


def _discard(wrapped: ssl.SSLSocket, count: int) -> None:
    """Take what a refused request still has on its way off the wire, up to `count` bytes."""
    while count > 0:
        chunk = wrapped.recv(min(count, 65536))
        if not chunk:
            return

        count -= len(chunk)


def _response(
    status: int,
    body: str,
    declare: str | None = None,
    retry_after: str | None = None,
    etag: str | None = None,
    unavailable: str | None = None,
    chunked: bool = False,
) -> bytes:
    """The head the plugin's `HttpResponse.Write` writes, and the body.

    The reason phrase is RFC 9110's, and the two headers that keep an answer out of a cache and a
    browser's guessing are there as the plugin sends them. The one header a script cannot give is
    `Allow`, which the plugin puts on every `405` and this does not. `unavailable` is the word a
    `503` names in `KPG-Unavailable` as well as in its body, which the answer to a HEAD keeps.
    With `chunked`, the body goes out as the plugin writes an answer it sends as it is written:
    under `Transfer-Encoding: chunked` and no length.
    """
    payload = body.encode("utf-8")
    if chunked:
        framing = "Transfer-Encoding: chunked\r\n"
        payload = _in_chunks(payload)
    else:
        length = str(len(payload)) if declare is None else declare
        framing = f"Content-Length: {length}\r\n" if length else ""

    try:
        phrase = HTTPStatus(status).phrase
    except ValueError:
        phrase = "Unknown"

    head = (
        f"HTTP/1.1 {status} {phrase}\r\n"
        "Content-Type: application/json; charset=utf-8\r\n"
        + framing
        + "Cache-Control: no-store\r\n"
        "X-Content-Type-Options: nosniff\r\n"
        + (f"Retry-After: {retry_after}\r\n" if retry_after is not None else "")
        + (f"KPG-Unavailable: {unavailable}\r\n" if unavailable is not None else "")
        + (f"ETag: {etag}\r\n" if etag is not None else "")
        + "Connection: close\r\n"
        "\r\n"
    )

    return head.encode("ascii") + payload


#: The paths whose `200` to a `GET` the plugin writes as it is sent, in chunks, which is every
#: answer that grows with what a database holds: each listing, and a read of an entry or of a
#: single field. A read of a single group or of an attachment goes out under a length, as every
#: other answer does. Matched against the path of a target, without its query.
_SENT_IN_CHUNKS = re.compile(
    r"/groups(/[^/]+/(groups|entries))?"
    r"|/entries(/[^/]+(/fields(/[^/]+)?|/attachments)?)?"
)

#: The most bytes of content the plugin puts in a chunk, `ChunkedContent.Most` on its side.
_MOST_IN_A_CHUNK = 16376


def _in_chunks(payload: bytes) -> bytes:
    """A body framed as the plugin's `ChunkedContent` frames it.

    Each chunk is its size in hex, a line end, at most `_MOST_IN_A_CHUNK` bytes and a line end,
    and the chunk of no bytes ends the body, with no extension and no trailer.
    """
    parts = (payload[at : at + _MOST_IN_A_CHUNK] for at in range(0, len(payload), _MOST_IN_A_CHUNK))
    return b"".join(b"%x\r\n%s\r\n" % (len(part), part) for part in parts) + b"0\r\n\r\n"


def _unavailable_word(answer: str) -> str | None:
    """The word a `503`'s body names under `error`, which the plugin carries in a field too.

    Read back out of the body, as the plugin reads its own, so that a script answering `503`
    names the word once. Only a word of ASCII letters is carried, and anything else sends none.
    """
    try:
        parsed = json.loads(answer)
    except ValueError:
        return None

    word = parsed.get("error") if isinstance(parsed, dict) else None
    if isinstance(word, str) and word.isascii() and word.isalpha():
        return word

    return None
