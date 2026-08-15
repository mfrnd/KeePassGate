"""The secret an operator hands to an invitee, and how it is written down.

Thirty-two bytes, because the same value is used as a gate key and a gate key is thirty-two bytes.
It is written as base64url without padding, so it survives being pasted into a URL, a shell, a
configuration file or a chat window without anything quoting it. That alphabet is RFC 4648
section 5, and the padding it omits is section 3.2. https://www.rfc-editor.org/rfc/rfc4648#section-5

The plugin only ever encodes and this only ever decodes, so `encode` exists here to be tested
against `decode` rather than because a client needs it.
"""

from __future__ import annotations

import base64

from .errors import MalformedToken

#: The same length as a gate key, because a token is used as one.
LENGTH = 32

#: Thirty-two bytes in base64url is forty-three characters, with no padding.
TEXT_LENGTH = 43

_ALPHABET = frozenset("ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789-_")


def encode(token: bytes) -> str:
    """Write a token the way the tab writes it."""
    if len(token) != LENGTH:
        raise MalformedToken(f"a token is {LENGTH} bytes, not {len(token)}")

    return base64.urlsafe_b64encode(token).decode("ascii").rstrip("=")


def decode(text: str) -> bytes:
    """Read one back.

    Refuses anything that is not exactly a token: the wrong length, a character outside the
    alphabet, or padding, which the plugin never writes. Fail closed, in the same way and for the
    same reason the plugin's own decoder does.
    """
    if len(text) != TEXT_LENGTH:
        raise MalformedToken(
            f"a token is {TEXT_LENGTH} characters, not {len(text)}"
        )

    if not _ALPHABET.issuperset(text):
        raise MalformedToken("a token is base64url, and this is not")

    # The padding the encoder omits, put back so the standard library will read it. 43 characters
    # is 32 bytes and one short of a whole block, so one "=" is what is missing; a second is
    # harmless and saves counting.
    token = base64.urlsafe_b64decode(text + "==")

    # 43 characters carry 258 bits for a 256 bit token, so the last one has two bits the token
    # does not use and the decoder does not look at: four spellings decode to the same bytes.
    # Re-encoding refuses the three the plugin would never write. There is no length check after
    # this, because 43 characters of the alphabet are 32 bytes and can be nothing else.
    if encode(token) != text:
        raise MalformedToken("a token has one spelling, and this is not it")

    return token
