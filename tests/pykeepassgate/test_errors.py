"""The promise `errors.py` makes: one root, and a name that says what to do next."""

from __future__ import annotations

import inspect

import pykeepassgate
from pykeepassgate import api, errors


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
    # failed, and a lock under `GateBusy` would spend the budget for a busy KeePass.
    separate = (
        errors.DatabaseLocked,
        errors.GateBusy,
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
