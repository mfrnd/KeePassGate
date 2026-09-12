"""The program the end to end tier makes its library calls through, run against a stand-in gate.

The end to end tier is the only thing that runs `live_calls.py` for real, and it runs where a
desktop and a KeePass are, which is not every run. So what it asks and what it prints are held here,
where every run can see them break.
"""

from __future__ import annotations

import io
import json
from pathlib import Path
from typing import Any

import pytest

import live_calls
from conftest import FakeGate
from pykeepassgate import credentials as credentials_module

UUID = "7b1c" * 8


@pytest.fixture
def kept(tmp_path: Path, bundle: dict[str, Any], gate: FakeGate) -> Path:
    """A paired identity on disk, pointed at this gate."""
    credentials_module.save(tmp_path / "kept", gate.port, bundle)
    return tmp_path / "kept"


def test_a_field_is_read_through_its_own_route_and_printed(
    gate: FakeGate, kept: Path, capsys: pytest.CaptureFixture[str]
) -> None:
    gate.handler = lambda path, body, client: (
        200,
        json.dumps({"name": "Password", "protected": True, "value": "not-a-real-password"}),
    )

    assert live_calls.main(["--directory", str(kept), "field", UUID, "Password"]) == 0

    assert gate.requests == [("GET", f"/entries/{UUID}/fields/Password")]
    assert capsys.readouterr().out == "not-a-real-password\n"


def test_a_password_is_set_to_the_line_on_standard_input(
    gate: FakeGate, kept: Path, monkeypatch: pytest.MonkeyPatch, capsys: pytest.CaptureFixture[str]
) -> None:
    sent: list[dict[str, Any]] = []

    def answer(path: str, body: str, client: bytes | None) -> tuple[int, str]:
        sent.append(json.loads(body))
        return 200, json.dumps({"ok": True})

    gate.handler = answer
    monkeypatch.setattr("sys.stdin", io.StringIO("a-rotated-invented-password\n"))

    assert live_calls.main(["--directory", str(kept), "password", UUID]) == 0

    assert gate.requests == [("PUT", f"/entries/{UUID}")]
    assert sent == [{"Password": "a-rotated-invented-password"}]
    assert capsys.readouterr().out == "updated\n"


def test_a_refusal_is_printed_under_its_name_with_the_exit_code_a_failure_has(
    gate: FakeGate, kept: Path, monkeypatch: pytest.MonkeyPatch, capsys: pytest.CaptureFixture[str]
) -> None:
    # What the fixture reads to tell a write refused as busy from one that failed otherwise.
    gate.handler = lambda path, body, client: (503, json.dumps({"error": "busy"}))
    monkeypatch.setattr("sys.stdin", io.StringIO("refused\n"))

    assert live_calls.main(["--directory", str(kept), "password", UUID]) == 1
    assert capsys.readouterr().err.startswith("GateBusy: ")


def test_nothing_paired_is_said_by_name_as_well(
    tmp_path: Path, capsys: pytest.CaptureFixture[str]
) -> None:
    assert live_calls.main(["--directory", str(tmp_path / "nothing"), "field", UUID, "x"]) == 1
    assert capsys.readouterr().err.startswith("NotPaired: ")
