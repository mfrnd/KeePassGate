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

from pathlib import Path

from .client import GateClient, Status
from .credentials import Credentials, default_directory, load, save
from .errors import (
    DatabaseLocked,
    GateBusy,
    GateError,
    MalformedToken,
    NotAdmitted,
    PairingError,
    PairingRefused,
    PairingUnanswered,
    ProofRefused,
    ProtocolError,
    RouteRefused,
    StorageError,
    TransportError,
)
from .pairing import Exchange, Pairing
from .transport import DEFAULT_TIMEOUT, PAIRING_TIMEOUT

__all__ = [
    "DEFAULT_PORT",
    "DEFAULT_TIMEOUT",
    "PAIRING_TIMEOUT",
    "Credentials",
    "DatabaseLocked",
    "Exchange",
    "GateBusy",
    "GateClient",
    "GateError",
    "MalformedToken",
    "NotAdmitted",
    "Pairing",
    "PairingError",
    "PairingRefused",
    "PairingUnanswered",
    "ProofRefused",
    "ProtocolError",
    "RouteRefused",
    "Status",
    "StorageError",
    "TransportError",
    "default_directory",
    "load",
    "pair",
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
    # Not `DEFAULT_TIMEOUT`. This is the one exchange that waits on a person, and the gate
    # gives itself five minutes for it because its own worst case is 185 seconds. Hanging up
    # first cancels nothing: the invitation is spent and the record is written and saved
    # anyway, and only the answer is lost.
    timeout: float = PAIRING_TIMEOUT,
) -> Credentials:
    """Pair with a KeePass and keep what it hands over.

    Blocks while the operator reads the dialog, which is the slow half and is meant to be. What
    comes back says which profiles were actually approved, and that is not always what was asked
    for: the dialog can take any of them away.
    """
    exchange = Pairing(port, invitation, timeout=timeout)
    started = exchange.hello()
    bundle = exchange.request(started, name, profiles or [])

    where = directory or default_directory(profile)
    return save(where, port, bundle)
