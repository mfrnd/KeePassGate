"""A gate to talk to, without a KeePass in the room.

The C# side draws the same line: a tier of tests that cannot load KeePass by construction, and a
tier that drives the real thing. This is the first of those. The gate itself is
`pykeepassgate.testing`, which the package ships so that a program built on this client can be
tested the same way: a certificate authority, a host certificate under it, and a TLS 1.3 server
that answers one request per connection and then closes, which is exactly what the plugin's
transport does. What is here are the fixtures that build one for each test.

It is not a second implementation of the protocol. It answers whatever a test tells it to answer,
including nonsense, because most of what a client has to get right is what it does with a refusal.
The one place a test computes something is the pairing exchange, and there it uses this package's
own `proofs`, so what those tests prove is that the client drives the exchange in the right order
with the right values carried across, not that the construction is the one the document describes.
The vectors taken from the plugin are what prove that.
"""

from __future__ import annotations

import base64
from collections.abc import Callable, Iterator
from pathlib import Path
from typing import Any

import pytest
from cryptography import x509
from cryptography.hazmat.primitives import serialization
from cryptography.hazmat.primitives.asymmetric import ec
from cryptography.hazmat.primitives.serialization import pkcs12

from pykeepassgate.testing import Authority, FakeGate, Handler, Issued

#: What the plugin writes into a responder certificate, copied from `AuthorityIssuer.ResponderClaim`
#: rather than read from the client, so that the client's own constant is compared with something.
#: `CA.md` 208 is why the marker is a URI with a path: `System.Uri` normalizes `kpg://responder` to
#: `kpg://responder/` on the way in, and a client then reads back something else than was written.
RESPONDER_CLAIM = "kpg://host/responder"


@pytest.fixture
def authority(tmp_path: Path) -> Authority:
    """A database's authority, and the two leaves under it the tests need."""
    return Authority(tmp_path)


@pytest.fixture
def host(authority: Authority) -> Issued:
    """The certificate the gate serves with."""
    return authority.issue("host", x509.oid.ExtendedKeyUsageOID.SERVER_AUTH, "host.pem")


@pytest.fixture
def responder(authority: Authority) -> Issued:
    """What a locked database answers with: the same authority, and the marker that says so."""
    return authority.issue(
        "KeePassGate responder",
        x509.oid.ExtendedKeyUsageOID.SERVER_AUTH,
        "responder.pem",
        uri=RESPONDER_CLAIM,
    )


@pytest.fixture
def client_certificate(authority: Authority) -> Issued:
    """A certificate the gate would have issued at pairing."""
    return authority.issue(
        "agent-invented", x509.oid.ExtendedKeyUsageOID.CLIENT_AUTH, "client.pem"
    )


@pytest.fixture
def gate(host: Issued, authority: Authority) -> Iterator[FakeGate]:
    """A gate that is listening, and stops when the test ends."""
    with FakeGate(host, authority.pem) as running:
        yield running


@pytest.fixture
def bundle(authority: Authority, client_certificate: Issued, host: Issued) -> dict[str, Any]:
    """What `/pair/request` hands over, with the members the plugin sends.

    The identity is a PKCS#12 with no password, which is what the gate sends: what protects it in
    the database is the database, and what protects it on this side is the file. It is one
    `cryptography` serialized itself, though, not one .NET Framework's export wrote, so reading it
    says nothing about reading the plugin's.
    """
    loaded = serialization.load_pem_private_key(client_certificate.pem.read_bytes(), password=None)
    # The authority issues P-256 and nothing else, which is what makes this cast safe.
    assert isinstance(loaded, ec.EllipticCurvePrivateKey)

    return {
        "identity": base64.b64encode(
            pkcs12.serialize_key_and_certificates(
                name=b"agent-invented",
                key=loaded,
                cert=client_certificate.certificate,
                cas=None,
                encryption_algorithm=serialization.NoEncryption(),
            )
        ).decode(),
        "authority": base64.b64encode(
            authority.certificate.public_bytes(serialization.Encoding.DER)
        ).decode(),
        "gateKey": base64.b64encode(bytes(range(32))).decode(),
        "subject": "CN=agent-invented",
        "database": "abcdef01",
        "expires": "2126-08-15T09:00:00Z",
        "profiles": ["reader"],
    }


@pytest.fixture
def answers() -> Callable[[int, dict[str, Any] | str], Handler]:
    """A handler that always answers the same thing, which is most of what a test wants."""
    import json

    def build(status: int, body: dict[str, Any] | str) -> Handler:
        text = body if isinstance(body, str) else json.dumps(body)
        return lambda path, request, client: (status, text)

    return build
