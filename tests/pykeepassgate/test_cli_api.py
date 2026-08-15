"""The commands that reach a database, which are a few lines over the library and stay that way."""

from __future__ import annotations

import base64
import json
from pathlib import Path
from typing import Any

import pytest

from conftest import FakeGate
from pykeepassgate import api, cli
from pykeepassgate import credentials as credentials_module

UUID = "7b1c" * 8


@pytest.fixture
def kept(tmp_path: Path, bundle: dict[str, Any], gate: FakeGate) -> Path:
    """A paired identity on disk, pointed at this gate."""
    credentials_module.save(tmp_path / "kept", gate.port, bundle)
    return tmp_path / "kept"


def answering(gate: FakeGate, body: dict[str, Any]) -> None:
    """Answer every request with this."""
    gate.handler = lambda path, sent, client: (200, json.dumps(body))


def test_groups_are_printed_one_to_a_line(
    gate: FakeGate, kept: Path, capsys: pytest.CaptureFixture[str]
) -> None:
    answering(gate, {"groups": [{"uuid": UUID, "name": "Fictitious", "parent": None}]})

    assert cli.main(["groups", "--directory", str(kept)]) == 0

    printed = capsys.readouterr().out
    assert UUID in printed
    assert "Fictitious" in printed


def test_timeout_reaches_the_client_rather_than_being_parsed_and_dropped(
    gate: FakeGate, kept: Path, monkeypatch: pytest.MonkeyPatch
) -> None:
    """Every command offers `--timeout` and seven of them used to ignore it.

    Accepted, parsed and thrown away is worse than not offered: a caller who asked for longer got
    the default and nothing said so, which on a slow database looks like the gate being broken.
    """
    answering(gate, {"groups": []})
    asked: list[float | None] = []

    opened = api.GateClient.open

    def watching(
        directory: Path | None = None, profile: str = "default", **rest: Any
    ) -> Any:
        asked.append(rest.get("timeout"))
        return opened(directory, profile, **rest)

    monkeypatch.setattr(api.GateClient, "open", watching)

    assert cli.main(["groups", "--directory", str(kept), "--timeout", "12.5"]) == 0
    assert asked == [12.5]


def test_groups_under_one_group_walks_a_level(gate: FakeGate, kept: Path) -> None:
    answering(gate, {"groups": []})

    assert cli.main(["groups", "--under", UUID, "--directory", str(kept)]) == 0
    assert gate.requests[-1] == ("GET", f"/groups/{UUID}/groups")


def test_entries_are_printed_and_can_be_scoped(
    gate: FakeGate, kept: Path, capsys: pytest.CaptureFixture[str]
) -> None:
    answering(
        gate,
        {
            "entries": [
                {"uuid": UUID, "group": "g", "title": "an invented service", "url": ""}
            ],
            "count": 1,
        },
    )

    assert cli.main(["entries", "--directory", str(kept)]) == 0
    assert "an invented service" in capsys.readouterr().out
    assert gate.requests[-1] == ("GET", "/entries")

    assert cli.main(["entries", "--group", UUID, "--directory", str(kept)]) == 0
    assert gate.requests[-1] == ("GET", f"/groups/{UUID}/entries")


def test_a_title_the_rule_withholds_is_listed_as_withheld(
    gate: FakeGate, kept: Path, capsys: pytest.CaptureFixture[str]
) -> None:
    # A protected title comes back `null` when the rule lacks `P`, and the word "None" in its
    # place would read as a title somebody gave the entry.
    answering(
        gate,
        {"entries": [{"uuid": UUID, "group": "g", "title": None, "url": None}], "count": 1},
    )

    assert cli.main(["entries", "--directory", str(kept)]) == 0

    printed = capsys.readouterr().out
    assert f"{UUID}  (withheld)" in printed
    assert "None" not in printed


def test_show_prints_every_string_and_says_which_are_protected(
    gate: FakeGate, kept: Path, capsys: pytest.CaptureFixture[str]
) -> None:
    answering(
        gate,
        {
            "uuid": UUID,
            "group": "g",
            "fields": {"Title": "an invented service", "Password": "not-a-real-password"},
            "protected": ["Password"],
        },
    )

    assert cli.main(["show", UUID, "--directory", str(kept)]) == 0

    printed = capsys.readouterr().out
    assert "not-a-real-password (protected)" in printed
    assert "an invented service" in printed


def test_show_says_a_value_the_rule_withholds_is_withheld(
    gate: FakeGate, kept: Path, capsys: pytest.CaptureFixture[str]
) -> None:
    # The module docstring's promise for a rule without `P`. Printed as it arrives, the `null`
    # would be the word "None" where a password goes, which reads like one chosen badly.
    answering(
        gate,
        {
            "uuid": UUID,
            "group": "g",
            "fields": {"Title": "an invented service", "Password": None},
            "protected": ["Password"],
        },
    )

    assert cli.main(["show", UUID, "--directory", str(kept)]) == 0

    printed = capsys.readouterr().out
    assert "Password: (withheld) (protected)" in printed
    assert "None" not in printed


def test_add_prints_what_addresses_the_new_entry(
    gate: FakeGate, kept: Path, capsys: pytest.CaptureFixture[str]
) -> None:
    answering(gate, {"uuid": UUID})

    assert cli.main(["add", "--title", "an invented service", "--directory", str(kept)]) == 0

    assert capsys.readouterr().out.strip() == UUID
    assert gate.requests[-1] == ("POST", "/groups/root/entries")


def test_a_password_is_read_rather_than_argued(
    gate: FakeGate, kept: Path, monkeypatch: pytest.MonkeyPatch
) -> None:
    # An argument is in the process list, where anything running as this user can read it.
    sent: list[dict[str, Any]] = []

    def answer(path: str, body: str, client: bytes | None) -> tuple[int, str]:
        if body:
            sent.append(json.loads(body))
        return 200, json.dumps({"uuid": UUID})

    gate.handler = answer
    monkeypatch.setattr("sys.stdin", _Line("not-a-real-password\n"))

    assert cli.main(["add", "--title", "x", "--password", "--directory", str(kept)]) == 0
    assert sent[-1]["Password"] == "not-a-real-password"


def test_a_password_keeps_the_spaces_around_it(
    gate: FakeGate, kept: Path, monkeypatch: pytest.MonkeyPatch
) -> None:
    # Only the line's own ending is taken off. The invitation is stripped, being one spelling of
    # thirty-two bytes, and a password is whatever was typed, spaces included: trimmed, it is
    # stored as something that no longer matches what the service holds, and nothing says why.
    sent: list[dict[str, Any]] = []

    def answer(path: str, body: str, client: bytes | None) -> tuple[int, str]:
        sent.append(json.loads(body))
        return 200, json.dumps({"uuid": UUID})

    gate.handler = answer
    monkeypatch.setattr("sys.stdin", _Line("  not a real password \t\n"))

    assert cli.main(["add", "--title", "x", "--password", "--directory", str(kept)]) == 0
    assert sent[-1]["Password"] == "  not a real password \t"


def test_remove_says_what_it_removed(
    gate: FakeGate, kept: Path, capsys: pytest.CaptureFixture[str]
) -> None:
    answering(gate, {"ok": True})

    assert cli.main(["remove", UUID, "--directory", str(kept)]) == 0
    assert UUID in capsys.readouterr().out
    assert gate.requests[-1] == ("DELETE", f"/entries/{UUID}")


def test_attachments_are_listed_with_their_sizes(
    gate: FakeGate, kept: Path, capsys: pytest.CaptureFixture[str]
) -> None:
    answering(gate, {"attachments": [{"name": "notes.txt", "size": 22}]})

    assert cli.main(["attachments", UUID, "--directory", str(kept)]) == 0

    printed = capsys.readouterr().out
    assert "notes.txt" in printed
    assert "22" in printed


def test_fetch_writes_the_bytes_to_a_file(
    gate: FakeGate, kept: Path, tmp_path: Path, capsys: pytest.CaptureFixture[str]
) -> None:
    content = b"an invented attachment"
    answering(
        gate,
        {
            "name": "notes.txt",
            "size": len(content),
            "content": base64.b64encode(content).decode("ascii"),
        },
    )

    where = tmp_path / "fetched.bin"

    assert cli.main(["fetch", UUID, "notes.txt", "--to", str(where), "--directory", str(kept)]) == 0
    assert where.read_bytes() == content
    assert "22 bytes" in capsys.readouterr().out


def test_a_refusal_becomes_an_exit_code_rather_than_a_stack_trace(
    gate: FakeGate, kept: Path, capsys: pytest.CaptureFixture[str]
) -> None:
    gate.handler = lambda path, body, client: (404, '{"error":"no such route"}')

    assert cli.main(["show", UUID, "--directory", str(kept)]) == cli.FAILED
    assert "RouteRefused" in capsys.readouterr().err


class _Line:
    """Standard input with one line in it."""

    def __init__(self, text: str) -> None:
        self._text = text

    def readline(self) -> str:
        return self._text
