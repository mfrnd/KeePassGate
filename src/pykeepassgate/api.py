"""The library, gathered into one name so that a program imports one thing.

Everything a caller needs is re-exported here: pairing, the paired client, the credentials on disk,
and the exceptions that say what to do next. The modules underneath stay importable, because the
pieces of the protocol are worth reading on their own, but nothing outside this package has to know
which module a name lives in.

The one thing `api` adds rather than re-exports is `pair`, which is the whole exchange as one call.
It exists because the two-connection shape is the protocol's business rather than the caller's, and
a reference implementation that made every caller drive both halves by hand would be teaching the
wrong lesson.
"""

from __future__ import annotations

from datetime import timedelta
from pathlib import Path

from .client import ROOT, GateClient, Renewal, Status
from .credentials import Credentials, Prepared, default_directory, forget, load, prepare, save
from .errors import (
    AuthorityUndecided,
    DatabaseLocked,
    GateBusy,
    GateError,
    GateUnavailable,
    MalformedToken,
    NotAdmitted,
    NotPaired,
    PairingError,
    PairingRefused,
    PairingUnanswered,
    ProofRefused,
    ProtocolError,
    RenewalError,
    RenewalRefused,
    RenewalRefusedForever,
    RenewalRefusedThisSession,
    RenewalTooEarly,
    RenewalUnanswered,
    RouteRefused,
    StorageError,
    TransportError,
    Withheld,
)
from .model import Attachment, Entry, Field, Fields, Group
from .pairing import (
    LONGEST_LIFETIME,
    SHORTEST_LIFETIME,
    Exchange,
    Pairing,
    lifetime_text,
    refuse_unusable,
)
from .transport import (
    DEFAULT_CONNECTION_TIMEOUT,
    DEFAULT_RETRY_BUSY_TIMEOUT,
    DEFAULT_RETRY_LOCKED_TIMEOUT,
    DEFAULT_RETRY_UNDECIDED_TIMEOUT,
    LOOPBACK,
    MAX_RESPONSE_BYTES,
    PAIRING_CONNECTION_TIMEOUT,
    KeyPassword,
    Retry,
    connection_limit,
    key_password_limit,
    key_secret,
    port_number,
    response_limit,
)

__all__ = [
    "DEFAULT_CONNECTION_TIMEOUT",
    "DEFAULT_PORT",
    "DEFAULT_RETRY_BUSY_TIMEOUT",
    "DEFAULT_RETRY_LOCKED_TIMEOUT",
    "DEFAULT_RETRY_UNDECIDED_TIMEOUT",
    "LONGEST_LIFETIME",
    "LOOPBACK",
    "MAX_RESPONSE_BYTES",
    "PAIRING_CONNECTION_TIMEOUT",
    "ROOT",
    "SHORTEST_LIFETIME",
    "Attachment",
    "AuthorityUndecided",
    "Credentials",
    "DatabaseLocked",
    "Entry",
    "Exchange",
    "Field",
    "Fields",
    "GateBusy",
    "GateClient",
    "GateError",
    "GateUnavailable",
    "Group",
    "KeyPassword",
    "MalformedToken",
    "NotAdmitted",
    "NotPaired",
    "Pairing",
    "PairingError",
    "PairingRefused",
    "PairingUnanswered",
    "Prepared",
    "ProofRefused",
    "ProtocolError",
    "Renewal",
    "RenewalError",
    "RenewalRefused",
    "RenewalRefusedForever",
    "RenewalRefusedThisSession",
    "RenewalTooEarly",
    "RenewalUnanswered",
    "RouteRefused",
    "Status",
    "StorageError",
    "TransportError",
    "Withheld",
    "default_directory",
    "forget",
    "load",
    "pair",
    "prepare",
    "save",
]

#: The port the gate listens on unless its operator chose another, the plugin's own
#: `GateOptions.DefaultPort`, so that pairing with a KeePass nobody reconfigured names none.
DEFAULT_PORT = 51776


def pair(
    invitation: str,
    name: str,
    *,
    port: int = DEFAULT_PORT,
    profiles: list[str] | None = None,
    directory: Path | None = None,
    profile: str = "default",
    # Not `DEFAULT_CONNECTION_TIMEOUT`. This is one of the two exchanges that wait on a person, and
    # the gate gives itself six minutes for it because its own worst case is 305 seconds. Hanging up
    # first cancels nothing: the invitation is spent and the record is written and saved
    # anyway, and only the answer is lost.
    connection_timeout: float = PAIRING_CONNECTION_TIMEOUT,
    host: str = LOOPBACK,
    retry_busy_timeout: float | None = DEFAULT_RETRY_BUSY_TIMEOUT,
    retry_locked_timeout: float | None = DEFAULT_RETRY_LOCKED_TIMEOUT,
    retry_undecided_timeout: float | None = DEFAULT_RETRY_UNDECIDED_TIMEOUT,
    allow_shared_directory: bool = False,
    max_response_bytes: int = MAX_RESPONSE_BYTES,
    lifetime: timedelta | None = None,
    key_password: KeyPassword | None = None,
) -> Credentials:
    """Pair with a KeePass and keep what it hands over.

    Blocks while the operator reads the dialog, which is the slow half and is meant to be. What
    comes back says which profiles were actually approved, and that is not always what was asked
    for: the dialog can take any of them away.

    **`lifetime` is how long the certificate is asked to last**, a `timedelta` of whole days from
    `SHORTEST_LIFETIME` to `LONGEST_LIFETIME`, and the operator picks the one it gets: `expires`
    on what comes back says which. Leaving it out asks for nothing, and the dialog offers a year.

    **An approval the gate holds is asked for again by itself.** When the operator approves and
    KeePass is too busy to write it, the gate keeps the approval and answers `GateBusy` with
    `held_until`, and this starts the exchange again, every `Retry-After`, until the approval is
    written or that moment passes, without spending the busy budget and within the six minutes the
    gate gives a pairing. The same name, profiles and lifetime are what make it the same request.

    **The exchange is the unit that is retried, not either half of it.** A `503` from the gate on
    this path means the invitation was not spent: `PAIRING.md` §303 refuses before the operator is
    disturbed, and `PairingHandler` answers `500` rather than `503` everywhere after the burn. So
    the whole thing can be started again, which is what "the invitee can simply ask again" in
    §303 means, and what this loop does. What it does not do is send either message twice: a new
    exchange is a new hello, a new nonce and a new challenge.

    **Where it will be kept is settled before anything is asked.** The gate spends the invitation
    and saves the admission record before it answers, so a directory that turns out to be a file
    or to refuse writes, found only then, loses an identity that cannot be asked for again.
    `prepare` finds it out first, when a refusal has cost nothing, and holds the four files open
    across every attempt, so the write goes through handles already granted; a pairing that does
    not complete takes away again whatever files opening created. `allow_shared_directory` is
    passed to it, and accepts a directory other accounts can reach; see `prepare`.

    **`key_password` stores the client's key encrypted under it**: the password, as text or
    bytes, or a callable returning it, which is asked once and before the hello, since a password
    that cannot be had after the invitation is spent loses an identity nobody can ask for again.
    What it protects is a copy of the directory taken elsewhere, a backup or a disk image, and
    nothing against a process running as the same user.
    """
    # Refused before `prepare`, which opens files, and before anything is asked of the gate: a
    # setting that could not work, a profile that is not one directory's name, each budget,
    # which `Retry` holds to the same rule, and a key password that is none.
    port_number(port)
    connection_limit(connection_timeout)
    response_limit(max_response_bytes)
    if lifetime is not None:
        lifetime_text(lifetime)
    where = directory or default_directory(profile)
    # An approval the gate holds is asked for again until it stops being held, whatever the busy
    # budget says, within the six minutes the gate gave the request: not asking loses it.
    retry = Retry(
        busy=retry_busy_timeout,
        locked=retry_locked_timeout,
        undecided=retry_undecided_timeout,
        held=PAIRING_CONNECTION_TIMEOUT,
    )
    refuse_unusable(invitation, name, profiles or [])
    key_password_limit(key_password)

    with prepare(where, allow_shared_directory=allow_shared_directory) as prepared:
        # Last, once nothing else can be refused before the hello, the directory among them,
        # because a callable may be asking a person.
        password = None if key_password is None else key_secret(key_password)

        while True:
            # Built without the budgets, deliberately. `hello` would otherwise wait one out on its
            # own and this loop would wait the same one out again, so a caller's thirty seconds
            # would buy some multiple of thirty seconds that nothing here could name.
            exchange = Pairing(
                port,
                invitation,
                connection_timeout=connection_timeout,
                host=host,
                max_response_bytes=max_response_bytes,
            )

            try:
                started = exchange.hello()
                bundle = exchange.request(started, name, profiles or [], lifetime)
            except GateUnavailable as refusal:
                if retry.waited(refusal):
                    continue

                raise

            return prepared.save(port, bundle, key_password=password)
