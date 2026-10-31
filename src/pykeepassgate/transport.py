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
import enum
import json
import math
import socket
import ssl
import time
from collections.abc import Callable, Iterator
from contextlib import contextmanager
from dataclasses import dataclass, field
from datetime import UTC, datetime
from pathlib import Path
from typing import Any

from cryptography import x509

from .errors import (
    AuthorityUndecided,
    DatabaseLocked,
    GateBusy,
    GateUnavailable,
    ProtocolError,
    StorageError,
    TransportError,
)

#: Where the gate binds, and therefore where a client dials unless it is told otherwise. Reaching
#: a gate from another machine is a tunnel's job, and a tunnel puts its own end somewhere: that is
#: the whole of why `connect` takes a host at all, and why the default is this one.
LOOPBACK = "127.0.0.1"

#: How much of an answer a client reads before giving up on it unless it is told otherwise, which
#: `max_response_bytes` does, so that no answer can make this client hold as much memory as it
#: likes. An attachment is not what decides it: the gate hands out at most 512 KiB of one
#: (`VaultHandlers.MaxAttachmentBytes`), under 700 KiB once it is base64 inside a JSON answer. What
#: can outgrow it is a listing, since the plugin does not page (`API.md` §407), and one that does
#: fails whole rather than being truncated. So a caller whose database is large enough gets a
#: setting to raise rather than a number to patch.
MAX_RESPONSE_BYTES = 4 * 1024 * 1024

#: What marks the certificate a locked database answers with, and the whole of what tells one
#: apart: `AuthorityIssuer.ResponderClaim` on the plugin's side, written into the subject
#: alternative name as a URI. A responder is minted at unlock and again at the lock it answers, and
#: held only in memory, and it can say nothing but that the database is locked, so a request is
#: never sent over one.
RESPONDER_CLAIM = "kpg://host/responder"

#: How long any one wait on the socket may last, in seconds, for anything that is not a pairing:
#: the dial, the handshake, and each read of the answer, each on its own. So it ends a wait on a
#: peer that has stopped sending, and not a connection to one that goes on sending, however
#: slowly; a caller that needs a bound on a whole call puts one around the call. It says nothing
#: about waiting out a refusal, which is what the three budgets below are for.
DEFAULT_CONNECTION_TIMEOUT = 120.0

#: What the socket gets instead for the two exchanges that wait on a person, a pairing and a
#: renewal, and the least a renewal waits whatever the client was given. It has to outlast the
#: plugin's own sum: `PairingOptions.ApprovalTimeout` two minutes waiting for the screen while
#: another pairing or renewal holds it, the same two minutes again for the dialog plus
#: `PairingApprovalPrompt.Grace` fifteen seconds, then `DatabaseWriter.LockTimeout` twenty and
#: `WriteTimeout` thirty. That sum is 305 seconds, and is why `GateOptions.PairingBudget` is six
#: minutes for both; this is the same six, since the gate closes the connection then and waiting
#: past it buys nothing. Hanging up early cancels nothing: the record is written and saved
#: regardless, an invitation spent with it, and only the answer is lost, leaving a record for a
#: certificate nobody holds.
PAIRING_CONNECTION_TIMEOUT = 360.0

#: How long to go on waiting out a `503 busy`, in seconds, and `None` for not waiting at all, which
#: is what a client does unless it is told otherwise: the refusal is raised, and what to do about it
#: is the caller's. There is one budget for each word a `503` is known to carry, because the three
#: end differently and an agent willing to wait out one is not necessarily willing to wait out
#: another, which one number could not say. Busy is KeePass in the middle of something of its own,
#: so it ends by itself.
DEFAULT_RETRY_BUSY_TIMEOUT: float | None = None

#: The same for a `503 locked`. A locked KeePass waits for a person to come back to their desk, and
#: a lock can arrive on its own from an idle timeout, a minimize, a Windows lock or a suspend, so it
#: can last an hour as easily as a minute.
DEFAULT_RETRY_LOCKED_TIMEOUT: float | None = None

#: The same for a `503 undecided`: a database holding two authorities that nobody has chosen
#: between yet. It is rare, it lasts until somebody chooses at KeePass, and it can end with the
#: authority this client belongs to not kept, which no amount of waiting mends. A `503` whose word
#: this client does not know is waited out by none of the three.
DEFAULT_RETRY_UNDECIDED_TIMEOUT: float | None = None

#: What to wait when the gate refused without saying when to come back. The gate sends
#: `Retry-After: 2` on every `503`, so what is left is an older gate, a refusal that reached no
#: response at all (a responder certificate is refused before a request is sent), or a header in
#: the date form, which this does not read because a date carries a clock the two sides have to
#: agree about. Not retrying in those cases would make a budget silently do nothing, which is the
#: failure a caller cannot see; waiting the two seconds the gate asks for everywhere else does
#: what they asked for.
FALLBACK_RETRY_AFTER = 2.0


def response_limit(max_response_bytes: int) -> int:
    """Refuse a cap on the answer that no answer could fit under, and hand back one that will do.

    Refused where it is given rather than where it is first used, so a client built with nothing
    to read into says so before it dials anything, or before a pairing has spent anything.
    """
    # A whole number, as the command line takes one: NaN compares false with every length, so a
    # cap of NaN would cap nothing, and `True` is a cap of one byte nobody meant.
    if (
        isinstance(max_response_bytes, bool)
        or not isinstance(max_response_bytes, int)
        or max_response_bytes <= 0
    ):
        raise ValueError(
            "max_response_bytes caps an answer at a positive number of bytes, and "
            f"{max_response_bytes!r} is not one"
        )

    return max_response_bytes


def port_number(port: int) -> int:
    """Refuse a port TCP does not have, and hand back one it does.

    Refused where it is given, as a cap is, rather than by `socket`, which raises `OverflowError`
    for it from inside a call: not a `GateError`, so a caller promised one root misses it, and the
    command line would print a traceback where it documents an exit code.
    """
    if isinstance(port, bool) or not isinstance(port, int) or not 0 < port <= 65535:
        raise ValueError(f"port is a whole number from 1 to 65535, not {port!r}")

    return port


def connection_limit(connection_timeout: float) -> float:
    """Refuse a socket timeout that cannot bound a wait, and hand back one that can.

    Zero makes the socket non-blocking, so the dial fails at once and reads as nothing listening,
    and a negative number, NaN or infinity leaves `socket` as `ValueError` or `OverflowError`,
    from deep inside a call rather than where it was given.
    """
    if not _finite(connection_timeout) or connection_timeout <= 0:
        raise ValueError(
            "connection_timeout is a finite number of seconds above zero, "
            f"not {connection_timeout!r}"
        )

    return connection_timeout


def retry_limit(name: str, seconds: float | None) -> float | None:
    """Refuse a retry budget that cannot be spent, and hand back one that can.

    `None` is no retry at all, and zero is a budget that waits for nothing. NaN compares false
    with every delay, so a budget of NaN is never spent and a refusal is asked again forever, and
    a negative one or infinity reads as a decision it is not.
    """
    if seconds is not None and (not _finite(seconds) or seconds < 0):
        raise ValueError(
            f"{name} is None or a finite number of seconds from zero, not {seconds!r}"
        )

    return seconds


def _finite(value: object) -> bool:
    """Whether this is a number of seconds at all: finite, and not `True` or `False`."""
    return isinstance(value, int | float) and not isinstance(value, bool) and math.isfinite(value)


#: What a password for this client's key may be given as: the password, as text or as bytes, or
#: something that returns one when it is needed, which is only when the key stored turns out to be
#: encrypted. Text is encoded as UTF-8, which is what `ssl` does with a text password too.
KeyPassword = str | bytes | Callable[[], str | bytes]

#: The longest password a key can be stored and opened under, in bytes, text counted as UTF-8:
#: `cryptography` refuses a longer one when it encrypts the key, and the password callback of
#: `ssl` reads one no longer than this when it opens it. Refused where it is given, so a pairing
#: whose key could never be stored is refused before its invitation is spent.
LONGEST_KEY_PASSWORD = 1023


class _NoKeyPassword(LookupError):
    """A key that needs a password, and nobody gave one: `paired_context` names the file."""


def key_password_limit(key_password: KeyPassword | None) -> KeyPassword | None:
    """Refuse a key password that could not be one, and hand back one that could.

    Refused where it is given, as every other setting is. A callable is not called here: it is
    asked only once a key turns out to need it, and what it returns is held to the same rule then.
    The value itself is never put in the message, because it is a password.
    """
    if key_password is None or callable(key_password):
        return key_password

    if isinstance(key_password, str | bytes) and key_password:
        _encoded(key_password)
        return key_password

    kind = type(key_password).__name__
    given = (
        f"an empty {kind}"
        if isinstance(key_password, str | bytes)
        else f"an object of type {kind}"
    )
    raise ValueError(
        f"key_password is a password, as text or bytes, or a callable returning one, not {given}"
    )


def key_secret(key_password: KeyPassword | None) -> bytes:
    """The password itself, asked of a callable if that is what was given, as bytes.

    What a callable returns is held to what a password given directly is, and refused the same
    way, without the value in the message.
    """
    if key_password is None:
        raise _NoKeyPassword

    given = key_password() if callable(key_password) else key_password
    if not isinstance(given, str | bytes) or not given:
        raise ValueError("key_password gave no password: it is text or bytes, and not empty")

    return _encoded(given)


def _encoded(password: str | bytes) -> bytes:
    """A password as the bytes a key is encrypted under, or a refusal that does not repeat it.

    Text that UTF-8 cannot encode, such as the lone surrogate Python leaves in text decoded from a
    file name or an environment variable that was not UTF-8, is refused here rather than by
    `str.encode`, whose message quotes the character and where it is.
    """
    try:
        encoded = password.encode("utf-8") if isinstance(password, str) else password
    except UnicodeEncodeError:
        raise ValueError("key_password is text that UTF-8 cannot encode") from None

    if len(encoded) > LONGEST_KEY_PASSWORD:
        raise ValueError(
            f"key_password is at most {LONGEST_KEY_PASSWORD} bytes, text counted as UTF-8, "
            f"and the one given is {len(encoded)}"
        )

    return encoded


def pause(seconds: float) -> None:
    """What this client does between a refusal and the attempt that follows it."""
    time.sleep(seconds)


def now() -> datetime:
    """The moment, in UTC, that a `heldUntil` is compared with."""
    return datetime.now(UTC)


#: What makes the connection a request goes over: given the host, the port and the timeout, a
#: connected socket, over which TLS and everything else is layered unchanged. A program that
#: reaches a gate through something of its own gives one: a relay bridging a socket pair to its
#: standard input and output, a socket the process that started it handed over, a proxy.
Dial = Callable[[str, int, float], socket.socket]


def create_connection(host: str, port: int, timeout: float) -> socket.socket:
    """The `dial` a client has unless it is given another: TCP, as `socket` connects."""
    return socket.create_connection((host, port), timeout=timeout)


def pinned_context(context: ssl.SSLContext) -> ssl.SSLContext:
    """Refuse a context offering anything but TLS 1.3, and hand back one that offers only that.

    Every context this client makes is pinned so already. This is what keeps one made some other way
    from reaching the socket, where it would offer a version the gate never speaks to whatever
    answers on the port.
    """
    if (
        context.minimum_version != ssl.TLSVersion.TLSv1_3
        or context.maximum_version != ssl.TLSVersion.TLSv1_3
    ):
        raise ValueError(
            "context offers TLS 1.3 alone, as the gate speaks no other version, and this "
            f"one offers {context.minimum_version.name} to {context.maximum_version.name}"
        )

    return context


def dial_limit(dial: Dial) -> Dial:
    """Refuse a `dial` that could not be called, where it is given, and hand back one that could."""
    if not callable(dial):
        raise ValueError(
            "dial is a callable taking the host, the port and the timeout and returning a "
            f"connected socket, not an object of type {type(dial).__name__}"
        )

    return dial


class Budget:
    """What one kind of `503` is worth waiting out, and what is left of it.

    **Counted in the delays it spends, not off a clock.** The budget bounds the waiting, which is
    what the caller was asked about; what the attempts themselves take is bounded separately, one
    wait on the socket at a time, by `connection_timeout`. Measuring elapsed time instead would
    mix the two into one number that answers for neither. **A refusal is not always quick**: a
    pairing's or a renewal's `503 busy` comes after the gate waited up to two minutes for the
    screen, and a write's after up to twenty seconds for its turn at the database, so a busy
    budget of ten seconds can hold a pairing for five attempts of up to two minutes each. The
    budget bounds the pauses between attempts, and the call is the caller's to bound.

    A budget of `None` is no retry at all, which is the default everywhere: the refusal is raised
    exactly as it was before any of this existed.
    """

    def __init__(self, seconds: float | None = None, name: str = "a retry budget") -> None:
        """Take what a caller is prepared to spend waiting, or `None` for none of it.

        One that could not be spent is refused here, by the name it was given under.
        """
        self._remaining = retry_limit(name, seconds)

    def wait(self, retry_after: float | None) -> bool:
        """Wait out one refusal, and say whether the budget allowed it.

        A delay longer than what is left is not waited out at all: sleeping for less than the gate
        asked for and asking again is a request it has already called too early, so the refusal is
        raised now rather than a shorter wait later.
        """
        if self._remaining is None:
            return False

        # A gate that says to come back immediately has said nothing a client can act on: it would
        # be a loop at whatever rate the network allows, against something that has just said it is
        # busy. The same for a header that is absent, a date, or not a number.
        delay = retry_after if retry_after is not None and retry_after > 0 else FALLBACK_RETRY_AFTER
        if delay > self._remaining:
            return False

        self._remaining -= delay
        pause(delay)
        return True


class Retry:
    """The three budgets, one per word a `503` is known to carry, spent by one call only.

    **And a fourth, for an approval the gate is holding**, which only `pair` and `renew` give.
    A `503 busy` carrying `heldUntil` is an operator's approval KeePass was too busy to write,
    kept for exactly the request that was approved until that moment, so asking again before then
    issues the certificate without the dialog, and not asking loses it. That one is waited out on
    its own budget, the six minutes the gate gave the request, and only while the moment has not
    passed, rather than on the busy budget, which a caller sets for KeePass being busy and which is
    `None` unless they set it.
    """

    def __init__(
        self,
        busy: float | None = None,
        locked: float | None = None,
        undecided: float | None = None,
        held: float | None = None,
    ) -> None:
        """Take what each kind of refusal is worth waiting out."""
        self.busy = Budget(busy, "retry_busy_timeout")
        self.locked = Budget(locked, "retry_locked_timeout")
        self.undecided = Budget(undecided, "retry_undecided_timeout")
        self.held = Budget(held, "held")

    def waited(self, refusal: GateUnavailable) -> bool:
        """Wait this refusal out, if the budget for its word still allows it.

        **The budget is picked by the refusal's class**, so the word is read in one place,
        `unavailable`, and a renewal or a pairing that met a `503` is waited out exactly as a
        read is. A refusal whose word this client does not know has no budget and is never waited
        out: nothing here knows what ends it, and a caller who gave thirty seconds for a busy
        KeePass gave them for that and nothing else.

        The refusal carries what the gate said about coming back, so nothing here has to invent a
        delay or reach for a response that is no longer in hand.
        """
        if isinstance(refusal, GateBusy) and refusal.held_until is not None:
            delay = (
                refusal.retry_after
                if refusal.retry_after is not None and refusal.retry_after > 0
                else FALLBACK_RETRY_AFTER
            )
            if (refusal.held_until - now()).total_seconds() >= delay and self.held.wait(
                refusal.retry_after
            ):
                return True

            budget = self.busy
        elif isinstance(refusal, GateBusy):
            budget = self.busy
        elif isinstance(refusal, DatabaseLocked):
            budget = self.locked
        elif isinstance(refusal, AuthorityUndecided):
            budget = self.undecided
        else:
            return False

        return budget.wait(refusal.retry_after)


class Keep(enum.Enum):
    """The one value meaning "whatever this client was built with".

    A retry budget of `None` is a decision, do not retry, so "not given" cannot also be `None`.
    An enum member rather than a bare `object()` because it is a type a signature can name and a
    checker can narrow.
    """

    TOKEN = enum.auto()


KEEP = Keep.TOKEN

#: What a per-call retry budget takes: seconds, `None` for no retry, or `KEEP` for the client's own.
Given = float | None | Keep


def given(value: Given, fallback: float | None) -> float | None:
    """What a call asked for, or what the client it was made on was built with."""
    return fallback if isinstance(value, Keep) else value


@dataclass(frozen=True)
class Response:
    """What came back from one request.

    Printed with its status and its delay and without its body, which is the entry, the field or
    the attachment that was asked for: a repr is what a log line or a traceback carries.
    """

    status: int
    body: str = field(repr=False)

    #: When the gate said to come back, in seconds, if it said. Only a `503` carries it.
    retry_after: float | None = None

    #: The `ETag` the gate answered with, quotes and all, or `None` when it sent none. A read about
    #: one group or one entry carries one, to a client whose rule may read everything it covers,
    #: and nothing else does: a write hands back the tag it left in its body, as `etag`.
    etag: str | None = None

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


def unavailable(reply: Response, *, busy: str, locked: str, undecided: str) -> GateUnavailable:
    """The refusal a `503` is, by the gate's word, saying what each means where it was met.

    **The word is read here and nowhere else**, for every route, for pairing and for renewal
    alike, so a word the gate learns is one class and one line here rather than a branch on every
    path, and `Retry` picks a budget by the class this hands back. The messages are the caller's,
    because what to do next differs: a busy pairing starts again from a hello, where a busy write
    is asked again as it was.

    A word this client does not know, or a `503` that names none, is `GateUnavailable` itself,
    which no budget waits out.
    """
    word = reply.error()

    if word == DatabaseLocked.word:
        return DatabaseLocked(locked, reply.retry_after)

    if word == GateBusy.word:
        return GateBusy(busy, reply.retry_after, held_until(reply))

    if word == AuthorityUndecided.word:
        return AuthorityUndecided(undecided, reply.retry_after)

    said = f"with {word!r}, a word this client does not know" if word else "and named no word"
    return GateUnavailable(
        f"the gate cannot serve this right now: it answered 503 {said}, so nothing here knows "
        "what ends it, and nothing waits it out",
        word,
        reply.retry_after,
    )


def held_until(reply: Response) -> datetime | None:
    """The moment a `503 busy` says the gate holds an approval until, in UTC, or `None`.

    The gate writes it as RFC 3339 in UTC to the second, and only on `pairRequest` and
    `renewCertificate`. Anything else in its place is read as no approval held, which is what
    waiting on the busy budget alone assumes: the dialog comes up again on the next try.
    """
    try:
        value = reply.json().get("heldUntil")
    except ProtocolError:
        return None

    if not isinstance(value, str):
        return None

    try:
        return datetime.strptime(value, "%Y-%m-%dT%H:%M:%SZ").replace(tzinfo=UTC)
    except ValueError:
        return None


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


def paired_context(
    authority: Path, identity: Path, key_password: KeyPassword | None = None
) -> ssl.SSLContext:
    """The context for a client that has been through pairing.

    The authority handed over at pairing is the only anchor loaded, so a certificate that does not
    chain to it fails the handshake rather than being reported afterwards. That is the difference
    from the plugin's own side of this, where the platform's chain builder had to be told to pin
    and then checked by hand: here the trust store holds one certificate and nothing else.

    Host name checking is off because the name rotates by design and is not a name anybody
    resolves. What replaces it is the anchor: only the authority that issued this client's own
    certificate can produce a host certificate this will accept.

    **Two of the checks `PAIRING.md` §304 lists are OpenSSL's rather than code here.** A context
    built for a client verifies the peer for the purpose of a TLS server, so a certificate of this
    authority's that lacks `serverAuth`, which is what a client of the same authority holds, fails
    the handshake as `CA.md` §215 needs it to; and it holds the certificate to its validity
    dates, which are the one bound on how long a leaked host or responder key is accepted,
    `CA.md` §208. `TestPinning` pins both, since nothing written here would notice either going.

    **A file that will not load is a `StorageError` naming it**, as a `client.json` that will not
    read is. These two are read here and nowhere before, so one cut short by a disk that filled
    during a re-pair, edited, or missing its key reaches `ssl` as an `SSLError`, and a missing one
    as a `FileNotFoundError`: both `OSError`, neither this library's, and a traceback out of every
    command that dials.

    **A key stored under a password is opened with `key_password`**, which `ssl` asks for only
    when the key is encrypted, so a callable is never asked about one that is not. It is always
    handed a callable, because given none it would fall back on OpenSSL's own prompt, which reads
    a terminal from inside a library call: an encrypted key with no `key_password` is a
    `StorageError` naming the file instead, and so is one the password given does not open.
    Whatever the callable raises comes out unchanged, a refusal of its own among it.
    """
    context = ssl.SSLContext(ssl.PROTOCOL_TLS_CLIENT)
    context.check_hostname = False
    context.verify_mode = ssl.CERT_REQUIRED

    try:
        context.load_verify_locations(cafile=str(authority))
    except OSError as error:
        raise StorageError(
            f"{authority} will not load as the authority this client trusts: {error}"
        ) from error

    asked: list[bool] = []

    def password() -> bytes:
        asked.append(True)
        return key_secret(key_password)

    try:
        context.load_cert_chain(certfile=str(identity), password=password)
    except _NoKeyPassword:
        raise StorageError(
            f"{identity} holds an encrypted key, and no key_password was given to open it"
        ) from None
    except OSError as error:
        if asked:
            raise StorageError(
                f"{identity} holds an encrypted key that the key_password given does not open: "
                f"{error}"
            ) from error

        raise StorageError(
            f"{identity} will not load as this client's certificate and key: {error}"
        ) from error

    _pin_to_tls13(context)
    return context


class Connection:
    """One open connection. Not reusable, because the gate does not reuse them either."""

    def __init__(
        self, wrapped: ssl.SSLSocket, max_response_bytes: int = MAX_RESPONSE_BYTES
    ) -> None:
        """Take a socket that has already shaken hands, and how much of an answer to read.

        Keeps the certificate it was served with, which the proofs are computed over, and reads
        off it whether the identity that answered is the one a locked database has.
        """
        self._socket = wrapped
        self._max_response_bytes = response_limit(max_response_bytes)
        der = wrapped.getpeercert(binary_form=True)
        if not der:
            raise TransportError("the server sent no certificate")

        self.server_certificate = der
        self.responder = _responder(der)

    def send(
        self, method: str, path: str, body: str = "", if_match: str | None = None
    ) -> Response:
        """Write one request and read the whole answer, unless a responder answered the handshake.

        Nothing is written to a responder. What a locked database answers is `503 locked` on
        every path and method, decided before it reads anything, so there is no request this
        could carry that would be answered; and an impostor that came by the responder key while
        the database was locked reads nothing but could answer with a fabricated secret. Waiting
        for the answer to decide that has already handed over the request, which on a write is
        the value being stored.

        `if_match` is sent as `If-Match` exactly as given, which is a tag as `etag` holds one,
        several of them with commas between, or `*`.
        """
        if self.responder:
            # Nothing says when to come back, because there is no response to read it off. What a
            # caller waits instead is `FALLBACK_RETRY_AFTER`, the number the gate would have sent.
            raise DatabaseLocked(
                "the connection was served with the responder identity, which only a locked "
                "database has, so no request was sent over it"
            )

        _check(method, path)
        check_condition(if_match)
        payload = body.encode("utf-8")
        head = (
            f"{method} {path} HTTP/1.1\r\n"
            # The name this connection was made under, which is what the gate resolved it by. It is
            # sent because HTTP/1.1 requires a `Host`, not because the gate reads it.
            f"Host: {self._socket.server_hostname}\r\n"
            "Content-Type: application/json\r\n"
            + (f"If-Match: {if_match}\r\n" if if_match is not None else "")
            + f"Content-Length: {len(payload)}\r\n"
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
            #
            # A `ProtocolError` rather than a `TransportError`, which says the connection failed
            # and invites asking again: this one worked, and asking again would read the same
            # answer to the same cap, each time costing the gate another whole listing.
            if len(buffer) > self._max_response_bytes:
                raise ProtocolError(
                    f"the answer is over {self._max_response_bytes} bytes, the most this client "
                    "was told to read of one: max_response_bytes, or --max-response-bytes, "
                    "raises it"
                )

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
    connection_timeout: float = DEFAULT_CONNECTION_TIMEOUT,
    max_response_bytes: int = MAX_RESPONSE_BYTES,
    dial: Dial = create_connection,
) -> Iterator[Connection]:
    """Open one connection, hand it over, and close it however this ends.

    The socket is whatever `dial` hands back for the host and the port, and every wait on it is
    then bounded by `connection_timeout` whatever made it, so a `dial` that returns a blocking
    socket cannot take away the bound on the handshake and the reads.
    """
    response_limit(max_response_bytes)
    port_number(port)
    connection_limit(connection_timeout)
    dial_limit(dial)
    pinned_context(context)

    try:
        # Read as anything at all, because a caller's `dial` can hand back anything at all.
        made: object = dial(host, port, connection_timeout)
    except OSError as error:
        raise TransportError(f"nothing is listening on {host}:{port}: {error}") from error

    if not isinstance(made, socket.socket):
        # Closed before it goes, as every failure here closes what it holds: what a `dial` hands
        # back by mistake is likely a wrapper from some other library that owns a descriptor.
        # Anything its own `close` raises is that library's, and the mistake reported is this.
        close = getattr(made, "close", None)
        if callable(close):
            with contextlib.suppress(Exception):
                close()

        raise TypeError(
            f"dial returned an object of type {type(made).__name__} rather than a connected "
            "socket"
        )

    raw = made

    try:
        # A refusal of its own, because a socket a caller's `dial` handed back can already be
        # closed, and setting its timeout then raises a bare `OSError` before any handshake.
        raw.settimeout(connection_timeout)
    except OSError as error:
        raw.close()
        raise TransportError(f"the socket dial handed back could not be used: {error}") from error

    try:
        # codeql[py/insecure-protocol] refused above unless it offers TLS 1.3 alone
        wrapped = context.wrap_socket(raw, server_hostname=server_name)
    except (OSError, ssl.SSLError) as error:
        raw.close()
        # The reason is the TLS library's, and it stays in the message because the causes want
        # different things done. Against a paired context: a certificate that does not chain to
        # the authority pinned at pairing, which is something other than the gate on the port,
        # since the gate answers a client only under the authority it paired with; one that has
        # expired, which is the certificate a locked database answers with once the lock has
        # outlasted its ninety days, and unlocking mends; or a peer that will not speak TLS 1.3.
        #
        # The gate also drops a connection before any TLS when nothing it serves answers to the
        # name it carries, and that ends the handshake here as an EOF. That is every such state:
        # a database not open, or not unlocked since KeePass started; two copies of one open at
        # once; and an authority recreated or deleted since this client paired, which takes away
        # the key every name this client sends is built from, so pairing again is what mends it.
        raise TransportError(f"the handshake failed: {error}") from error

    connection = Connection(wrapped, max_response_bytes)
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


def check_condition(if_match: str | None) -> None:
    """Refuse an `If-Match` that could not be one header line, before it reaches the wire.

    A carriage return or a line feed in it would end the header and start another, which is the
    head the gate parses replaced with whatever the caller wrote, and a tag is caller data: it is
    whatever came back from somewhere, and the threat model says a caller can be prompt-injected.
    So only what a header value can hold goes, visible ASCII, spaces and tabs, which RFC 9110
    allows between the tags of a list, and not nothing, since an empty one asks for something no
    node holds and is never what a caller meant.

    Public because the client asks it before anything is dialed, and the command line before it
    asks for anything at a terminal: `send` asks it again, as the last thing between a caller and
    the wire.
    """
    if if_match is None:
        return

    if not if_match.strip(" \t") or any(
        character != "\t" and not " " <= character <= "~" for character in if_match
    ):
        raise ProtocolError(f"that is not a tag this client will send: {if_match!r}")


def _header(head: str, wanted: str) -> str | None:
    """The first value of one header in an answer's head, without the spaces around it, or `None`.

    One reader for every header this client reads, so that what counts as present, and which of
    two is the one, cannot come to differ between them. The gate writes each header once.
    """
    for line in head.split("\r\n")[1:]:
        name, colon, value = line.partition(":")
        if colon and name.strip().lower() == wanted:
            return value.strip(" \t")

    return None


def _declared_length(head: str) -> int | None:
    """What the answer said its body would be, if it said."""
    value = _header(head, "content-length")
    if value is None:
        return None

    try:
        return int(value)
    except ValueError:
        raise ProtocolError(f"that is not a content length: {value!r}") from None


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
    head = raw[:blank].decode("ascii", "replace")
    declared = _declared_length(head)
    if declared is not None and len(body) < declared:
        raise TransportError(
            f"the answer stopped {declared - len(body)} bytes short of its own content length"
        )

    return Response(
        status=status,
        body=body.decode("utf-8", "replace"),
        retry_after=_retry_after(head),
        etag=_etag(head),
    )


def _etag(head: str) -> str | None:
    """The `ETag` the answer carried, or `None` when it carried none.

    Kept as the field value came, quotes and all, because it is opaque and goes back to the gate
    as it is: `If-Match` names a tag the way `ETag` gave it. What goes is the spaces around it,
    which RFC 9110 section 5.5 makes no part of a field's value, so nothing of the tag itself is
    taken; an empty one is no tag.
    """
    return _header(head, "etag") or None


def _retry_after(head: str) -> float | None:
    """When the gate said to come back, if it said it in seconds.

    RFC 9110 section 10.2.3 allows a date as well, and this reads only the seconds: a date is a
    clock the two sides would have to agree about, which is why the gate sends seconds. A form
    this cannot read is the same as none at all, and `FALLBACK_RETRY_AFTER` answers that, so an
    unreadable header narrows what a client knows rather than stopping it.

    Nothing is trusted beyond the number itself. What gets waited is bounded by the budget the
    caller gave, so a peer asking for a day gets no more than the caller was prepared to spend.
    """
    value = _header(head, "retry-after")
    if value is None:
        return None

    try:
        seconds = int(value)
    except ValueError:
        return None

    return None if seconds < 0 else float(seconds)
