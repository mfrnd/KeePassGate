"""How a stranger becomes a client: the two connections, and the order that makes it revocable.

The exchange spans two connections because the gate serves one request per connection. What carries
it across is the challenge: the server issues one against the binding and the nonce of the hello,
holds it in memory, and spends it when the request takes it. So a request has to name a hello that
really happened, on a connection served with the same certificate.

**The client speaks first, and checks second.** Its proof goes out before the server has done
anything, so a captured name replayed at the gate gets no further than a refusal. Only then is the
server's own proof checked, and that check is what tells a client with no anchor yet that it
reached the KeePass whose operator holds the token, rather than whatever accepted the connection.
If it does not recompute, this stops: there is nothing else to go on.

**Nothing here spends the invitation.** A wrong proof, a refused dialog, an unanswered one: the
gate decides what each costs, and this reports it rather than guessing. A refusal spends the
invitation; a timeout does not.
"""

from __future__ import annotations

import base64
import json
from collections.abc import Iterable
from dataclasses import dataclass
from datetime import timedelta
from typing import Any

from . import proofs, sni, transport
from . import token as token_text
from .errors import (
    DatabaseLocked,
    GateUnavailable,
    PairingError,
    PairingRefused,
    PairingUnanswered,
    ProofRefused,
    ProtocolError,
    RouteRefused,
)

HELLO_PATH = "/pair/hello"
REQUEST_PATH = "/pair/request"

#: What `AuthorityIssuer.IsUsableSubjectName` allows a client to be called, and
#: `IsUsableProfileName` a profile: 1 to 64 characters out of these, and for a subject no
#: leading or trailing space. Mirrored here rather than discovered by asking: the gate checks
#: both before it takes the challenge, so a name it will not have costs only a round trip, and
#: refusing here saves that and names the rule that was broken. It is also what makes
#: `proofs.asked` unambiguous: neither of its separators can appear inside a part.
SUBJECT_CHARACTERS = frozenset("ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789._ -")
PROFILE_CHARACTERS = frozenset("ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789_-")
NAME_LENGTH = 64

#: The most profiles one pairing may ask for: the routing document's `maxItems` on `profiles`,
#: which the gate holds a pairing's body to before it takes the challenge, as it does the names.
MAX_PROFILES = 32

#: The shortest and the longest a certificate may be asked to last: `Lifetime.ShortestDays` and
#: `Lifetime.LongestDays` on the plugin's side, in whole days. Mirrored as the name rules are and
#: for the same reason: the gate refuses anything else as `400 malformed` before it takes the
#: challenge, so refusing here saves the round trip and names the rule that was broken.
SHORTEST_LIFETIME = timedelta(days=1)
LONGEST_LIFETIME = timedelta(days=365)

_DAY = timedelta(days=1)


@dataclass(frozen=True)
class Exchange:
    """What a hello leaves behind for the request that follows it.

    The binding and the nonce are the hello's, not the request's: the challenge was issued against
    those, and the gate compares them. Carrying the request's own would be the mistake that makes a
    request fail to follow its own hello.
    """

    challenge: bytes
    binding: bytes
    nonce: bytes


class Pairing:
    """One invitation, used once.

    **A lost invitation is not a refusal.** An expired or already-spent one makes the
    name resolve to nothing, and the gate drops that connection without answering at
    all, so it reaches a caller as `TransportError` rather than as anything in the
    pairing family. `PAIRING.md` asks the client library to name that possibility,
    because it is otherwise indistinguishable from nothing listening.

    **Every refusal `request` can meet from the gate comes after the challenge
    is taken, or says the invitation is gone.** The gate checks the name and the
    profiles before it takes the challenge, and `_usable` refuses those here
    first. So recovery from a refusal that spares the invitation is a fresh
    `hello`, never a second `request`.

    **Only `hello` waits out a `503`, and that asymmetry is the whole of what the
    retry budgets mean here.** Phase 3 spends nothing and carries nothing forward,
    so repeating it is starting it; phase 4 carries a challenge the gate spends when
    it reads it, and is the message that puts a pairing in front of a person. What
    is repeatable after a refused `request` is the exchange, not the request, and
    `api.pair` is where that is done.
    """

    def __init__(
        self,
        port: int,
        invitation: str,
        connection_timeout: float = transport.PAIRING_CONNECTION_TIMEOUT,
        host: str = transport.LOOPBACK,
        retry_busy_timeout: float | None = transport.DEFAULT_RETRY_BUSY_TIMEOUT,
        retry_locked_timeout: float | None = transport.DEFAULT_RETRY_LOCKED_TIMEOUT,
        retry_undecided_timeout: float | None = transport.DEFAULT_RETRY_UNDECIDED_TIMEOUT,
        max_response_bytes: int = transport.MAX_RESPONSE_BYTES,
    ) -> None:
        """Take the port the operator gave, the token from the tab, and where to dial.

        Both connections of the exchange go to the same host, because they are two halves of one
        conversation with one KeePass. The proofs are over the certificate the gate serves and the
        token the operator handed out, so nothing about them depends on the address.

        The three retry budgets are spent by `hello` and nowhere else, for the reason on the
        class. There is no per-call override here as there is on `GateClient`: one `Pairing` is
        one invitation used once, so there is no second call for a different number to apply to.

        `max_response_bytes` caps each answer as it does on `GateClient`, and it, the port, the
        socket timeout and the three budgets are refused the same way, here and before any hello.
        """
        self._port = transport.port_number(port)
        # Stripped here rather than in `token.decode`, which refuses anything that is not
        # exactly a token as the plugin's decoder does. What arrives here is pasted text, which
        # brings a line break or a space with it as often as not, and a program handed the same
        # paste as the command line is not refused what the command line accepts.
        self._token = token_text.decode(invitation.strip())
        self._connection_timeout = transport.connection_limit(connection_timeout)
        self._host = host
        self._retry_busy_timeout = transport.retry_limit("retry_busy_timeout", retry_busy_timeout)
        self._retry_locked_timeout = transport.retry_limit(
            "retry_locked_timeout", retry_locked_timeout
        )
        self._retry_undecided_timeout = transport.retry_limit(
            "retry_undecided_timeout", retry_undecided_timeout
        )
        self._max_response_bytes = transport.response_limit(max_response_bytes)

    def hello(self) -> Exchange:
        """Phase 3: prove the token, then check that the answer proves it too.

        **This phase is safe to repeat, which is why it is the one that waits out a `503`.**
        A hello spends nothing on either side: `PAIRING.md` §306 burns the token on an approval
        or a refusal and on nothing else, and the gate's own answer here reaches no dialog, no
        issuance and no write. Each attempt mints a fresh nonce and a fresh name and takes a
        fresh challenge, so a retry is a new phase 3 rather than the same one sent twice: there
        is no message being replayed and nothing carried over from the attempt that was refused.
        """
        retry = transport.Retry(
            busy=self._retry_busy_timeout,
            locked=self._retry_locked_timeout,
            undecided=self._retry_undecided_timeout,
        )

        while True:
            try:
                bound, nonce, reply = self._hello_once()
                _refuse(reply)
            except GateUnavailable as refusal:
                if retry.waited(refusal):
                    continue

                raise

            body = reply.json()
            challenge = _bytes(body, "challenge")
            answered = _bytes(body, "proof")

            expected = proofs.server(self._token, bound, nonce, challenge)
            if not proofs.matches(expected, answered):
                # Whatever answered is not holding this token. A client with no anchor has nothing
                # else to check, so this is where it stops rather than where it warns.
                raise ProofRefused(
                    "the gate's proof did not verify, so this is not the right KeePass"
                )

            return Exchange(challenge=challenge, binding=bound, nonce=nonce)

    def _hello_once(self) -> tuple[bytes, bytes, transport.Response]:
        """One attempt at phase 3, and the binding and nonce it was made under.

        Those two come back with the answer because they are what the challenge was issued
        against. A retry makes new ones, so reading them off anything but the attempt that was
        answered is the mistake that makes a request fail to follow its own hello.
        """
        name, nonce = sni.new_name(self._token)
        context = transport.unpaired_context()

        with transport.connect(
            self._port, context, name, host=self._host,
            connection_timeout=self._connection_timeout,
            max_response_bytes=self._max_response_bytes,
        ) as connection:
            bound = proofs.binding(connection.server_certificate)
            offered = proofs.client(self._token, bound, nonce)
            reply = connection.send(
                "POST", HELLO_PATH, _json({"proof": _b64(offered)})
            )

        return bound, nonce, reply

    def request(
        self,
        exchange: Exchange,
        name: str,
        profiles: list[str],
        lifetime: timedelta | None = None,
    ) -> dict[str, Any]:
        """Phases 4 and 5: ask, wait for the operator, and take the bundle if there is one.

        The profiles asked for are a ceiling rather than a grant. The operator can take any of them
        away in the dialog, and what comes back says what was actually approved, which is not
        always what was asked for.

        **The lifetime is a proposal.** The dialog offers it preselected among its presets, and a
        year when none is asked for, and the operator picks; the bundle's `expires` is what was
        picked. A `timedelta` of whole days from `SHORTEST_LIFETIME` to `LONGEST_LIFETIME`, and
        anything else is a `ValueError` here, before anything is sent.

        **Nothing here is ever sent twice, whatever budget the caller gave.** Two separate reasons,
        and either one alone would be enough. The challenge is spent by the gate as it reads this,
        so a second send earns `409 no exchange` and has told the operator nothing. And this is the
        message that can end with an invitation spent: everything after the dialog still happens
        even if this client hangs up, so a client that resent it could not know whether what it was
        repeating had already issued a certificate and written a record. A refusal here is reported
        rather than retried, and what may be tried again is the whole exchange, which is `api.pair`.
        """
        written = None if lifetime is None else lifetime_text(lifetime)
        _usable(name, profiles)

        server_name, _ = sni.new_name(self._token)
        context = transport.unpaired_context()

        proof = proofs.request(
            self._token,
            exchange.binding,
            exchange.nonce,
            exchange.challenge,
            proofs.asked(name, profiles, written),
        )

        asking: dict[str, Any] = {"name": name, "profiles": profiles, "proof": _b64(proof)}
        if written is not None:
            asking["lifetime"] = written

        body = _json(asking)

        with transport.connect(
            self._port, context, server_name, host=self._host,
            connection_timeout=self._connection_timeout,
            max_response_bytes=self._max_response_bytes,
        ) as connection:
            # The hello's proof was over the certificate the *hello* was served with,
            # and this is a different connection: an unpaired client has no anchor, so
            # without this it would take its own key, its trust anchor and the gate
            # key from whatever is answering the port now. The gate makes the mirror
            # comparison and answers `409 no exchange` (`PairingHandler.Request`);
            # this is the half that protects the client, which is the party being
            # handed something to trust.
            #
            # The responder is told apart first, because it too is a certificate the
            # hello never proved, and what it means is a database locked since the
            # hello rather than something else on the port: a lock to wait out, as
            # `send` reports one, not a reason to stop. Nothing is sent either way.
            if connection.responder:
                raise DatabaseLocked(
                    "the database locked after the hello, so the request was served with the "
                    "responder identity and nothing was sent over it"
                )

            if not proofs.matches(
                proofs.binding(connection.server_certificate), exchange.binding
            ):
                raise ProofRefused(
                    "the request reached a different certificate from its own hello"
                )

            reply = connection.send("POST", REQUEST_PATH, body)

        _refuse(reply)
        return reply.json()


def lifetime_text(lifetime: timedelta) -> str:
    """What the wire carries for a lifetime: an ISO 8601 duration in whole days, such as `P30D`.

    Refused with `ValueError` unless it is a `timedelta` of whole days from `SHORTEST_LIFETIME` to
    `LONGEST_LIFETIME`, which is what the document's pattern allows. A part of a day is refused
    rather than rounded either way, because the certificate it would ask for is not the one meant.
    """
    if (
        not isinstance(lifetime, timedelta)
        or lifetime % _DAY
        or not SHORTEST_LIFETIME <= lifetime <= LONGEST_LIFETIME
    ):
        raise ValueError(
            f"lifetime is a whole number of days from {SHORTEST_LIFETIME.days} to "
            f"{LONGEST_LIFETIME.days}, not {lifetime!r}"
        )

    return f"P{lifetime.days}D"


def refuse_unusable(invitation: str, name: str, profiles: Iterable[str]) -> None:
    """Refuse an invitation that is not one, and a name or profiles the gate would refuse.

    Everything about a pairing that can be refused without the gate, asked before a person is
    asked for a key password: one typed and then thrown away for a malformed invitation is a
    person's answer wasted.
    """
    token_text.decode(invitation.strip())
    _usable(name, profiles)


def _usable(name: str, profiles: Iterable[str]) -> None:
    """Refuse what the gate would refuse, without spending a connection to be told.

    `PairingHandler.Request` checks the name and the profiles before it takes the challenge, and
    the gate holds the body to the document's ceiling on how many profiles before that, so
    `400 name` and `400 profile` cost nothing on the gate's side: the exchange survives, and the
    same hello can be followed by a corrected request. What this saves is the round trip and a
    message that names the rule that was broken rather than the field it was broken in.
    """
    if not 0 < len(name) <= NAME_LENGTH or not SUBJECT_CHARACTERS.issuperset(name):
        raise PairingError(f"that is not a name this database will issue to: {name!r}")

    if name.startswith(" ") or name.endswith(" "):
        # Measured on the plugin's side: `X500DistinguishedName` strips the space either end,
        # so two names that differ only there become one identity in a record and in a log.
        raise PairingError("a name cannot begin or end with a space")

    asked = list(profiles)
    if len(asked) > MAX_PROFILES:
        raise PairingError(
            f"at most {MAX_PROFILES} profiles can be asked for at once, not {len(asked)}"
        )

    for profile in asked:
        if not 0 < len(profile) <= NAME_LENGTH or not PROFILE_CHARACTERS.issuperset(profile):
            raise PairingError(f"that is not a profile name: {profile!r}")

        if profile.lower() == "default":
            raise PairingError("`default` is the rule that applies when none is named")


def _refuse(reply: transport.Response) -> None:
    """Turn what the gate said into what a caller can act on.

    Each of these is a different thing to do next, which is why they are different exceptions: wait
    and retry, ask the operator to look at the screen, or stop and get a new invitation.
    """
    if reply.status == 200:
        return

    if reply.status == 503:
        # The invitation survives, and that is a property of the gate rather than a hope.
        # `PairingHandler` answers `503` only before the invitation is burned: the blocked check
        # before the dialog and again after it, a dialog that could not be shown, one that never
        # reached the screen, and the write finding KeePass busy before it spent anything, which
        # it does under the writer's lock. After the burn it answers `500` instead, in as many
        # words, because a client told to come back would be turned away at the name. So a `503`
        # from this route means no certificate was issued and no record was written, and
        # `locked` and `undecided` are answered before the request is read at all. A `busy`
        # after the operator approved carries `heldUntil`: the approval is held until then for
        # exactly this request, which `api.pair` asks for again by itself.
        #
        # What `busy` asks about is the writer's `Blocked`, which looks at no group dialog, and
        # the screen: so the message names those rather than guessing at a dialog the gate never
        # asked about.
        #
        # The challenge does not survive, so retrying this half alone earns `409 no exchange`.
        # What can be tried again is the whole pairing, which is what `api.pair` is.
        raise transport.unavailable(
            reply,
            locked="the database is locked, so pairing has nothing to issue from",
            busy=(
                "the gate cannot put this pairing to the operator right now: KeePass is part way "
                "through something of its own, Database Maintenance is open, saving the database "
                "would stop to ask the operator something, another pairing or renewal is in front "
                "of them, or there is no screen to ask on. The invitation is still good, but this "
                "exchange is not: start again from a hello"
            ),
            undecided=(
                "the database holds more than one certificate authority and the operator has "
                "not kept one, so there is nothing to issue from until they do. The invitation "
                "is still good"
            ),
        )

    error = reply.error()

    if reply.status == 504:
        raise PairingUnanswered("nobody answered the dialog, and the invitation is not spent")

    if reply.status == 403:
        raise PairingRefused("the operator refused, and the invitation is spent")

    if reply.status == 401 and error == "proof":
        raise ProofRefused("the gate refused the proof, so that is not the token it is holding")

    if reply.status == 404:
        # A path under `/pair/` that is neither hello nor request. Not what a connection
        # that lost its invitation gets: that name resolves to nothing and the gate drops
        # the connection without a word, so it arrives here as a `TransportError`.
        raise PairingError(
            "the gate has no such pairing route, so these two halves disagree about the wire"
        )

    if reply.status == 409 and error == "no exchange":
        # The challenge is what is gone, not the invitation: it ran out, a second hello took it
        # away, or this request reached a different certificate from its hello. The gate answers
        # this having spent nothing but the challenge, so the same invitation starts again.
        raise PairingError(
            "the gate holds no exchange for this request, but the invitation is not spent: "
            "start again from a hello"
        )

    if reply.status == 409 and error == "no invitation":
        raise PairingError("the invitation is gone: it expired, or it was spent")

    raise RouteRefused(reply.status, error)


def _bytes(body: dict[str, Any], key: str) -> bytes:
    value = body.get(key)
    if not isinstance(value, str):
        raise ProtocolError(f"the gate's answer is missing {key}")

    try:
        return base64.b64decode(value, validate=True)
    except ValueError as error:
        raise ProtocolError(f"the gate's {key} is not base64") from error


def _b64(value: bytes) -> str:
    return base64.b64encode(value).decode("ascii")


def _json(body: dict[str, Any]) -> str:
    return json.dumps(body, separators=(",", ":"))
