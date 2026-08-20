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
from typing import Any

from . import proofs, sni, transport
from . import token as token_text
from .errors import (
    DatabaseLocked,
    GateBusy,
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
    """

    def __init__(
        self, port: int, invitation: str, timeout: float = transport.PAIRING_TIMEOUT
    ) -> None:
        """Take the port the operator gave and the token from the tab."""
        self._port = port
        self._token = token_text.decode(invitation)
        self._timeout = timeout

    def hello(self) -> Exchange:
        """Phase 3: prove the token, then check that the answer proves it too."""
        name, nonce = sni.new_name(self._token)
        context = transport.unpaired_context()

        with transport.connect(self._port, context, name, timeout=self._timeout) as connection:
            bound = proofs.binding(connection.server_certificate)
            offered = proofs.client(self._token, bound, nonce)
            reply = connection.send(
                "POST", HELLO_PATH, _json({"proof": _b64(offered)})
            )

        _refuse(reply)
        body = reply.json()
        challenge = _bytes(body, "challenge")
        answered = _bytes(body, "proof")

        expected = proofs.server(self._token, bound, nonce, challenge)
        if not proofs.matches(expected, answered):
            # Whatever answered is not holding this token. A client with no anchor has nothing else
            # to check, so this is where it stops rather than where it warns.
            raise ProofRefused("the gate's proof did not verify, so this is not the right KeePass")

        return Exchange(challenge=challenge, binding=bound, nonce=nonce)

    def request(self, exchange: Exchange, name: str, profiles: list[str]) -> dict[str, Any]:
        """Phases 4 and 5: ask, wait for the operator, and take the bundle if there is one.

        The profiles asked for are a ceiling rather than a grant. The operator can take any of them
        away in the dialog, and what comes back says what was actually approved, which is not
        always what was asked for.
        """
        _usable(name, profiles)

        server_name, _ = sni.new_name(self._token)
        context = transport.unpaired_context()

        proof = proofs.request(
            self._token,
            exchange.binding,
            exchange.nonce,
            exchange.challenge,
            proofs.asked(name, profiles),
        )

        body = _json({"name": name, "profiles": profiles, "proof": _b64(proof)})

        with transport.connect(
            self._port, context, server_name, timeout=self._timeout
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

    error = reply.error()

    if reply.status == 503 and error == "locked":
        raise DatabaseLocked("the database is locked, so pairing has nothing to issue from")

    if reply.status == 503:
        # The invitation survives, because the gate takes the challenge before it reaches
        # the check that answers this and burns the invitation nowhere on this path. The
        # challenge does not survive, so retrying this half alone earns `409 no exchange`.
        # What can be tried again is the whole pairing, which is what `api.pair` is.
        raise GateBusy(
            "the gate cannot write a record right now, most likely because a group "
            "dialog is open. The invitation is still good, but this exchange is not: "
            "say hello again."
        )

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
