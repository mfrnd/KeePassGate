"""The commands that reach a database, which are a few lines over the library and stay that way."""

from __future__ import annotations

import base64
import json
from collections.abc import Callable
from pathlib import Path
from typing import Any

import pytest

from pykeepassgate import api, cli, transport
from pykeepassgate import credentials as credentials_module
from pykeepassgate.testing import FakeGate

UUID = "7b1c" * 8


@pytest.fixture
def kept(tmp_path: Path, bundle: dict[str, Any], gate: FakeGate) -> Path:
    """A paired identity on disk, pointed at this gate."""
    credentials_module.save(tmp_path / "kept", gate.port, bundle)
    return tmp_path / "kept"


def answering(gate: FakeGate, body: dict[str, Any]) -> None:
    """Answer every request with this."""
    gate.handler = lambda path, sent, client: (200, json.dumps(body))


def given(rest: dict[str, Any]) -> dict[str, Any]:
    """The settings a command passed on, less the key password every command offers.

    That one is always a callable, which asks at a terminal only if the key is encrypted, so it
    is not one of the options whose silence leaves the library's default.
    """
    assert callable(rest.get("key_password")), "the key password is offered by every command"
    return {name: value for name, value in rest.items() if name != "key_password"}


def test_whoami_says_who_the_gate_admits_this_client_as(
    gate: FakeGate, kept: Path, capsys: pytest.CaptureFixture[str]
) -> None:
    # What holds now, which is not what pairing or the last renewal stored when a profile has been
    # removed since.
    answering(
        gate,
        {
            "subject": "CN=agent-invented",
            "clientKey": "3a" * 32,
            "admissionKey": "5f" * 32,
            "expires": "2027-02-03T04:05:06Z",
            "profiles": ["reader", "writer"],
            "removedProfiles": [],
        },
    )

    assert cli.main(["whoami", "--directory", str(kept)]) == 0

    assert gate.requests == [("GET", "/gate/whoami")]
    assert capsys.readouterr().out.splitlines() == [
        "subject:          CN=agent-invented",
        f"client key:       {'3a' * 32}",
        f"admission key:    {'5f' * 32}",
        "expires:          2027-02-03T04:05:06Z",
        "profiles:         reader, writer",
        "removed profiles: none",
    ]


def test_whoami_names_a_profile_the_database_no_longer_holds(
    gate: FakeGate, kept: Path, capsys: pytest.CaptureFixture[str]
) -> None:
    answering(
        gate,
        {
            "subject": "CN=agent-invented",
            "clientKey": "3a" * 32,
            "admissionKey": "5f" * 32,
            "expires": "2027-02-03T04:05:06Z",
            "profiles": [],
            "removedProfiles": ["reader"],
        },
    )

    assert cli.main(["whoami", "--directory", str(kept)]) == 0

    printed = capsys.readouterr().out.splitlines()
    assert "profiles:         none" in printed
    assert "removed profiles: reader" in printed


def test_groups_are_printed_one_to_a_line(
    gate: FakeGate, kept: Path, capsys: pytest.CaptureFixture[str]
) -> None:
    answering(gate, {"groups": [{"uuid": UUID, "name": "Fictitious", "parent": None}]})

    assert cli.main(["groups", "--directory", str(kept)]) == 0

    printed = capsys.readouterr().out
    assert UUID in printed
    assert "Fictitious" in printed


def test_connection_timeout_reaches_the_client_rather_than_being_parsed_and_dropped(
    gate: FakeGate, kept: Path, monkeypatch: pytest.MonkeyPatch
) -> None:
    """Every command offers `--connection-timeout` and seven of them used to ignore it.

    Accepted, parsed and thrown away is worse than not offered: a caller who asked for longer got
    the default and nothing said so, which on a slow database looks like the gate being broken.
    """
    answering(gate, {"groups": []})
    asked: list[float | None] = []

    opened = api.GateClient.open

    def watching(
        directory: Path | None = None,
        profile: str = "default",
        host: str = transport.LOOPBACK,
        **rest: Any,
    ) -> Any:
        asked.append(rest.get("connection_timeout"))
        return opened(directory, profile, host=host, **rest)

    monkeypatch.setattr(api.GateClient, "open", watching)

    assert cli.main(["groups", "--directory", str(kept), "--connection-timeout", "12.5"]) == 0
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


def _bodies(gate: FakeGate) -> list[dict[str, Any]]:
    """Answer every request as done, keeping each body sent."""
    sent: list[dict[str, Any]] = []

    def answer(path: str, body: str, client: bytes | None) -> tuple[int, str]:
        sent.append(json.loads(body))
        return 200, json.dumps({"ok": True})

    gate.handler = answer
    return sent


def test_update_writes_what_was_named_and_nothing_else(
    gate: FakeGate, kept: Path, capsys: pytest.CaptureFixture[str]
) -> None:
    sent = _bodies(gate)

    assert cli.main(["update", UUID, "--title", "renamed", "--directory", str(kept)]) == 0

    assert sent[-1] == {"Title": "renamed"}
    assert gate.requests[-1] == ("PUT", f"/entries/{UUID}")
    assert capsys.readouterr().out.strip() == f"updated {UUID}"


def test_update_reads_a_new_password_rather_than_arguing_it(
    gate: FakeGate, kept: Path, monkeypatch: pytest.MonkeyPatch
) -> None:
    sent = _bodies(gate)
    monkeypatch.setattr("sys.stdin", _Line("  not a real password, rotated\n"))

    assert cli.main(["update", UUID, "--password", "--directory", str(kept)]) == 0

    assert sent[-1] == {"Password": "  not a real password, rotated"}


def test_update_takes_an_expiry_off_with_never(gate: FakeGate, kept: Path) -> None:
    sent = _bodies(gate)

    assert cli.main(["update", UUID, "--expires", "never", "--directory", str(kept)]) == 0

    assert sent[-1] == {"expires": ""}


@pytest.mark.parametrize("given", ["auto", "2031-02-03T04:05:06", "tomorrow"])
def test_update_refuses_an_expiry_it_cannot_send_as_an_argument(
    given: str, gate: FakeGate, kept: Path
) -> None:
    with pytest.raises(SystemExit) as raised:
        cli.main(["update", UUID, "--expires", given, "--directory", str(kept)])

    assert raised.value.code == 2
    assert gate.requests == []


def test_update_with_nothing_to_change_is_refused_before_anything_is_dialed(
    gate: FakeGate, kept: Path, capsys: pytest.CaptureFixture[str]
) -> None:
    # A body naming nothing writes nothing, and a command saying "updated" for it would read to a
    # script exactly like a change that landed.
    assert cli.main(["update", UUID, "--directory", str(kept)]) == 2

    assert "needs something to change" in capsys.readouterr().err
    assert gate.requests == []


def test_attachments_are_listed_with_their_sizes(
    gate: FakeGate, kept: Path, capsys: pytest.CaptureFixture[str]
) -> None:
    answering(gate, {"attachments": [{"name": "notes.txt", "size": 22}]})

    assert cli.main(["attachments", UUID, "--directory", str(kept)]) == 0

    printed = capsys.readouterr().out
    assert "notes.txt" in printed
    assert "22" in printed


@pytest.mark.parametrize(
    ("command", "answer", "line"),
    [
        (
            ["groups"],
            {"groups": [{"uuid": UUID, "name": "Real\r\x1b[2KForged", "parent": None}]},
            f"{UUID}  Real\\r\\x1b[2KForged",
        ),
        (
            ["entries"],
            {
                "entries": [
                    {"uuid": UUID, "group": "g", "title": "Real\N{LINE SEPARATOR}Forged", "url": ""}
                ],
                "count": 1,
            },
            f"{UUID}  Real\\u2028Forged",
        ),
        (
            ["show", UUID],
            {"uuid": UUID, "group": "g", "fields": {"Real\n\x85Forged": "x"}, "protected": []},
            "Real\\n\\x85Forged: x",
        ),
        (
            ["attachments", UUID],
            {"attachments": [{"name": "a.txt\n      2048  invoice.pdf", "size": 22}]},
            "        22  a.txt\\n      2048  invoice.pdf",
        ),
    ],
    ids=["groups", "entries", "show", "attachments"],
)
def test_a_name_that_would_break_a_line_is_printed_on_the_one_it_belongs_to(
    command: list[str],
    answer: dict[str, Any],
    line: str,
    gate: FakeGate,
    kept: Path,
    capsys: pytest.CaptureFixture[str],
) -> None:
    # Whoever may create a name chooses it, and that can be another client of the same database.
    # Printed raw, a line feed in an attachment's name lists an attachment that does not exist,
    # and a carriage return followed by an erase hides the real name behind the forged one.
    answering(gate, answer)

    assert cli.main([*command, "--directory", str(kept)]) == 0
    assert line in capsys.readouterr().out.splitlines()


def test_show_keeps_a_value_on_the_line_of_the_field_it_belongs_to(
    gate: FakeGate, kept: Path, capsys: pytest.CaptureFixture[str]
) -> None:
    # A client that may change the notes and not the protected password could otherwise put a
    # `Password:` row of its own above the real one, and a script reading the first match would
    # take it. Notes written over several lines print on one, with `\n` where each break was.
    answering(
        gate,
        {
            "uuid": UUID,
            "group": "g",
            "fields": {
                "Notes": "first line\nPassword: planted (protected)",
                "Password": "not-a-real-password",
            },
            "protected": ["Password"],
        },
    )

    assert cli.main(["show", UUID, "--directory", str(kept)]) == 0

    printed = capsys.readouterr().out.splitlines()
    assert "Notes: first line\\nPassword: planted (protected)" in printed
    assert [line for line in printed if line.startswith("Password:")] == [
        "Password: not-a-real-password (protected)"
    ]


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


@pytest.mark.parametrize("where", ["a directory", "in a folder that is not there"])
def test_fetch_to_a_place_that_cannot_be_written_is_a_wrong_argument(
    where: str, gate: FakeGate, kept: Path, tmp_path: Path, capsys: pytest.CaptureFixture[str]
) -> None:
    # One line and exit code 2, rather than a traceback and the exit code a gate failure has:
    # the gate answered, and the place given for the file is what cannot be used.
    content = b"an invented attachment"
    answering(
        gate,
        {
            "name": "notes.txt",
            "size": len(content),
            "content": base64.b64encode(content).decode("ascii"),
        },
    )
    to = tmp_path if where == "a directory" else tmp_path / "missing" / "fetched.bin"

    assert cli.main(["fetch", UUID, "notes.txt", "--to", str(to), "--directory", str(kept)]) == 2
    printed = capsys.readouterr().err
    assert to.name in printed
    assert len(printed.strip().splitlines()) == 1


def test_fetch_writes_nothing_for_an_attachment_that_came_without_its_bytes(
    kept: Path, tmp_path: Path, monkeypatch: pytest.MonkeyPatch, capsys: pytest.CaptureFixture[str]
) -> None:
    # The library refuses such an answer before it gets here, so this stands in for whatever
    # later path might not. An empty file reported as written would be a successful download of
    # nothing, which is the guess `Attachment.read` refuses.
    def attachment(self: api.GateClient, uuid: str, name: str) -> api.Attachment:
        return api.Attachment(name=name, size=22)

    monkeypatch.setattr(api.GateClient, "attachment", attachment)
    where = tmp_path / "fetched.bin"

    assert cli.main(["fetch", UUID, "notes.txt", "--to", str(where), "--directory", str(kept)]) == 1
    assert "ProtocolError" in capsys.readouterr().err
    assert not where.exists()


def test_a_refusal_becomes_an_exit_code_rather_than_a_stack_trace(
    gate: FakeGate, kept: Path, capsys: pytest.CaptureFixture[str]
) -> None:
    gate.handler = lambda path, body, client: (404, '{"error":"no such route"}')

    assert cli.main(["show", UUID, "--directory", str(kept)]) == cli.FAILED
    assert "RouteRefused" in capsys.readouterr().err


def test_a_command_dials_the_host_it_was_given(
    gate: FakeGate, kept: Path, capsys: pytest.CaptureFixture[str]
) -> None:
    # `--host` is the local end of a tunnel, and `localhost` is this gate's own socket by another
    # name, so the request arriving is what says the option reached the socket rather than being
    # dropped on the way down.
    answering(gate, {"groups": []})

    assert cli.main(["groups", "--host", "localhost", "--directory", str(kept)]) == 0
    assert gate.requests == [("GET", "/groups")]


def test_a_command_that_dials_nowhere_says_where_it_tried(
    gate: FakeGate, kept: Path, capsys: pytest.CaptureFixture[str]
) -> None:
    assert cli.main(["groups", "--host", "127.0.0.2", "--directory", str(kept)]) == cli.FAILED
    assert "127.0.0.2" in capsys.readouterr().err


def test_identity_is_not_offered_a_host_because_it_opens_nothing(kept: Path) -> None:
    # An option that cannot change what a command does is an option that misleads whoever reads
    # the help, and this one answers out of the file rather than off a socket.
    with pytest.raises(SystemExit) as raised:
        cli.main(["identity", "--host", "localhost", "--directory", str(kept)])

    assert raised.value.code == 2


class _Line:
    """Standard input with one line in it, piped rather than typed."""

    def __init__(self, text: str) -> None:
        self._text = text

    def isatty(self) -> bool:
        return False

    def readline(self) -> str:
        return self._text


class _Terminal:
    """Standard input that is a terminal, which nothing may read a line from."""

    def isatty(self) -> bool:
        return True

    def readline(self) -> str:
        raise AssertionError("a terminal is asked through getpass, not read a line from")


def test_at_a_terminal_the_password_is_asked_for_and_shown_as_stars(
    gate: FakeGate, kept: Path, monkeypatch: pytest.MonkeyPatch
) -> None:
    # A bare read at a terminal printed no prompt, so the command looked hung, and echoed the
    # password that reading it was meant to keep out of sight. `getpass` does neither, and shows
    # a star for each character so that a paste is seen to land.
    sent: list[dict[str, Any]] = []
    prompts: list[str] = []

    def answer(path: str, body: str, client: bytes | None) -> tuple[int, str]:
        sent.append(json.loads(body))
        return 200, json.dumps({"uuid": UUID})

    def typed(
        prompt: str = "Password: ", stream: object = None, *, echo_char: str | None = None
    ) -> str:
        assert echo_char == "*"
        prompts.append(prompt)
        return "  not a real password "

    gate.handler = answer
    monkeypatch.setattr("sys.stdin", _Terminal())
    monkeypatch.setattr("getpass.getpass", typed)

    assert cli.main(["add", "--title", "x", "--password", "--directory", str(kept)]) == 0
    assert prompts == ["Password for the new entry: "]
    assert sent[-1]["Password"] == "  not a real password "


def test_the_retry_budgets_reach_the_client_and_nothing_is_passed_for_what_was_not_given(
    gate: FakeGate, kept: Path, monkeypatch: pytest.MonkeyPatch
) -> None:
    """All three options, and the silence that is the default.

    Passing nothing for an option nobody gave is what lets each caller keep its own default, and
    for these three the default is a decision rather than a number: `None` means do not retry.
    """
    answering(gate, {"groups": []})
    asked: list[dict[str, Any]] = []

    opened = api.GateClient.open

    def watching(
        directory: Path | None = None,
        profile: str = "default",
        host: str = transport.LOOPBACK,
        **rest: Any,
    ) -> Any:
        asked.append(given(rest))
        return opened(directory, profile, host=host, **rest)

    monkeypatch.setattr(api.GateClient, "open", watching)

    assert cli.main(["groups", "--directory", str(kept)]) == 0
    assert cli.main(
        [
            "groups",
            "--directory",
            str(kept),
            "--retry-busy-timeout",
            "30",
            "--retry-locked-timeout",
            "60",
            "--retry-undecided-timeout",
            "90",
        ]
    ) == 0

    assert asked == [
        {},
        {
            "retry_busy_timeout": 30.0,
            "retry_locked_timeout": 60.0,
            "retry_undecided_timeout": 90.0,
        },
    ]


def test_pairing_takes_the_retry_budgets_as_well(
    gate: FakeGate, tmp_path: Path, monkeypatch: pytest.MonkeyPatch
) -> None:
    # Pairing is where a `503 busy` is most likely of all: one pairing is put to the operator at
    # a time, so a second that cannot get the screen is refused with nothing spent.
    asked: dict[str, Any] = {}

    def watching(**rest: Any) -> Any:
        asked.update(rest)
        raise api.PairingRefused("not going any further than this")

    monkeypatch.setattr(api, "pair", watching)

    assert cli.main(
        [
            "pair",
            "--port",
            str(gate.port),
            "--name",
            "agent-invented",
            "--invitation",
            "x",
            "--directory",
            str(tmp_path / "kept"),
            "--retry-busy-timeout",
            "45",
            "--retry-undecided-timeout",
            "600",
        ]
    ) == 1

    assert asked["retry_busy_timeout"] == 45.0
    assert asked["retry_undecided_timeout"] == 600.0
    assert "retry_locked_timeout" not in asked


def test_the_response_cap_reaches_the_client_and_nothing_is_passed_when_it_was_not_given(
    gate: FakeGate, kept: Path, monkeypatch: pytest.MonkeyPatch
) -> None:
    # Saying nothing leaves the library's default, which is the one place that number lives.
    answering(gate, {"groups": []})
    asked: list[dict[str, Any]] = []

    opened = api.GateClient.open

    def watching(
        directory: Path | None = None,
        profile: str = "default",
        host: str = transport.LOOPBACK,
        **rest: Any,
    ) -> Any:
        asked.append(given(rest))
        return opened(directory, profile, host=host, **rest)

    monkeypatch.setattr(api.GateClient, "open", watching)

    assert cli.main(["groups", "--directory", str(kept)]) == 0
    assert cli.main(
        ["groups", "--directory", str(kept), "--max-response-bytes", "16777216"]
    ) == 0

    assert asked == [{}, {"max_response_bytes": 16777216}]


def test_a_cap_the_client_was_given_is_the_one_it_reads_under(
    gate: FakeGate, kept: Path, capsys: pytest.CaptureFixture[str]
) -> None:
    # All the way down, rather than stopping at the keyword: the answer is over the cap given.
    answering(gate, {"groups": [{"uuid": UUID, "name": "x" * 4096, "parent": None}]})

    assert cli.main(["groups", "--directory", str(kept), "--max-response-bytes", "1024"]) == 1

    printed = capsys.readouterr().err
    assert printed.startswith("ProtocolError: ")
    assert "over 1024 bytes" in printed
    assert "--max-response-bytes" in printed, "the message names what raises the cap"


def test_pairing_takes_the_response_cap_as_well(
    gate: FakeGate, tmp_path: Path, monkeypatch: pytest.MonkeyPatch
) -> None:
    asked: dict[str, Any] = {}

    def watching(**rest: Any) -> Any:
        asked.update(rest)
        raise api.PairingRefused("not going any further than this")

    monkeypatch.setattr(api, "pair", watching)

    assert cli.main(
        [
            "pair",
            "--port",
            str(gate.port),
            "--name",
            "agent-invented",
            "--invitation",
            "x",
            "--directory",
            str(tmp_path / "kept"),
            "--max-response-bytes",
            "65536",
        ]
    ) == 1

    assert asked["max_response_bytes"] == 65536


@pytest.mark.parametrize("given", ["0", "-5", "4MiB", "1.5"])
def test_a_cap_that_is_not_a_positive_number_of_bytes_is_refused_as_an_argument(
    given: str, kept: Path, gate: FakeGate
) -> None:
    # Exit code 2, which the module reserves for arguments that are wrong, and nothing dialed.
    with pytest.raises(SystemExit) as raised:
        cli.main(["groups", "--directory", str(kept), "--max-response-bytes", given])

    assert raised.value.code == 2
    assert gate.requests == []


@pytest.mark.parametrize(
    ("option", "given"),
    [
        ("--connection-timeout", "0"),
        ("--connection-timeout", "-1"),
        ("--connection-timeout", "nan"),
        ("--connection-timeout", "inf"),
        ("--connection-timeout", "soon"),
        ("--retry-busy-timeout", "-1"),
        ("--retry-busy-timeout", "nan"),
        ("--retry-locked-timeout", "inf"),
        ("--retry-undecided-timeout", "-0.5"),
    ],
)
def test_a_timeout_that_is_not_a_number_of_seconds_is_refused_as_an_argument(
    option: str, given: str, kept: Path, gate: FakeGate
) -> None:
    # Exit code 2 and nothing dialed. Passed on, a zero socket timeout dialed non-blocking and
    # was reported as nothing listening, exit 1; a negative, NaN or infinite one left `main` as a
    # traceback; and a NaN budget, which no delay is ever larger than, never ran out.
    with pytest.raises(SystemExit) as raised:
        cli.main(["groups", "--directory", str(kept), option, given])

    assert raised.value.code == 2
    assert gate.requests == []


@pytest.mark.parametrize("given", ["0", "-1", "65536", "70000", "port"])
def test_a_port_tcp_does_not_have_is_refused_as_an_argument(given: str, tmp_path: Path) -> None:
    # Rather than dialed, and reported as nothing listening there, exit 1, which a script cannot
    # tell from a gate that is not running.
    with pytest.raises(SystemExit) as raised:
        cli.main(
            [
                "pair",
                "--port",
                given,
                "--name",
                "agent-invented",
                "--invitation",
                "x",
                "--directory",
                str(tmp_path / "kept"),
            ]
        )

    assert raised.value.code == 2


def test_the_edges_of_every_numeric_option_are_accepted() -> None:
    # The other side of the refusals above: the checks refuse what cannot work and nothing else.
    # A budget of zero is a decision to wait for nothing, and the smallest positive socket
    # timeout is still a socket timeout.
    parsed = cli._parser().parse_args(
        [
            "pair",
            "--port",
            "65535",
            "--name",
            "agent-invented",
            "--connection-timeout",
            "0.5",
            "--retry-busy-timeout",
            "0",
            "--retry-locked-timeout",
            "3600",
            "--retry-undecided-timeout",
            "86400",
        ]
    )

    assert parsed.port == 65535
    assert parsed.connection_timeout == 0.5
    assert parsed.retry_busy_timeout == 0.0
    assert parsed.retry_locked_timeout == 3600.0
    assert parsed.retry_undecided_timeout == 86400.0
    assert cli._parser().parse_args(["pair", "--port", "1", "--name", "agent"]).port == 1


#: Every number the command line refuses as an argument, by the option that refuses it.
REFUSED_BY_BOTH: list[tuple[str, str]] = [
    ("--port", "0"),
    ("--port", "65536"),
    ("--connection-timeout", "0"),
    ("--connection-timeout", "-1"),
    ("--connection-timeout", "nan"),
    ("--connection-timeout", "inf"),
    ("--retry-busy-timeout", "-1"),
    ("--retry-busy-timeout", "nan"),
    ("--retry-locked-timeout", "inf"),
    ("--retry-undecided-timeout", "nan"),
    ("--max-response-bytes", "0"),
    ("--max-response-bytes", "-5"),
]

#: What the library asks of the same setting, given the text converted as the option converts it.
LIBRARY: dict[str, Callable[[str], object]] = {
    "--port": lambda text: transport.port_number(int(text)),
    "--connection-timeout": lambda text: transport.connection_limit(float(text)),
    "--retry-busy-timeout": lambda text: transport.retry_limit("retry_busy_timeout", float(text)),
    "--retry-locked-timeout": lambda text: transport.retry_limit(
        "retry_locked_timeout", float(text)
    ),
    "--retry-undecided-timeout": lambda text: transport.retry_limit(
        "retry_undecided_timeout", float(text)
    ),
    "--max-response-bytes": lambda text: transport.response_limit(int(text)),
}


@pytest.mark.parametrize(("option", "given"), REFUSED_BY_BOTH)
def test_the_library_refuses_every_number_the_command_line_refuses(
    option: str, given: str
) -> None:
    # The command line does nothing the library cannot, and it refuses nothing the library takes
    # either: a program calling `api.pair` or `GateClient` meets each of these as a `ValueError`
    # naming the setting, where the command line exits 2.
    with pytest.raises(SystemExit) as raised:
        cli._parser().parse_args(["pair", "--port", "1", "--name", "agent", option, given])

    assert raised.value.code == 2

    with pytest.raises(ValueError):
        LIBRARY[option](given)


def test_entries_keep_the_expired_ones_unless_told_to_leave_them_out(
    gate: FakeGate, kept: Path
) -> None:
    answering(gate, {"entries": [], "count": 0})

    assert cli.main(["entries", "--directory", str(kept)]) == 0
    assert gate.requests[-1] == ("GET", "/entries")

    assert cli.main(["entries", "--directory", str(kept), "--no-include-expired"]) == 0
    assert gate.requests[-1] == ("GET", "/entries?includeExpired=false")


@pytest.mark.parametrize(
    ("said", "printed"),
    [
        (None, "expires: never"),
        ("2001-02-03T04:05:06Z", "expires: 2001-02-03T04:05:06Z (expired)"),
        ("2999-02-03T04:05:06Z", "expires: 2999-02-03T04:05:06Z\n"),
    ],
    ids=["never", "expired", "later"],
)
def test_show_says_when_an_entry_expires(
    gate: FakeGate,
    kept: Path,
    capsys: pytest.CaptureFixture[str],
    said: str | None,
    printed: str,
) -> None:
    answering(gate, {"uuid": UUID, "group": UUID, "fields": {}, "protected": [], "expires": said})

    assert cli.main(["show", UUID, "--directory", str(kept)]) == 0
    assert printed in capsys.readouterr().out


def test_show_says_nothing_about_an_expiry_the_gate_said_nothing_about(
    gate: FakeGate, kept: Path, capsys: pytest.CaptureFixture[str]
) -> None:
    # Not "never", which would be this client answering for the gate.
    answering(gate, {"uuid": UUID, "group": UUID, "fields": {}, "protected": []})

    assert cli.main(["show", UUID, "--directory", str(kept)]) == 0
    assert "expires" not in capsys.readouterr().out


@pytest.mark.parametrize(
    ("given", "sent"),
    [("auto", "auto"), ("2031-02-03T06:05:06+02:00", "2031-02-03T04:05:06Z")],
    ids=["auto", "a moment in another zone"],
)
def test_add_gives_the_entry_the_expiry_it_was_given(
    gate: FakeGate, kept: Path, given: str, sent: str
) -> None:
    bodies: list[dict[str, Any]] = []

    def answer(path: str, body: str, client: bytes | None) -> tuple[int, str]:
        bodies.append(json.loads(body))
        return 201, json.dumps({"uuid": UUID})

    gate.handler = answer

    assert cli.main(["add", "--title", "x", "--expires", given, "--directory", str(kept)]) == 0
    assert bodies[-1] == {"Title": "x", "expires": sent}


@pytest.mark.parametrize("given", ["2031-02-03T04:05:06", "tomorrow", "AUTO"])
def test_an_expiry_that_names_no_instant_is_refused_as_an_argument(
    given: str, kept: Path, gate: FakeGate
) -> None:
    with pytest.raises(SystemExit) as raised:
        cli.main(["add", "--title", "x", "--expires", given, "--directory", str(kept)])

    assert raised.value.code == 2
    assert gate.requests == []


def test_a_key_that_will_not_load_is_an_exit_code_rather_than_a_stack_trace(
    gate: FakeGate, kept: Path, capsys: pytest.CaptureFixture[str]
) -> None:
    # A file cut short by a disk that filled during a re-pair. Every command that dials reads
    # it, and each used to end in an `SSLError` traceback.
    identity = kept / credentials_module.IDENTITY_FILE
    identity.write_bytes(identity.read_bytes()[:300])

    assert cli.main(["status", "--directory", str(kept)]) == 1

    printed = capsys.readouterr().err
    assert printed.startswith("StorageError: ")
    assert credentials_module.IDENTITY_FILE in printed
    assert gate.names == []


def test_an_answer_the_client_cannot_read_is_an_exit_code_rather_than_a_stack_trace(
    gate: FakeGate, kept: Path, capsys: pytest.CaptureFixture[str]
) -> None:
    # A create answered without the identifier of what it made used to leave `main` as a
    # `KeyError`, which is no exit code a script can branch on.
    gate.handler = lambda path, body, client: (201, "{}")

    assert cli.main(["add", "--title", "x", "--directory", str(kept)]) == 1
    assert capsys.readouterr().err.startswith("ProtocolError: ")
