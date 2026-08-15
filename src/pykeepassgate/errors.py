"""What this client raises, and nothing else.

One base class, so a caller can catch everything this library does without also catching what the
standard library does. The tree mirrors where a failure happened rather than what it felt like: the
transport, the pairing exchange, or a request the gate refused. A caller deciding whether to retry,
to wait, or to give up needs that distinction, and a single exception type would take it away.

Nothing here carries a token, a key or a certificate. An exception is the value most likely to be
logged, printed, and pasted into an issue.
"""

from __future__ import annotations


class GateError(Exception):
    """Anything this library refuses or cannot do."""


class MalformedToken(GateError):
    """The text handed over is not a pairing token."""


class TransportError(GateError):
    """The connection failed, or was closed before an answer arrived."""


class ProtocolError(GateError):
    """The gate answered, but not with something this client can read."""


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


class GateBusy(GateError):
    """The gate cannot serve this right now and the caller should try again.

    A dialog open on the group or entry being written is the usual reason: the plugin
    refuses a write that the dialog would take straight back off again when it is accepted.
    """


class DatabaseLocked(GateError):
    """Every route answers this while the database is locked.

    It names the lock and nothing else, deliberately: not which database, not whether the caller
    would have been admitted. A client learns that it should wait, which is the only thing it can
    act on.
    """


class NotAdmitted(GateError):
    """The certificate is not one this database holds a record for.

    Every connection is checked against the record, not against the certificate alone, so this is
    also what a revoked client sees, immediately.
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
