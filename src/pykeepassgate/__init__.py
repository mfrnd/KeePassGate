"""PyKeePassGate: the reference client for KeePassGate.

Two halves, and the split is deliberate. `pykeepassgate.api` is the library: pairing, the paired
client, and the pieces of the protocol on their own so that each can be read and tested without a
KeePass in the room. `pykeepassgate.cli` is a command line built on that library and nothing else,
so anything the command line can do, a program can do the same way.

`pykeepassgate.testing` stands beside them and is not imported here: a scripted stand-in gate for
testing a program built on the library, which answers what a test tells it and is not a model of
the rules. So does `pykeepassgate.refs`, which follows field references in a value as KeePass
does, reading each entry one names under this client's own rules.

Being the reference implementation means the code is the second copy of the specification, so what
it does not do is a statement too: a route with no method here is a route the gate does not serve.
"""

from __future__ import annotations

from .api import (
    Access,
    AmbiguousName,
    AuthorityUndecided,
    Credentials,
    DatabaseLocked,
    Exchange,
    GateBusy,
    GateClient,
    GateError,
    GateUnavailable,
    Identity,
    IncompatibleGate,
    MalformedToken,
    NotAdmitted,
    NotPaired,
    Pairing,
    PairingError,
    PairingRefused,
    PairingUnanswered,
    PreconditionFailed,
    ProofRefused,
    ProtocolError,
    Renewal,
    RenewalError,
    RenewalRefused,
    RenewalRefusedForever,
    RenewalRefusedThisSession,
    RenewalTooEarly,
    RenewalUnanswered,
    RouteRefused,
    Status,
    StorageError,
    TransportError,
    Unresolved,
    Withheld,
    pair,
)
from .compatibility import CLIENT_VERSION

#: The version this client was installed as, which the plugin and the routing document carry too,
#: and which `compatibility` compares with the version a gate reports.
__version__ = CLIENT_VERSION

__all__ = [
    "Access",
    "AmbiguousName",
    "AuthorityUndecided",
    "Credentials",
    "DatabaseLocked",
    "Exchange",
    "GateBusy",
    "GateClient",
    "GateError",
    "GateUnavailable",
    "Identity",
    "IncompatibleGate",
    "MalformedToken",
    "NotAdmitted",
    "NotPaired",
    "Pairing",
    "PairingError",
    "PairingRefused",
    "PairingUnanswered",
    "PreconditionFailed",
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
    "Unresolved",
    "Withheld",
    "__version__",
    "pair",
]
