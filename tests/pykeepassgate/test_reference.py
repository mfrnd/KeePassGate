"""The claim that this is the reference client, made checkable.

`src/pykeepassgate/README.md` says a route the gate serves and this does not cover is a gap, and a
method here with no route behind it is a lie. Until the plugin's routing table was a document, that
was a promise. Now it is a file, so it is a test: this reads the same `openapi.yaml` the plugin
embeds and holds the client to it.

The map below is the whole of the claim. Adding a route to the document fails this until somebody
adds the call, and removing one fails it until somebody removes the call.
"""

from __future__ import annotations

import inspect
from pathlib import Path
from typing import Any

import pytest
import yaml

import pykeepassgate
from pykeepassgate import api
from pykeepassgate import client as client_module
from pykeepassgate import model as model_module
from pykeepassgate import pairing as pairing_module
from pykeepassgate.client import GateClient
from pykeepassgate.pairing import Pairing

#: `operationId` to what a caller uses to reach it. Written here rather than in the package because
#: it is an assertion about the package rather than something the package needs.
#:
#: One name appears against two operations twice over. `head()` answers for every `head` the
#: document declares, because a `head` carries no content and so has nothing to shape per route.
#: And `entries()` asks the flat sweep with no group and the group's own listing with one,
#: because they are two questions rather than one narrowed, and a caller says which by naming a
#: group or not.
COVERED = {
    "getStatus": (GateClient, "status"),
    "headStatus": (GateClient, "head"),
    "getIdentity": (GateClient, "identity"),
    "headIdentity": (GateClient, "head"),
    "revokeCertificate": (GateClient, "revoke_certificate"),
    "renewCertificate": (GateClient, "renew"),
    "pairHello": (Pairing, "hello"),
    "pairRequest": (Pairing, "request"),

    "listGroups": (GateClient, "groups"),
    "headGroups": (GateClient, "head"),
    "readGroup": (GateClient, "group"),
    "headGroup": (GateClient, "head"),
    "listSubgroups": (GateClient, "subgroups"),
    "headSubgroups": (GateClient, "head"),
    "createGroup": (GateClient, "create_group"),
    "updateGroup": (GateClient, "update_group"),
    "deleteGroup": (GateClient, "delete_group"),
    "getGroupAccess": (GateClient, "group_access"),
    "headGroupAccess": (GateClient, "head"),

    "listEntries": (GateClient, "entries"),
    "headEntries": (GateClient, "head"),
    "listGroupEntries": (GateClient, "entries"),
    "headGroupEntries": (GateClient, "head"),
    "readEntry": (GateClient, "entry"),
    "headEntry": (GateClient, "head"),
    "createEntry": (GateClient, "create_entry"),
    "updateEntry": (GateClient, "update_entry"),
    "deleteEntry": (GateClient, "delete_entry"),
    "getEntryAccess": (GateClient, "entry_access"),
    "headEntryAccess": (GateClient, "head"),

    "listFields": (GateClient, "fields"),
    "headFields": (GateClient, "head"),
    "readField": (GateClient, "field"),
    "headField": (GateClient, "head"),
    "createField": (GateClient, "create_field"),
    "updateField": (GateClient, "update_field"),
    "stripField": (GateClient, "strip_field"),

    "listAttachments": (GateClient, "attachments"),
    "headAttachments": (GateClient, "head"),
    "readAttachment": (GateClient, "attachment"),
    "headAttachment": (GateClient, "head"),
    "createAttachment": (GateClient, "create_attachment"),
    "updateAttachment": (GateClient, "update_attachment"),
    "stripAttachment": (GateClient, "strip_attachment"),
}

#: Where the repository is, if these tests are in one: its `pyproject.toml` is what says so.
REPOSITORY = Path(__file__).resolve().parents[2]

DOCUMENT = REPOSITORY / "src" / "KeePassGate" / "Spec" / "openapi.yaml"


def routing_table() -> dict[str, Any]:
    """The routing table the plugin embeds, or a failure that names where it was looked for.

    Skipped only where these tests are not in the repository, which leaves nothing to hold the
    client to. Inside it, a document that is not at its path fails rather than skips: the plugin
    embeds it by path, so moving it is one edit on that side, and a skip here would turn the
    check that this client covers every route the gate serves into a row of green skips.
    """
    if not (REPOSITORY / "pyproject.toml").exists():  # pragma: no cover, only outside it
        pytest.skip(f"no repository at {REPOSITORY}, so no routing table to hold the client to")

    if not DOCUMENT.exists():
        pytest.fail(f"no routing table at {DOCUMENT}, which is where the plugin embeds it from")

    parsed: dict[str, Any] = yaml.safe_load(DOCUMENT.read_text("utf-8"))
    return parsed


@pytest.fixture(scope="module")
def document() -> dict[str, Any]:
    """The routing table the plugin embeds, read from the same file it embeds."""
    return routing_table()


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


def test_the_document_carries_the_version_this_client_is_installed_as(
    document: dict[str, Any],
) -> None:
    """One version for the plugin, this client and the document, moved together.

    It is written once, in `Directory.Build.props`, and this client is installed with it, but the
    document is YAML and takes it from nowhere, so this is what holds the two to each other. The
    plugin's tests hold the same document to the plugin.
    """
    assert str(document["info"]["version"]) == pykeepassgate.__version__


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
    assert declared["getIdentity"]["path"] == client_module.IDENTITY_PATH
    assert declared["revokeCertificate"]["path"] == client_module.CERTIFICATE_PATH
    assert declared["renewCertificate"]["path"] == client_module.RENEW_PATH
    assert declared["pairHello"]["path"] == pairing_module.HELLO_PATH
    assert declared["pairRequest"]["path"] == pairing_module.REQUEST_PATH


def test_the_methods_this_client_uses_are_the_ones_declared(document: dict[str, Any]) -> None:
    declared = operations(document)

    assert declared["getStatus"]["method"] == "get"
    assert declared["getIdentity"]["method"] == "get"
    assert declared["revokeCertificate"]["method"] == "delete"
    assert declared["renewCertificate"]["method"] == "post"
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
    would have refused, which is an oracle rather than a route. The same goes for everything else
    that decides who may ask and what is reached, and the plugin refuses to load a pair that
    differs in any of it: whether it reaches attachments, whether it needs a certificate, and
    whether it carries a body.
    """
    for path, methods in document["paths"].items():
        if "get" in methods and "head" in methods:
            head, get = methods["head"], methods["get"]
            for rule in ("x-kpg-verb", "x-kpg-attachments", "x-kpg-unauthenticated"):
                assert head.get(rule) == get.get(rule), (
                    f"{path} declares a head whose {rule} is not its own get's"
                )

            assert ("requestBody" in head) == ("requestBody" in get), (
                f"{path} declares a body on one of its head and its get and not on the other"
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


def followed(declared: dict[str, Any], document: dict[str, Any]) -> dict[str, Any]:
    """A declaration with its `$ref` followed, which is how the document declares a parameter."""
    while "$ref" in declared:
        target: Any = document
        for part in declared["$ref"].removeprefix("#/").split("/"):
            target = target[part]
        declared = target
    return declared


def test_every_query_parameter_the_gate_reads_is_one_this_client_sends(
    document: dict[str, Any],
) -> None:
    """A query name the gate reads and this client never sends is a gap, as a route would be.

    There is one, the flag that leaves expired entries out of the two entry listings, and it is
    `entries(include_expired=False)` that sends it, under the name `client.INCLUDE_EXPIRED` spells.
    Declared on the path item or on the operation, since OpenAPI allows either.
    """
    declared: dict[str, set[str]] = {}
    for methods in document["paths"].values():
        for method, operation in methods.items():
            if method not in METHODS:
                continue

            for given in methods.get("parameters", []) + operation.get("parameters", []):
                parameter = followed(given, document)
                if parameter["in"] == "query":
                    declared.setdefault(parameter["name"], set()).add(operation["operationId"])

    assert declared == {
        client_module.INCLUDE_EXPIRED: {
            "listEntries", "headEntries", "listGroupEntries", "headGroupEntries",
        },
    }
    assert "include_expired" in inspect.signature(GateClient.entries).parameters


def test_the_expiry_this_client_reads_and_writes_is_the_member_the_document_declares(
    document: dict[str, Any],
) -> None:
    """Read off a read, written into a create or an update, under the one name `model` spells."""
    schemas = document["components"]["schemas"]

    assert model_module.EXPIRES in schemas["EntryFields"]["properties"]
    assert model_module.EXPIRES in schemas["Entry"]["required"]
    assert "expires" in inspect.signature(GateClient.create_entry).parameters
    assert "expires" in inspect.signature(GateClient.update_entry).parameters
