"""Which gates this client works with, by the version each gate reports.

**The version is the version of the API's contract**, which is what makes it something a client
can compare: a change a client may rely on moves the minor, a change that breaks an existing client
moves the major, and a patch changes no contract. So a client works with a gate of its own major
version whose major and minor it does not lead, and the patch, released on each side on its own, is
never compared. `API.md` §411 holds the rule; this is the client's half of it.
"""

from __future__ import annotations

import re
from importlib.metadata import version

from .errors import IncompatibleGate

#: The version this client was installed as, which the plugin and the routing document carry too.
#: It is written in one place, `Directory.Build.props`, and an installed package keeps it in its
#: metadata, so it is read from there rather than written a second time here.
CLIENT_VERSION = version("pykeepassgate")

#: A version as `Directory.Build.props` holds it: three whole numbers, and nothing else.
#: ASCII digits only, since `\d` would take a digit of any script and `int` would read it.
_RELEASE = re.compile(r"([0-9]+)\.([0-9]+)\.([0-9]+)")


def refuse_incompatible(gate: str) -> None:
    """Raise `IncompatibleGate` unless this client works with a gate reporting `gate`.

    A version that is not three whole numbers is refused as well, since nothing about it can be
    compared: the plugin reports `unknown` when its assembly gives none, and a gate that cannot
    name the contract it serves is not a gate this client can know it works with. The empty string
    is a gate that reported none.
    """
    ours = _release(CLIENT_VERSION)
    theirs = _release(gate)

    if not gate:
        reason = "and every gate this client works with reports a version"
    elif theirs is None:
        reason = "which is not a version this client can compare"
    elif ours is None:
        reason = "and this client's own version is not a version it can compare"
    elif ours[0] != theirs[0]:
        reason = (
            "and a client works only with a gate of its own major version, so whichever is "
            "older has to be updated"
        )
    elif ours[1] > theirs[1]:
        reason = (
            "and a client works only with a gate at its own minor version or later, so the "
            f"plugin has to be updated, or pykeepassgate {theirs[0]}.{theirs[1]} installed"
        )
    else:
        return

    # Quoted only when it does not read, so that nothing it holds passes for the message's own.
    if not gate:
        shown = "no version"
    elif theirs is None:
        shown = f"KeePassGate {gate!r}"
    else:
        shown = f"KeePassGate {gate}"

    raise IncompatibleGate(
        f"this client is pykeepassgate {CLIENT_VERSION} and the gate reports {shown}, {reason}",
        CLIENT_VERSION,
        gate,
    )


def _release(text: str) -> tuple[int, int] | None:
    """The major and minor version of a release, or `None` for text that is not a release."""
    found = _RELEASE.fullmatch(text)
    if found is None:
        return None

    try:
        return int(found.group(1)), int(found.group(2))
    except ValueError:
        # More digits than `int` reads from text, which is no release, and was a `ValueError` out
        # of the status route and the pairing hello rather than the refusal of a version.
        return None
