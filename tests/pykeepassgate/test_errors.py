"""The promise `errors.py` makes: one root, and a name that says what to do next."""

from __future__ import annotations

import inspect

from pykeepassgate import errors


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
