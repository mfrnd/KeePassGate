"""What this client raises, and nothing else.

One base class, so a caller can catch everything this library does without also catching what the
standard library does. The tree mirrors where a failure happened rather than what it felt like: the
transport, the pairing exchange, or a request the gate refused. A caller deciding whether to retry,
to wait, or to give up needs that distinction, and a single exception type would take it away.

Nothing here carries a token, a key or a certificate. An exception is the value most likely to be
logged, printed, and pasted into an issue.
"""

from __future__ import annotations

from datetime import datetime
from typing import ClassVar


class GateError(Exception):
    """Anything this library refuses or cannot do."""


class MalformedToken(GateError):
    """The text handed over is not a pairing token."""


class TransportError(GateError):
    """The connection failed, or was closed before an answer arrived.

    A handshake that failed carries the reason the TLS library gave, because the reasons want
    different things done: a certificate that does not chain to the authority pinned at pairing,
    one that has expired, and a connection the gate dropped because no open database answers to
    this client are three of them.
    """


class ProtocolError(GateError):
    """Something on this wire is not what the protocol allows.

    Either direction: an answer this client cannot read, or a request it will not send. An answer
    longer than `max_response_bytes` is one this client will not read, and it is here rather than
    under `TransportError` because the connection worked: asking again reads the same answer to
    the same cap, and what changes it is raising the setting, which the message names.
    """


class StorageError(GateError):
    """What is stored for a paired client could not be written, read or protected.

    Separate from `ProtocolError`, which is about the wire. This is about the four files: one that
    will not parse, a port that is not a number, a key the platform would not narrow. All of them
    are ordinary states, reached by a partial write, a full disk or an edit by hand. Every one of
    them would otherwise leave the standard library's own exception where a caller is promised
    this hierarchy.
    """


class NotPaired(StorageError):
    """Nothing is stored for a paired client where this looked.

    The one state of the four files that is not damage: this client has not paired there yet, or
    gave its identity up, or was told to look in the wrong place. A `StorageError` and nothing
    else, not a `FileNotFoundError` as well, so `except GateError` catches it with everything
    else this client raises and nothing outside this hierarchy is ever needed to catch it.
    """


class PairingError(GateError):
    """The pairing exchange did not reach a certificate."""


class ProofRefused(PairingError):
    """A proof did not verify.

    On the client's own proof this means the token is wrong. On the server's, it means whatever
    answered is not the KeePass whose operator holds the token, and the exchange stops here: a
    client with no anchor yet has nothing else to go on.
    """


class PairingRefused(PairingError):
    """The operator said no, and the invitation is spent."""


class PairingUnanswered(PairingError):
    """Nobody answered the dialog. The invitation is not spent, so this can be tried again."""


class RenewalError(GateError):
    """A renewal did not reach a new certificate, and the one this client holds is unchanged.

    Its own branch rather than pairing's: nothing about an invitation applies, and what to do
    next is a question about the certificate this client already holds, which still works until
    it expires whatever the reason here was.
    """


class RenewalRefused(RenewalError):
    """The operator refused this renewal.

    The three below are the operator answering more than this one request, and each is answered
    from then on without anybody being asked. **On the dialog's own turn the gate answers all four
    alike**, so this may be the operator refusing in this session, until a date or forever as well
    as only this once: asking again raises the dialog again after a refusal of this one only, and
    otherwise is answered at once with the subclass that says which.
    """


class RenewalRefusedThisSession(RenewalRefused):
    """The operator refused every renewal of this certificate until KeePass exits.

    The gate holds that answer in memory and not in the database, so asking again after KeePass
    has been restarted raises the dialog again, and so does asking once the operator has allowed
    renewal again on the KPG tab's clients page. Until then pairing again is the other way to a new
    certificate, and the one this client holds still works until it expires.
    """


class RenewalTooEarly(RenewalRefused):
    """The operator refused renewing this certificate until `not_before`, which has not come.

    Asking again before then gets this again without a dialog, unless the operator has allowed
    renewal again on the KPG tab's clients page; from then on the operator is asked again, unless a
    refusal in this session also stands, which is then what the gate answers.
    """

    def __init__(self, message: str, not_before: datetime) -> None:
        """Keep the moment the gate said renewals are asked about again, in UTC."""
        super().__init__(message)
        self.not_before = not_before


class RenewalRefusedForever(RenewalRefused):
    """The operator refused every renewal of this certificate.

    Short of the operator allowing renewal again on the KPG tab's clients page, pairing again is
    the way to a new one, with an invitation from the operator, and the sooner the better: the
    certificate this client holds still works only until it expires.
    """


class RenewalUnanswered(RenewalError):
    """Nobody answered the dialog. Nothing changed, so this can be asked again."""


class GateUnavailable(GateError):
    """The gate answered `503`: not now, and nothing was done.

    The gate refuses with a `503` before it acts, every time, so this is the one refusal that says
    nothing happened, and the only one this client will wait out. Why is the gate's word, kept as
    `error`, and each word the gate is known to send has a class of its own below, because each
    ends differently and a caller willing to wait out one is not necessarily willing to wait out
    another. A word this client does not know, or none at all, is this class itself, and it is
    never waited out: nothing here knows what ends it.
    """

    def __init__(self, message: str, error: str = "", retry_after: float | None = None) -> None:
        """Keep the gate's word, and what the gate said about coming back, when it said it.

        On the exception rather than only on the response, because the response is gone by the
        time a caller sees this and because a retry loop that had to reach for it would be
        inventing a delay instead. `None` is a refusal that said nothing readable, and an empty
        `error` one that named no word.
        """
        super().__init__(message)
        self.error = error
        self.retry_after = retry_after


class GateBusy(GateUnavailable):
    """`503 busy`: KeePass is in the middle of something, and this waits until it is not.

    A dialog open on the group or entry being written is the usual reason: the plugin refuses a
    write that the dialog would take straight back off again when it is accepted. KeePass part way
    through a save or an import, and a save that would stop to ask the operator something, are
    the others, and a read meets it when the database kept changing while it was being read.
    """

    #: The word the gate sends with this refusal.
    word: ClassVar[str] = "busy"

    def __init__(
        self,
        message: str,
        retry_after: float | None = None,
        held_until: datetime | None = None,
    ) -> None:
        """Keep what the gate said about coming back, and until when it holds an approval.

        `held_until` is set only on `pair` and `renew`, when the operator approved and KeePass
        was too busy to write it: the gate holds the approval until then, in UTC, and the same
        request asked again before then is written without the dialog. `None` everywhere else.
        """
        super().__init__(message, self.word, retry_after)
        self.held_until = held_until


class DatabaseLocked(GateUnavailable):
    """`503 locked`: every route answers this while the database is locked.

    It names the lock and nothing else, deliberately: not which database, not whether the caller
    would have been admitted. A client learns that it should wait, which is the only thing it can
    act on.

    Raised as well before anything is sent, when the handshake was answered with the responder
    certificate only a locked database has, and then it says nothing about coming back, because
    there is no response to have read a delay off at all. Its word is the same either way.
    """

    #: The word the gate sends with this refusal.
    word: ClassVar[str] = "locked"

    def __init__(self, message: str, retry_after: float | None = None) -> None:
        """Keep what the gate said about coming back, when it said it."""
        super().__init__(message, self.word, retry_after)


class AuthorityUndecided(GateUnavailable):
    """`503 undecided`: the database holds more than one authority, and nobody has kept one.

    Two copies of one database that each made an authority meet in a synchronize, and the
    operator is asked at KeePass which to keep. Until somebody answers, every route answers this,
    pairing included. It ends only when a person chooses, and it can end with the authority this
    client belongs to not kept, after which this client reaches nothing and pairs again.
    """

    #: The word the gate sends with this refusal.
    word: ClassVar[str] = "undecided"

    def __init__(self, message: str, retry_after: float | None = None) -> None:
        """Keep what the gate said about coming back, when it said it."""
        super().__init__(message, self.word, retry_after)


class NotAdmitted(GateError):
    """The gate does not admit this certificate: no record, a revoked one, or an expired one.

    Every connection is checked against the record, not against the certificate alone, so this is
    also what a revoked client sees, immediately. A certificate that has expired by the gate's clock
    is refused as one with no record, and this client names it as expired, with the moment, before
    it dials, when this machine's clock is `client.CLOCK_LEEWAY` past the expiry stored with it: a
    clock running behind the gate's dials and meets the `401`. Pairing again is the way back from
    all three for a client that still wants to reach the gate, because a renewal needs a certificate
    that is still admitted and has not expired; for one that does not, what is stored protects
    nothing, and `forget`, or the command line's `revoke`, takes it away.
    """


class Withheld(GateError):
    """A protected value this client may not read: the gate answered it with `null`.

    The database protects the value, and the rule that let this client reach the entry does not
    grant `P`. The gate says so with `null` rather than by leaving the field out, so a value being
    withheld is a different answer from a field that is not there, and this is that answer, raised
    by every way of reading one. Not `None` and not an empty string, because either reads as a
    value: a caller printing, comparing or storing it carries on with something the database
    does not hold. And not an `AttributeError`, because `getattr(entry, "password", "")` and
    `hasattr` swallow that and turn a withheld secret into a default without a word.

    `Entry.withheld` and `Field.withheld` say which values are withheld, for a caller that would
    rather ask than catch.

    `pykeepassgate.refs` raises it too, for a withheld value a reference or a placeholder stands
    for, and then the message says which one, since the value is then not the one asked for.
    """

    def __init__(self, name: str, listed: bool = False, through: str = "") -> None:
        """Name the field, and say whose rule would have to change for it to be read.

        `through` is the reference or the placeholder that stands for it, when it was reached
        through one, which the message names, since the value asked for was another.
        """
        reached = "list" if listed else "read"
        holder = "the entry holding it" if through else "the entry"
        said = f"{name!r}, which {through} stands for," if through else repr(name)
        super().__init__(
            f"{said} is withheld: the database protects it, and the rule that lets this client "
            f"{reached} {holder} does not grant `P`, which the operator can add to that rule"
        )
        self.name = name


class Unresolved(GateError):
    """A value `pykeepassgate.refs` will not give as KeePass would give it, and says why.

    The resolver follows what it can follow exactly and refuses the rest rather than hand back
    text KeePass would have changed: a reference that searches by anything but a whole
    identifier, which KeePass answers from the whole database and the gate from what this client
    may see; a reference to an entry this client cannot read, absent, in the recycle bin and
    refused being one answer; a placeholder KeePass replaces and the resolver does not;
    references that go round in a circle, or further than KeePass follows; a placeholder whose
    name matches a field only when letters beyond ASCII are compared without their case; and a
    standard string the entry holds under no name spelled exactly so, beside a name beyond ASCII
    that KeePass's collation may read as it. The message names the reference, the placeholder or
    the field, and never the value it was found in.
    """


class RouteRefused(GateError):
    """The gate refused this request, with the status and the reason it gave, if it gave one.

    The reason is read out of the answer's body, so an answer with no body carries none: `error`
    is then empty, and the status is all there is to act on.
    """

    def __init__(self, status: int, error: str) -> None:
        """Keep the status and the reason the gate gave, which may be empty."""
        answered = f"the gate answered {status}"
        super().__init__(f"{answered}: {error}" if error else answered)
        self.status = status
        self.error = error
