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
