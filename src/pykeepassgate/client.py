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
import contextlib
import json
from collections.abc import Callable
from dataclasses import dataclass
from datetime import UTC, datetime, timedelta
from pathlib import Path
from typing import Any, Literal
from urllib.parse import quote

from . import credentials as credentials_module
from . import sni, transport
from .errors import (
    GateError,
    GateUnavailable,
    NotAdmitted,
    ProtocolError,
    RenewalRefused,
    RenewalRefusedForever,
    RenewalRefusedThisSession,
    RenewalTooEarly,
    RenewalUnanswered,
    RouteRefused,
    StorageError,
)
from .model import EXPIRES, Attachment, Entry, Field, Group, Identity, made
from .pairing import lifetime_text

STATUS_PATH = "/gate/status"

IDENTITY_PATH = "/gate/whoami"

CERTIFICATE_PATH = "/gate/certificate"

RENEW_PATH = "/gate/renew"

CLOCK_LEEWAY = timedelta(minutes=5)
"""How long after its stored expiry a certificate is still offered, since the gate's clock decides.

The five minutes the plugin's authority backdates a certificate by, `AuthorityIssuer.Backdate`, for
a client clock behind the gate's, granted here the other way, to a clock ahead of it.
"""

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
    """What the gate says about itself and the database this client reached.

    `version` is the plugin's release, which this client and the routing document share, and
    `revision` the commit the plugin was built from, in full, or empty for a build that carries
    none: every build between two releases carries the same version, and this tells them apart.
    """

    ok: bool
    database: str
    version: str
    revision: str


@dataclass(frozen=True)
class Renewal:
    """What a renewal left: the identity the client is now, and what became of the one before.

    `unrevoked` and `retired` are both `None` when everything went: the old certificate is
    revoked and what was stored for it is gone. Otherwise `retired` is where the old identity is
    still kept, because revoking a certificate takes presenting it, and `unrevoked` says why the
    gate did not take it back, when that is the reason it is kept.
    """

    credentials: credentials_module.Credentials
    unrevoked: GateError | None
    retired: Path | None


class GateClient:
    """One paired identity, talking to one gate."""

    def __init__(
        self,
        credentials: credentials_module.Credentials,
        *,
        connection_timeout: float = transport.DEFAULT_CONNECTION_TIMEOUT,
        host: str = transport.LOOPBACK,
        dial: transport.Dial = transport.create_connection,
        retry_busy_timeout: float | None = transport.DEFAULT_RETRY_BUSY_TIMEOUT,
        retry_locked_timeout: float | None = transport.DEFAULT_RETRY_LOCKED_TIMEOUT,
        retry_undecided_timeout: float | None = transport.DEFAULT_RETRY_UNDECIDED_TIMEOUT,
        max_response_bytes: int = transport.MAX_RESPONSE_BYTES,
        key_password: transport.KeyPassword | None = None,
    ) -> None:
        """Take a paired identity, how long to wait on its socket, and where to dial it.

        The three retry budgets are how long this client will go on waiting out a `503`, one per
        word, and all three are `None` here: nothing retries unless a caller says so, and the
        refusal is raised exactly as it was before there was anything to say. They are separate
        because each ends differently: a busy KeePass is in the middle of something, a locked one
        is waiting for a person to come back, and an undecided one for a person to choose between
        two authorities, which may not be this client's. A `503` with a word none of them names
        is never waited out. Any of them can be overridden on one call, which `send` takes.

        `max_response_bytes` is how much of one answer this client will read, and an answer over
        it fails whole with `ProtocolError` naming the setting, rather than arriving cut short.
        The plugin does not page, so a listing grows with the database, and a database large
        enough to outgrow the default is one whose caller raises this.

        **A setting that could not work is refused here**, with a `ValueError` naming it, before
        anything is dialed, and the command line refuses the same ones: a cap that is not a
        positive whole number of bytes, a socket timeout that is not a finite number of seconds
        above zero, a budget that is neither `None` nor a finite number of seconds from zero, and
        a port in the credentials that TCP does not have.

        `key_password` opens a key that pairing or a renewal stored under one: the password, as text
        or bytes, or a callable returning it, which is asked only if the key turns out to be
        encrypted, and once. A key that is not encrypted is opened without it, and one given for it
        is not used. An encrypted key with no password, or one the password does not open, is a
        `StorageError` naming `identity.pem`.

        The host is where the socket goes and nothing else. It is not part of the identity and is
        not stored with it: the same paired client reaches the same KeePass through a tunnel or
        directly, and which of those is happening is a property of the caller's machine rather than
        of the pairing. So it is given here rather than read back from the credentials, and it
        defaults to loopback, which is where the gate itself binds.

        It does not reach the name in the ClientHello either. That is minted per connection from
        the gate's key and is what the gate resolves this client by; a tunnel changes the address
        the bytes travel to and changes nothing the gate sees.

        `dial` is what makes the connection for the host and the port: a callable taking them and
        the timeout and returning a connected socket, `socket.create_connection` unless given.
        TLS and everything above it is layered over what it returns, and each wait on that socket
        is bounded by `connection_timeout` whatever made it. A renewal dials with it as well, both
        for the renewal and for giving the old certificate up.

        **A renewal waits at least `transport.PAIRING_CONNECTION_TIMEOUT`** for its answer,
        whatever `connection_timeout` says, because it waits on a person as a pairing does and the
        gate gives it the same six minutes: a client that hung up sooner would leave the operator's
        approval written and saved for a certificate nobody holds. A longer `connection_timeout`
        is kept. Giving the old certificate up afterwards asks nobody, and waits
        `connection_timeout`.
        """
        transport.port_number(credentials.port)
        self._credentials = credentials
        self._connection_timeout = transport.connection_limit(connection_timeout)
        self._host = host
        self._dial = transport.dial_limit(dial)
        self._retry_busy_timeout = transport.retry_limit("retry_busy_timeout", retry_busy_timeout)
        self._retry_locked_timeout = transport.retry_limit(
            "retry_locked_timeout", retry_locked_timeout
        )
        self._retry_undecided_timeout = transport.retry_limit(
            "retry_undecided_timeout", retry_undecided_timeout
        )
        self._max_response_bytes = transport.response_limit(max_response_bytes)
        self._key_password = transport.key_password_limit(key_password)
        self._opened: bytes | None = None
        self._context = transport.paired_context(
            credentials.authority, credentials.identity, self._unlock
        )

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
        *,
        host: str = transport.LOOPBACK,
        dial: transport.Dial = transport.create_connection,
        connection_timeout: float = transport.DEFAULT_CONNECTION_TIMEOUT,
        retry_busy_timeout: float | None = transport.DEFAULT_RETRY_BUSY_TIMEOUT,
        retry_locked_timeout: float | None = transport.DEFAULT_RETRY_LOCKED_TIMEOUT,
        retry_undecided_timeout: float | None = transport.DEFAULT_RETRY_UNDECIDED_TIMEOUT,
        max_response_bytes: int = transport.MAX_RESPONSE_BYTES,
        key_password: transport.KeyPassword | None = None,
    ) -> GateClient:
        """Load a paired identity from where `pair` put it.

        Everything that is not the identity is given here rather than read back with it, and for
        the same reason: where to dial, how long to wait on the socket and how long to wait out a
        refusal are the caller's, not the pairing's. This is the way a caller is meant to build
        one, so a setting it cannot reach is a setting nobody has. The key's password is given
        here as well, for a key stored under one, since it is not stored beside the key.
        """
        where = directory or credentials_module.default_directory(profile)
        return cls(
            credentials_module.load(where),
            connection_timeout=connection_timeout,
            host=host,
            dial=dial,
            retry_busy_timeout=retry_busy_timeout,
            retry_locked_timeout=retry_locked_timeout,
            retry_undecided_timeout=retry_undecided_timeout,
            max_response_bytes=max_response_bytes,
            key_password=key_password,
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

        It reads nothing out of the database on purpose, which is why it is one of the routes
        declaring `x-kpg-verb: none`, beside `identity`, `revoke_certificate`, `renew` and the two
        pairing routes, as the routing document lists them: no grant could allow or refuse it,
        because it reaches nothing a grant is about. It exists to prove a connection rather than
        to be useful.

        A client can use it to answer three questions at once: whether the gate is listening,
        whether this certificate is still admitted, and whether the database is unlocked.

        Every member is read as the document declares it, as every other answer's are: `bool`
        would read the text "false" as true, and `str` a `null` as the word "None".
        """
        body = self.get(STATUS_PATH)
        ok, database = body.get("ok"), body.get("database")
        version, revision = body.get("version"), body.get("revision")
        if not isinstance(ok, bool) or not (
            isinstance(database, str) and isinstance(version, str) and isinstance(revision, str)
        ):
            raise ProtocolError("the gate answered its status in a form this client cannot read")

        return Status(ok=ok, database=database, version=version, revision=revision)

    def identity(self) -> Identity:
        """Who this client is, as the database it reached admitted it.

        `GET /gate/whoami` declares `x-kpg-verb: none`, as the status route does: everything it
        answers was settled when the connection was admitted, out of this client's own record, so
        no rule is asked and a client the rules let reach nothing may still ask who it is.

        What pairing or the last renewal stored is what the operator approved then, and
        `credentials` says it without connecting. This is what holds now: `profiles` are the ones
        the rules are asked about, which leave out any name the database no longer holds, and
        `removed_profiles` are those, which grant nothing until the operator names them again.
        """
        return Identity.of(self.get(IDENTITY_PATH))

    def revoke_certificate(self) -> None:
        """Give up this client's admission, by the certificate it presents.

        `DELETE /gate/certificate` takes no body and names nothing, because the certificate on
        the connection is what is revoked: a client can revoke itself and nobody else. The gate
        writes what its Clients page writes, saves, and reads the file back before it answers,
        and from the next connection on every call here raises `NotAdmitted`.

        **A failure says what it left behind, in the gate's word on the `RouteRefused`.** `not
        saved` kept nothing, so the client is still admitted and asking again is safe. `not
        verified` is a revocation KeePass holds in memory and may not have in the file: the
        certificate is already refused, so asking again raises `NotAdmitted`, and whether the
        file keeps it is the operator's to settle. A `503` is answered before anything is
        written, so it is waited out as on every route.

        What is stored for this client is left as it is, since files are the caller's business:
        `forget` is the call that takes them away, and the command line's `revoke` makes it once
        this has returned.
        """
        self.send("DELETE", CERTIFICATE_PATH)

    def renew(
        self, lifetime: timedelta | None = None, allow_shared_directory: bool = False
    ) -> Renewal:
        """Ask the operator for a new certificate, keep it in place of this one, give this one up.

        `POST /gate/renew` blocks while the operator reads a dialog, as pairing does, and answers
        with the bundle pairing answers with. **`lifetime` is a proposal**, a `timedelta` of
        whole days from `SHORTEST_LIFETIME` to `LONGEST_LIFETIME`, refused with `ValueError`
        before anything is sent; the operator picks, and `expires` on what comes back says which.

        **Stored whole beside the old identity before it is put in place.** Three directories are
        made ready before anything is asked, so that one that cannot hold an identity costs
        nothing: `<dir>.renewing`, which receives the new identity, `<dir>.retired`, which receives
        a copy of the old one, and the identity's own. Once the new identity is whole in the first
        and the old one in the second, the four files of the identity's own directory are written
        over with the new ones, and the first is taken away. **Only those four files move**:
        anything else a program keeps in the directory stays where it is. A failure while they
        are written over puts the old ones back, and says where each identity is; at every moment
        each of the two is whole somewhere, and a person can finish what was interrupted.
        `allow_shared_directory` waives `prepare`'s ownership check on the identity's own
        directory, for one paired into a directory shared on purpose; the two beside it are this
        client's own and are never waived.

        **A renewed identity an earlier renewal left in that directory is never written over.**
        The gate hands one over only once its record is saved, so it is admitted and its key is the
        only one there is: a renewal refuses to start while it is there, with a `StorageError`
        saying how to finish the one that stopped. Anything there that does not load as an identity
        is what a renewal that stopped before its answer leaves, and is written over.

        **Then this certificate is given up**, with `revoke_certificate` as the identity it
        renewed from, because the gate leaves the old record in place: an answer lost on the way
        costs nothing that way, since the old certificate still works and the renewal can be asked
        for again. **A revocation that did not happen is reported on what comes back rather than
        raised**, because the renewal itself succeeded and this client is already the new
        identity: raising would read as the opposite. The old identity is then kept beside the new
        one, at `retired`, since giving it up later takes presenting it.

        **A later renewal gives it up first.** It presents the kept certificate again before it
        asks for anything, and a certificate the gate then refuses, or one that has expired and is
        not dialed at all, counts as given up as well: nothing can use it, so what was kept for it
        is forgotten and the renewal goes on. While the gate still admits it and will not take it
        back, the renewal refuses to start, with a `StorageError` naming the directory, since a
        second one kept beside the first would have nowhere to go.

        **This client carries on as the new identity**, for every call after this one: it is the
        same client asking for the same things, and the old certificate is about to be refused.

        **The new key is stored as the old one was**: under the password that opened the old one,
        or under none when that needed none. Nobody is asked for it again, since the client
        already holds it.

        **The request waits at least `transport.PAIRING_CONNECTION_TIMEOUT`** on the socket, six
        minutes, or this client's `connection_timeout` when that is longer: the gate answers only
        once the operator has, after waiting for the screen behind any other pairing or renewal,
        and then writes and saves before it says so. Hanging up sooner would lose an approval
        already saved, and a `RenewalUnanswered` the gate sends once its dialog's two minutes are
        up.

        A refusal raises `RenewalRefused`, or `RenewalRefusedThisSession`, `RenewalTooEarly` or
        `RenewalRefusedForever` when the operator answered more than this once, and nobody
        answering raises `RenewalUnanswered`; none of them changes anything stored, and this
        client stays what it was. Nor does `500 not recorded`, a `RouteRefused`: the operator
        approved and the new record did not reach the disk, so nothing was handed over.

        **A `503` is the gate's, answered before anything is written**, and raised as on every
        route, `GateBusy`, `DatabaseLocked` or `AuthorityUndecided`, or waited out on this client's
        own budgets, since there is no budget given per call here. Waiting one out sends the
        renewal again, and a renewal refused busy waits its turn for the screen again: the
        operator's dialog comes up again for it, and a busy one can take two minutes to arrive.
        **Except an approval the gate holds**: when the operator approved and KeePass was too busy
        to write it, `GateBusy` carries `held_until`, and this asks again by itself, every
        `Retry-After`, until the renewal is written without the dialog or that moment passes,
        within the six minutes the gate gives a renewal and whatever the busy budget.
        """
        written = None if lifetime is None else lifetime_text(lifetime)
        directory = self._credentials.directory
        staging = _beside(directory, "renewing")
        retired = _beside(directory, "retired")

        self._unfinished(staging)
        self._settle(retired)
        kept = _held(directory)

        prepared: list[credentials_module.Prepared] = []
        try:
            for where, shared in (
                (staging, False),
                (retired, False),
                (directory, allow_shared_directory),
            ):
                prepared.append(credentials_module.prepare(where, allow_shared_directory=shared))

            renewing, keeping, live = prepared
            bundle = self._request(
                "POST", RENEW_PATH, {} if written is None else {"lifetime": written},
                _refuse_renewal,
                connection_timeout=max(
                    self._connection_timeout, transport.PAIRING_CONNECTION_TIMEOUT
                ),
                held=transport.PAIRING_CONNECTION_TIMEOUT,
            )
            # The same client under a new certificate, so the client key is the one it renews from,
            # which is what the gate copied into the new record as well.
            renewing.save(
                self._credentials.port,
                bundle,
                key_password=self._opened,
                client_key=self._credentials.client_key,
            )
        except BaseException:
            # Only empty ones: whatever opening created was taken away when it closed.
            for each in prepared:
                each.close()
            for where in (staging, retired):
                with contextlib.suppress(OSError):
                    where.rmdir()
            raise

        _put_in_place(directory, staging, retired, renewing, keeping, live, kept)

        old = credentials_module.load(retired)
        renewed = credentials_module.load(directory)
        self._become(renewed)

        unrevoked = self._give_up(old)
        if unrevoked is not None:
            return Renewal(credentials=renewed, unrevoked=unrevoked, retired=retired)

        try:
            credentials_module.forget(retired)
        except StorageError:
            return Renewal(credentials=renewed, unrevoked=None, retired=retired)

        return Renewal(credentials=renewed, unrevoked=None, retired=None)

    def _unfinished(self, staging: Path) -> None:
        """Refuse to renew over a renewed identity an earlier renewal could not put in place.

        It is admitted, since the gate hands one over only once the record is written and saved,
        and its key may be the only one there is: preparing over it would destroy it, and ask the
        operator to approve the same program again for a record nobody would then hold the key
        to. A directory that does not load as an identity is what a renewal that stopped before
        its answer leaves. One holding the certificate this client already presents is a copy an
        earlier renewal put in place and could not take away afterwards, and is forgotten. A link
        there is refused rather than read through, since what it names is not this renewal's.
        """
        credentials_module.refuse_link(staging)
        try:
            found = credentials_module.load(staging)
        except StorageError:
            return

        if _same(found, self._credentials):
            credentials_module.forget(staging)
            return

        raise StorageError(
            f"{staging} holds a renewed identity, {found.subject}, which an earlier renewal was "
            "given and could not put in place, and the gate admits it. Finish that renewal "
            f"rather than asking for another: {_finishing(self._credentials.directory, staging)}"
        )

    def _settle(self, retired: Path) -> None:
        """Give up what an earlier renewal kept at `retired`, or refuse to renew while it stands.

        Kept because its give-up did not happen then: locked, busy, not saved. Asked again now,
        presenting it, and forgotten once the gate takes it back or already refuses it, which
        includes one that has expired, since `revoke_certificate` refuses that before dialing.
        What does not load is no kept identity, and the renewal writes over it. A link there is
        refused before anything is read through it: presenting what it names would revoke some
        other identity of this user's, and forgetting it would take that identity's files away.
        """
        credentials_module.refuse_link(retired)
        try:
            old = credentials_module.load(retired)
        except StorageError:
            # Nothing there, or files opened and never written, which a renewal stopped while the
            # operator read leaves: the old identity was never copied out of its own directory.
            return

        if _same(old, self._credentials):
            # A copy of this client's own identity, which a renewal that put the old one back left.
            # Presenting it would revoke this client.
            credentials_module.forget(retired)
            return

        unrevoked = self._give_up(old)
        if unrevoked is not None:
            raise StorageError(
                f"{retired} holds the identity an earlier renewal renewed from, and the gate "
                f"still admits it and did not take it back now either: {unrevoked}. Revoke it "
                f"first, which `pykeepassgate revoke --directory {retired}` does"
            )

        credentials_module.forget(retired)

    def _give_up(self, old: credentials_module.Credentials) -> GateError | None:
        """Revoke the certificate a renewal replaced, presenting it, and say why not if not."""
        try:
            GateClient(
                old,
                connection_timeout=self._connection_timeout,
                host=self._host,
                dial=self._dial,
                retry_busy_timeout=self._retry_busy_timeout,
                retry_locked_timeout=self._retry_locked_timeout,
                retry_undecided_timeout=self._retry_undecided_timeout,
                max_response_bytes=self._max_response_bytes,
                key_password=self._opened,
            ).revoke_certificate()
        except NotAdmitted:
            # Already refused, by a `401` or because it has expired, which is what giving it up
            # was for.
            return None
        except GateError as error:
            return error

        return None

    def _become(self, renewed: credentials_module.Credentials) -> None:
        """Carry on as a renewed identity: its certificate, its anchor and its gate key."""
        context = transport.paired_context(renewed.authority, renewed.identity, self._unlock)
        self._credentials = renewed
        self._context = context
        self._gate_key = renewed.gate_key

    def _unlock(self) -> bytes:
        """The password `ssl` asks for when this client's key is encrypted, asked for once.

        Kept once it is known, because a renewal stores the new key under it, opens the new key
        with it, and opens the old one again to give it up: asking a person three times for one
        password is twice too many. A key that is not encrypted never asks, so a password given
        for one is not used, and a renewal stores the next one unencrypted as well.
        """
        if self._opened is None:
            self._opened = transport.key_secret(self._key_password)

        return self._opened

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
        retry_undecided_timeout: transport.Given = transport.KEEP,
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
            retry_undecided_timeout=retry_undecided_timeout,
        )

    def get(
        self,
        path: str,
        *,
        retry_busy_timeout: transport.Given = transport.KEEP,
        retry_locked_timeout: transport.Given = transport.KEEP,
        retry_undecided_timeout: transport.Given = transport.KEEP,
    ) -> dict[str, Any]:
        """One GET, on its own connection, under a name built for it."""
        return self.send(
            "GET",
            path,
            retry_busy_timeout=retry_busy_timeout,
            retry_locked_timeout=retry_locked_timeout,
            retry_undecided_timeout=retry_undecided_timeout,
        )

    def send(
        self,
        method: str,
        path: str,
        body: dict[str, Any] | None = None,
        *,
        retry_busy_timeout: transport.Given = transport.KEEP,
        retry_locked_timeout: transport.Given = transport.KEEP,
        retry_undecided_timeout: transport.Given = transport.KEEP,
    ) -> dict[str, Any]:
        """One request, on its own connection, retried for as long as a budget allows.

        Public because the gate is a specification-defined API and a client that could only call
        the methods this class happens to name would be a worse reference than one that can reach
        anything the document declares.

        **This is also where a retry budget is overridden for one call**, and the reason it is
        here rather than on each method above is that a call to the gate is a request: every one
        of those methods comes down to the request this makes, so `send` is where a caller can
        say that this request in particular is worth waiting for. Three more arguments on each of
        them would say the same thing once for every route and turn the file that is meant to
        read as the list of what the gate serves into a file about timeouts.

        Nothing but a `503` is retried, and a `503` only when its word is one a budget names. A
        refusal a client cannot fix by waiting must not be sent round a loop, and a write that
        failed for any other reason may well have landed. A budget given for one call is refused
        as the client's own are, before anything is dialed.
        """
        return self._request(
            method,
            path,
            body,
            _refuse,
            retry_busy_timeout=retry_busy_timeout,
            retry_locked_timeout=retry_locked_timeout,
            retry_undecided_timeout=retry_undecided_timeout,
        )

    def _request(
        self,
        method: str,
        path: str,
        body: dict[str, Any] | None,
        refuse: Callable[[transport.Response], None],
        *,
        retry_busy_timeout: transport.Given = transport.KEEP,
        retry_locked_timeout: transport.Given = transport.KEEP,
        retry_undecided_timeout: transport.Given = transport.KEEP,
        connection_timeout: float | None = None,
        held: float | None = None,
    ) -> dict[str, Any]:
        """What `send` does, with what each refusal means given by the caller.

        A renewal's refusals mean things no other route's do, and one of them carries a moment in
        its body, so the route that has them says what they are rather than every route learning
        them. Its `503`s are read by `_refuse` as every route's are, so a renewal is waited out
        exactly as a read is. It waits on a person as well, so it gives its own
        `connection_timeout` too, and `None` is this client's. And it can be answered with an
        approval the gate holds, which `held` is how long to go on asking for, `None` for not.
        """
        retry = transport.Retry(
            busy=transport.given(retry_busy_timeout, self._retry_busy_timeout),
            locked=transport.given(retry_locked_timeout, self._retry_locked_timeout),
            undecided=transport.given(retry_undecided_timeout, self._retry_undecided_timeout),
            held=held,
        )

        payload = "" if body is None else json.dumps(body, separators=(",", ":"))

        while True:
            try:
                reply = self._once(method, path, payload, connection_timeout)
                refuse(reply)
            except GateUnavailable as refusal:
                # Safe to repeat whichever it was: the gate refuses before it acts, so a `503` is
                # the one refusal that says nothing happened. A `DatabaseLocked` read off the
                # certificate is safer still, since the request was never sent. Whether to repeat
                # it at all is the budget for its word, and a word with none is raised as it came.
                if retry.waited(refusal):
                    continue

                raise

            if not reply.body:
                return {}

            return reply.json()

    def _once(
        self, method: str, path: str, payload: str, connection_timeout: float | None = None
    ) -> transport.Response:
        """One attempt, on a connection of its own and under a name of its own.

        A retry is a new connection because the gate serves one request per connection, and a new
        name because the name is minted per connection: an observer watching a client wait out a
        busy KeePass sees a different name each time rather than one repeated.

        **A certificate that has expired is refused here, before anything is dialed.** The gate
        refuses one as it refuses a stranger, `401` with nothing to say why, and this client
        would then report the missing record its `401` usually means, when the record is there
        and the certificate is what ran out. What is stored says when it expires, so this says
        so by name, and spends no connection learning it. **Only once `CLOCK_LEEWAY` has passed
        as well**, since the gate judges by its own clock and this one may run ahead of it:
        until then the gate is asked, and still serves, and renews, a certificate it holds
        current.
        """
        if self._credentials.expired_by(CLOCK_LEEWAY):
            raise NotAdmitted(
                f"this client's certificate expired at {self._credentials.expires}, so the gate "
                "refuses it. To go on reaching the gate, pair again, since a renewal needs a "
                "certificate that has not expired; what is stored for this one protects nothing "
                "now, and forgetting it, which `revoke` does, takes it away"
            )

        name, _ = sni.new_name(self._gate_key)

        with transport.connect(
            self._credentials.port,
            self._context,
            name,
            host=self._host,
            connection_timeout=(
                self._connection_timeout if connection_timeout is None else connection_timeout
            ),
            max_response_bytes=self._max_response_bytes,
            dial=self._dial,
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


def _beside(directory: Path, purpose: str) -> Path:
    """A directory next to this one, named for what it holds during a renewal."""
    return directory.with_name(f"{directory.name}.{purpose}")


def _held(directory: Path) -> dict[str, bytes]:
    """The four files of the identity in a directory, as they are, before a renewal changes them."""
    held: dict[str, bytes] = {}
    for name in credentials_module.FILES:
        try:
            held[name] = (directory / name).read_bytes()
        except OSError as error:
            raise StorageError(f"{directory / name} will not read: {error}") from error

    return held


def _same(found: credentials_module.Credentials, live: credentials_module.Credentials) -> bool:
    """Whether two stored identities hold one certificate, so that one is a copy of the other."""
    try:
        return found.admission_key == live.admission_key
    except StorageError:
        return False


def _finishing(directory: Path, staging: Path) -> str:
    """How a person puts a renewed identity left in `staging` in place, or gives it up."""
    return (
        f"copy the four files in {staging} over the ones in {directory} and remove {staging}, or "
        f"give it up with `pykeepassgate revoke --directory {staging}`"
    )


def _put_in_place(
    directory: Path,
    staging: Path,
    retired: Path,
    renewing: credentials_module.Prepared,
    keeping: credentials_module.Prepared,
    live: credentials_module.Prepared,
    kept: dict[str, bytes],
) -> None:
    """Copy the old identity out, write the renewed one over it, and take the staging copy away.

    **Only the four files.** Writing them over in place, rather than renaming the directory, is
    what leaves anything else a program keeps beside them where it was. Writing in place is not
    atomic, so both identities are kept whole elsewhere first: the renewed one in `staging`, which
    `save` wrote, and the old one in `retired`. A failure while the four are written over writes
    the old ones back, and either way the message says where each identity is.

    Every handle is closed before anything is taken away, which Windows needs.
    """
    try:
        fresh = _held(staging)
        keeping.write(kept)
    except BaseException as error:
        for each in (renewing, keeping, live):
            each.close()
        with contextlib.suppress(OSError):
            retired.rmdir()
        if not isinstance(error, Exception):
            raise
        raise StorageError(
            f"the renewed identity is whole in {staging} and the gate admits it, and it could not "
            f"be put in place: {error}. The one it renewed from is still in {directory} and still "
            f"admitted. To finish, {_finishing(directory, staging)}"
        ) from error

    try:
        live.write(fresh)
    except BaseException as error:
        try:
            live.write(kept)
        except BaseException:
            restored = False
        else:
            restored = True

        for each in (renewing, keeping, live):
            each.close()

        if restored:
            # Back where it was, so the copy is no kept identity, and a later renewal that found
            # it would otherwise give up this client's own certificate.
            with contextlib.suppress(StorageError):
                credentials_module.forget(retired)

        if not isinstance(error, Exception):
            raise

        where = (
            f"The one it renewed from is back in {directory} and still admitted"
            if restored
            else f"{directory} holds part of each, and the one it renewed from is whole in "
            f"{retired} and still admitted, which a later renewal gives up"
        )
        raise StorageError(
            f"the renewed identity is whole in {staging} and the gate admits it, and it could not "
            f"be put in place: {error}. {where}. To finish, {_finishing(directory, staging)}"
        ) from error

    for each in (renewing, keeping, live):
        each.close()

    # A copy now, of what is in place. One left behind is recognized as a copy and forgotten by
    # the next renewal, so failing to take it away is no reason to report a renewal as failed.
    with contextlib.suppress(StorageError):
        credentials_module.forget(staging)


def _refuse_renewal(reply: transport.Response) -> None:
    """What each refusal of a renewal means, and what to do next."""
    if reply.status == 403:
        raise RenewalRefused(
            "the operator refused this renewal, and may have refused more than this once, which "
            "the next request is answered with at once. The certificate this client holds still "
            "works until it expires: ask again later, or pair again before then"
        )

    if reply.status == 409 and reply.error() == "this session":
        raise RenewalRefusedThisSession(
            "the operator refused every renewal of this certificate until KeePass exits. Ask "
            "again after KeePass has been restarted or once the operator has allowed renewal "
            "again on the KPG tab's clients page, or pair again, with an invitation from the "
            "operator, before the one this client holds expires"
        )

    if reply.status == 409 and reply.error() == "forever":
        raise RenewalRefusedForever(
            "the operator refused every renewal of this certificate. Ask again once the operator "
            "has allowed renewal again on the KPG tab's clients page, or pair again, with an "
            "invitation from the operator, before the one this client holds expires"
        )

    if reply.status == 409 and reply.error() == "too early":
        not_before = _moment(reply.json().get("notBefore"))
        raise RenewalTooEarly(
            f"the operator said not before {not_before:%Y-%m-%dT%H:%M:%SZ}: ask again from then. "
            "The certificate this client holds still works until it expires",
            not_before,
        )

    if reply.status == 504:
        raise RenewalUnanswered(
            "nobody answered the renewal dialog, and nothing changed: ask again when somebody is "
            "at the screen"
        )

    _refuse(reply)


def _moment(value: object) -> datetime:
    """A moment the gate wrote, as RFC 3339 in UTC to the second, or a refusal to guess."""
    if isinstance(value, str):
        with contextlib.suppress(ValueError):
            moment = datetime.strptime(value, "%Y-%m-%dT%H:%M:%SZ")
            return moment.replace(tzinfo=UTC)

    raise ProtocolError("the gate refused a renewal until a moment this client cannot read")


def _refuse(reply: transport.Response) -> None:
    """What each refusal means to a caller deciding what to do next."""
    # Both of the document's successes, not just the common one. Every create answers `201`,
    # and a client that knows only `200` turns all four of them into refusals.
    if reply.status in (200, 201):
        return

    if reply.status == 503:
        raise transport.unavailable(
            reply,
            locked="the database is locked, so every route answers this until it is not",
            busy=(
                "the gate cannot serve this right now: KeePass is busy, with a dialog open on "
                "what this would change or with something of its own. Nothing was done, so "
                "asking again shortly is safe"
            ),
            undecided=(
                "the database holds more than one certificate authority and the operator has "
                "not kept one, which KeePass asks about on its own screen. If the one kept is "
                "not this client's, this client reaches nothing afterwards and pairs again"
            ),
        )

    error = reply.error()

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
