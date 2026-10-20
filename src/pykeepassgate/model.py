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

**Withheld is not empty either.** A protected value the caller's rule does not open arrives as
`null`, and every way of reading one raises `Withheld`, never `None`, an empty string or any other
stand-in, because each of those reads as a value. Its name stays where the gate put it, so what an
entry holds is still told truthfully, and `withheld` says which values are kept back.
"""

from __future__ import annotations

import base64
from collections.abc import Container, Iterator, Mapping
from dataclasses import dataclass, field
from datetime import UTC, datetime
from typing import Any

from .errors import ProtocolError, Withheld

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
    """Text the gate may answer with `null`, which stays `None` rather than becoming the word."""
    value = body.get(key)
    if value is not None and not isinstance(value, str):
        raise ProtocolError(f"the gate's answer has no {key} this client can read")

    return value


def _nullable(body: dict[str, Any], key: str) -> str | None:
    """A value the gate answers with `null` when it is withheld, and always answers.

    `None` here is the gate's `null`, which `Fields` turns into a name with no value behind it.
    A member that is missing is not that: the document declares it on every answer that carries
    it, so its absence is an answer this client cannot read rather than one it can read as
    withheld or as empty.
    """
    if key not in body:
        raise ProtocolError(f"the gate's answer has no {key} this client can read")

    return _optional(body, key)


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


class Fields(Mapping[str, str]):
    """An entry's strings by name, none of them a stand-in for a value this client may not read.

    **Its keys are every string the entry holds**, withheld ones included, so `in`, `len` and the
    names tell the truth about what is there. **Reading a withheld value raises `Withheld`** by
    every path a mapping has, `[]`, `get`, `values()`, `items()` and `dict()` over it, so nothing
    looked up or iterated ever hands back `None` or an empty string where a secret is. A name the
    entry does not hold is a `KeyError`, as in any mapping, and `get` answers its default for that
    and for nothing else.

    `withheld` names the values kept back, for a caller that would rather ask than catch, so every
    value this client may read is `{name: fields[name] for name in fields if name not in
    fields.withheld}`.

    Read only. Compared and hashed by what it holds without reading a withheld value, which is
    what lets two entries holding one be compared at all, and printed with its names and no value.
    """

    def __init__(
        self, strings: Mapping[str, str | None] | None = None, listed: bool = False
    ) -> None:
        """Take what the gate answered, with `None` for each `null` it withheld a value with.

        `listed` is whether these came from a listing, which decides nothing but whose rule the
        message of a `Withheld` names.
        """
        given = dict(strings or {})
        self._names = tuple(given)
        self._values = {name: value for name, value in given.items() if value is not None}
        self._withheld = tuple(name for name, value in given.items() if value is None)
        self._listed = listed

    @property
    def withheld(self) -> tuple[str, ...]:
        """The names whose values the gate withheld, in the order it gave them."""
        return self._withheld

    def __getitem__(self, name: str) -> str:
        """The value of the string by this name, or `Withheld` when the gate kept it back."""
        if name in self._values:
            return self._values[name]

        if name in self._withheld:
            raise Withheld(name, self._listed)

        raise KeyError(name)

    def __iter__(self) -> Iterator[str]:
        """Every name, withheld ones included, in the order the gate gave them."""
        return iter(self._names)

    def __len__(self) -> int:
        """How many strings the entry holds, withheld ones included."""
        return len(self._names)

    def __contains__(self, name: object) -> bool:
        """Whether the entry holds a string by this name, which reads no value."""
        return name in self._values or name in self._withheld

    def __eq__(self, other: object) -> bool:
        """Whether both hold the same names and values, compared without reading a withheld one.

        A plain mapping can hold no withheld value, so it is equal to one only when none is.
        """
        if isinstance(other, Fields):
            return self._values == other._values and set(self._withheld) == set(other._withheld)

        if isinstance(other, Mapping):
            return not self._withheld and self._values == dict(other)

        return NotImplemented

    def __hash__(self) -> int:
        """What it holds, as equality compares it."""
        return hash((frozenset(self._values.items()), frozenset(self._withheld)))

    def __repr__(self) -> str:
        """The names, and which of them are withheld, and no value: a repr is what gets logged."""
        return f"Fields(names={self._names!r}, withheld={self._withheld!r})"

    def _but(self, names: Container[str]) -> Fields:
        """The same strings less the ones by these names, as `Entry.custom` hands them out."""
        kept = {name: self._values.get(name) for name in self._names if name not in names}
        return Fields(kept, self._listed)


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
    the two times. Reading one fills in `fields`, and only then do `username`, `password` and
    `notes` exist at all. Asking for one that was never read raises `AttributeError`, which is the
    point: absence is not emptiness. `expires` and `expired` are the same: a listing does not say
    when an entry expires, so a listed entry has neither, rather than an expiry that reads as
    never.

    **A value the rule withholds raises `Withheld`** from every one of them, the title and the URL
    of a listed entry as much as the password of a read one, and `withheld` names which. That is
    a protected value whose rule does not grant `P`, and it is never `None` or empty, from an
    attribute, from `fields` or from `custom()`.
    """

    uuid: str
    group: str
    created: datetime | None = None
    modified: datetime | None = None
    fields: Fields | None = None
    protected: tuple[str, ...] = field(default_factory=tuple)
    #: The title and the URL a listing carried, which is all a listing carries of an entry's
    #: strings. Empty on a read entry, which holds both in `fields` with the rest.
    summary: Fields = field(default_factory=Fields, repr=False)
    expiry: _Expiry | None = field(default=None, repr=False)

    @property
    def title(self) -> str:
        """The title, listed or read: empty for an entry that holds none."""
        return self._shown(TITLE)

    @property
    def url(self) -> str:
        """The URL, listed or read: empty for an entry that holds none."""
        return self._shown(URL)

    @property
    def withheld(self) -> tuple[str, ...]:
        """The names of the values the gate withheld, so a caller can ask rather than catch."""
        return self._strings.withheld

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

    def custom(self) -> Fields:
        """Every string but the five standard ones, withheld ones kept as `fields` keeps them."""
        if self.fields is None:
            raise AttributeError("this entry was listed rather than read, so it holds no fields")

        return self.fields._but(STANDARD)

    @property
    def _strings(self) -> Fields:
        """What the gate carried of the entry's strings: every one when read, two when listed."""
        return self.summary if self.fields is None else self.fields

    def _shown(self, name: str) -> str:
        """A string a listing carries as well as a read, from whichever this came from."""
        # A listing always carries both, so only a read can lack one, and that is the entry not
        # holding it: empty, as `_field` reads the other three and as a listing of it says.
        return self._strings.get(name, "")

    def _field(self, name: str) -> str:
        if self.fields is None:
            raise AttributeError(
                f"this entry was listed rather than read, so it has no {name}. "
                "Read it with GateClient.entry() first."
            )

        # A standard string the read does not carry is one the entry does not hold, and KeePass
        # reads that as empty: its own `ReadSafe` answers "" and its dialog shows a blank. So this
        # is "" rather than absent, and absence is kept for what this client never read, above.
        # One the rule withheld is neither, and `Fields` raises `Withheld` for it, `get` included.
        return self.fields.get(name, "")

    @classmethod
    def listing(cls, body: dict[str, Any]) -> list[Entry]:
        """Every entry a listing answered with."""
        return [cls.listed(item) for item in _objects(body, "entries")]

    @classmethod
    def listed(cls, body: dict[str, Any]) -> Entry:
        """One out of a listing, which carries no values beyond a title and a URL."""
        # A protected title or URL comes back as `null` when the rule lacks `P`, which `Fields`
        # keeps as a name whose value is withheld rather than as the word "None" or as nothing.
        summary = {TITLE: _nullable(body, "title"), URL: _nullable(body, "url")}

        return cls(
            uuid=_text(body, "uuid"),
            group=_text(body, "group"),
            created=_moment(body.get("created")),
            modified=_moment(body.get("modified")),
            summary=Fields(summary, listed=True),
        )

    @classmethod
    def read(cls, body: dict[str, Any]) -> Entry:
        """One out of a read, which carries everything the entry holds."""
        # A `null` is a protected value this client may not read, which `Fields` keeps as a name
        # with no value behind it: never the word "None", and never an empty string.
        return cls(
            uuid=_text(body, "uuid"),
            group=_text(body, "group"),
            created=_moment(body.get("created")),
            modified=_moment(body.get("modified")),
            fields=Fields(_strings(body, "fields")),
            protected=_names(body, "protected"),
            expiry=_expiry(body),
        )


@dataclass(frozen=True)
class Field:
    """One of an entry's strings, named, with its value once it has been read on its own route.

    A listing of an entry's fields names them and says which are protected, and nothing more, so a
    listed field has no `value` at all: asking raises `AttributeError`, as asking a listed entry
    for its password does. A field read on its own route has one, unless the rule withholds it,
    and then `withheld` is true and `value` raises `Withheld`, never `None` or an empty string.
    """

    name: str
    protected: bool
    #: Whether the gate withheld the value: the database protects it and the rule that let this
    #: client read the entry does not grant `P`. Asked without a `try`, where `value` raises.
    withheld: bool = False
    _value: str | None = field(default=None, repr=False)

    @property
    def value(self) -> str:
        """The value, once read, unless the rule withheld it."""
        if self.withheld:
            raise Withheld(self.name)

        if self._value is None:
            raise AttributeError(
                f"the field {self.name!r} was listed rather than read, so it has no value. "
                "Read it with GateClient.field() first."
            )

        return self._value

    @property
    def is_read(self) -> bool:
        """Whether this came from reading the field on its own route rather than from a listing.

        A withheld value was read and refused, so it counts as read, and `withheld` is what says
        its value is not here. `protected` tells neither apart from the other: a protected value
        this client may read and one it may not are both protected.
        """
        return self.withheld or self._value is not None

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
        # The gate answers a protected value this client may not read with `null`, which is
        # `withheld` here rather than a value of `None` or the word "None".
        value = _nullable(body, "value")

        return cls(
            name=_text(body, "name"),
            protected=_flag(body, "protected"),
            withheld=value is None,
            _value=value,
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
