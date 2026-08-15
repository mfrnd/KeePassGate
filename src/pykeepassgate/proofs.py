"""The proofs the two sides exchange before anybody is asked to approve anything.

Three values, all ``HMAC-SHA256`` under the invitation token, each over a different label so that no
proof can ever be replayed as another.

**The binding is RFC 5929 ``tls-server-end-point``**: ``SHA-256`` over the DER of the certificate
the server is actually serving with. It needs no TLS interface on either side, which is what makes
it usable here: the server knows the certificate it chose, and the client has just been sent it.
``SHA-256`` is not negotiated, because this authority signs with ``ecdsa-with-SHA256`` and nothing
else. https://www.rfc-editor.org/rfc/rfc5929#section-4

What each one is for:

- **The client speaks first.** An impostor learns nothing and does no work until the caller has
  shown it holds the token, so a captured name replayed at the gate dies at the first route rather
  than in front of the operator.
- **The server answers.** An unpaired client has no anchor yet and would otherwise accept whatever
  certificate arrived. Because the binding hashes the certificate in use, a machine in the middle
  can only produce a proof over its own certificate, which recomputes to something else here.
- **The request is proved too.** ``tls-server-end-point`` is a constant for the life of a
  certificate, so the freshness comes from the nonce the client chose and the challenge the server
  chose, and the request carries the name and profiles being asked for, so what the operator is
  shown is what was signed for.
"""

from __future__ import annotations

import hmac
from collections.abc import Iterable, Sequence
from hashlib import sha256

CLIENT_LABEL = b"kpg-client-proof"
SERVER_LABEL = b"kpg-server-proof"
REQUEST_LABEL = b"kpg-request-proof"

#: ``HMAC-SHA256`` output, so every proof is this long.
LENGTH = 32


def binding(server_certificate_der: bytes) -> bytes:
    """RFC 5929 ``tls-server-end-point`` over the certificate the connection is served with."""
    return sha256(server_certificate_der).digest()


def client(token: bytes, bound: bytes, nonce: bytes) -> bytes:
    """What the client sends first, over the certificate it was given and the name it chose."""
    return _compute(token, CLIENT_LABEL, [bound, nonce])


def server(token: bytes, bound: bytes, nonce: bytes, challenge: bytes) -> bytes:
    """What the server answers with, and what tells a client with no anchor who it reached."""
    return _compute(token, SERVER_LABEL, [bound, nonce, challenge])


def request(
    token: bytes, bound: bytes, nonce: bytes, challenge: bytes, asked_for: bytes
) -> bytes:
    """What commits the request to the name and the profiles it asks for."""
    return _compute(token, REQUEST_LABEL, [bound, nonce, challenge, asked_for])


def asked(name: str, profiles: Iterable[str]) -> bytes:
    """What a pairing request commits to: the name asked for, and the profiles, in order.

    Both sides build this from the same values, so a request altered on the way no longer proves
    itself, and the operator is shown what was signed for rather than what arrived.
    """
    return f"{name}\n{','.join(profiles)}".encode()


def matches(expected: bytes, offered: bytes) -> bool:
    """Compare two proofs in a time that does not depend on where they first differ."""
    return hmac.compare_digest(expected, offered)


def _compute(token: bytes, label: bytes, parts: Sequence[bytes]) -> bytes:
    """Every part length prefixed, so no two different sets of parts produce the same bytes.

    Without the prefix a name and a profile list could be shuffled across the boundary between them
    and still verify.
    """
    buffer = bytearray(label)
    buffer.append(0)

    for part in parts:
        buffer += len(part).to_bytes(4, "big")
        buffer += part

    return hmac.new(token, bytes(buffer), sha256).digest()
