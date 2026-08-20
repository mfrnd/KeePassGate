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

from dataclasses import dataclass, field
from datetime import UTC, datetime
from typing import Any

TITLE = "Title"
USERNAME = "UserName"
# The name of a field, not one.
PASSWORD = "Password"  # noqa: S105
URL = "URL"
NOTES = "Notes"

STANDARD = (TITLE, USERNAME, PASSWORD, URL, NOTES)


def _optional(value: Any) -> str | None:
    """A string the gate sent, or nothing when it withheld one."""
    return None if value is None else str(value)


def _moment(text: str | None) -> datetime | None:
    """RFC 3339 as the gate writes it, or None when it said nothing or said it wrongly."""
    if not text:
        return None

    try:
        return datetime.strptime(text, "%Y-%m-%dT%H:%M:%SZ").replace(tzinfo=UTC)
    except ValueError:
        return None


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
            uuid=str(body.get("uuid", "")),
            name=str(body.get("name", "")),
            parent=body.get("parent"),
            created=_moment(body.get("created")),
            modified=_moment(body.get("modified")),
            read_notes=str(body.get("notes") or "") if "notes" in body else None,
        )


@dataclass(frozen=True)
class Entry:
    """An entry, with as much of it as the gate was asked for.

    A listing fills in what a listing gives: the identifier, the group, the title, the URL and
    the two times. Reading one
    fills in `fields`, and only then do `username`, `password` and `notes` exist at all. Asking for
    one that was never read raises `AttributeError`, which is the point: absence is not emptiness.
    """

    uuid: str
    group: str
    title: str | None
    url: str | None
    created: datetime | None = None
    modified: datetime | None = None
    fields: dict[str, str | None] | None = None
    protected: tuple[str, ...] = field(default_factory=tuple)

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
    def listed(cls, body: dict[str, Any]) -> Entry:
        """One out of a listing, which carries no values."""
        # A protected title or URL comes back as `null` when the rule lacks `P`, so neither is
        # forced through `str`: the word "None" is not what the gate said.
        return cls(
            uuid=str(body.get("uuid", "")),
            group=str(body.get("group", "")),
            title=_optional(body.get("title")),
            url=_optional(body.get("url")),
            created=_moment(body.get("created")),
            modified=_moment(body.get("modified")),
        )

    @classmethod
    def read(cls, body: dict[str, Any]) -> Entry:
        """One out of a read, which carries everything the entry holds."""
        # `None` survives as `None`: the gate says `null` for a protected value this
        # client may not read, and `str` would turn that into the word "None".
        fields = {
            str(k): (None if v is None else str(v))
            for k, v in dict(body.get("fields", {})).items()
        }

        return cls(
            uuid=str(body.get("uuid", "")),
            group=str(body.get("group", "")),
            title=fields.get(TITLE),
            url=fields.get(URL),
            created=_moment(body.get("created")),
            modified=_moment(body.get("modified")),
            fields=fields,
            protected=tuple(str(name) for name in body.get("protected", [])),
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
