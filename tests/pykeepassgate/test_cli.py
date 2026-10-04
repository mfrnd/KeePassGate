"""The command line, which is a few lines over the library and has to stay that way."""

from __future__ import annotations

import base64
import inspect
import json
import re
import subprocess
import sys
from datetime import timedelta
from pathlib import Path
from typing import Any

import pytest

from pykeepassgate import api, cli, transport
from pykeepassgate import credentials as credentials_module
from pykeepassgate.errors import StorageError
from pykeepassgate.testing import FakeGate


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
    # Reported the way every other refusal is, under its class's name, which is what a script
    # greps for; there is no second path through `main` for the standard library's exceptions.
    assert cli.main(["whoami", "--directory", str(tmp_path / "nothing")]) == 1

    printed = capsys.readouterr().err
    assert printed.startswith("NotPaired: no paired client")
    assert str(tmp_path / "nothing") in printed


def test_a_command_that_dials_with_nothing_paired_says_so_before_dialing(
    tmp_path: Path, gate: FakeGate, capsys: pytest.CaptureFixture[str]
) -> None:
    assert cli.main(["status", "--directory", str(tmp_path / "nothing")]) == 1

    assert capsys.readouterr().err.startswith("NotPaired: ")
    assert gate.names == []


def test_status_prints_what_the_gate_said(
    tmp_path: Path, bundle: dict[str, Any], gate: FakeGate, capsys: pytest.CaptureFixture[str]
) -> None:
    credentials_module.save(tmp_path / "kept", gate.port, bundle)
    gate.handler = lambda path, body, client: (
        200,
        json.dumps(
            {"ok": True, "database": "abcdef01", "version": "0.1.0", "revision": "0123abcd"}
        ),
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
        json.dumps(
            {"ok": False, "database": "abcdef01", "version": "0.1.0", "revision": "0123abcd"}
        ),
    )

    assert cli.main(["status", "--directory", str(tmp_path / "kept")]) == 1
    assert "abcdef01" in capsys.readouterr().out


def test_a_certificate_that_has_expired_is_reported_as_expired(
    tmp_path: Path, bundle: dict[str, Any], gate: FakeGate, capsys: pytest.CaptureFixture[str]
) -> None:
    # Not as a missing record, which is what the gate's `401` alone would have said.
    credentials_module.save(
        tmp_path / "kept", gate.port, {**bundle, "expires": "2001-02-03T04:05:06Z"}
    )

    assert cli.main(["status", "--directory", str(tmp_path / "kept")]) == 1

    printed = capsys.readouterr().err
    assert printed.startswith("NotAdmitted: this client's certificate expired at 2001-02-03")
    assert gate.names == []


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


@pytest.mark.parametrize("given", ["argument", "standard input"])
def test_an_invitation_pasted_with_white_space_around_it_is_taken_either_way(
    given: str,
    tmp_path: Path,
    gate: FakeGate,
    monkeypatch: pytest.MonkeyPatch,
    capsys: pytest.CaptureFixture[str],
) -> None:
    # A paste brings a space or a line break with it, and the token's alphabet holds neither.
    # Standard input was stripped and the argument was not, so the same paste was refused as
    # malformed from one and taken from the other. A locked gate is what shows it was taken.
    import io

    from pykeepassgate import token as token_module

    pasted = " " + token_module.encode(bytes(range(32))) + " \t"
    invitation = ["--invitation", pasted] if given == "argument" else []
    monkeypatch.setattr("sys.stdin", io.StringIO(pasted + "\n"))
    gate.handler = lambda path, body, client: (503, json.dumps({"error": "locked"}))

    code = cli.main(
        [
            "pair",
            "--port",
            str(gate.port),
            "--name",
            "agent-invented",
            *invitation,
            "--directory",
            str(tmp_path / "kept"),
        ]
    )

    assert code == cli.FAILED
    assert "DatabaseLocked" in capsys.readouterr().err, "it reached the gate"


@pytest.mark.parametrize("ended", [False, True], ids=["typed", "input ended at the prompt"])
def test_at_a_terminal_the_invitation_is_asked_for_and_not_echoed(
    ended: bool,
    tmp_path: Path,
    gate: FakeGate,
    monkeypatch: pytest.MonkeyPatch,
    capsys: pytest.CaptureFixture[str],
) -> None:
    # The same read, made through `getpass` when a person is typing: a prompt, so the command
    # does not look hung, and a star for each character rather than the character, so a paste is
    # seen to land and the invitation is not left on the screen. Input ending at the prompt is an
    # empty invitation, refused as a pipe's empty line is.
    from pykeepassgate import token as token_module

    prompts: list[str] = []
    echoes: list[str | None] = []

    def typed(
        prompt: str = "Password: ", stream: object = None, *, echo_char: str | None = None
    ) -> str:
        prompts.append(prompt)
        echoes.append(echo_char)
        if ended:
            raise EOFError
        return token_module.encode(bytes(range(32)))

    class Terminal:
        def isatty(self) -> bool:
            return True

    monkeypatch.setattr("sys.stdin", Terminal())
    monkeypatch.setattr("getpass.getpass", typed)
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

    assert code == 1
    assert prompts == ["Invitation: "]
    assert echoes == ["*"]
    assert ("MalformedToken" if ended else "DatabaseLocked") in capsys.readouterr().err


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


@pytest.mark.parametrize("spoiled", ["with a space inside it", "with a character outside it"])
def test_an_invitation_refused_as_malformed_is_not_repeated_back(
    spoiled: str, tmp_path: Path, capsys: pytest.CaptureFixture[str]
) -> None:
    # Nothing was sent, so the invitation is still good, and a refusal that quoted it would put it
    # wherever standard error goes. A space inside it is one no stripping takes away, so it is the
    # length that refuses it; the one below it fails the alphabet instead.
    from pykeepassgate import token as token_module

    invitation = token_module.encode(bytes(range(32)))
    pasted = (
        invitation[:21] + " " + invitation[21:]
        if spoiled == "with a space inside it"
        else invitation[:-1] + "!"
    )

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
    assert f"expires: {bundle['expires']}" in printed, "the lifetime the operator picked"


def test_pairing_from_the_command_line_passes_on_what_it_was_given(
    tmp_path: Path,
    gate: FakeGate,
    bundle: dict[str, Any],
    monkeypatch: pytest.MonkeyPatch,
) -> None:
    # No `--connection-timeout`, which is the case that matters: saying nothing has to leave the
    # pairing its own longer timeout rather than the ordinary one, or the command hangs up on an
    # operator who is still reading. And a profile rather than a directory, because saving
    # writes in place and a profile that did not arrive stores over the default identity.
    from test_pairing import INVITATION, Gate, spy_on_pairing

    monkeypatch.setenv("LOCALAPPDATA", str(tmp_path))
    monkeypatch.setenv("XDG_CONFIG_HOME", str(tmp_path))
    handler = Gate(gate, gate.host_certificate, bundle=bundle)
    gate.handler = handler
    built = spy_on_pairing(monkeypatch)

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
            "--profile",
            "work",
            "--host",
            "gate.invalid",
        ]
    )

    assert code == 0
    assert built == [
        {
            "port": gate.port,
            "connection_timeout": transport.PAIRING_CONNECTION_TIMEOUT,
            "host": "gate.invalid",
            "dial": transport.create_connection,
            "max_response_bytes": transport.MAX_RESPONSE_BYTES,
        }
    ]
    assert handler.asked is not None
    assert handler.asked["profiles"] == ["reader", "writer"]
    assert api.load(api.default_directory("work")).port == gate.port
    assert not api.default_directory().exists(), "nothing was kept under the default profile"


def test_revoking_gives_up_the_admission_and_removes_what_was_stored(
    tmp_path: Path, gate: FakeGate, bundle: dict[str, Any], capsys: pytest.CaptureFixture[str]
) -> None:
    # Removed because nothing can use it any more: a private key kept for a certificate the gate
    # refuses protects nothing. Pairing again stores a new one in the same place.
    gate.handler = lambda path, body, client: (200, json.dumps({"ok": True}))
    where = tmp_path / "kept"
    credentials_module.save(where, gate.port, bundle)

    code = cli.main(["revoke", "--directory", str(where)])

    printed = capsys.readouterr().out
    assert code == 0
    assert gate.requests == [("DELETE", "/gate/certificate")]
    assert "revoked CN=agent-invented" in printed
    assert not where.exists()


@pytest.mark.parametrize("error", ["not saved", "not verified"])
def test_a_revocation_the_gate_did_not_confirm_keeps_what_was_stored(
    tmp_path: Path, gate: FakeGate, bundle: dict[str, Any], error: str,
    capsys: pytest.CaptureFixture[str],
) -> None:
    # `not saved` leaves the client admitted, so the files are still its identity. `not verified`
    # is a revocation the operator may yet lose, and the next `status` says which it was.
    gate.handler = lambda path, body, client: (500, json.dumps({"error": error}))
    where = tmp_path / "kept"
    credentials_module.save(where, gate.port, bundle)

    code = cli.main(["revoke", "--directory", str(where)])

    assert code == cli.FAILED
    assert error in capsys.readouterr().err
    assert api.load(where).subject == "CN=agent-invented"


@pytest.mark.parametrize(
    ("expires", "asked"),
    [
        ("2126-08-15T09:00:00Z", [("DELETE", "/gate/certificate")]),
        ("2001-02-03T04:05:06Z", []),
    ],
)
def test_a_certificate_the_gate_already_refuses_counts_as_revoked(
    tmp_path: Path,
    gate: FakeGate,
    bundle: dict[str, Any],
    capsys: pytest.CaptureFixture[str],
    expires: str,
    asked: list[tuple[str, str]],
) -> None:
    # Revoked on the Clients page, left refused by an earlier `not verified`, or expired, which is
    # not dialed at all: what was asked for has happened, and a key kept for a certificate the gate
    # refuses protects nothing, so it goes as it would after a `200`.
    gate.handler = lambda path, body, client: (401, json.dumps({"error": "unpaired"}))
    where = tmp_path / "kept"
    credentials_module.save(where, gate.port, {**bundle, "expires": expires})

    code = cli.main(["revoke", "--directory", str(where)])

    printed = capsys.readouterr().out
    assert code == 0
    assert gate.requests == asked
    assert "the gate already refuses CN=agent-invented" in printed
    assert f"removed what was stored in {where}" in printed
    assert not where.exists()


def test_pairing_asks_for_the_lifetime_it_was_given(monkeypatch: pytest.MonkeyPatch) -> None:
    # Days on the command line, a `timedelta` in the library, and nothing at all when nothing was
    # said, which the dialog answers by offering a year.
    asked: list[timedelta | None] = []

    def pair(**arguments: Any) -> Any:
        asked.append(arguments["lifetime"])
        raise StorageError("stop here")

    monkeypatch.setattr(api, "pair", pair)
    typed = ["pair", "--port", "1", "--name", "agent", "--invitation", "x"]

    cli.main(typed)
    cli.main([*typed, "--lifetime", "30"])

    assert asked == [None, timedelta(days=30)]


@pytest.mark.parametrize("days", ["0", "366", "-7", "1.5", "a week", "99999999999"])
def test_a_lifetime_no_certificate_can_have_is_an_argument_error(
    days: str, capsys: pytest.CaptureFixture[str], monkeypatch: pytest.MonkeyPatch
) -> None:
    # Exit code 2, rather than the library's `ValueError` leaving `main` as a traceback. With an
    # invitation given and the library stood in for, a value the argument took would reach the
    # library rather than be caught by reading standard input, so only the refusal passes.
    def pair(**arguments: Any) -> Any:
        raise AssertionError(f"{arguments['lifetime']} reached the library")

    monkeypatch.setattr(api, "pair", pair)

    with pytest.raises(SystemExit) as raised:
        cli.main(
            ["pair", "--port", "1", "--name", "agent", "--invitation", "x", "--lifetime", days]
        )

    assert raised.value.code == 2
    assert "days" in capsys.readouterr().err


def test_pairing_passes_the_shared_directory_choice_to_the_library(
    monkeypatch: pytest.MonkeyPatch,
) -> None:
    # The flag is a front end to the parameter and nothing more, so what is pinned is that it
    # arrives, and that saying nothing arrives as the refusal.
    asked: list[bool] = []

    def pair(**arguments: Any) -> Any:
        asked.append(arguments["allow_shared_directory"])
        raise StorageError("stop here")

    monkeypatch.setattr(api, "pair", pair)
    typed = ["pair", "--port", "1", "--name", "agent", "--invitation", "x"]

    cli.main(typed)
    cli.main([*typed, "--allow-shared-directory"])

    assert asked == [False, True]


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


@pytest.mark.parametrize(
    "option",
    [
        "--connection-timeout",
        "--retry-busy-timeout",
        "--retry-locked-timeout",
        "--retry-undecided-timeout",
        "--max-response-bytes",
    ],
)
def test_whoami_takes_no_option_about_a_connection_it_never_makes(
    option: str, tmp_path: Path
) -> None:
    # `whoami` answers out of the stored identity and never dials, so an option about the socket
    # or about waiting out an answer could change nothing it does. Refused, exit code 2, rather
    # than accepted and ignored, which is what `--connection-timeout` used to be here.
    with pytest.raises(SystemExit) as raised:
        cli.main(["whoami", "--directory", str(tmp_path / "kept"), option, "5"])

    assert raised.value.code == 2


@pytest.mark.parametrize("profile", ["../escaped", "a/b", "", "work."])
def test_a_profile_that_is_a_path_is_an_argument_error(
    profile: str, capsys: pytest.CaptureFixture[str]
) -> None:
    # `pair --profile ../escaped` stored the identity beside the profiles directory and said so,
    # exit 0, and `whoami --profile /tmp` looked for one there. A path is `--directory`.
    with pytest.raises(SystemExit) as raised:
        cli.main(["whoami", "--profile", profile])

    assert raised.value.code == 2
    assert "not one directory's name" in capsys.readouterr().err


def test_a_pairing_under_a_profile_that_is_a_path_touches_nothing(
    gate: FakeGate, tmp_path: Path, monkeypatch: pytest.MonkeyPatch
) -> None:
    # The library's own refusal, which `api.pair` meets before `prepare` opens a file.
    monkeypatch.setenv("LOCALAPPDATA", str(tmp_path))
    monkeypatch.setenv("XDG_CONFIG_HOME", str(tmp_path))

    with pytest.raises(ValueError, match="not one directory's name"):
        api.pair(
            port=gate.port,
            invitation="A" * 43,
            name="agent-invented",
            profile="../escaped",
        )

    assert not (tmp_path / "pykeepassgate").exists()
    assert not (tmp_path / "escaped").exists()
    assert gate.names == []
