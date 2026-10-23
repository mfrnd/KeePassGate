"""What the condition tests share: invented identifiers and tags, and a gate that hands tags out.

The library's tests and the command line's ask the same of the stand-in gate, and a copy in each
of them would be two answers to how the plugin tags, which can only come to disagree.
"""

from __future__ import annotations

import json
from typing import Any

from pykeepassgate.testing import FakeGate

UUID = "7b1c" * 8
TAG = '"3f9c0e7a1b2d4c5e6f708192a3b4c5d6"'
NEXT = '"0a1b2c3d4e5f60718293a4b5c6d7e8f9"'

#: The methods the plugin answers with the tag as `ETag`, rather than in the body as `etag`.
READS = ("GET", "HEAD")


def tagging(
    gate: FakeGate, body: dict[str, Any], etag: str | None, status: int = 200
) -> list[str | None]:
    """Answer every request with this, carrying `etag` where the plugin carries one.

    A read's tag in the `ETag` header, and a write's that worked in its body, as `etag`, which is
    how the plugin hands each out. `None` is a gate that hands none, which is what a client whose
    rule does not read everything a tag covers meets. What comes back fills with the `If-Match`
    of every request answered, in order.
    """
    conditions: list[str | None] = []

    def answer(path: str, sent: str, client: bytes | None) -> tuple[int, str]:
        conditions.append(gate.if_match())
        answered = dict(body)
        if etag is not None and gate.method() in READS:
            gate.tag(etag)
        elif etag is not None and status in (200, 201):
            answered["etag"] = etag

        return status, json.dumps(answered)

    gate.handler = answer
    return conditions
