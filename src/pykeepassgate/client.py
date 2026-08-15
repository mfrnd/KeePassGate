"""A paired client, and every route the gate serves it.

**This is the reference implementation, so what is here is what exists.** The gate serves one route
today besides pairing, and this covers it. When the specification router lands and the document
becomes the routing table, the routes it declares belong here, one method each, and the absence of
a method here should read as the absence of a route rather than as something nobody got round to.

Every request is its own connection, named freshly from the gate key. That is not a cost worth
optimizing away: it is what makes a passive observer see a different name each time.
"""

from __future__ import annotations

import json
from dataclasses import dataclass
from pathlib import Path
from typing import Any

from . import credentials as credentials_module
from . import sni, transport
from .errors import DatabaseLocked, GateBusy, NotAdmitted, RouteRefused

STATUS_PATH = "/gate/status"


@dataclass(frozen=True)
class Status:
    """What the gate says about itself and the database this client reached."""

    ok: bool
    database: str
    version: str


class GateClient:
    """One paired identity, talking to one gate."""

    def __init__(
        self,
        credentials: credentials_module.Credentials,
        timeout: float = transport.DEFAULT_TIMEOUT,
    ) -> None:
        """Take a paired identity and the port it was paired against."""
        self._credentials = credentials
        self._timeout = timeout
        self._context = transport.paired_context(credentials.authority, credentials.identity)

        # Read once, beside the context, because the two are one identity. `gate_key` is a
        # property that reads its file on every access, so leaving it to `send` would let a
        # re-pair under a live client send names built from the new key while presenting the
        # old certificate and pinning the old anchor. A client is one identity, and a
        # re-pair is a new client.
        self._gate_key = credentials.gate_key

    @classmethod
    def open(
        cls,
        directory: Path | None = None,
        profile: str = "default",
        timeout: float = transport.DEFAULT_TIMEOUT,
    ) -> GateClient:
        """Load a paired identity from where `pair` put it.

        The timeout is here as well as on the constructor because this is the way a caller
        is meant to build one, and a setting nothing can reach is a setting nobody has.
        """
        where = directory or credentials_module.default_directory(profile)
        return cls(credentials_module.load(where), timeout=timeout)

    @property
    def credentials(self) -> credentials_module.Credentials:
        """What this client is, which is worth showing a person before it does anything."""
        return self._credentials

    def status(self) -> Status:
        """The route that proves a connection and nothing else.

        It reads nothing out of the database on purpose, which is why it will be one of the
        three routes declaring `x-kpg-verb: none`, the other two being the pairing ones: no
        grant could allow or refuse it, because it reaches nothing a grant is about. It
        exists to prove a connection rather than to be useful.
        TODO(step 12): the routing document declares that extension at step 11, and every
        route carries one from step 12, which is when the count of three becomes true.

        A client can use it to answer three questions at once: whether the gate is listening,
        whether this certificate is still admitted, and whether the database is unlocked.
        """
        body = self.get(STATUS_PATH)
        return Status(
            ok=bool(body.get("ok")),
            database=str(body.get("database", "")),
            version=str(body.get("version", "")),
        )

    def get(self, path: str) -> dict[str, Any]:
        """One GET, on its own connection, under a name built for it."""
        return self.send("GET", path)

    def send(self, method: str, path: str, body: dict[str, Any] | None = None) -> dict[str, Any]:
        """One request, on its own connection.

        Public because the gate is a specification-defined API and a client that could only call
        the methods this class happens to name would be a worse reference than one that can reach
        anything the document declares.
        """
        name, _ = sni.new_name(self._gate_key)
        payload = "" if body is None else json.dumps(body, separators=(",", ":"))

        with transport.connect(
            self._credentials.port, self._context, name, timeout=self._timeout
        ) as connection:
            reply = connection.send(method, path, payload)

        _refuse(reply)

        if not reply.body:
            return {}

        return reply.json()


def _refuse(reply: transport.Response) -> None:
    """What each refusal means to a caller deciding what to do next."""
    # Both of the document's successes, not just the common one.
    # TODO(step 15): the creates arrive with the database routes, every one answering `201`,
    # and a client that knew only `200` would turn all four of them into refusals.
    if reply.status in (200, 201):
        return

    error = reply.error()

    if reply.status == 503 and error == "locked":
        raise DatabaseLocked("the database is locked, so every route answers this until it is not")

    if reply.status == 503:
        raise GateBusy("the gate cannot serve this right now")

    if reply.status == 401:
        # The record is read from the live database on every connection and never cached, so this
        # is also what revocation looks like: immediate, and from the client's side no different
        # from never having been admitted, which is the point.
        raise NotAdmitted("this database holds no admission record for this certificate")

    raise RouteRefused(reply.status, error)
