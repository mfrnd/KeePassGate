"""PyKeePassGate: the reference client for KeePassGate.

Two halves, and the split is deliberate. `pykeepassgate.api` is the library: pairing, the paired
client, and the pieces of the protocol on their own so that each can be read and tested without a
KeePass in the room. `pykeepassgate.cli` is a command line built on that library and nothing else,
so anything the command line can do, a program can do the same way.

Being the reference implementation means the code is the second copy of the specification, so what
it does not do is a statement too: a route with no method here is a route the gate does not serve.
"""

from __future__ import annotations

from .api import (
    Credentials,
    DatabaseLocked,
    Exchange,
    GateBusy,
    GateClient,
    GateError,
    MalformedToken,
    NotAdmitted,
    Pairing,
    PairingError,
    PairingRefused,
    PairingUnanswered,
    ProofRefused,
    ProtocolError,
    RouteRefused,
    Status,
    StorageError,
    TransportError,
    pair,
)

__version__ = "0.1.0"

__all__ = [
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
    "__version__",
    "pair",
]
