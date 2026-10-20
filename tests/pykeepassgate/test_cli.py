"""The command line, which is a few lines over the library and has to stay that way."""

from __future__ import annotations

import base64
import inspect
import json
import re
import subprocess
import sys
from pathlib import Path
from typing import Any

import pytest

from conftest import FakeGate
from pykeepassgate import api, cli
from pykeepassgate import credentials as credentials_module


def test_a_command_is_required(capsys: pytest.CaptureFixture[str]) -> None:
    # Exit 2 is what a shell reads as "you typed it wrong", and `argparse` raises rather than
    # returns, so this asserts the raise rather than the return.
    with pytest.raises(SystemExit) as raised:
        cli.main([])

    assert raised.value.code == 2


def test_pairing_without_a_port_dials_the_one_the_gate_listens_on_by_default() -> None:
    arguments = cli._parser().parse_args(["pair", "--name", "agent-invented"])

    assert arguments.port == cli.DEFAULT_PORT


def test_the_library_dials_the_same_port_when_none_is_named() -> None:
    assert inspect.signature(api.pair).parameters["port"].default == cli.DEFAULT_PORT


def test_the_default_port_is_the_plugin_s_own() -> None:
    # Read out of the plugin's source rather than written down twice, so that moving the gate's
    # default moves this test's answer and fails it until the client follows.
    repository = Path(__file__).resolve().parents[2]
    source = repository / "src" / "KeePassGate" / "Transport" / "GateOptions.cs"
    declared = re.search(
        r"public const int DefaultPort = (\d+);", source.read_text(encoding="utf-8")
    )

    assert declared is not None, "GateOptions.cs no longer declares DefaultPort"
    assert int(declared.group(1)) == cli.DEFAULT_PORT


def test_whoami_says_what_this_client_is_without_connecting(
    tmp_path: Path, bundle: dict[str, Any], capsys: pytest.CaptureFixture[str]
) -> None:
    credentials_module.save(tmp_path / "kept", 45678, bundle)

    assert cli.main(["whoami", "--directory", str(tmp_path / "kept")]) == 0

    printed = capsys.readouterr().out
    assert "CN=agent-invented" in printed
    assert "reader" in printed
    assert "45678" in printed


def _shows_no_credential(printed: str, bundle: dict[str, Any], *also: str) -> None:
    """Whether output keeps the promise the module docstring makes: no credential, ever.

    The gate key in both of its spellings, the start of the identity the gate handed over, and
    the marker of a private key in PEM, which is what the identity becomes on disk. What gets
    printed ends up in scrollback, CI logs and agent transcripts.
    """
    gate_key = bundle["gateKey"]
    for secret in (gate_key, base64.b64decode(gate_key).hex(), bundle["identity"][:24], *also):
        assert secret not in printed

    assert "PRIVATE KEY" not in printed


def test_whoami_prints_what_the_credentials_are_and_nothing_they_hold(
    tmp_path: Path, bundle: dict[str, Any], capsys: pytest.CaptureFixture[str]
) -> None:
    credentials_module.save(tmp_path / "kept", 45678, bundle)

    assert cli.main(["whoami", "--directory", str(tmp_path / "kept")]) == 0

    captured = capsys.readouterr()
    _shows_no_credential(captured.out + captured.err, bundle)


def test_whoami_says_where_it_looked_when_there_is_nothing_there(
    tmp_path: Path, capsys: pytest.CaptureFixture[str]
) -> None:
    assert cli.main(["whoami", "--directory", str(tmp_path / "nothing")]) == cli.FAILED
    assert "no paired client" in capsys.readouterr().err


def test_status_prints_what_the_gate_said(
    tmp_path: Path, bundle: dict[str, Any], gate: FakeGate, capsys: pytest.CaptureFixture[str]
) -> None:
    credentials_module.save(tmp_path / "kept", gate.port, bundle)
    gate.handler = lambda path, body, client: (
        200,
        json.dumps({"ok": True, "database": "abcdef01", "version": "0.1.0"}),
    )

    assert cli.main(["status", "--directory", str(tmp_path / "kept")]) == 0
    assert "abcdef01" in capsys.readouterr().out


def test_a_gate_that_says_it_is_not_ok_exits_with_the_number_a_script_reads(
    tmp_path: Path, bundle: dict[str, Any], gate: FakeGate, capsys: pytest.CaptureFixture[str]
) -> None:
    # The number rather than `cli.FAILED`, which every other failure here is compared with: a
    # constant compared with itself passes whatever it is set to, and `1` is what the module
    # docstring promises a script. `ok: false` is the one failure no exception carries, so this
    # also reads it all the way from the body to the exit code.
    credentials_module.save(tmp_path / "kept", gate.port, bundle)
    gate.handler = lambda path, body, client: (
        200,
        json.dumps({"ok": False, "database": "abcdef01", "version": "0.1.0"}),
    )

    assert cli.main(["status", "--directory", str(tmp_path / "kept")]) == 1
    assert "abcdef01" in capsys.readouterr().out


def test_a_locked_database_is_reported_as_something_to_wait_for(
    tmp_path: Path, bundle: dict[str, Any], gate: FakeGate, capsys: pytest.CaptureFixture[str]
) -> None:
    credentials_module.save(tmp_path / "kept", gate.port, bundle)
    gate.handler = lambda path, body, client: (503, json.dumps({"error": "locked"}))

    assert cli.main(["status", "--directory", str(tmp_path / "kept")]) == cli.FAILED

    printed = capsys.readouterr().err
    assert "DatabaseLocked" in printed, "a script should be able to grep for the kind"
    assert "locked" in printed


def test_an_invitation_can_be_read_from_standard_input(
    tmp_path: Path,
    gate: FakeGate,
    monkeypatch: pytest.MonkeyPatch,
    capsys: pytest.CaptureFixture[str],
) -> None:
    # The reason the argument is optional: a token on a command line ends up in a shell history and
    # in the process list, where anything running as this user can read it.
    import io

    from pykeepassgate import token as token_module

    monkeypatch.setattr("sys.stdin", io.StringIO(token_module.encode(bytes(range(32))) + "\n"))
    gate.handler = lambda path, body, client: (503, json.dumps({"error": "locked"}))

    code = cli.main(
        [
            "pair",
            "--port",
            str(gate.port),
            "--name",
            "agent-invented",
            "--directory",
            str(tmp_path / "kept"),
        ]
    )

    assert code == cli.FAILED
    assert "DatabaseLocked" in capsys.readouterr().err


def test_a_token_that_is_not_one_is_refused_before_anything_is_dialed(
    tmp_path: Path, capsys: pytest.CaptureFixture[str]
) -> None:
    code = cli.main(
        [
            "pair",
            "--port",
            "1",
            "--name",
            "agent-invented",
            "--invitation",
            "nonsense",
            "--directory",
            str(tmp_path / "kept"),
        ]
    )

    assert code == cli.FAILED
    assert "MalformedToken" in capsys.readouterr().err


@pytest.mark.parametrize("spoiled", ["with a space after it", "with a character outside it"])
def test_an_invitation_refused_as_malformed_is_not_repeated_back(
    spoiled: str, tmp_path: Path, capsys: pytest.CaptureFixture[str]
) -> None:
    # Nothing was sent, so the invitation is still good, and a refusal that quoted it would put it
    # wherever standard error goes. A space pasted after it is the likely way to get here, since
    # `--invitation` is taken as given; the one below it fails the alphabet instead.
    from pykeepassgate import token as token_module

    invitation = token_module.encode(bytes(range(32)))
    pasted = invitation + " " if spoiled == "with a space after it" else invitation[:-1] + "!"

    code = cli.main(
        [
            "pair",
            "--port",
            "1",
            "--name",
            "agent-invented",
            "--invitation",
            pasted,
            "--directory",
            str(tmp_path / "kept"),
        ]
    )

    captured = capsys.readouterr()
    assert code == 1
    assert "MalformedToken" in captured.err
    assert invitation[:16] not in captured.out + captured.err


def test_pairing_prints_neither_the_invitation_nor_what_it_was_handed(
    tmp_path: Path, gate: FakeGate, bundle: dict[str, Any], capsys: pytest.CaptureFixture[str]
) -> None:
    from test_pairing import INVITATION, Gate

    gate.handler = Gate(gate, gate.host_certificate, bundle=bundle)

    code = cli.main(
        [
            "pair",
            "--port",
            str(gate.port),
            "--name",
            "agent-invented",
            "--invitation",
            INVITATION,
            "--directory",
            str(tmp_path / "kept"),
        ]
    )

    captured = capsys.readouterr()
    assert code == 0
    _shows_no_credential(captured.out + captured.err, bundle, INVITATION)


def test_pairing_prints_what_was_approved_rather_than_what_was_asked(
    tmp_path: Path, gate: FakeGate, bundle: dict[str, Any], capsys: pytest.CaptureFixture[str]
) -> None:
    from test_pairing import INVITATION, Gate

    gate.handler = Gate(gate, gate.host_certificate, bundle=bundle)

    code = cli.main(
        [
            "pair",
            "--port",
            str(gate.port),
            "--name",
            "agent-invented",
            "--profile-name",
            "reader",
            "--profile-name",
            "writer",
            "--invitation",
            INVITATION,
            "--directory",
            str(tmp_path / "kept"),
        ]
    )

    printed = capsys.readouterr().out
    assert code == 0
    assert "profiles approved: reader" in printed
    assert "writer" not in printed, "the operator took it away, so it is not what was stored"


def test_the_module_runs_as_its_own_entry_point() -> None:
    """`python -m pykeepassgate.cli` runs the module top to bottom, unlike the console script.

    The entry point guard sat in the middle of the file once, above half the functions the parser
    binds, so running the module this way raised `NameError` while the installed command worked.
    """
    # A real interpreter rather than `runpy` in this one: the module is already imported here, so
    # re-executing it in place is not what `python -m` does, and warns that it is not.
    ended = subprocess.run(
        [sys.executable, "-m", "pykeepassgate.cli", "--help"],
        capture_output=True,
        text=True,
        check=False,
    )

    assert ended.returncode == 0, ended.stderr
    assert "usage: pykeepassgate" in ended.stdout


def test_naming_the_identity_twice_is_an_argument_error(tmp_path: Path) -> None:
    # `--profile` and `--directory` are two ways to say one thing, and the directory used to win
    # in silence: `pair --directory D --profile work` stored where `status --profile work` would
    # never look. Exit code 2, which the module docstring reserves for arguments that are wrong.
    with pytest.raises(SystemExit) as raised:
        cli.main(["whoami", "--profile", "work", "--directory", str(tmp_path / "kept")])

    assert raised.value.code == 2
