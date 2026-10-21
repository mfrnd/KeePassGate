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
from pykeepassgate.errors import GateBusy, RouteRefused, Unresolved, Withheld
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


def answer(uuid: str, fields: dict[str, str | None]) -> dict[str, Any]:
    """What the gate answers a read of an entry with, `null` for a value it withholds."""
    return {
        "uuid": uuid,
        "group": GROUP,
        "fields": fields,
        "protected": [name for name, value in fields.items() if value is None],
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

        return Entry.read(answer(found["uuid"], dict(found["fields"])))


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
        with pytest.raises(Unresolved):
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
        ("}\u00fc{S:", "{S:}\u00dc{S:}\u00fc{S:}"),
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

    with pytest.raises(Unresolved, match="its own comparison"):
        refs.resolve(Database([]), entry, value)


@pytest.mark.parametrize("withheld", [False, True], ids=["read", "withheld"])
@pytest.mark.parametrize(
    "value",
    ["{PASSWORD}", "{REF:P@I:A2000000000000000000000000000002}"],
    ids=["a placeholder", "a reference"],
)
def test_a_standard_string_held_under_a_look_alike_name_is_refused_rather_than_empty(
    value: str, withheld: bool
) -> None:
    # KeePass reads `Password` out of a dictionary ordered by the culture's collation, which gives
    # the soft hyphen no weight, so to it this entry holds a password. Empty would stand where
    # KeePass has a value, and where the gate withholds one, where a value never may.
    look_alike = "Pass" + chr(0xAD) + "word"
    held = {"Title": "Invented Look-alike", look_alike: None if withheld else "an invented value"}
    entry = Entry.read(answer("A2000000000000000000000000000002", held))
    database = Database([{"uuid": "A2000000000000000000000000000002", "fields": held}])

    with pytest.raises(Unresolved, match="beyond ASCII"):
        refs.resolve(database, entry, value)


def test_a_standard_string_an_entry_of_ascii_names_does_not_hold_is_empty() -> None:
    # The other side of the same rule: with no name beyond ASCII there is no look-alike, and KeePass
    # reads a standard string nobody stored as empty, as `ReferenceVectors.json` shows.
    entry = Entry.read(answer(SERVICE, {"Title": "Invented Service"}))

    assert refs.resolve(Database([]), entry, "[{PASSWORD}]") == "[]"


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
        refs.resolve(client, entry, "{REF:P@I:" + SERVICE + "}")
