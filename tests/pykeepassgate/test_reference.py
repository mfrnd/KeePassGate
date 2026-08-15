"""The claim that this is the reference client, made checkable.

`src/pykeepassgate/README.md` says a route the gate serves and this does not cover is a gap, and a
method here with no route behind it is a lie. Until the plugin's routing table was a document, that
was a promise. Now it is a file, so it is a test: this reads the same `openapi.yaml` the plugin
embeds and holds the client to it.

The map below is the whole of the claim. Adding a route to the document fails this until somebody
adds the call, and removing one fails it until somebody removes the call.
"""

from __future__ import annotations

from pathlib import Path
from typing import Any

import pytest
import yaml

from pykeepassgate import api
from pykeepassgate import client as client_module
from pykeepassgate import pairing as pairing_module
from pykeepassgate.client import GateClient
from pykeepassgate.pairing import Pairing

#: `operationId` to what a caller uses to reach it. Written here rather than in the package because
#: it is an assertion about the package rather than something the package needs.
COVERED = {
    "getStatus": (GateClient, "status"),
    "headStatus": (GateClient, "head"),
    "pairHello": (Pairing, "hello"),
    "pairRequest": (Pairing, "request"),
}

#: What a path item may carry beside its methods. Read as methods, these would be indexed for
#: an `operationId` they do not have.
NOT_METHODS = {"summary", "description", "parameters", "servers", "$ref"}

DOCUMENT = Path(__file__).resolve().parents[2] / "src" / "KeePassGate" / "Spec" / "openapi.yaml"


@pytest.fixture(scope="module")
def document() -> dict[str, Any]:
    """The routing table the plugin embeds, read from the same file it embeds."""
    if not DOCUMENT.exists():  # pragma: no cover, only when the tree is not the repository
        pytest.skip(f"no routing table at {DOCUMENT}")

    parsed: dict[str, Any] = yaml.safe_load(DOCUMENT.read_text("utf-8"))
    return parsed


#: What a key under a path item has to be to be an operation. OpenAPI 3.0.3 also allows `summary`,
#: `description`, `parameters`, `servers` and `$ref` there, and the plugin's reader ignores them.
#: Reading every key as a method would make this file report a `TypeError` out of a helper whose
#: whole job is to report that the document and this client disagree.
METHODS = frozenset(
    {"get", "put", "post", "delete", "options", "head", "patch", "trace"}
)


def operations(document: dict[str, Any]) -> dict[str, dict[str, Any]]:
    """Every declared operation, by name."""
    found: dict[str, dict[str, Any]] = {}
    for path, methods in document["paths"].items():
        for method, operation in methods.items():
            if method not in METHODS:
                continue

            found[operation["operationId"]] = {"path": path, "method": method, **operation}

    return found


def test_every_operation_the_gate_declares_has_a_call_here(document: dict[str, Any]) -> None:
    declared = set(operations(document))

    assert declared == set(COVERED), (
        "the document and this client disagree about what the API is: "
        f"only in the document {sorted(declared - set(COVERED))}, "
        f"only here {sorted(set(COVERED) - declared)}"
    )


def test_each_call_exists_and_is_callable(document: dict[str, Any]) -> None:
    for name, (owner, method) in COVERED.items():
        missing = f"{name} maps to {owner.__name__}.{method}, which is not there"
        assert hasattr(owner, method), missing
        assert callable(getattr(owner, method))


def test_the_paths_this_client_sends_are_the_paths_the_document_declares(
    document: dict[str, Any],
) -> None:
    # The one place the client writes a path down, held to the document rather than to itself.
    declared = operations(document)

    assert declared["getStatus"]["path"] == client_module.STATUS_PATH
    assert declared["pairHello"]["path"] == pairing_module.HELLO_PATH
    assert declared["pairRequest"]["path"] == pairing_module.REQUEST_PATH


def test_the_methods_this_client_uses_are_the_ones_declared(document: dict[str, Any]) -> None:
    declared = operations(document)

    assert declared["getStatus"]["method"] == "get"
    assert declared["pairHello"]["method"] == "post"
    assert declared["pairRequest"]["method"] == "post"


def test_every_get_declares_the_head_that_answers_for_it(document: dict[str, Any]) -> None:
    """RFC 9110 section 9.3.2: a `head` is what its `get` answers, without the content.

    They are two declarations sharing one handler, so the plugin refuses to load a `get`
    with no `head`. This is the same rule read from the other side, so a document that
    grew a `get` without one fails here before anybody starts KeePass.
    """
    for path, methods in document["paths"].items():
        if "get" in methods:
            assert "head" in methods, f"{path} declares a get and no head"


def test_a_head_is_governed_exactly_as_its_own_get_is(document: dict[str, Any]) -> None:
    """A `head` is governed by whatever its own `get` is governed by.

    One carrying a different verb would answer whether a node is there to a caller its `get`
    would have refused, which is an oracle rather than a route.
    """
    for path, methods in document["paths"].items():
        if "get" in methods and "head" in methods:
            assert methods["head"].get("x-kpg-verb") == methods["get"].get("x-kpg-verb"), (
                f"{path} declares a head governed differently from its own get"
            )


def test_both_pairing_routes_declare_what_a_caller_that_may_not_pair_gets(
    document: dict[str, Any],
) -> None:
    """Neither of them answers one thing to everybody, so the document declares both.

    A connection that may not pair gets `401` when the database does not admit it and `404` when it
    does. Declaring only one would leave a client reading this ready for half of what the gate
    says. The `404` was the one declared, which is the half a stranger never sees.
    """
    for name in ("pairHello", "pairRequest"):
        declared = set(operations(document)[name]["responses"])
        assert {"401", "404"} <= declared, (
            f"{name} answers 401 and 404, and declares {sorted(declared)}"
        )


def test_the_two_pairing_routes_are_the_only_ones_reachable_without_a_certificate(
    document: dict[str, Any],
) -> None:
    # What `api.pair` relies on: it has no certificate to offer, so if anything else ever became
    # reachable without one, this client would be the wrong shape for it.
    open_routes = {
        name for name, operation in operations(document).items()
        if operation.get("x-kpg-unauthenticated") is True
    }

    assert open_routes == {"pairHello", "pairRequest"}
    assert callable(api.pair)
