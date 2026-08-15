"""What the gate answers with, as objects rather than dictionaries.

**Duck-typed after `pykeepass`, and honest about not being it.** A `PyKeePass` cannot be
synthesized from this side of an API: that class is a decrypted `.kdbx`, and this client has
neither the file nor the master password on purpose. The `spikes/pykeepass-compatibility` branch
measures what that costs, member by member. What is offered instead is the
shape a reader expects, so code written against `pykeepass` mostly moves across, under two rules
decided before any of it was built.

**No `save`.** A method that looks like `pykeepass`'s and does something else is worse than no
method. Writes go through named calls on the client that say they are talking to a gate.

**Absent rather than empty.** Where the gate does not serve something, the attribute does not exist,
so `hasattr` is the compatibility test and a missing feature is never an empty list. An `Entry` from
a listing therefore has no `password`: it was not withheld, it was never asked for, and a client
that treats absence as emptiness would report a database of blank passwords.
"""

from __future__ import annotations

import base64
from dataclasses import dataclass, field
from datetime import UTC, datetime
from typing import Any

from .errors import ProtocolError

TITLE = "Title"
USERNAME = "UserName"
# The name of a field, not one.
PASSWORD = "Password"  # noqa: S105
URL = "URL"
NOTES = "Notes"

STANDARD = (TITLE, USERNAME, PASSWORD, URL, NOTES)

EXPIRES = "expires"
"""The member an entry's expiry travels under, in a read and in a create or an update."""


def made(body: dict[str, Any]) -> str:
    """What a create answered with, which is what addresses the thing it made."""
    return _text(body, "uuid")


def _text(body: dict[str, Any], key: str) -> str:
    """A member the document declares as text, or a refusal to guess at one.

    **Every member an answer is read by goes through one of these**, so that an answer this
    client cannot read is a `ProtocolError`, which `errors.py` promises, rather than whatever the
    standard library raises on the way. `str` around a member is the other thing they are here
    to avoid: it makes a `null` identifier the word "None", which a caller would then address.
    Only a plugin defect or a plugin and a client of different versions reaches any of them.
    """
    value = body.get(key)
    if not isinstance(value, str):
        raise ProtocolError(f"the gate's answer has no {key} this client can read")

    return value


def _optional(body: dict[str, Any], key: str) -> str | None:
    """Text the gate withholds with `null`, which stays `None` rather than becoming the word."""
    value = body.get(key)
    if value is not None and not isinstance(value, str):
        raise ProtocolError(f"the gate's answer has no {key} this client can read")

    return value


def _flag(body: dict[str, Any], key: str) -> bool:
    """A member the document declares as true or false."""
    value = body.get(key)
    if not isinstance(value, bool):
        raise ProtocolError(f"the gate's answer does not say whether it is {key}")

    return value


def _size(body: dict[str, Any], key: str) -> int:
    """A count of bytes, which is a whole number and never a negative one."""
    value = body.get(key)
    if isinstance(value, bool) or not isinstance(value, int) or value < 0:
        raise ProtocolError(f"the gate's answer has no {key} this client can read")

    return value


def _objects(body: dict[str, Any], key: str) -> list[dict[str, Any]]:
    """What a listing lists, each an object with members of its own."""
    value = body.get(key)
    if not isinstance(value, list) or not all(isinstance(item, dict) for item in value):
        raise ProtocolError(f"the gate's answer has no list of {key} this client can read")

    return value


def _strings(body: dict[str, Any], key: str) -> dict[str, str | None]:
    """An entry's strings by name, each text, or `null` where the rule does not reach it."""
    value = body.get(key)
    if not isinstance(value, dict) or not all(
        text is None or isinstance(text, str) for text in value.values()
    ):
        raise ProtocolError(f"the gate's answer has no {key} this client can read")

    return value


def _names(body: dict[str, Any], key: str) -> tuple[str, ...]:
    """A list of names, which is every one of them text."""
    value = body.get(key)
    if not isinstance(value, list) or not all(isinstance(name, str) for name in value):
        raise ProtocolError(f"the gate's answer has no list of {key} this client can read")

    return tuple(value)


def _content(value: object) -> bytes:
    """An attachment's bytes, or a refusal to guess at them.

    `str` around this would turn a `null` into the word "None", which is valid base64 and
    decodes to three bytes of something. Wrong data is worse than an error, so this is an error,
    and so is text that is not base64: decoded leniently, a character outside the alphabet is
    dropped and the rest decodes to bytes the gate never sent.
    """
    if not isinstance(value, str):
        raise ProtocolError("the gate sent an attachment with no content")

    try:
        return base64.b64decode(value, validate=True)
    except ValueError as error:
        raise ProtocolError("the gate sent an attachment whose content is not base64") from error


def _moment(value: object) -> datetime | None:
    """RFC 3339 as the gate writes it, or None when it said nothing or said it wrongly."""
    if not isinstance(value, str) or not value:
        return None

    try:
        return datetime.strptime(value, "%Y-%m-%dT%H:%M:%SZ").replace(tzinfo=UTC)
    except ValueError:
        return None


@dataclass(frozen=True)
class _Expiry:
    """What a read said about when an entry expires: a moment, or `None` for never."""

    when: datetime | None


def _expiry(body: dict[str, Any]) -> _Expiry | None:
    """What a read said about when the entry expires, or None when it said nothing about it.

    `null` is an entry that does not expire, so a value this cannot read is refused rather than
    read as one: that would report a credential the operator retired as a live one, which is the
    one direction a reader of this must not be wrong in. `_moment` answers `None` for a time it
    cannot read, which is right for `created` and wrong here.
    """
    if EXPIRES not in body:
        return None

    said = body[EXPIRES]
    if said is None:
        return _Expiry(None)

    when = _moment(said)
    if when is None:
        raise ProtocolError("the gate said when an entry expires in a form this client cannot read")

    return _Expiry(when)


@dataclass(frozen=True)
class Group:
    """A group, as the gate describes one.

    A listing fills in where it is and what it is called. Reading one fills in the notes as well,
    and only then does `notes` exist: they are free text, which the gate hands out for `read` and
    not for `list`, so a listed group raises `AttributeError` rather than answering `None`.
    """

    uuid: str
    name: str
    parent: str | None
    created: datetime | None = None
    modified: datetime | None = None
    read_notes: str | None = field(default=None, repr=False)

    @property
    def is_root(self) -> bool:
        """The one group with nothing above it, which is how a client finds the top."""
        return self.parent is None

    @property
    def is_read(self) -> bool:
        """Whether this came from reading a group rather than from listing one."""
        return self.read_notes is not None

    @property
    def notes(self) -> str:
        """The notes, when this group has been read."""
        if self.read_notes is None:
            raise AttributeError(
                "this group was listed rather than read, so it has no notes. "
                "Read it with GateClient.group() first."
            )

        return self.read_notes

    @classmethod
    def of(cls, body: dict[str, Any]) -> Group:
        """Read one out of an answer."""
        return cls(
            uuid=_text(body, "uuid"),
            name=_text(body, "name"),
            parent=_optional(body, "parent"),
            created=_moment(body.get("created")),
            modified=_moment(body.get("modified")),
            read_notes=_text(body, "notes") if "notes" in body else None,
        )

    @classmethod
    def listing(cls, body: dict[str, Any]) -> list[Group]:
        """Every group a listing answered with."""
        return [cls.of(item) for item in _objects(body, "groups")]


@dataclass(frozen=True)
class Entry:
    """An entry, with as much of it as the gate was asked for.

    A listing fills in what a listing gives: the identifier, the group, the title, the URL and
    the two times. Reading one
    fills in `fields`, and only then do `username`, `password` and `notes` exist at all. Asking for
    one that was never read raises `AttributeError`, which is the point: absence is not emptiness.
    `expires` and `expired` are the same: a listing does not say when an entry expires, so a
    listed entry has neither, rather than an expiry that reads as never.
    """

    uuid: str
    group: str
    title: str | None
    url: str | None
    created: datetime | None = None
    modified: datetime | None = None
    fields: dict[str, str | None] | None = None
    protected: tuple[str, ...] = field(default_factory=tuple)
    expiry: _Expiry | None = field(default=None, repr=False)

    @property
    def expires(self) -> datetime | None:
        """When the entry expires, in UTC, or `None` when it does not, once it has been read."""
        if self.expiry is None:
            raise AttributeError(
                "this entry was listed rather than read, so it says nothing about when it expires. "
                "Read it with GateClient.entry() first."
                if self.fields is None
                else "the gate said nothing about when this entry expires"
            )

        return self.expiry.when

    @property
    def expired(self) -> bool:
        """Whether the entry has expired, on this machine's clock, once it has been read.

        As KeePass judges one, and as `pykeepass` answers the same question: it expires, and its
        moment is now or earlier. A listing asked to leave expired entries out is judged by the
        gate on its own clock, which is this machine's too unless something is tunneling.
        """
        when = self.expires
        return when is not None and when <= datetime.now(UTC)

    @property
    def username(self) -> str:
        """The user name, when this entry has been read."""
        return self._field(USERNAME)

    @property
    def password(self) -> str:
        """The password, when this entry has been read."""
        return self._field(PASSWORD)

    @property
    def notes(self) -> str:
        """The notes, when this entry has been read."""
        return self._field(NOTES)

    @property
    def is_read(self) -> bool:
        """Whether this came from reading an entry rather than from listing one."""
        return self.fields is not None

    def custom(self) -> dict[str, str | None]:
        """Everything that is not one of the five standard strings."""
        if self.fields is None:
            raise AttributeError("this entry was listed rather than read, so it holds no fields")

        return {name: value for name, value in self.fields.items() if name not in STANDARD}

    def _field(self, name: str) -> str:
        if self.fields is None:
            raise AttributeError(
                f"this entry was listed rather than read, so it has no {name}. "
                "Read it with GateClient.entry() first."
            )

        # A standard string the read does not carry is one the entry does not hold, and KeePass
        # reads that as empty: its own `ReadSafe` answers "" and its dialog shows a blank. So this
        # is "" rather than absent, and absence is kept for what this client never read, above,
        # and for what the rule withheld, below.
        value = self.fields.get(name, "")
        if value is None:
            # Withheld rather than absent: the field is there and this client was not given
            # `protected`. Raising keeps the promise the class docstring makes, and stops a
            # stand-in value being mistaken for the real one.
            raise AttributeError(
                f"{name} is protected and this client may not read protected values. "
                "Pair with a profile granting `P` to see it."
            )

        return value

    @classmethod
    def listing(cls, body: dict[str, Any]) -> list[Entry]:
        """Every entry a listing answered with."""
        return [cls.listed(item) for item in _objects(body, "entries")]

    @classmethod
    def listed(cls, body: dict[str, Any]) -> Entry:
        """One out of a listing, which carries no values."""
        # A protected title or URL comes back as `null` when the rule lacks `P`, so neither is
        # forced through `str`: the word "None" is not what the gate said.
        return cls(
            uuid=_text(body, "uuid"),
            group=_text(body, "group"),
            title=_optional(body, "title"),
            url=_optional(body, "url"),
            created=_moment(body.get("created")),
            modified=_moment(body.get("modified")),
        )

    @classmethod
    def read(cls, body: dict[str, Any]) -> Entry:
        """One out of a read, which carries everything the entry holds."""
        # `None` survives as `None`: the gate says `null` for a protected value this
        # client may not read, and `str` would turn that into the word "None".
        fields = _strings(body, "fields")

        # A title or a URL the read does not carry is empty, as `_field` answers for the other
        # three standard strings and as a listing of the same entry says, and `None` stays what it
        # means everywhere else: withheld.
        return cls(
            uuid=_text(body, "uuid"),
            group=_text(body, "group"),
            title=fields.get(TITLE, ""),
            url=fields.get(URL, ""),
            created=_moment(body.get("created")),
            modified=_moment(body.get("modified")),
            fields=fields,
            protected=_names(body, "protected"),
            expiry=_expiry(body),
        )


@dataclass(frozen=True)
class Field:
    """One of an entry's strings, named and possibly quoted."""

    name: str
    protected: bool
    value: str | None = None

    @property
    def is_read(self) -> bool:
        """Whether this came from reading the field rather than from listing the names.

        A protected value the rule does not reach reads as withheld rather than as read: it arrives
        as `None`, the same as a name that was only listed. `protected` is what tells those apart.
        """
        return self.value is not None

    @classmethod
    def listing(cls, body: dict[str, Any]) -> list[Field]:
        """The names a listing answered with, which carries no values."""
        return [
            cls(name=_text(item, "name"), protected=_flag(item, "protected"))
            for item in _objects(body, "fields")
        ]

    @classmethod
    def read(cls, body: dict[str, Any]) -> Field:
        """One string's value, read on its own."""
        # `None` survives as `None`, for the reason `Entry.read` gives: the gate answers a
        # protected value this client may not read with `null`, and `str` would make that the word
        # "None". `is_read` is what asks, and it can only answer while the `None` is intact.
        return cls(
            name=_text(body, "name"),
            protected=_flag(body, "protected"),
            value=_optional(body, "value"),
        )


@dataclass(frozen=True)
class Attachment:
    """One of an entry's attachments, named and sized, with its bytes when they were asked for."""

    name: str
    size: int
    content: bytes | None = None

    @property
    def is_read(self) -> bool:
        """Whether the bytes were fetched, as opposed to only the name and the size."""
        return self.content is not None

    @classmethod
    def listing(cls, body: dict[str, Any]) -> list[Attachment]:
        """The names and sizes a listing answered with, never the contents."""
        return [
            cls(name=_text(item, "name"), size=_size(item, "size"))
            for item in _objects(body, "attachments")
        ]

    @classmethod
    def read(cls, body: dict[str, Any]) -> Attachment:
        """One attachment, contents and all."""
        return cls(
            name=_text(body, "name"),
            size=_size(body, "size"),
            content=_content(body.get("content")),
        )
