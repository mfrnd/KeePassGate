"""The promise `errors.py` makes: one root, and a name that says what to do next."""

from __future__ import annotations

import inspect
import re
from pathlib import Path

import pytest

import pykeepassgate
from pykeepassgate import api, errors

#: Where the repository is, if these tests are in one, as `test_reference.py` finds it.
REPOSITORY = Path(__file__).resolve().parents[2]

#: The two pages a caller reads to learn what to catch: the documentation and the package's own.
TABLES = {
    "docs": REPOSITORY / "docs" / "pykeepassgate.md",
    "README": REPOSITORY / "src" / "pykeepassgate" / "README.md",
}

#: What the client raises from the standard library, each for a mistake in the calling program
#: rather than anything the gate said.
STANDARD = {"ValueError", "AttributeError", "KeyError"}


def test_everything_this_library_raises_has_one_root() -> None:
    # The reason a caller can wrap a whole session in `except GateError` and be sure it caught this
    # library rather than the standard library as well.
    raised = [
        value
        for _, value in inspect.getmembers(errors, inspect.isclass)
        if issubclass(value, Exception) and value.__module__ == errors.__name__
    ]

    assert len(raised) > 1
    for kind in raised:
        assert issubclass(kind, errors.GateError)


def test_everything_this_library_raises_is_exported_from_api() -> None:
    # `api` is the one name a program imports, and catching a failure should not mean reaching into
    # `errors` for it. Every class, rather than a list written here, so the next one added is
    # checked without anybody remembering to add it.
    raised = [
        name
        for name, value in inspect.getmembers(errors, inspect.isclass)
        if issubclass(value, Exception) and value.__module__ == errors.__name__
    ]

    assert "StorageError" in raised
    for name in raised:
        assert name in api.__all__
        assert getattr(api, name) is getattr(errors, name)


def test_everything_this_library_raises_is_exported_from_the_package_as_well() -> None:
    # The package root hands out `pair` and `GateClient`, so a program importing nothing else
    # has to be able to catch whatever those two raise without going looking for it.
    raised = [
        name
        for name, value in inspect.getmembers(errors, inspect.isclass)
        if issubclass(value, Exception) and value.__module__ == errors.__name__
    ]

    for name in raised:
        assert name in pykeepassgate.__all__
        assert getattr(pykeepassgate, name) is getattr(errors, name)


@pytest.mark.parametrize("page", list(TABLES.values()), ids=list(TABLES))
def test_each_table_of_what_to_catch_names_everything_this_client_raises(page: Path) -> None:
    # A row for every class `errors.py` defines but the root, which the text above each table
    # names instead, and one for each of the standard library's that a caller can meet. A class
    # added without a row fails here, and so does a row left behind by a class taken away.
    if not page.exists():  # pragma: no cover, only outside the repository
        pytest.skip(f"no page at {page} to hold to this module")

    section = page.read_text("utf-8").split("## What to catch", 1)[1].split("\n## ", 1)[0]
    rows = set(re.findall(r"^\| `(\w+)` \|", section, flags=re.MULTILINE))
    raised = {
        name
        for name, value in inspect.getmembers(errors, inspect.isclass)
        if issubclass(value, Exception) and value.__module__ == errors.__name__
    }

    assert rows == (raised - {"GateError"}) | STANDARD


def test_the_pairing_failures_are_one_family() -> None:
    # So a caller that only wants to know whether pairing worked can catch one thing, and one that
    # wants to know whether to try again can catch the two that say.
    for kind in (errors.ProofRefused, errors.PairingRefused, errors.PairingUnanswered):
        assert issubclass(kind, errors.PairingError)


def test_a_refusal_keeps_the_status_and_the_word_it_came_with() -> None:
    refused = errors.RouteRefused(404, "no such route")

    assert refused.status == 404
    assert refused.error == "no such route"
    assert "404" in str(refused)


def test_a_refusal_with_no_word_ends_at_its_status() -> None:
    # An answer with no body names no word, and a message ending in a colon reads as though
    # something that was there got cut off. The word, when there is one, still follows it.
    refused = errors.RouteRefused(413, "")

    assert str(refused) == "the gate answered 413"
    assert refused.error == ""
    assert str(errors.RouteRefused(404, "no such route")) == "the gate answered 404: no such route"


def test_nothing_is_told_apart_only_by_its_message() -> None:
    # Two failures a caller has to act on differently are two classes. This is the check that stops
    # a later one being added as a string in an existing message instead: an unanswered dialog does
    # not spend the invitation and a refused one does, so catching both together loses the answer.
    assert not issubclass(errors.PairingUnanswered, errors.PairingRefused)
    assert not issubclass(errors.PairingRefused, errors.PairingUnanswered)


def test_what_a_caller_does_next_is_one_class_each_under_the_one_root() -> None:
    # Each of these is its own row in the tables a caller acts on, and each says something
    # different to do next. One moved under another is caught by the wrong handler: a revoked
    # client under `TransportError` would be retried by code that retries a connection that
    # failed, and a refused route under `GateUnavailable` would be waited out as a `503`.
    separate = (
        errors.GateUnavailable,
        errors.NotAdmitted,
        errors.Withheld,
        errors.RouteRefused,
        errors.PairingError,
        errors.MalformedToken,
        errors.ProtocolError,
        errors.StorageError,
        errors.TransportError,
    )

    for kind in separate:
        assert kind.__bases__ == (errors.GateError,), f"{kind.__name__} is under something else"


def test_every_503_is_one_family_with_one_class_per_word() -> None:
    # A caller that waits out any `503` catches the base; one that waits out a lock and not a
    # busy KeePass catches the one it means. None of the three sits under another, or a lock
    # caught as `GateBusy` would spend the budget for a busy KeePass.
    for kind, word in (
        (errors.GateBusy, "busy"),
        (errors.DatabaseLocked, "locked"),
        (errors.AuthorityUndecided, "undecided"),
    ):
        assert kind.__bases__ == (errors.GateUnavailable,), f"{kind.__name__} is elsewhere"
        assert kind.word == word

        refused = kind("not now", 2.0)
        assert (refused.error, refused.retry_after) == (word, 2.0)


def test_nothing_paired_is_a_storage_error_and_only_that() -> None:
    # The command line used to catch the standard library's `FileNotFoundError` by name for it.
    # Under `StorageError` it is caught with the rest, and by nothing outside this tree.
    assert errors.NotPaired.__bases__ == (errors.StorageError,)
    assert not issubclass(errors.NotPaired, OSError)


def test_a_withheld_value_is_not_an_attribute_error() -> None:
    # `getattr(entry, "password", "")` and `hasattr` catch `AttributeError`, so a withheld secret
    # raised as one would become a default without a word. That is kept for what was never read.
    assert not issubclass(errors.Withheld, AttributeError)


def test_a_withheld_value_names_the_field_and_what_would_open_it() -> None:
    refused = errors.Withheld("Password")

    assert refused.name == "Password"
    assert "'Password'" in str(refused)
    assert "the rule that lets this client read the entry does not grant `P`" in str(refused)
    assert "operator can add" in str(refused)
    assert "list the entry" in str(errors.Withheld("Title", listed=True))


def test_a_503_with_a_word_nobody_knows_keeps_it() -> None:
    refused = errors.GateUnavailable("not now", "teapot", 2.0)

    assert (refused.error, refused.retry_after) == ("teapot", 2.0)
    assert errors.GateUnavailable("not now").error == ""
