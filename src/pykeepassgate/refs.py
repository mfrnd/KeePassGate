"""Field references and placeholders, followed as KeePass follows them, under this client's rules.

The gate serves a value as the entry stores it and never compiles it (`openapi.yaml` §512): KeePass
resolves a reference against the whole open database, which knows nothing about the rules. This
compiles one on this side instead, and every entry it reads to do so is a request of its own, which
the rules decide and the audit records like any other. So `resolve` gives what KeePass would give
for a value, or raises: it never hands back text KeePass would have changed.

**What it follows.** A reference by identifier, `{REF:<T|U|P|A|N|I>@I:<uuid>}`, and the
placeholders for the entry's own strings, `{TITLE}`, `{USERNAME}`, `{PASSWORD}`, `{URL}`, `{NOTES}`
and `{S:<name>}`, in any case. A value a reference or a placeholder stands for is compiled again in
the context of the entry it came from, as KeePass does, so a reference to a reference is followed
too, as far as KeePass follows it.

**What it refuses, with `Unresolved`.** A reference that searches by anything but a whole
identifier: KeePass takes the first match in the whole database, and a search through the gate
would see only what this client may list, so the two could answer with different secrets. A
reference to an entry this client cannot read, since the gate answers one that is not there, one in
the recycle bin, which the API does not serve though KeePass follows a reference into it, and one
it may not reach alike. Every other placeholder KeePass replaces, from `{UUID}` to `{CMD:...}`,
listed in `REFUSED`, and one of those an earlier replacement made, which KeePass replaces or
leaves as text by where in its own order the placeholder's step falls. References that go round in
a circle or further than KeePass follows, where KeePass gives up at the deepest level and answers
that value as empty, keeping the text around it: this refuses the whole value rather than give
that. A placeholder whose name matches a field only when letters
beyond ASCII are compared without their case, which is a comparison KeePass makes its own way. And
a standard string the entry holds under no name spelled exactly so while it holds a name beyond
ASCII, which KeePass's collation may read as that string. A value it may not read, behind a
reference or a placeholder, raises `Withheld`.

**What it leaves as it is.** Braces that name nothing KeePass knows, which are ordinary in a
generated password, and a reference too malformed for KeePass to read, both as KeePass leaves them.
And `%NAME%`, which KeePass replaces with the environment variable of that name on the machine it
runs on: that is a fact about that machine rather than about the database, so it stays as text here.
A placeholder another plugin installed in KeePass adds is unknown here, and stays as text too.

**Pinned against KeePass's own compiler.** `tests/Shared/ReferenceVectors.json` holds what
`SprEngine` makes of every case, regenerated from KeePass by `ReferenceVectorsTests`, and the
client's tests hold this to the same file. The order things are done in is KeePass's for the same
reason: the entry's strings first, in the order the gate lists them, then the references, at most
twenty to a value, then the checks for what it refuses, after each step, since one replacement can
make a placeholder out of the text around it.
"""

from __future__ import annotations

from typing import Protocol

from .errors import RouteRefused, Unresolved, Withheld
from .model import NOTES, PASSWORD, STANDARD, TITLE, URL, USERNAME, Entry, Fields

#: How deep KeePass compiles a value before it gives up and answers that value as empty, which is
#: where a cycle ends in KeePass: `SprEngine.MaxRecursionDepth`. Each value a placeholder or a
#: reference stands for is compiled one level below the value it is in.
DEPTH = 12

#: How many references KeePass looks at in one value, the loop in `FillRefPlaceholders`, counting
#: the ones it skips; one after them is left as text there, and so here.
REFERENCES = 20

#: What each letter of a reference asks for, which `I`, the entry's identifier, completes.
WANTED = {"T": TITLE, "U": USERNAME, "P": PASSWORD, "A": URL, "N": NOTES}

# KeePass's own placeholders below, named the way KeePass names them.
# cspell:ignore APPDIR DIRSEP DOCDIR GOOGLECHROME GROUPPATH HMACOTP INTERNETEXPLORER PICKCHARS
# cspell:ignore PICKPASSWORDCHARS RMVSCM SELPATH TIMEOTP USERINFO

#: Every placeholder KeePass 2.61.1 replaces that this does not follow, read off `SprEngine`,
#: `EntryUtil` and `AppLocator`, and the start of each one that takes parameters. Some answer out
#: of what this client cannot see: the machine KeePass runs on, its clock, its clipboard, the group
#: selected in its window. Some act rather than answer: running a command, generating a password,
#: showing a dialog, setting the clipboard. And the rest, an entry's identifier, the parts of its
#: URL, its group, a comment or a text transformation, are outside what this was given to follow.
#: Each is refused rather than left as text KeePass would have changed.
REFUSED = (
    "{C:",
    "{CMD:",
    "{T-CONV:",
    "{T-REPLACE-RX:",
    "{CLIPBOARD-SET:",
    "{CLIPBOARD}",
    "{INTERNETEXPLORER}",
    "{FIREFOX}",
    "{OPERA}",
    "{GOOGLECHROME}",
    "{SAFARI}",
    "{EDGE}",
    "{PICKPASSWORDCHARS",
    "{PICKCHARS",
    "{PICKFIELD}",
    "{URL:RMVSCM}",
    "{URL:SCM}",
    "{URL:HOST}",
    "{URL:PORT}",
    "{URL:PATH}",
    "{URL:QUERY}",
    "{URL:USERINFO}",
    "{URL:USERNAME}",
    "{URL:PASSWORD}",
    "{UUID}",
    "{PASSWORD_ENC}",
    "{GROUP}",
    "{GROUP_PATH}",
    "{GROUPPATH}",
    "{GROUP_NOTES}",
    "{GROUP_SEL}",
    "{GROUP_SEL_PATH}",
    "{GROUP_SELPATH}",
    "{GROUP_SEL_NOTES}",
    "{APPDIR}",
    "{ENV_DIRSEP}",
    "{ENV_PROGRAMFILES_X86}",
    "{DOCDIR}",
    "{DB_PATH}",
    "{DB_DIR}",
    "{DB_NAME}",
    "{DB_BASENAME}",
    "{DB_EXT}",
    "{CLEARFIELD}",
    "{DT_SIMPLE}",
    "{DT_YEAR}",
    "{DT_MONTH}",
    "{DT_DAY}",
    "{DT_HOUR}",
    "{DT_MINUTE}",
    "{DT_SECOND}",
    "{DT_UTC_SIMPLE}",
    "{DT_UTC_YEAR}",
    "{DT_UTC_MONTH}",
    "{DT_UTC_DAY}",
    "{DT_UTC_HOUR}",
    "{DT_UTC_MINUTE}",
    "{DT_UTC_SECOND}",
    "{BASE}",
    "{BASE:RMVSCM}",
    "{BASE:SCM}",
    "{BASE:HOST}",
    "{BASE:PORT}",
    "{BASE:PATH}",
    "{BASE:QUERY}",
    "{BASE:USERINFO}",
    "{BASE:USERNAME}",
    "{BASE:PASSWORD}",
    "{NEWPASSWORD",
    "{HMACOTP}",
    "{TIMEOTP}",
)

#: What KeePass replaces once more after the entry's strings, with the URL compiled again, so that a
#: `{URL}` made by putting one of them next to the text around it is replaced there too.
LATE = (*REFUSED, "{URL}")


class Entries(Protocol):
    """What the resolver reads other entries through, which `GateClient` is."""

    def entry(self, uuid: str) -> Entry:
        """One entry, read, by its identifier."""


def resolve(client: Entries, entry: Entry, value: str) -> str:
    """What KeePass would give for this value of this entry, or a refusal saying why not.

    `entry` is the entry the value came from, read rather than listed, since the placeholders for
    its own strings are answered out of it: a listed one raises `AttributeError`, as asking it for
    a string does. `client` reads every entry a reference names, with `GateClient.entry`.

    A value with nothing in it KeePass could replace comes back as it is, and nothing is asked.
    """
    _fields(entry)
    if not _might_change(value):
        return value

    return _Resolution(client).compile(value, entry, 0)


def _fields(entry: Entry) -> Fields:
    """An entry's strings, which only a read entry carries."""
    if entry.fields is None:
        raise AttributeError(
            "this entry was listed rather than read, so the placeholders for its strings cannot "
            "be answered: read it with GateClient.entry() first"
        )

    return entry.fields


def _might_change(value: str) -> bool:
    """Whether KeePass would compile this at all: a brace before a later one, or two percent signs.

    `SprEngine.MightChange`, which is how KeePass decides not to start.
    """
    opened = value.find("{")
    percent = value.find("%")
    return (0 <= opened < value.rfind("}")) or (0 <= percent < value.rfind("%"))


def _fold(text: str) -> str:
    """Text as KeePass compares it without case, for the letters this can be sure of: ASCII's."""
    return "".join(letter.upper() if letter.isascii() else letter for letter in text)


def _wide(text: str) -> str:
    """Text with every letter's case taken away that has one, beyond ASCII as well.

    Each letter is folded through its upper case where that is one letter, and otherwise through
    its lower case: a Greek letter with an iota subscript has an upper case of two letters, and
    .NET Framework still joins it with its title case form, which shares its lower case. So two
    letters KeePass reads as one always fold to one here, measured over every character .NET
    Framework's `char.ToUpperInvariant` changes; this joins some it keeps apart as well, which only
    ever refuses more. A letter beyond ASCII never becomes one inside it here: KeePass does not
    read an i with no dot as an `I` or a long s as an `S`, which `ReferenceVectors.json` shows, so
    neither may this.
    """
    def folded(letter: str) -> str:
        for cased in (letter.upper(), letter.lower()):
            if len(cased) == 1 and (letter.isascii() or not cased.isascii()):
                return cased

        return letter

    return "".join(folded(letter) for letter in text)


def _present(text: str, placeholder: str) -> bool:
    """Whether the placeholder is in the text, compared without case as KeePass compares.

    For ASCII that is certain. Beyond it, `_wide` finds every spelling a comparison without case
    could take for the placeholder, and one that `_fold` does not also find is a spelling whose
    fate is KeePass's own comparison to decide, which this will not guess at. **Where they are
    found is compared, not how many**: a placeholder that begins with what it ends with can be
    found at different places the same number of times, and KeePass replaces the first it finds.
    """
    folded, wide = _fold(placeholder), _wide(placeholder)
    if _found(_wide(text), wide) != _found(_fold(text), folded):
        raise Unresolved(
            f"{placeholder} is spelled here in a case other than the field's, in letters beyond "
            "ASCII, and whether KeePass reads the two as one name is its own comparison to make"
        )

    return folded in _fold(text)


def _found(text: str, wanted: str) -> list[int]:
    """Where `_replace` would replace, left to right and each past the last: KeePass's order."""
    found: list[int] = []
    position = text.find(wanted)
    while position >= 0:
        found.append(position)
        position = text.find(wanted, position + len(wanted))

    return found


def _replace(text: str, placeholder: str, value: str) -> str:
    """Every occurrence of the placeholder, in any case, replaced: `StrUtil.ReplaceCaseInsensitive`.

    Left to right, and the search goes on after what was put in, so a value is never searched for
    the placeholder it replaced.
    """
    wanted = _fold(placeholder)
    position = 0
    while position < len(text):
        found = _fold(text).find(wanted, position)
        if found < 0:
            break

        text = text[:found] + value + text[found + len(placeholder) :]
        position = found + len(value)

    return text


class _Resolution:
    """One value being resolved: what it has read, and what it is in the middle of.

    KeePass compiles a value inside one context that every value it compiles on the way shares, and
    this is that context. `cache` is KeePass's reference cache, each reference and what it came to,
    which it puts back into every value from then on before it looks for another; it decides how
    many references a value can hold, so it is kept rather than left out. `path` is the fields being
    compiled, outermost first, which is how a circle is seen when KeePass would only run out of
    depth. `read` keeps each entry a reference named, since one resolution is one moment.
    """

    def __init__(self, client: Entries) -> None:
        self._client = client
        self._cache: list[tuple[str, str]] = []
        self._path: list[tuple[str, str]] = []
        self._read: dict[str, Entry] = {}

    def compile(self, text: str, entry: Entry, level: int) -> str:
        """What `SprEngine.CompileInternal` makes of this text, for what this follows."""
        if level >= DEPTH:
            raise Unresolved(
                f"the references and placeholders in this value go {DEPTH} deep, where KeePass "
                "stops following them and answers the deepest as empty, keeping the text around "
                "it, which this will not give as an answer"
            )

        _refuse(text, REFUSED)
        text = self._strings(text, entry, level)
        _refuse(text, LATE, made=True)
        text = self._references(text, level)
        _refuse(text, LATE, made=True)
        return text

    def _strings(self, text: str, entry: Entry, level: int) -> str:
        """The placeholders for the entry's own strings: `FillEntryStrings`.

        In the order KeePass keeps the strings, which is the order the gate lists them in, and then
        the standard ones the entry does not hold, whose placeholders stand for nothing. What one
        stands for is worked out only when it is there, as KeePass does, so a string nothing names
        is never read, and one withheld that nothing names raises nothing.
        """
        fields = _fields(entry)
        names = [*fields, *(name for name in STANDARD if name not in fields)]

        for name in names:
            placeholder = "{" + name + "}" if name in STANDARD else "{S:" + name + "}"
            if _present(text, placeholder):
                text = _replace(text, placeholder, self._compiled(entry, name, placeholder, level))

        return text

    def _compiled(self, entry: Entry, name: str, through: str, level: int) -> str:
        """One of an entry's strings, compiled in its own entry one level down.

        Read the way KeePass reads one, as empty when the entry does not hold it. One the rule
        withholds is not empty, and raises `Withheld` naming what stood for it.

        **Not held is a question KeePass answers with its own comparison** for the five standard
        strings: it reads them out of a dictionary ordered by the current culture's collation,
        under which a name holding a character that collation ignores, a soft hyphen in
        `Pass­word` for one, is the same name as `Password`. A file another program wrote can
        hold one, and the gate answers it under the name it is stored by. So a standard string the
        entry holds under no name spelled exactly so, while it holds a name beyond ASCII, which is
        the only way such a spelling can arise, is refused rather than read as empty: empty could
        stand where KeePass has a value, or where the gate withheld one.
        """
        fields = _fields(entry)
        if name in entry.withheld:
            raise Withheld(name, through=through)

        if name in STANDARD and name not in fields and not all(held.isascii() for held in fields):
            raise Unresolved(
                f"{through} stands for {name!r}, which this entry holds under no name spelled "
                "exactly so, and it holds a name beyond ASCII that KeePass's own comparison may "
                f"read as {name!r}, which this will not guess at"
            )

        held = fields.get(name, "")
        visiting = (entry.uuid.upper(), name)
        if visiting in self._path:
            raise Unresolved(
                f"{through} leads back to {name!r} of the entry {visiting[0]} while that is being "
                "worked out, which KeePass follows until it gives up and answers the deepest as "
                "empty, keeping the text around it"
            )

        self._path.append(visiting)
        try:
            return self.compile(held, entry, level + 1)
        finally:
            self._path.pop()

    def _references(self, text: str, level: int) -> str:
        """The references: `FillRefPlaceholders`, with its cache, its order and its limit."""
        offset = 0
        for _ in range(REFERENCES):
            text = self._cached(text)

            start = _fold(text).find("{REF:", offset)
            if start < 0:
                break

            end = text.find("}", start + 1)
            if end < 0:
                break

            reference = text[start : end + 1]
            followed = self._follow(reference, level)
            if followed is None:
                offset = start + 1
                continue

            # Never one the cache holds already: every one it holds was put back into the text
            # before this was looked for, here and in every value compiled on the way here.
            self._cache.append((reference, followed))
            text = self._cached(text)

        return text

    def _cached(self, text: str) -> str:
        """Every reference followed so far put back, in the order they were: `SprRefCache.Fill`."""
        for reference, followed in self._cache:
            text = _replace(text, reference, followed)

        return text

    def _follow(self, reference: str, level: int) -> str | None:
        """What a reference comes to, or `None` for one KeePass leaves as text: `FindRefTarget`."""
        inner = reference[len("{REF:") : -1]
        if len(inner) <= 4 or inner[1] != "@" or inner[3] != ":":
            return None

        # As `char.ToUpperInvariant` reads them, which leaves every letter beyond ASCII as it is.
        wanted, scan, search = _upper(inner[0]), _upper(inner[2]), inner[4:]
        if scan not in (*_SEARCHES, "I") or wanted not in (*WANTED, "I"):
            return None

        if scan != "I":
            raise Unresolved(
                f"a reference asking for {wanted} searches by {_SEARCHES[scan]}, which KeePass "
                "answers with the first match in the whole database and the gate only out of what "
                "this client may list: refer by identifier, {REF:" + wanted + "@I:<uuid>}, instead"
            )

        if len(search) != 32 or any(digit not in "0123456789abcdefABCDEF" for digit in search):
            raise Unresolved(
                f"a reference asking for {wanted} names no whole identifier, so KeePass would "
                "search every identifier in the database for it and take the first that matches"
            )

        target = self._entry(search, wanted)
        if wanted == "I":
            return self.compile(target.uuid.upper(), target, level + 1)

        through = "{REF:" + wanted + "@I:" + search + "}"
        return self._compiled(target, WANTED[wanted], through, level)

    def _entry(self, uuid: str, wanted: str) -> Entry:
        """The entry a reference names, read once, or a refusal for one this client cannot read."""
        key = uuid.upper()
        if key not in self._read:
            try:
                self._read[key] = self._client.entry(uuid)
            except RouteRefused as refused:
                if refused.status != 404:
                    raise

                raise Unresolved(
                    "{REF:" + wanted + "@I:" + uuid + "} refers to an entry this client cannot "
                    "read: it is not there, it is in the recycle bin, which the API does not "
                    "serve though KeePass follows a reference into it, or the rules do not let "
                    "this client reach it, which the gate answers alike"
                ) from refused

        return self._read[key]


#: What each search letter of a reference searches, as a refusal names it.
_SEARCHES = {
    "T": "title",
    "U": "user name",
    "P": "password",
    "A": "URL",
    "N": "notes",
    "O": "the other strings",
}


def _upper(letter: str) -> str:
    """One letter as `char.ToUpperInvariant` gives it, for the letters a reference can hold."""
    return letter.upper() if letter.isascii() else letter


def _refuse(text: str, refused: tuple[str, ...], made: bool = False) -> None:
    """Raise for the first placeholder in the text KeePass would replace and this does not.

    `made` is for the checks after a replacement, where a placeholder may have been made by one:
    KeePass handles each placeholder at one step of its order, and replaces one made after that
    step no more, so whether it would is a question of where the step falls, and the refusal says
    so rather than claiming KeePass replaces it.
    """
    folded = _fold(text)
    for placeholder in refused:
        if _fold(placeholder) in folded:
            named = f"{placeholder}{'' if placeholder.endswith('}') else '...}'}"
            if made:
                raise Unresolved(
                    f"{named} is a placeholder this client does not follow, and it is here after "
                    "a replacement, which may have made it: whether KeePass would still replace "
                    "it depends on where in its own order that placeholder is handled, so this "
                    "value cannot be given as KeePass would give it"
                )

            raise Unresolved(
                f"{named} is a placeholder KeePass replaces and this client does not, so this "
                "value cannot be given as KeePass would give it"
            )
