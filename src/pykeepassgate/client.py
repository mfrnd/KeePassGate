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
from datetime import UTC, datetime
from pathlib import Path
from typing import Any, Literal
from urllib.parse import quote

from . import credentials as credentials_module
from . import sni, transport
from .errors import DatabaseLocked, GateBusy, NotAdmitted, ProtocolError, RouteRefused
from .model import EXPIRES, Attachment, Entry, Field, Group, made

STATUS_PATH = "/gate/status"

ROOT = "root"
"""What every group identifier may say instead of a UUID."""

INCLUDE_EXPIRED = "includeExpired"
"""The query parameter that leaves expired entries out of a listing when it says `false`."""

AUTO = "auto"
"""What a create's expiry says to ask for the one KeePass's options give every new entry."""


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
        connection_timeout: float = transport.DEFAULT_CONNECTION_TIMEOUT,
        host: str = transport.LOOPBACK,
        retry_busy_timeout: float | None = transport.DEFAULT_RETRY_BUSY_TIMEOUT,
        retry_locked_timeout: float | None = transport.DEFAULT_RETRY_LOCKED_TIMEOUT,
        max_response_bytes: int = transport.MAX_RESPONSE_BYTES,
    ) -> None:
        """Take a paired identity, how long to wait on its socket, and where to dial it.

        The two retry budgets are how long this client will go on waiting out a `503`, one per
        kind, and both are `None` here: nothing retries unless a caller says so, and the refusal
        is raised exactly as it was before there was anything to say. They are separate because a
        busy KeePass is in the middle of something and a locked one is waiting for a person.
        Either can be overridden on one call, which `send` takes.

        `max_response_bytes` is how much of one answer this client will read, and an answer over
        it fails whole with `ProtocolError` naming the setting, rather than arriving cut short.
        The plugin does not page, so a listing grows with the database, and a database large
        enough to outgrow the default is one whose caller raises this.

        **A setting that could not work is refused here**, with a `ValueError` naming it, before
        anything is dialed, and the command line refuses the same ones: a cap that is not a
        positive whole number of bytes, a socket timeout that is not a finite number of seconds
        above zero, a budget that is neither `None` nor a finite number of seconds from zero, and
        a port in the credentials that TCP does not have.

        The host is where the socket goes and nothing else. It is not part of the identity and is
        not stored with it: the same paired client reaches the same KeePass through a tunnel or
        directly, and which of those is happening is a property of the caller's machine rather than
        of the pairing. So it is given here rather than read back from the credentials, and it
        defaults to loopback, which is where the gate itself binds.

        It does not reach the name in the ClientHello either. That is minted per connection from
        the gate's key and is what the gate resolves this client by; a tunnel changes the address
        the bytes travel to and changes nothing the gate sees.
        """
        transport.port_number(credentials.port)
        self._credentials = credentials
        self._connection_timeout = transport.connection_limit(connection_timeout)
        self._host = host
        self._retry_busy_timeout = transport.retry_limit("retry_busy_timeout", retry_busy_timeout)
        self._retry_locked_timeout = transport.retry_limit(
            "retry_locked_timeout", retry_locked_timeout
        )
        self._max_response_bytes = transport.response_limit(max_response_bytes)
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
        host: str = transport.LOOPBACK,
        connection_timeout: float = transport.DEFAULT_CONNECTION_TIMEOUT,
        retry_busy_timeout: float | None = transport.DEFAULT_RETRY_BUSY_TIMEOUT,
        retry_locked_timeout: float | None = transport.DEFAULT_RETRY_LOCKED_TIMEOUT,
        max_response_bytes: int = transport.MAX_RESPONSE_BYTES,
    ) -> GateClient:
        """Load a paired identity from where `pair` put it.

        Everything that is not the identity is given here rather than read back with it, and for
        the same reason: where to dial, how long to wait on the socket and how long to wait out a
        refusal are the caller's, not the pairing's. This is the way a caller is meant to build
        one, so a setting it cannot reach is a setting nobody has.
        """
        where = directory or credentials_module.default_directory(profile)
        return cls(
            credentials_module.load(where),
            connection_timeout=connection_timeout,
            host=host,
            retry_busy_timeout=retry_busy_timeout,
            retry_locked_timeout=retry_locked_timeout,
            max_response_bytes=max_response_bytes,
        )

    @property
    def host(self) -> str:
        """Where this client dials, which is loopback unless something is tunneling."""
        return self._host

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

        The three members are read as the document declares them, as every other answer's are:
        `bool` would read the text "false" as true, and `str` a `null` as the word "None".
        """
        body = self.get(STATUS_PATH)
        ok, database, version = body.get("ok"), body.get("database"), body.get("version")
        if not isinstance(ok, bool) or not (isinstance(database, str) and isinstance(version, str)):
            raise ProtocolError("the gate answered its status in a form this client cannot read")

        return Status(ok=ok, database=database, version=version)

    # Groups

    def groups(self) -> list[Group]:
        """Every group in the database, root first.

        Anchored at the root, so it needs `list` there. A client granted less asks the group it was
        granted, with `subgroups`.
        """
        return Group.listing(self.get("/groups"))

    def group(self, uuid: str = ROOT) -> Group:
        """One group's own settings, which needs `read` rather than `list`."""
        return Group.of(self.get(f"/groups/{_segment(uuid)}"))

    def subgroups(self, uuid: str = ROOT) -> list[Group]:
        """The groups directly inside this one."""
        return Group.listing(self.get(f"/groups/{_segment(uuid)}/groups"))

    def create_group(self, name: str, parent: str = ROOT, notes: str | None = None) -> str:
        """Put a new group inside this one, and answer with what addresses it."""
        body: dict[str, Any] = {"Name": name}
        if notes is not None:
            body["Notes"] = notes

        return made(self.send("POST", f"/groups/{_segment(parent)}/groups", body))

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

    def entries(self, group: str | None = None, include_expired: bool = True) -> list[Entry]:
        """Every entry, or the ones directly inside a group.

        With no group this is the flat sweep, anchored at the root and recursive. With one it is
        that group's own entries, one level: a different question rather than the same one narrowed.

        `include_expired=False` leaves out every entry that has expired, judged by the gate on its
        own clock as KeePass judges one: it expires, at a moment that is now or earlier. The
        default keeps them, as KeePass's own list does, and asks nothing of the query.
        """
        path = "/entries" if group is None else f"/groups/{_segment(group)}/entries"
        if not include_expired:
            path = f"{path}?{INCLUDE_EXPIRED}=false"

        return Entry.listing(self.get(path))

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
        expires: datetime | Literal["auto", False] | None = None,
    ) -> str:
        """Put a new entry in a group, and answer with what addresses it.

        Only the five standard strings, because adding anything else is a different verb: use
        `create_field` afterwards, which is what `create` and `update` being separate means.

        `expires` is when the entry expires: a moment with a time zone; `"auto"`, for whatever
        KeePass's options give every new entry, which is none unless the operator switched that
        on; or `False`, or leaving it out, for an entry that does not expire. Nothing is assumed:
        the options apply only when asked for.
        """
        body = self._standard(title, username, password, url, notes)
        if expires is not None:
            body[EXPIRES] = _expiry(expires, creating=True)

        return made(self.send("POST", f"/groups/{_segment(group)}/entries", body))

    def update_entry(
        self,
        uuid: str,
        title: str | None = None,
        username: str | None = None,
        password: str | None = None,
        url: str | None = None,
        notes: str | None = None,
        expires: datetime | Literal[False] | None = None,
    ) -> None:
        """Change the standard fields of an entry. Only what is named is written.

        `expires` moves the entry's expiry to a moment, and `False` takes it off, which is how a
        program that rotates an expired password leaves it unmarked. Left out, the expiry stays
        as it is. `"auto"` is a new entry's alone and is refused here with `ValueError`.
        """
        body = self._standard(title, username, password, url, notes)
        if expires is not None:
            body[EXPIRES] = _expiry(expires, creating=False)

        self.send("PUT", f"/entries/{_segment(uuid)}", body)

    def delete_entry(self, uuid: str) -> None:
        """Remove an entry, to the recycle bin when the database keeps one."""
        self.send("DELETE", f"/entries/{_segment(uuid)}")

    # Fields

    def fields(self, uuid: str) -> list[Field]:
        """The names of an entry's strings, never their values."""
        return Field.listing(self.get(f"/entries/{_segment(uuid)}/fields"))

    def field(self, uuid: str, name: str) -> Field:
        """One string's value."""
        return Field.read(self.get(f"/entries/{_segment(uuid)}/fields/{_segment(name)}"))

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
        The five standard fields every entry has are refused, `RouteRefused` with `409`: empty one
        with `update_field` instead.
        """
        self.send("DELETE", f"/entries/{_segment(uuid)}/fields/{_segment(name)}")

    # Attachments

    def attachments(self, uuid: str) -> list[Attachment]:
        """The names and sizes of an entry's attachments, never their contents.

        Needs the attachment switch as well as `list`, which is what the seventh letter of a grant
        means: not "may read attachments" but "and the attachments too".
        """
        return Attachment.listing(self.get(f"/entries/{_segment(uuid)}/attachments"))

    def attachment(self, uuid: str, name: str) -> Attachment:
        """One attachment, contents and all."""
        return Attachment.read(self.get(f"/entries/{_segment(uuid)}/attachments/{_segment(name)}"))

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

    def head(
        self,
        path: str,
        *,
        retry_busy_timeout: transport.Given = transport.KEEP,
        retry_locked_timeout: transport.Given = transport.KEEP,
    ) -> None:
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
        self.send(
            "HEAD",
            path,
            retry_busy_timeout=retry_busy_timeout,
            retry_locked_timeout=retry_locked_timeout,
        )

    def get(
        self,
        path: str,
        *,
        retry_busy_timeout: transport.Given = transport.KEEP,
        retry_locked_timeout: transport.Given = transport.KEEP,
    ) -> dict[str, Any]:
        """One GET, on its own connection, under a name built for it."""
        return self.send(
            "GET",
            path,
            retry_busy_timeout=retry_busy_timeout,
            retry_locked_timeout=retry_locked_timeout,
        )

    def send(
        self,
        method: str,
        path: str,
        body: dict[str, Any] | None = None,
        *,
        retry_busy_timeout: transport.Given = transport.KEEP,
        retry_locked_timeout: transport.Given = transport.KEEP,
    ) -> dict[str, Any]:
        """One request, on its own connection, retried for as long as a budget allows.

        Public because the gate is a specification-defined API and a client that could only call
        the methods this class happens to name would be a worse reference than one that can reach
        anything the document declares.

        **This is also where a retry budget is overridden for one call**, and the reason it is
        here rather than on each of the twenty-two methods above is that a call to the gate is a
        request: every one of those methods is one call to this, so `send` is where a caller can
        say that this request in particular is worth waiting for. Two more arguments on each of
        them would say the same thing twenty-two times over and turn the file that is meant to
        read as the list of what the gate serves into a file about timeouts.

        Nothing but a `503` is retried. A refusal a client cannot fix by waiting must not be sent
        round a loop, and a write that failed for any other reason may well have landed. A budget
        given for one call is refused as the client's own are, before anything is dialed.
        """
        retry = transport.Retry(
            busy=transport.given(retry_busy_timeout, self._retry_busy_timeout),
            locked=transport.given(retry_locked_timeout, self._retry_locked_timeout),
        )

        payload = "" if body is None else json.dumps(body, separators=(",", ":"))

        while True:
            try:
                reply = self._once(method, path, payload)
                _refuse(reply)
            except (GateBusy, DatabaseLocked) as refusal:
                # Safe to repeat whichever of the two it was: the gate refuses before it acts, so
                # a `503` is the one refusal that says nothing happened. A `DatabaseLocked` read
                # off the certificate is safer still, since the request was never sent.
                if retry.waited(refusal):
                    continue

                raise

            if not reply.body:
                return {}

            return reply.json()

    def _once(self, method: str, path: str, payload: str) -> transport.Response:
        """One attempt, on a connection of its own and under a name of its own.

        A retry is a new connection because the gate serves one request per connection, and a new
        name because the name is minted per connection: an observer watching a client wait out a
        busy KeePass sees a different name each time rather than one repeated.

        **A certificate that has expired is refused here, before anything is dialed.** The gate
        refuses one as it refuses a stranger, `401` with nothing to say why, and this client
        would then report the missing record its `401` usually means, when the record is there
        and the certificate is what ran out. Pairing stored when it expires, so this says so by
        name, and spends no connection learning what it already knows.
        """
        if self._credentials.expired:
            raise NotAdmitted(
                f"this client's certificate expired at {self._credentials.expires}, so the gate "
                "refuses it: pair again"
            )

        name, _ = sni.new_name(self._gate_key)

        with transport.connect(
            self._credentials.port,
            self._context,
            name,
            host=self._host,
            connection_timeout=self._connection_timeout,
            max_response_bytes=self._max_response_bytes,
        ) as connection:
            return connection.send(method, path, payload)


def _expiry(value: object, *, creating: bool) -> str:
    """What `expires` carries for what a caller asked, or a refusal to send it.

    `False` is an entry that does not expire, which on an update takes an expiry off. A moment is
    sent in UTC to the second, the one way the gate reads one and all a `.kdbx` keeps of one, so
    anything below the second is dropped. One with no time zone is refused, because which instant
    it names would be a guess. `"auto"` is a create's, and the gate answers it `400` on an update,
    so it is refused here before anything is sent.
    """
    if value is False:
        return ""

    if isinstance(value, datetime):
        if value.utcoffset() is None:
            raise ValueError("an expiry needs a time zone, which datetime.now(UTC) has")

        return value.astimezone(UTC).replace(microsecond=0, tzinfo=None).isoformat() + "Z"

    if value == AUTO and creating:
        return AUTO

    allowed = "a moment, 'auto' or False" if creating else "a moment or False"
    raise ValueError(f"an expiry is {allowed}, not {value!r}")


def _refuse(reply: transport.Response) -> None:
    """What each refusal means to a caller deciding what to do next."""
    # Both of the document's successes, not just the common one. Every create answers `201`,
    # and a client that knows only `200` turns all four of them into refusals.
    if reply.status in (200, 201):
        return

    error = reply.error()

    if reply.status == 503 and error == "locked":
        raise DatabaseLocked(
            "the database is locked, so every route answers this until it is not",
            reply.retry_after,
        )

    if reply.status == 503:
        raise GateBusy("the gate cannot serve this right now", reply.retry_after)

    if reply.status == 401:
        # The record is read from the live database on every connection and never cached, so this
        # is also what revocation looks like: immediate, and from the client's side no different
        # from never having been admitted, which is the point. A certificate that has expired is
        # refused the same way, and is named before dialing when this client knows it has; one
        # whose expiry passed by the gate's clock and not yet by this machine's arrives here.
        raise NotAdmitted(
            "the gate does not admit this certificate: it was never admitted, it was revoked, or "
            "it has expired. Pair again"
        )

    raise RouteRefused(reply.status, error)
