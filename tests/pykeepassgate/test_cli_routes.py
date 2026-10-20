"""The commands for the routes the command line reached last: groups, fields and attachments.

Each is a few lines over one library call, like every command, so what is held here is what the
command adds: which call it makes with what, what it prints, and what it refuses before dialing.
"""

from __future__ import annotations

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


def answering(gate: FakeGate, body: dict[str, Any]) -> list[str]:
    """Answer every request with this, keeping each body sent."""
    sent: list[str] = []

    def answer(path: str, received: str, client: bytes | None) -> tuple[int, str]:
        sent.append(received)
        return 200, json.dumps(body)

    gate.handler = answer
    return sent


def test_group_reads_one_group_and_its_notes(
    gate: FakeGate, kept: Path, capsys: pytest.CaptureFixture[str]
) -> None:
    answering(
        gate,
        {"uuid": GROUP, "name": "Automation", "parent": UUID, "notes": "first\nsecond"},
    )

    assert cli.main(["group", GROUP, "--directory", str(kept)]) == 0

    assert gate.requests == [("GET", f"/groups/{GROUP}")]
    assert capsys.readouterr().out.splitlines() == [
        f"uuid:   {GROUP}",
        "name:   Automation",
        f"parent: {UUID}",
        "notes:  first\\nsecond",
    ]


def test_the_root_has_no_group_above_it(
    gate: FakeGate, kept: Path, capsys: pytest.CaptureFixture[str]
) -> None:
    answering(gate, {"uuid": GROUP, "name": "Root", "parent": None, "notes": ""})

    assert cli.main(["group", "root", "--directory", str(kept)]) == 0

    assert gate.requests == [("GET", "/groups/root")]
    assert "parent: none" in capsys.readouterr().out.splitlines()


def test_group_prints_its_notes_in_a_document(
    gate: FakeGate, kept: Path, capsys: pytest.CaptureFixture[str]
) -> None:
    answering(
        gate,
        {
            "uuid": GROUP,
            "name": "Automation",
            "parent": None,
            "modified": "2026-01-02T03:04:05Z",
            "notes": "first\nsecond",
        },
    )

    assert cli.main(["group", GROUP, "--directory", str(kept), "-o", "json"]) == 0

    assert json.loads(capsys.readouterr().out) == {
        "uuid": GROUP,
        "name": "Automation",
        "parent": None,
        "modified": "2026-01-02T03:04:05Z",
        "notes": "first\nsecond",
    }


def test_add_group_prints_what_addresses_the_new_group(
    gate: FakeGate, kept: Path, capsys: pytest.CaptureFixture[str]
) -> None:
    sent = answering(gate, {"uuid": GROUP})

    code = cli.main(
        ["add-group", "--name", "Automation", "--notes", "For agents", "--directory", str(kept)]
    )

    assert code == 0
    assert gate.requests == [("POST", "/groups/root/groups")]
    assert json.loads(sent[-1]) == {"Name": "Automation", "Notes": "For agents"}
    assert capsys.readouterr().out.strip() == GROUP


def test_add_group_puts_it_where_it_was_told(gate: FakeGate, kept: Path) -> None:
    sent = answering(gate, {"uuid": GROUP})

    code = cli.main(["add-group", "--name", "Inner", "--parent", UUID, "--directory", str(kept)])

    assert code == 0
    assert gate.requests == [("POST", f"/groups/{UUID}/groups")]
    assert json.loads(sent[-1]) == {"Name": "Inner"}


def test_add_group_needs_a_name(kept: Path, gate: FakeGate) -> None:
    with pytest.raises(SystemExit) as raised:
        cli.main(["add-group", "--directory", str(kept)])

    assert raised.value.code == 2
    assert gate.requests == []


def test_update_group_renames_and_writes_nothing_else(
    gate: FakeGate, kept: Path, capsys: pytest.CaptureFixture[str]
) -> None:
    sent = answering(gate, {"ok": True})

    assert cli.main(["update-group", GROUP, "--name", "Renamed", "--directory", str(kept)]) == 0

    assert gate.requests == [("PUT", f"/groups/{GROUP}")]
    assert json.loads(sent[-1]) == {"Name": "Renamed"}
    assert capsys.readouterr().out.strip() == f"updated {GROUP}"


def test_update_group_with_nothing_to_change_is_refused_before_anything_is_dialed(
    gate: FakeGate, kept: Path, capsys: pytest.CaptureFixture[str]
) -> None:
    assert cli.main(["update-group", GROUP, "--directory", str(kept)]) == 2

    assert "needs something to change" in capsys.readouterr().err
    assert gate.requests == []


def test_remove_group_says_what_it_removed(
    gate: FakeGate, kept: Path, capsys: pytest.CaptureFixture[str]
) -> None:
    answering(gate, {"ok": True})

    assert cli.main(["remove-group", GROUP, "--directory", str(kept), "-o", "json"]) == 0

    assert gate.requests == [("DELETE", f"/groups/{GROUP}")]
    assert json.loads(capsys.readouterr().out) == {"uuid": GROUP}


def test_fields_lists_names_and_says_which_are_protected(
    gate: FakeGate, kept: Path, capsys: pytest.CaptureFixture[str]
) -> None:
    answering(
        gate,
        {
            "fields": [
                {"name": "Title", "protected": False},
                {"name": "Password", "protected": True},
            ]
        },
    )

    assert cli.main(["fields", UUID, "--directory", str(kept)]) == 0

    assert gate.requests == [("GET", f"/entries/{UUID}/fields")]
    assert capsys.readouterr().out.splitlines() == ["Title", "Password (protected)"]


def test_field_prints_the_value_alone(
    gate: FakeGate, kept: Path, capsys: pytest.CaptureFixture[str]
) -> None:
    answering(
        gate, {"name": "Password", "protected": True, "value": "not-a-real-password"}
    )

    assert cli.main(["field", UUID, "Password", "--directory", str(kept)]) == 0

    assert gate.requests == [("GET", f"/entries/{UUID}/fields/Password")]
    assert capsys.readouterr().out == "not-a-real-password\n"


def test_field_names_its_route_by_the_field_s_name_as_written(gate: FakeGate, kept: Path) -> None:
    answering(gate, {"name": "Deploy key", "protected": False, "value": "x"})

    assert cli.main(["field", UUID, "Deploy key", "--directory", str(kept)]) == 0

    assert gate.requests == [("GET", f"/entries/{UUID}/fields/Deploy%20key")]


def test_field_keeps_a_value_on_one_line_unless_asked_for_it_exactly(
    gate: FakeGate, kept: Path, capsys: pytest.CaptureFixture[str]
) -> None:
    answering(gate, {"name": "Notes", "protected": False, "value": "first\nsecond"})

    assert cli.main(["field", UUID, "Notes", "--directory", str(kept)]) == 0
    assert capsys.readouterr().out == "first\\nsecond\n"

    assert cli.main(["field", UUID, "Notes", "--directory", str(kept), "-o", "json"]) == 0
    assert json.loads(capsys.readouterr().out) == {
        "name": "Notes",
        "protected": False,
        "value": "first\nsecond",
    }


@pytest.mark.parametrize("output", ["plain", "json"])
def test_a_withheld_value_fails_the_command_rather_than_printing_anything(
    gate: FakeGate, kept: Path, capsys: pytest.CaptureFixture[str], output: str
) -> None:
    # A script reading one value would take `(withheld)` for it, and might set it somewhere as
    # a password.
    answering(gate, {"name": "Password", "protected": True, "value": None})

    code = cli.main(["field", UUID, "Password", "--directory", str(kept), "-o", output])

    captured = capsys.readouterr()
    assert code == 1
    assert captured.out == ""
    assert captured.err.startswith("Withheld: ")


def test_add_field_takes_a_value_it_was_given(
    gate: FakeGate, kept: Path, capsys: pytest.CaptureFixture[str]
) -> None:
    sent = answering(gate, {"ok": True})

    code = cli.main(
        ["add-field", UUID, "Environment", "--value", "staging", "--directory", str(kept)]
    )

    assert code == 0
    assert gate.requests == [("POST", f"/entries/{UUID}/fields")]
    assert json.loads(sent[-1]) == {"name": "Environment", "value": "staging"}
    assert capsys.readouterr().out.strip() == f"added Environment to {UUID}"


def test_add_field_reads_a_value_it_was_not_given(
    gate: FakeGate, kept: Path, monkeypatch: pytest.MonkeyPatch
) -> None:
    from test_cli_api import _Line

    sent = answering(gate, {"ok": True})
    monkeypatch.setattr("sys.stdin", _Line(" not a real key \n"))

    code = cli.main(["add-field", UUID, "Deploy key", "--protected", "--directory", str(kept)])

    assert code == 0
    assert json.loads(sent[-1]) == {
        "name": "Deploy key",
        "value": " not a real key ",
        "protected": "true",
    }


def test_at_a_terminal_a_value_is_asked_for_and_shown_as_stars(
    gate: FakeGate, kept: Path, monkeypatch: pytest.MonkeyPatch
) -> None:
    from test_cli_api import _Terminal

    sent = answering(gate, {"ok": True})
    prompts: list[str] = []

    def typed(
        prompt: str = "Password: ", stream: object = None, *, echo_char: str | None = None
    ) -> str:
        assert echo_char == "*"
        prompts.append(prompt)
        return "not a real key"

    monkeypatch.setattr("sys.stdin", _Terminal())
    monkeypatch.setattr("getpass.getpass", typed)

    assert cli.main(["update-field", UUID, "Deploy key", "--directory", str(kept)]) == 0

    assert prompts == ["New value of Deploy key: "]
    assert json.loads(sent[-1]) == {"value": "not a real key"}


def test_a_protected_value_can_be_given_as_an_argument(gate: FakeGate, kept: Path) -> None:
    sent = answering(gate, {"ok": True})

    code = cli.main(
        ["add-field", UUID, "Deploy key", "--protected", "--value", "x", "--directory", str(kept)]
    )

    assert code == 0
    assert json.loads(sent[-1]) == {"name": "Deploy key", "value": "x", "protected": "true"}


def test_update_field_writes_the_value_and_nothing_about_protection(
    gate: FakeGate, kept: Path, capsys: pytest.CaptureFixture[str]
) -> None:
    sent = answering(gate, {"ok": True})

    code = cli.main(
        ["update-field", UUID, "Environment", "--value", "production", "--directory", str(kept)]
    )

    assert code == 0
    assert gate.requests == [("PUT", f"/entries/{UUID}/fields/Environment")]
    assert json.loads(sent[-1]) == {"value": "production"}
    assert capsys.readouterr().out.strip() == f"updated Environment on {UUID}"


def test_strip_field_says_what_it_took_off(
    gate: FakeGate, kept: Path, capsys: pytest.CaptureFixture[str]
) -> None:
    answering(gate, {"ok": True})

    assert cli.main(["strip-field", UUID, "Environment", "--directory", str(kept)]) == 0

    assert gate.requests == [("DELETE", f"/entries/{UUID}/fields/Environment")]
    assert capsys.readouterr().out.strip() == f"stripped Environment from {UUID}"

    code = cli.main(["strip-field", UUID, "Environment", "--directory", str(kept), "-o", "json"])

    assert code == 0
    assert json.loads(capsys.readouterr().out) == {"uuid": UUID, "name": "Environment"}
