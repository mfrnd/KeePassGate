"""The name a client puts in its ClientHello.

It is how a connection says which database it is for without naming it.

``hex(nonce) "." hex(HMAC-SHA256(key, nonce)[0..16]) ".kpg"``, rebuilt for every connection. Two
labels because 64 hex characters exceed the 63-octet limit on one, which is RFC 1035 section 2.3.4.
https://www.rfc-editor.org/rfc/rfc1035#section-2.3.4

The key is the gate key once a client is paired, and the invitation token while it is pairing. They
are the same length for exactly that reason, so one construction covers both.

Two honest limits, carried over from the plugin's own account of it. The tag is replayable, because
the nonce is the client's and the server keeps no record of which have been used, so this stops
blind probing rather than somebody who captured a connection. And the name travels in the clear, so
an observer learns that some database is being addressed.
"""

from __future__ import annotations

import hmac
import secrets
from hashlib import sha256

#: The last label. Not a real top level domain and never resolved: nothing looks this name up,
#: because the client is told where to connect and the name only says which database it means once
#: it is there.
SUFFIX = "kpg"

#: Sixteen bytes, so the label is 32 hex characters and fits what a DNS label allows.
NONCE_LENGTH = 16

#: The first sixteen bytes of the HMAC, for the same reason. Truncating an HMAC is what RFC 2104
#: section 5 describes, and 128 bits is far more than a value an attacker gets one guess at needs.
#: https://www.rfc-editor.org/rfc/rfc2104#section-5
TAG_LENGTH = 16


def tag(key: bytes, nonce: bytes) -> bytes:
    """The tag for a nonce under a key, truncated as the name requires."""
    return hmac.new(key, nonce, sha256).digest()[:TAG_LENGTH]


def name_for(key: bytes, nonce: bytes) -> str:
    """The name for a nonce that has already been chosen."""
    if len(nonce) != NONCE_LENGTH:
        raise ValueError(f"a nonce is {NONCE_LENGTH} bytes, not {len(nonce)}")

    return f"{nonce.hex()}.{tag(key, nonce).hex()}.{SUFFIX}"


def new_name(key: bytes) -> tuple[str, bytes]:
    """A fresh name for one connection, and the nonce it was built from.

    The nonce is returned because the proofs are computed over it: the client picks it, so it is
    the client's half of the freshness in an exchange where the binding is a constant.
    """
    nonce = secrets.token_bytes(NONCE_LENGTH)
    return name_for(key, nonce), nonce
