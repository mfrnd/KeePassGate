"""A paired client, and every route the gate serves it.

**This is the reference implementation, so what is here is what exists.** Every operation the
routing table declares has a method, and the absence of a method reads as the absence of a route
rather than as something nobody got round to. `tests/pykeepassgate/test_reference.py` holds this to
the document itself, so a route added to the plugin and not to this fails there.

Every request is its own connection, named freshly from the gate key. That is not a cost worth
optimizing away: it is what makes a passive observer see a different name each time.

**`root` is a group identifier.** Every method taking a group takes it, so a client granted only
`create` can put something somewhere without first being granted `list` to find out where.
"""

from __future__ import annotations

import base64
import json
from dataclasses import dataclass
from pathlib import Path
from typing import Any
from urllib.parse import quote

from . import credentials as credentials_module
from . import sni, transport
from .errors import DatabaseLocked, GateBusy, NotAdmitted, ProtocolError, RouteRefused
from .model import Attachment, Entry, Field, Group

STATUS_PATH = "/gate/status"

ROOT = "root"
"""What every group identifier may say instead of a UUID."""


def _segment(text: str) -> str:
    """One path segment, escaped.

    Nothing is left unescaped, the separator least of all: the gate splits a path before it decodes
    it, so an attachment called `a/b` is addressable exactly because its slash arrives escaped.
    """
    return quote(text, safe="")


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

        It reads nothing out of the database on purpose, which is why it is one of the three
        routes declaring `x-kpg-verb: none`, the other two being the pairing ones: no grant
        could allow or refuse it, because it reaches nothing a grant is about. It exists to
        prove a connection rather than to be useful.

        A client can use it to answer three questions at once: whether the gate is listening,
        whether this certificate is still admitted, and whether the database is unlocked.
        """
        body = self.get(STATUS_PATH)
        return Status(
            ok=bool(body.get("ok")),
            database=str(body.get("database", "")),
            version=str(body.get("version", "")),
        )

    # Groups

    def groups(self) -> list[Group]:
        """Every group in the database, root first.

        Anchored at the root, so it needs `list` there. A client granted less asks the group it was
        granted, with `subgroups`.
        """
        return [Group.of(item) for item in self.get("/groups").get("groups", [])]

    def group(self, uuid: str = ROOT) -> Group:
        """One group's own settings, which needs `read` rather than `list`."""
        return Group.of(self.get(f"/groups/{_segment(uuid)}"))

    def subgroups(self, uuid: str = ROOT) -> list[Group]:
        """The groups directly inside this one."""
        body = self.get(f"/groups/{_segment(uuid)}/groups")
        return [Group.of(item) for item in body.get("groups", [])]

    def create_group(self, name: str, parent: str = ROOT, notes: str | None = None) -> str:
        """Put a new group inside this one, and answer with what addresses it."""
        body: dict[str, Any] = {"Name": name}
        if notes is not None:
            body["Notes"] = notes

        return str(self.send("POST", f"/groups/{_segment(parent)}/groups", body)["uuid"])

    def update_group(self, uuid: str, name: str | None = None, notes: str | None = None) -> None:
        """Rename a group, or change its notes. Only what is named is written."""
        body: dict[str, Any] = {}
        if name is not None:
            body["Name"] = name
        if notes is not None:
            body["Notes"] = notes

        self.send("PUT", f"/groups/{_segment(uuid)}", body)

    def delete_group(self, uuid: str) -> None:
        """Remove a group, which the gate refuses while it still holds anything."""
        self.send("DELETE", f"/groups/{_segment(uuid)}")

    # Entries

    def entries(self, group: str | None = None) -> list[Entry]:
        """Every entry, or the ones directly inside a group.

        With no group this is the flat sweep, anchored at the root and recursive. With one it is
        that group's own entries, one level: a different question rather than the same one narrowed.
        """
        path = "/entries" if group is None else f"/groups/{_segment(group)}/entries"
        return [Entry.listed(item) for item in self.get(path).get("entries", [])]

    def entry(self, uuid: str) -> Entry:
        """One entry and every string it holds.

        The protected ones arrive only when this client may read them.
        """
        return Entry.read(self.get(f"/entries/{_segment(uuid)}"))

    def create_entry(
        self,
        group: str = ROOT,
        title: str | None = None,
        username: str | None = None,
        password: str | None = None,
        url: str | None = None,
        notes: str | None = None,
    ) -> str:
        """Put a new entry in a group, and answer with what addresses it.

        Only the five standard strings, because adding anything else is a different verb: use
        `create_field` afterwards, which is what `create` and `update` being separate means.
        """
        body = self._standard(title, username, password, url, notes)
        return str(self.send("POST", f"/groups/{_segment(group)}/entries", body)["uuid"])

    def update_entry(
        self,
        uuid: str,
        title: str | None = None,
        username: str | None = None,
        password: str | None = None,
        url: str | None = None,
        notes: str | None = None,
    ) -> None:
        """Change the standard fields of an entry. Only what is named is written."""
        body = self._standard(title, username, password, url, notes)
        self.send("PUT", f"/entries/{_segment(uuid)}", body)

    def delete_entry(self, uuid: str) -> None:
        """Remove an entry, to the recycle bin when the database keeps one."""
        self.send("DELETE", f"/entries/{_segment(uuid)}")

    # Fields

    def fields(self, uuid: str) -> list[Field]:
        """The names of an entry's strings, never their values."""
        body = self.get(f"/entries/{_segment(uuid)}/fields")
        return [
            Field(name=str(item["name"]), protected=bool(item["protected"]))
            for item in body.get("fields", [])
        ]

    def field(self, uuid: str, name: str) -> Field:
        """One string's value."""
        body = self.get(f"/entries/{_segment(uuid)}/fields/{_segment(name)}")
        # `None` survives as `None`, for the reason `Entry.read` gives: the gate answers a
        # protected value this client may not read with `null`, and `str` would make that the word
        # "None". `Field.is_read` is what asks, and it can only answer while the `None` is intact.
        raw = body.get("value")

        return Field(
            name=str(body.get("name", name)),
            protected=bool(body.get("protected")),
            value=None if raw is None else str(raw),
        )

    def create_field(self, uuid: str, name: str, value: str, protected: bool = False) -> None:
        """Add a string this entry does not have. Refused when the name is already taken.

        `protected=True` stores it protected whatever the name. Leaving it out does not mean
        unprotected: the marker is not sent, and the database protects the value if it protects
        that name, as it does `Password`.
        """
        body: dict[str, Any] = {"name": name, "value": value}
        if protected:
            body["protected"] = "true"

        self.send("POST", f"/entries/{_segment(uuid)}/fields", body)

    def update_field(self, uuid: str, name: str, value: str) -> None:
        """Change a string this entry already has. Refused when there is no such name.

        What it does not change is whether the field is protected: the operator arranged that, and
        writing a value is not the moment to decide it differently.
        """
        self.send("PUT", f"/entries/{_segment(uuid)}/fields/{_segment(name)}", {"value": value})

    def strip_field(self, uuid: str, name: str) -> None:
        """Take a string off an entry.

        A strip rather than a delete: it removes a part of the entry and leaves the entry, so it
        needs `strip` on the entry and says nothing about whether the entry itself may be removed.
        """
        self.send("DELETE", f"/entries/{_segment(uuid)}/fields/{_segment(name)}")

    # Attachments

    def attachments(self, uuid: str) -> list[Attachment]:
        """The names and sizes of an entry's attachments, never their contents.

        Needs the attachment switch as well as `list`, which is what the seventh letter of a grant
        means: not "may read attachments" but "and the attachments too".
        """
        body = self.get(f"/entries/{_segment(uuid)}/attachments")
        return [
            Attachment(name=str(item["name"]), size=int(item["size"]))
            for item in body.get("attachments", [])
        ]

    def attachment(self, uuid: str, name: str) -> Attachment:
        """One attachment, contents and all."""
        body = self.get(f"/entries/{_segment(uuid)}/attachments/{_segment(name)}")
        return Attachment(
            name=str(body.get("name", name)),
            size=int(body.get("size", 0)),
            content=_bytes(body.get("content")),
        )

    def create_attachment(self, uuid: str, name: str, content: bytes) -> None:
        """Add an attachment this entry does not have."""
        self.send(
            "POST",
            f"/entries/{_segment(uuid)}/attachments",
            {"name": name, "content": base64.b64encode(content).decode("ascii")},
        )

    def update_attachment(self, uuid: str, name: str, content: bytes) -> None:
        """Replace an attachment this entry already has."""
        self.send(
            "PUT",
            f"/entries/{_segment(uuid)}/attachments/{_segment(name)}",
            {"content": base64.b64encode(content).decode("ascii")},
        )

    def strip_attachment(self, uuid: str, name: str) -> None:
        """Take an attachment off an entry.

        Needs `strip`, and not the attachment switch: removing shows nobody anything.
        """
        self.send("DELETE", f"/entries/{_segment(uuid)}/attachments/{_segment(name)}")

    @staticmethod
    def _standard(
        title: str | None,
        username: str | None,
        password: str | None,
        url: str | None,
        notes: str | None,
    ) -> dict[str, Any]:
        """The five strings, by KeePass's own names, and only the ones that were given."""
        named = {
            "Title": title,
            "UserName": username,
            "Password": password,
            "URL": url,
            "Notes": notes,
        }

        return {name: value for name, value in named.items() if value is not None}

    def head(self, path: str) -> None:
        """Whether a route answers, and whether this client may reach it, without the answer.

        One method for every `head` the document declares, because a `head` is generic in a way
        a `get` is not: it carries no content, so there is nothing to shape per route. What it
        reports is the status, which is the whole of what was asked.

        It hands back nothing: a refusal is an exception here as it is everywhere else in this
        client, so reaching the next line is the answer. What a refusal cannot carry is the
        gate's word, since that is written in a body and this answer has none: a `RouteRefused`
        from here holds the status and an empty `error`. The gate omits `Content-Length` on
        these, which RFC 9110 section 9.3.2 permits for a value determined only while
        generating content, so this one reads to the close the gate sends anyway.
        """
        self.send("HEAD", path)

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


def _bytes(value: object) -> bytes:
    """An attachment's content, or a refusal to guess at one.

    `str` around this would turn a `null` into the word "None", which is valid base64 and
    decodes to three bytes of something. Wrong data is worse than an error, so this is an error.
    """
    if not isinstance(value, str):
        raise ProtocolError("the gate sent an attachment with no content")

    return base64.b64decode(value)


def _refuse(reply: transport.Response) -> None:
    """What each refusal means to a caller deciding what to do next."""
    # Both of the document's successes, not just the common one. Every create answers `201`,
    # and a client that knows only `200` turns all four of them into refusals.
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
