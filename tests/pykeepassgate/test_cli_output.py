"""What `-o`/`--output` chooses, held to the rules the plain lines already keep.

`plain` is what every command printed before there was a choice, and the tests beside this one
still hold it to that. What is here is `json`: one document a program can read, saying what the
plain lines say, under the same two rules. A value the rule withholds is named as withheld and never
printed as empty, and what the gate did not say is left out rather than printed as nothing.
"""

from __future__ import annotations

import argparse
import base64
import json
from pathlib import Path
from typing import Any

import pytest

from pykeepassgate import cli
from pykeepassgate import credentials as credentials_module
from pykeepassgate.testing import FakeGate

UUID = "7b1c" * 8
GROUP = "40ac" * 8


@pytest.fixture
def kept(tmp_path: Path, bundle: dict[str, Any], gate: FakeGate) -> Path:
    """A paired identity on disk, pointed at this gate."""
    credentials_module.save(tmp_path / "kept", gate.port, bundle)
    return tmp_path / "kept"


def answering(gate: FakeGate, body: dict[str, Any]) -> None:
    """Answer every request with this."""
    gate.handler = lambda path, sent, client: (200, json.dumps(body))


def printed(capsys: pytest.CaptureFixture[str]) -> Any:
    """What a command printed, read as the one JSON document it has to be."""
    return json.loads(capsys.readouterr().out)


def commands() -> dict[str, argparse.ArgumentParser]:
    """Every command the parser has, by name."""
    parser = cli._parser()
    chosen = next(
        action for action in parser._actions if isinstance(action, argparse._SubParsersAction)
    )
    found: dict[str, argparse.ArgumentParser] = chosen.choices
    return found


def test_every_command_prints_plain_unless_asked_for_another_format() -> None:
    for name, command in commands().items():
        output = next(
            (action for action in command._actions if action.dest == "output"), None
        )

        assert output is not None, f"{name} offers no -o/--output"
        assert output.option_strings == ["-o", "--output"], name
        assert output.default == "plain", name
        assert tuple(output.choices or ()) == cli.OUTPUTS, name


def test_a_format_nobody_offers_is_refused_as_an_argument(kept: Path, gate: FakeGate) -> None:
    with pytest.raises(SystemExit) as raised:
        cli.main(["groups", "--directory", str(kept), "-o", "yaml"])

    assert raised.value.code == 2
    assert gate.requests == []


def test_a_listing_is_one_array_of_what_the_gate_said(
    gate: FakeGate, kept: Path, capsys: pytest.CaptureFixture[str]
) -> None:
    answering(
        gate,
        {
            "groups": [
                {"uuid": GROUP, "name": "Root", "parent": None, "created": "2026-01-02T03:04:05Z"},
                {"uuid": UUID, "name": "Line\nbreak", "parent": GROUP},
            ]
        },
    )

    assert cli.main(["groups", "--directory", str(kept), "--output", "json"]) == 0

    # The name as it is, which the plain lines escape, and no `created` where none was said.
    assert printed(capsys) == [
        {"uuid": GROUP, "name": "Root", "parent": None, "created": "2026-01-02T03:04:05Z"},
        {"uuid": UUID, "name": "Line\nbreak", "parent": GROUP},
    ]


def test_a_listed_entry_names_a_withheld_title_rather_than_printing_it_empty(
    gate: FakeGate, kept: Path, capsys: pytest.CaptureFixture[str]
) -> None:
    answering(
        gate,
        {
            "entries": [
                {"uuid": UUID, "group": GROUP, "title": None, "url": "https://invented.invalid"},
            ]
        },
    )

    assert cli.main(["entries", "--directory", str(kept), "-o", "json"]) == 0

    assert printed(capsys) == [
        {"uuid": UUID, "group": GROUP, "url": "https://invented.invalid", "withheld": ["Title"]}
    ]


def test_show_prints_the_values_it_may_and_names_the_rest(
    gate: FakeGate, kept: Path, capsys: pytest.CaptureFixture[str]
) -> None:
    answering(
        gate,
        {
            "uuid": UUID,
            "group": GROUP,
            "fields": {
                "Title": "Invented Service",
                "Notes": "first\nsecond",
                "Password": None,
            },
            "protected": ["Password"],
            "expires": None,
        },
    )

    assert cli.main(["show", UUID, "--directory", str(kept), "-o", "json"]) == 0

    assert printed(capsys) == {
        "uuid": UUID,
        "group": GROUP,
        "expires": None,
        "expired": False,
        "fields": {"Notes": "first\nsecond", "Title": "Invented Service"},
        "protected": ["Password"],
        "withheld": ["Password"],
    }


def test_show_says_nothing_about_an_expiry_the_gate_said_nothing_about(
    gate: FakeGate, kept: Path, capsys: pytest.CaptureFixture[str]
) -> None:
    answering(
        gate,
        {"uuid": UUID, "group": GROUP, "fields": {"Title": "Invented"}, "protected": []},
    )

    assert cli.main(["show", UUID, "--directory", str(kept), "-o", "json"]) == 0

    shown = printed(capsys)
    assert "expires" not in shown
    assert "expired" not in shown


def test_show_prints_an_expiry_as_the_gate_writes_one(
    gate: FakeGate, kept: Path, capsys: pytest.CaptureFixture[str]
) -> None:
    answering(
        gate,
        {
            "uuid": UUID,
            "group": GROUP,
            "fields": {},
            "protected": [],
            "expires": "2001-02-03T04:05:06Z",
        },
    )

    assert cli.main(["show", UUID, "--directory", str(kept), "-o", "json"]) == 0

    shown = printed(capsys)
    assert shown["expires"] == "2001-02-03T04:05:06Z"
    assert shown["expired"] is True


def test_whoami_answers_under_the_names_the_route_documents(
    gate: FakeGate, kept: Path, capsys: pytest.CaptureFixture[str]
) -> None:
    body = {
        "subject": "CN=agent-invented",
        "clientKey": "3a" * 32,
        "admissionKey": "5f" * 32,
        "expires": "2027-02-03T04:05:06Z",
        "profiles": ["reader"],
        "removedProfiles": ["writer"],
    }
    answering(gate, body)

    assert cli.main(["whoami", "--directory", str(kept), "-o", "json"]) == 0

    assert printed(capsys) == body


def test_access_prints_the_grants_by_profile(
    gate: FakeGate, kept: Path, capsys: pytest.CaptureFixture[str]
) -> None:
    answering(gate, {"grants": {"reader": ".R...L.P"}})

    assert cli.main(["access", "--group", "root", "--directory", str(kept), "-o", "json"]) == 0

    assert printed(capsys) == {"grants": {"reader": ".R...L.P"}}


def test_status_prints_what_the_gate_said_and_keeps_its_exit_code(
    gate: FakeGate, kept: Path, capsys: pytest.CaptureFixture[str]
) -> None:
    body = {"ok": False, "database": "abcdef01", "version": "0.1.0", "revision": "0123abcd"}
    answering(gate, body)

    assert cli.main(["status", "--directory", str(kept), "-o", "json"]) == 1

    assert printed(capsys) == body


def test_identity_says_what_is_stored_and_nothing_it_holds(
    kept: Path, bundle: dict[str, Any], capsys: pytest.CaptureFixture[str]
) -> None:
    assert cli.main(["identity", "--directory", str(kept), "-o", "json"]) == 0

    shown = printed(capsys)
    assert shown["subject"] == "CN=agent-invented"
    assert shown["profiles"] == ["reader"]
    assert shown["expires"] == bundle["expires"]
    assert shown["keyEncrypted"] is False
    assert shown["directory"] == str(kept)
    assert set(shown) == {
        "subject",
        "clientKey",
        "admissionKey",
        "database",
        "profiles",
        "expires",
        "expired",
        "keyEncrypted",
        "port",
        "directory",
    }


def test_identity_leaves_an_expiry_it_cannot_read_to_the_gate(
    kept: Path, capsys: pytest.CaptureFixture[str]
) -> None:
    stored = kept / "client.json"
    contents = json.loads(stored.read_text("utf-8"))
    stored.write_text(json.dumps(contents | {"expires": "whenever"}), "utf-8")

    assert cli.main(["identity", "--directory", str(kept), "-o", "json"]) == 0

    assert printed(capsys)["expired"] is None


def test_pairing_says_what_was_approved_and_where_it_was_stored(
    tmp_path: Path, gate: FakeGate, bundle: dict[str, Any], capsys: pytest.CaptureFixture[str]
) -> None:
    from test_pairing import INVITATION, Gate

    gate.handler = Gate(gate, gate.host_certificate, bundle=bundle)
    where = tmp_path / "kept"

    code = cli.main(
        [
            "pair",
            "--port",
            str(gate.port),
            "--name",
            "agent-invented",
            "--profile-name",
            "reader",
            "--invitation",
            INVITATION,
            "--directory",
            str(where),
            "-o",
            "json",
        ]
    )

    assert code == 0
    shown = printed(capsys)
    assert shown["subject"] == "CN=agent-invented"
    assert shown["profiles"] == ["reader"]
    assert shown["expires"] == bundle["expires"]
    assert shown["directory"] == str(where)
    assert INVITATION not in json.dumps(shown)


def test_revoke_says_what_it_gave_up(
    gate: FakeGate, kept: Path, capsys: pytest.CaptureFixture[str]
) -> None:
    answering(gate, {"ok": True})

    assert cli.main(["revoke", "--directory", str(kept), "-o", "json"]) == 0

    shown = printed(capsys)
    assert shown["subject"] == "CN=agent-invented"
    assert shown["alreadyRefused"] is False
    assert shown["removed"] == str(kept)
    assert not kept.exists()


def test_revoke_says_when_the_gate_refused_the_certificate_already(
    gate: FakeGate, kept: Path, capsys: pytest.CaptureFixture[str]
) -> None:
    gate.handler = lambda path, sent, client: (401, json.dumps({"error": "unpaired"}))

    assert cli.main(["revoke", "--directory", str(kept), "-o", "json"]) == 0

    assert printed(capsys)["alreadyRefused"] is True


@pytest.mark.parametrize(
    ("arguments", "answer"),
    [
        (["add", "--title", "Invented"], {"uuid": UUID}),
        (["update", UUID, "--title", "Invented"], {}),
        (["remove", UUID], {}),
    ],
    ids=["add", "update", "remove"],
)
def test_a_write_names_what_it_wrote(
    gate: FakeGate,
    kept: Path,
    capsys: pytest.CaptureFixture[str],
    arguments: list[str],
    answer: dict[str, Any],
) -> None:
    answering(gate, answer)

    assert cli.main([*arguments, "--directory", str(kept), "-o", "json"]) == 0

    assert printed(capsys) == {"uuid": UUID}


def test_attachments_are_listed_by_name_and_size(
    gate: FakeGate, kept: Path, capsys: pytest.CaptureFixture[str]
) -> None:
    answering(gate, {"attachments": [{"name": "key.pem", "size": 451}]})

    assert cli.main(["attachments", UUID, "--directory", str(kept), "-o", "json"]) == 0

    assert printed(capsys) == [{"name": "key.pem", "size": 451}]


def test_fetch_says_where_it_wrote_and_never_what(
    gate: FakeGate, kept: Path, tmp_path: Path, capsys: pytest.CaptureFixture[str]
) -> None:
    content = b"not a real key"
    answering(
        gate,
        {
            "name": "key.pem",
            "size": len(content),
            "content": base64.b64encode(content).decode("ascii"),
        },
    )
    to = tmp_path / "key.pem"

    code = cli.main(
        ["fetch", UUID, "key.pem", "--to", str(to), "--directory", str(kept), "-o", "json"]
    )

    assert code == 0
    assert printed(capsys) == {"uuid": UUID, "name": "key.pem", "size": 14, "path": str(to)}
    assert to.read_bytes() == content


def test_a_failure_is_a_sentence_on_standard_error_whichever_format_was_asked_for(
    gate: FakeGate, kept: Path, capsys: pytest.CaptureFixture[str]
) -> None:
    gate.handler = lambda path, sent, client: (404, json.dumps({"error": "not found"}))

    assert cli.main(["show", UUID, "--directory", str(kept), "-o", "json"]) == 1

    captured = capsys.readouterr()
    assert captured.out == ""
    assert captured.err.startswith("RouteRefused: ")


def test_a_document_is_ascii_whatever_the_database_holds(
    gate: FakeGate, kept: Path, capsys: pytest.CaptureFixture[str]
) -> None:
    # A console whose code page holds less than the database does still prints it, and `jq`
    # reads the escape back as the letter.
    answering(gate, {"groups": [{"uuid": GROUP, "name": "Café", "parent": None}]})

    assert cli.main(["groups", "--directory", str(kept), "-o", "json"]) == 0

    out = capsys.readouterr().out
    assert out.isascii()
    assert json.loads(out)[0]["name"] == "Café"
