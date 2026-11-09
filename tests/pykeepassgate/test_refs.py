"""The resolver, held to what KeePass's own compiler makes of the same values.

`tests/Shared/ReferenceVectors.json` was made by `SprEngine`, over the entries written down in it,
by `ReferenceVectorsTests` in the host tier, which holds KeePass to the same file. This holds
`pykeepassgate.refs` to it: what the resolver follows has to come to what KeePass makes, what it
refuses has to raise, and what it keeps has to stay as it was. A file regenerated from a KeePass
that compiles differently fails here until the resolver agrees with it again.

The rest is what a gate decides and KeePass does not: a value withheld, an entry this client cannot
read, and a refusal that has to name what stopped it without repeating the value it was in.
"""

from __future__ import annotations

import json
import re
from pathlib import Path
from typing import Any

import pytest

from pykeepassgate import refs
from pykeepassgate.client import GateClient
from pykeepassgate.errors import AmbiguousName, GateBusy, RouteRefused, Unresolved, Withheld
from pykeepassgate.model import Entry
from pykeepassgate.testing import FakeGate
from test_reference import REPOSITORY

VECTORS = REPOSITORY / "tests" / "Shared" / "ReferenceVectors.json"

#: The group every entry in the vectors is answered as being in, which the resolver never reads.
GROUP = "C1" * 16


def _vectors() -> dict[str, Any]:
    """What KeePass made, or nothing where these tests are not in the repository."""
    if not VECTORS.exists():  # pragma: no cover, only outside the repository
        return {"entries": [], "cases": []}

    parsed: dict[str, Any] = json.loads(VECTORS.read_text("utf-8"))
    return parsed


DOCUMENT = _vectors()


def answer(
    uuid: str, fields: dict[str, str | None], protected: tuple[str, ...] = ()
) -> dict[str, Any]:
    """What the gate answers a read of an entry with, `null` for a value it withholds.

    Every value withheld is protected, and so is every name in `protected`, readable here.
    """
    withheld = [name for name, value in fields.items() if value is None]
    return {
        "uuid": uuid,
        "group": GROUP,
        "fields": fields,
        "protected": [*withheld, *(name for name in protected if name not in withheld)],
        "expires": None,
    }


class Database:
    """Entries answered as the gate answers a read of each, and a record of what was asked."""

    def __init__(self, entries: list[dict[str, Any]]) -> None:
        self._entries = {entry["uuid"].upper(): entry for entry in entries}
        self.asked: list[str] = []

    def entry(self, uuid: str) -> Entry:
        """One entry by identifier in either case, or the `404` the gate gives anything else."""
        self.asked.append(uuid)
        found = self._entries.get(uuid.upper())
        if found is None:
            raise RouteRefused(404, "no such route")

        return Entry.read(
            answer(found["uuid"], dict(found["fields"]), tuple(found.get("protected", ())))
        )


def test_the_vectors_are_where_the_host_test_writes_them() -> None:
    # Inside the repository a file that is not there fails rather than skipping every case below:
    # the host tier writes it by path, and a row of green nothing would hold the resolver to
    # nothing at all.
    if not (REPOSITORY / "pyproject.toml").exists():  # pragma: no cover, only outside it
        pytest.skip("no repository, so no vectors")

    assert VECTORS.exists()
    pinned = (REPOSITORY / "keepass.version").read_text("utf-8").strip()
    assert DOCUMENT["keepass"] == pinned, "the KeePass `keepass.version` pins"
    assert {case["resolver"] for case in DOCUMENT["cases"]} == {"follows", "refuses", "keeps"}


@pytest.mark.parametrize(
    "case", DOCUMENT["cases"], ids=[case["name"] for case in DOCUMENT["cases"]]
)
def test_the_resolver_gives_what_keepass_gives_or_refuses(case: dict[str, Any]) -> None:
    database = Database(DOCUMENT["entries"])
    entry = database.entry(case["entry"])

    if case["resolver"] == "refuses":
        # Either refusal: which of the two each case is, the tests below pin by name.
        with pytest.raises((Unresolved, AmbiguousName)):
            refs.resolve(database, entry, case["value"])
    elif case["resolver"] == "keeps":
        assert case["keepass"] != case["value"], "KeePass changes it, which is why it is said"
        assert refs.resolve(database, entry, case["value"]) == case["value"]
    else:
        assert refs.resolve(database, entry, case["value"]) == case["keepass"]


@pytest.mark.parametrize(
    ("name", "value"),
    [
        ("\u1f80", "{S:\u1f88}"),
        ("\u1fb3", "{S:\u1fbc}"),
        ("}ü{S:", "{S:}Ü{S:}ü{S:}"),
    ],
    ids=[
        "an iota subscript small letter spelled title case",
        "an alpha with an iota subscript spelled title case",
        "a placeholder found where KeePass would not replace it",
    ],
)
def test_a_spelling_only_keepass_can_compare_is_refused(name: str, value: str) -> None:
    # KeePass replaces each of these: .NET Framework reads a Greek small letter with an iota
    # subscript and its title case form as one, though Python's upper case of the first is two
    # letters, and in the third it replaces the first occurrence, where the Ü is, while ASCII
    # alone finds only the second. Neither can be answered without KeePass's own comparison.
    entry = Entry.read(answer(SERVICE, {"Title": "Invented Service", name: "an invented value"}))

    with pytest.raises(AmbiguousName, match="its own comparison"):
        refs.resolve(Database([]), entry, value)


#: What the culture's collation gives no weight to, so that a name holding such a character is the
#: name without it: a soft hyphen beyond ASCII, and the null, the only such character within it.
IGNORED = pytest.mark.parametrize(
    "ignored", [chr(0xAD), "\0"], ids=["a soft hyphen", "a null"]
)


@IGNORED
@pytest.mark.parametrize("withheld", [False, True], ids=["read", "withheld"])
@pytest.mark.parametrize(
    "value",
    ["{PASSWORD}", "{REF:P@I:A2000000000000000000000000000002}"],
    ids=["a placeholder", "a reference"],
)
def test_a_standard_string_held_under_a_look_alike_name_is_refused_rather_than_empty(
    value: str, withheld: bool, ignored: str
) -> None:
    # KeePass reads `Password` out of a dictionary ordered by the culture's collation, which gives
    # the soft hyphen no weight, nor the null, so to it this entry holds a password. Empty would
    # stand where KeePass has a value, and where the gate withholds one, where a value never may.
    # The null is ASCII, so a refusal asking only for a name beyond ASCII let it through.
    look_alike = "Pass" + ignored + "word"
    held = {"Title": "Invented Look-alike", look_alike: None if withheld else "an invented value"}
    entry = Entry.read(answer("A2000000000000000000000000000002", held))
    database = Database([{"uuid": "A2000000000000000000000000000002", "fields": held}])

    with pytest.raises(AmbiguousName, match="beyond ASCII or holding a null"):
        refs.resolve(database, entry, value)


def test_a_standard_string_an_entry_of_ascii_names_does_not_hold_is_empty() -> None:
    # The other side of the same rule: with no name beyond ASCII there is no look-alike, and KeePass
    # reads a standard string nobody stored as empty, as `ReferenceVectors.json` shows.
    entry = Entry.read(answer(SERVICE, {"Title": "Invented Service"}))

    assert refs.resolve(Database([]), entry, "[{PASSWORD}]") == "[]"


@IGNORED
@pytest.mark.parametrize("withheld", [False, True], ids=["read", "withheld"])
@pytest.mark.parametrize("standard", ["title", "username", "password", "url", "notes"])
def test_the_read_model_refuses_a_standard_string_held_under_a_look_alike_name(
    withheld: bool, standard: str, ignored: str
) -> None:
    # The resolver's rule on the read model. `entry.password` answered "" for this entry, where
    # KeePass shows a value, and where the gate withheld a value, which this client never reads as
    # empty; the soft hyphen is invisible, which is the whole of the trap.
    names = {"title": "Title", "username": "UserName", "password": "Password", "url": "URL"}
    name = names.get(standard, "Notes")
    held: dict[str, str | None] = {} if standard == "title" else {"Title": "Invented Look-alike"}
    held[name[:2] + ignored + name[2:]] = None if withheld else "an invented value"
    entry = Entry.read(answer("A2000000000000000000000000000002", held))

    with pytest.raises(AmbiguousName, match="beyond ASCII or holding a null"):
        getattr(entry, standard)


def test_the_read_model_reads_a_standard_string_ascii_names_leave_out_as_empty() -> None:
    entry = Entry.read(answer(SERVICE, {"Title": "Invented Service"}))

    assert (entry.username, entry.password, entry.url, entry.notes) == ("", "", "", "")


def _case(name: str) -> dict[str, Any]:
    """One of the vectors, by the name it is written under."""
    found: dict[str, Any] = next(case for case in DOCUMENT["cases"] if case["name"] == name)
    return found


@pytest.mark.parametrize(
    "name", ["part of an identifier", "an identifier after a space", "an identifier negated"]
)
def test_a_reference_by_less_than_a_whole_identifier_is_refused_before_anything_is_read(
    name: str,
) -> None:
    # Refused for what it is, a search KeePass answers from the whole database, and not because a
    # read the gate cannot serve came back `404`, which reads as an entry this client cannot read.
    case = _case(name)
    database = Database(DOCUMENT["entries"])
    entry = database.entry(case["entry"])

    with pytest.raises(Unresolved, match="names no whole identifier"):
        refs.resolve(database, entry, case["value"])

    assert database.asked == [case["entry"]], "nothing was read but the entry the value is in"


@pytest.mark.parametrize("name", ["a cycle of references", "a field naming itself"])
def test_a_circle_is_refused_as_a_circle_rather_than_by_running_out_of_depth(name: str) -> None:
    case = _case(name)
    database = Database(DOCUMENT["entries"])

    with pytest.raises(Unresolved, match="leads back to"):
        refs.resolve(database, database.entry(case["entry"]), case["value"])


SERVICE = "A1000000000000000000000000000001"
VAULT = "A2000000000000000000000000000001"
#: An entry nothing here holds, which the gate answers `404` alike for an entry absent or refused.
ABSENT = "A3000000000000000000000000000001"


def test_a_withheld_value_a_placeholder_stands_for_raises_withheld() -> None:
    # Never the empty string, and never the placeholder left as it was: either would be a value.
    # Named as KeePass spells the placeholder, which is the field's own name in braces.
    entry = Entry.read(answer(SERVICE, {"Title": "Invented Service", "Password": None}))

    with pytest.raises(Withheld, match=r"'Password', which \{Password\} stands for") as raised:
        refs.resolve(Database([]), entry, "pw={PASSWORD}")

    assert raised.value.name == "Password"


def test_a_withheld_value_nothing_names_is_not_read() -> None:
    # As KeePass works out a placeholder only when it is there.
    entry = Entry.read(answer(SERVICE, {"Title": "Invented Service", "Password": None}))

    assert refs.resolve(Database([]), entry, "{TITLE}") == "Invented Service"


def test_a_withheld_value_a_reference_stands_for_raises_withheld_naming_it() -> None:
    reference = "{REF:P@I:" + VAULT + "}"
    database = Database([{"uuid": VAULT, "fields": {"Title": "Invented Vault", "Password": None}}])
    entry = Entry.read(answer(SERVICE, {"Password": reference}))

    with pytest.raises(Withheld, match=f"which {re.escape(reference)} stands for"):
        refs.resolve(database, entry, "{PASSWORD}")


def test_a_reference_to_an_entry_this_client_cannot_read_names_the_reference() -> None:
    # Absent and refused are one answer from the gate, so neither is left as text: KeePass would
    # have resolved the one that is there.
    reference = "{REF:U@I:" + VAULT + "}"
    entry = Entry.read(answer(SERVICE, {"Title": "Invented Service"}))

    with pytest.raises(Unresolved, match=VAULT) as raised:
        refs.resolve(Database([]), entry, "user=" + reference)

    assert isinstance(raised.value.__cause__, RouteRefused)
    assert raised.value.__cause__.status == 404


def test_any_other_refusal_on_the_way_is_the_one_the_gate_gave() -> None:
    # Not the reference's fault: whatever a read would raise anywhere, it raises here.
    class Busy:
        def entry(self, uuid: str) -> Entry:
            raise GateBusy("the gate cannot serve this right now")

    entry = Entry.read(answer(SERVICE, {"Title": "Invented Service"}))

    with pytest.raises(GateBusy):
        refs.resolve(Busy(), entry, "{REF:U@I:" + VAULT + "}")


def test_another_refusal_status_is_raised_as_it_came() -> None:
    class Teapot:
        def entry(self, uuid: str) -> Entry:
            raise RouteRefused(418, "teapot")

    entry = Entry.read(answer(SERVICE, {"Title": "Invented Service"}))

    with pytest.raises(RouteRefused) as raised:
        refs.resolve(Teapot(), entry, "{REF:U@I:" + VAULT + "}")

    assert raised.value.status == 418


def test_a_listed_entry_cannot_answer_for_its_strings() -> None:
    # Absent rather than empty, as asking a listed entry for its password is.
    listed = Entry.listed({"uuid": SERVICE, "group": GROUP, "title": "x", "url": ""})

    with pytest.raises(AttributeError, match="listed rather than read"):
        refs.resolve(Database([]), listed, "{TITLE}")


@pytest.mark.parametrize("placeholder", refs.REFUSED)
def test_every_placeholder_keepass_replaces_and_this_does_not_is_refused(placeholder: str) -> None:
    # The ones that act, a command run, a password generated, a dialog shown, the clipboard set,
    # are among them, and are the ones the host tier cannot ask KeePass about without acting.
    value = placeholder if placeholder.endswith("}") else placeholder + "/x/}"
    entry = Entry.read(answer(SERVICE, {"Title": "Invented Service"}))

    with pytest.raises(Unresolved, match="KeePass replaces and this client does not"):
        refs.resolve(Database([]), entry, "a" + value.lower() + "b")


def test_a_placeholder_a_replacement_made_is_refused_for_what_it_is() -> None:
    # KeePass handles `{C:` before the entry's strings, so one an earlier replacement made is left
    # as text there: refused all the same, but not by saying KeePass replaces it.
    entry = Entry.read(answer(SERVICE, {"Title": "Invented Service", "Brace": "{"}))

    with pytest.raises(Unresolved, match="after a replacement, which may have made it"):
        refs.resolve(Database([]), entry, "{S:Brace}C:an invented comment}")


def test_a_placeholder_the_strings_made_is_refused_before_any_reference_is_followed() -> None:
    # The check after the strings comes before the references are followed: without it the check
    # after them still refuses the value, but only once each entry named has been read through the
    # gate, an audit line each, and a withheld value there raised `Withheld` in its place.
    lending = {"uuid": VAULT, "fields": {"Title": "Invented Vault", "Password": None}}
    database = Database([lending])
    entry = Entry.read(answer(SERVICE, {"Title": "UID}"}))

    with pytest.raises(Unresolved, match="after a replacement"):
        refs.resolve(database, entry, "{U{TITLE} {REF:P@I:" + VAULT + "}")

    assert database.asked == [], "no other entry was read"


@pytest.mark.parametrize("placeholder", ["BASE}", "TIMEOTP}"])  # cspell:ignore TIMEOTP
def test_a_placeholder_a_reference_made_is_refused_for_what_it_is(placeholder: str) -> None:
    # The check after the references, which nothing else reaches: an entry titled `{` makes a
    # placeholder KeePass handles after following references, and replaces with '' there, as its
    # own compiler answered on the review's measurement, where this would hand back the text.
    database = Database([{"uuid": VAULT, "fields": {"Title": "{"}}])
    entry = Entry.read(answer(SERVICE, {"Title": "Invented Service"}))

    with pytest.raises(Unresolved, match="after a replacement"):
        refs.resolve(database, entry, "{REF:T@I:" + VAULT + "}" + placeholder)


# KeePass's order, measured with its own compiler on the review: each was removable with every test
# green, and its mutant refused or misread what KeePass follows. `ReferenceVectors.json` holds the
# same shapes, which the host tier holds to KeePass on every run; these keep each choice beside a
# sentence on why the resolver makes it.


def test_a_malformed_reference_is_skipped_by_one_character_so_one_inside_it_is_followed() -> None:
    database = Database([{"uuid": SERVICE, "fields": {"Title": "Invented Service"}}])
    entry = Entry.read(answer(VAULT, {"Title": "Invented Vault"}))

    value = "{REF:Z@I:{REF:T@I:" + SERVICE + "}"
    assert refs.resolve(database, entry, value) == "{REF:Z@I:Invented Service"


def test_a_reference_followed_leaves_the_search_where_it_was() -> None:
    # Ten skipped references count against KeePass's twenty, and following a reference does not
    # start the search again, so the two after them are still reached.
    other = "A3000000000000000000000000000001"
    database = Database(
        [
            {"uuid": SERVICE, "fields": {"Title": "Invented Service"}},
            {"uuid": other, "fields": {"Title": "Other"}},
        ]
    )
    entry = Entry.read(answer(VAULT, {"Title": "Invented Vault"}))
    skipped = "{REF:Z@I:" + SERVICE + "}"

    value = skipped * 10 + "{REF:T@I:" + SERVICE + "}{REF:T@I:" + other + "}"
    assert refs.resolve(database, entry, value) == skipped * 10 + "Invented ServiceOther"


def test_a_value_keepass_would_not_compile_is_handed_back_without_a_look() -> None:
    # `SprEngine.MightChange`: no brace before a closing brace, so `{CMD:` is never seen.
    entry = Entry.read(answer(SERVICE, {"Title": "Invented Service"}))

    assert refs.resolve(Database([]), entry, "a{CMD:b") == "a{CMD:b"


def test_the_standard_names_an_entry_does_not_hold_come_after_its_own() -> None:
    # As KeePass appends them: `{S:Brace}` makes `{URL}` before the URL is looked for, and the
    # entry holds none, so it is empty, where looking for it first left a `{URL}` behind.
    entry = Entry.read(answer(SERVICE, {"Title": "Invented Service", "Brace": "{"}))

    assert refs.resolve(Database([]), entry, "{S:Brace}URL}") == ""


def test_a_string_met_on_two_branches_is_no_circle() -> None:
    # The path is popped after each string, so a title both the user name and the password name
    # is compiled twice rather than read as leading back to itself.
    entry = Entry.read(
        answer(SERVICE, {"Title": "T", "UserName": "{TITLE}u", "Password": "{TITLE}p"})
    )

    assert refs.resolve(Database([]), entry, "{USERNAME}{PASSWORD}") == "TuTp"


def test_a_value_with_nothing_to_replace_asks_nothing() -> None:
    database = Database([])
    entry = Entry.read(answer(SERVICE, {"Title": "Invented Service"}))

    assert refs.resolve(database, entry, "no braces here") == "no braces here"
    assert refs.resolve(database, entry, "} before {") == "} before {"
    assert database.asked == []


def test_each_entry_a_value_names_is_read_once() -> None:
    database = Database([{"uuid": VAULT, "fields": {"Title": "Invented Vault", "UserName": "u"}}])
    entry = Entry.read(answer(SERVICE, {"Title": "Invented Service"}))
    named = "{REF:T@I:" + VAULT + "} {REF:U@I:" + VAULT + "} {REF:I@I:" + VAULT.lower() + "}"

    assert refs.resolve(database, entry, named) == f"Invented Vault u {VAULT}"
    assert database.asked == [VAULT]


def layered(width: int, layers: int) -> dict[str, str | None]:
    """Strings in layers, each naming every string of the layer below, the last holding a letter.

    Acyclic and inside `refs.DEPTH`, so neither check stops it, and compiled once for every path
    through it, which is `width` to the power of `layers`.
    """

    def named(layer: int, index: int) -> str:
        return f"L{layer}_{index}"

    strings: dict[str, str | None] = {named(layers - 1, index): "x" for index in range(width)}
    for layer in range(layers - 1):
        below = "".join("{S:" + named(layer + 1, index) + "}" for index in range(width))
        strings |= {named(layer, index): below for index in range(width)}

    return strings


def test_strings_that_name_each_other_in_wide_layers_are_refused_rather_than_waited_on() -> None:
    # A few hundred characters any client that may change the entry can write: 34 such strings
    # took 37 seconds, and a few more never finished.
    entry = Entry.read(answer(SERVICE, layered(width=3, layers=9)))

    with pytest.raises(Unresolved, match=f"more than {refs.COMPILES} compilations"):
        refs.resolve(Database([]), entry, "{S:L0_0}")


def test_strings_that_name_each_other_narrowly_are_followed_as_before() -> None:
    entry = Entry.read(answer(SERVICE, layered(width=2, layers=6)))

    assert refs.resolve(Database([]), entry, "{S:L0_0}") == "x" * 2**5


def test_a_value_that_comes_to_more_than_a_client_holds_is_refused() -> None:
    # Few compilations, many characters: a long string named many times over.
    entry = Entry.read(answer(SERVICE, {"Notes": "n" * 300_000}))

    with pytest.raises(Unresolved, match=f"more than {refs.CHARACTERS} characters"):
        refs.resolve(Database([]), entry, "{NOTES}" * 20)

    assert refs.resolve(Database([]), entry, "{NOTES}" * 2) == "n" * 600_000

    # Each value inside the limit, and what they come to together over it.
    entry = Entry.read(answer(SERVICE, {"Notes": "n" * 300_000, "Big": "{NOTES}" * 10}))

    with pytest.raises(Unresolved, match=f"more than {refs.CHARACTERS} characters"):
        refs.resolve(Database([]), entry, "{S:Big}{NOTES}")


def test_a_reference_to_the_entry_a_value_came_from_is_answered_out_of_it() -> None:
    # One resolution is one moment: read again through the gate, the entry could answer as a
    # newer version than the version its own placeholders were answered out of.
    database = Database([{"uuid": SERVICE, "fields": {"Title": "newer title"}}])
    entry = Entry.read(answer(SERVICE, {"Title": "older title"}))

    resolved = refs.resolve(database, entry, "{TITLE} / {REF:T@I:" + SERVICE.lower() + "}")

    assert resolved == "older title / older title"
    assert database.asked == []


def test_an_entry_s_strings_are_resolved_together_reading_each_entry_once() -> None:
    database = Database([{"uuid": VAULT, "fields": {"URL": "vault.example", "Notes": "n"}}])
    fields: dict[str, str | None] = {
        "Title": "Invented Service",
        "URL": "{REF:A@I:" + VAULT + "}",
        "Notes": "{REF:N@I:" + VAULT + "} of {REF:T@I:" + SERVICE + "}",
        "Password": None,
    }
    entry = Entry.read(answer(SERVICE, fields))

    resolved = refs.resolve_entry(database, entry)

    assert {name: found.value for name, found in resolved.items()} == {
        "Title": "Invented Service",
        "URL": "vault.example",
        "Notes": "n of Invented Service",
    }, "a withheld string is left out, as `withheld` names it"
    assert database.asked == [VAULT], "the other entry once, and this entry not again"


def test_a_string_a_protected_value_goes_into_is_protected() -> None:
    # A password put into a URL is a password still, by a reference or by a placeholder of the
    # entry's own, and a string the database protects is protected whatever it names.
    database = Database(
        [{"uuid": VAULT, "fields": {"Password": "not-a-real-one"}, "protected": ("Password",)}]
    )
    fields: dict[str, str | None] = {
        "Title": "Invented Service",
        "UserName": "{TITLE}",
        "URL": "https://u:{REF:P@I:" + VAULT + "}@host.example",
        "Notes": "the password is {PASSWORD}",
        "Password": "plain",
    }
    entry = Entry.read(answer(SERVICE, fields, protected=("Password",)))

    resolved = refs.resolve_entry(database, entry)

    assert {name: found.protected for name, found in resolved.items()} == {
        "Title": False,
        "UserName": False,
        "URL": True,
        "Notes": True,
        "Password": True,
    }
    assert resolved["URL"].value == "https://u:not-a-real-one@host.example"


def test_a_search_is_refused_without_repeating_what_it_searched_for() -> None:
    # The search text is part of the value, and a refusal is what gets logged.
    entry = Entry.read(answer(SERVICE, {"Title": "Invented Service"}))

    with pytest.raises(Unresolved) as raised:
        refs.resolve(Database([]), entry, "{REF:P@T:not-a-real-secret-title}")

    assert "title" in str(raised.value)
    assert "not-a-real-secret-title" not in str(raised.value)


def test_an_environment_variable_is_left_for_the_machine_keepass_runs_on() -> None:
    entry = Entry.read(answer(SERVICE, {"Title": "Invented Service"}))

    assert refs.resolve(Database([]), entry, "%PATH% {TITLE}") == "%PATH% Invented Service"


def test_a_reference_is_followed_through_the_gate(
    tmp_path: Path, bundle: dict[str, Any], gate: FakeGate
) -> None:
    # The real client and a real connection: each entry a reference names is a read of its own,
    # which the rules decide and the audit records like any other.
    from pykeepassgate import credentials as credentials_module

    client = GateClient(credentials_module.save(tmp_path / "kept", gate.port, bundle))
    read = {VAULT: answer(VAULT, {"Title": "Invented Vault", "Password": "not-a-real-one"})}

    def handler(path: str, body: str, presented: bytes | None) -> tuple[int, str]:
        uuid = path.rsplit("/", 1)[-1].upper()
        if uuid in read:
            return 200, json.dumps(read[uuid])

        return 404, json.dumps({"error": "no such route"})

    gate.handler = handler
    entry = Entry.read(answer(SERVICE, {"Password": "{REF:P@I:" + VAULT.lower() + "}"}))

    assert refs.resolve(client, entry, "{PASSWORD}") == "not-a-real-one"
    assert gate.requests == [("GET", f"/entries/{VAULT.lower()}")]

    with pytest.raises(Unresolved, match="cannot read"):
        refs.resolve(client, entry, "{REF:P@I:" + ABSENT + "}")
