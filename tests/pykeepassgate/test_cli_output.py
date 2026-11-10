"""What `-o`/`--output` chooses, held to the rules the plain lines already keep.

`plain` is what every command printed before there was a choice, and the tests beside this one
still hold it to that. What is here is `json`: one document a program can read, saying what the
plain lines say, under the same two rules. A value the rule withholds is named as withheld and never
printed as empty, and what the gate did not say is left out rather than printed as nothing.
"""

from __future__ import annotations

import argparse
import base64
import io
import json
import os
import subprocess
import sys
import types
import warnings
from pathlib import Path
from typing import Any

import pytest

from pykeepassgate import __version__, cli
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
    """Every command the parser has, by name, a command made of others by each of those.

    `head` is made of one command for each kind of node, so it is `head entry` and the rest that
    are commands here, and `head` on its own is not a command here.
    """

    def chosen(parser: argparse.ArgumentParser) -> dict[str, argparse.ArgumentParser]:
        found: dict[str, argparse.ArgumentParser] = next(
            action for action in parser._actions if isinstance(action, argparse._SubParsersAction)
        ).choices
        return found

    every: dict[str, argparse.ArgumentParser] = {}
    for name, command in chosen(cli._parser()).items():
        if any(isinstance(action, argparse._SubParsersAction) for action in command._actions):
            every |= {f"{name} {kind}": each for kind, each in chosen(command).items()}
        else:
            every[name] = command

    return every


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


def test_show_prints_no_document_for_an_entry_read_without_its_expiry(
    gate: FakeGate, kept: Path, capsys: pytest.CaptureFixture[str]
) -> None:
    # A failure is never a document, and this client cannot read the answer.
    read = {"uuid": UUID, "group": GROUP, "fields": {"Title": "Invented"}, "protected": []}
    answering(gate, read)

    assert cli.main(["show", UUID, "--directory", str(kept), "-o", "json"]) == cli.FAILED
    assert capsys.readouterr().out == ""


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
        "profilesUnreadable": False,
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
    gate.version = None
    body = {"ok": False, "database": "abcdef01", "version": __version__, "revision": "0123abcd"}
    answering(gate, body)

    assert cli.main(["status", "--directory", str(kept), "-o", "json"]) == 1

    # What the gate answered is what was found, and the failure is the sentence beside it.
    out, error = capsys.readouterr()
    assert json.loads(out) == body
    assert error == "the gate answered that it is not serving this database: ok is false\n"


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
        (["add-group", "--name", "Invented"], {"uuid": UUID}),
    ],
    ids=["add", "update", "remove", "add-group"],
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


def test_plain_output_written_through_a_code_page_is_utf8_and_whole(
    gate: FakeGate, kept: Path, monkeypatch: pytest.MonkeyPatch
) -> None:
    # A file or a pipe on Windows is a stream in the code page, cp1252 here: the first name
    # beyond it raised `UnicodeEncodeError` part way through the listing, after the rows before
    # it were written, and the command ended in a traceback.
    answering(gate, {"attachments": [{"name": "a.pem", "size": 1}, {"name": "Ω.pem", "size": 2}]})
    written = io.BytesIO()
    monkeypatch.setattr("sys.stdout", io.TextIOWrapper(written, encoding="cp1252"))

    assert cli.main(["attachments", UUID, "--directory", str(kept)]) == 0

    sys.stdout.flush()
    lines = written.getvalue().decode("utf-8").splitlines()
    assert [line.split()[-1] for line in lines] == ["a.pem", "Ω.pem"]


def test_plain_output_is_written_in_the_encoding_pythonioencoding_names(
    gate: FakeGate, kept: Path, monkeypatch: pytest.MonkeyPatch
) -> None:
    # For a reader that decodes with another code page. What the code page lacks is written as its
    # escape, as UTF-8 writes a lone surrogate, rather than ending the listing part way through.
    monkeypatch.setenv("PYTHONIOENCODING", "cp437")
    answering(gate, {"attachments": [{"name": "ä.pem", "size": 1}, {"name": "€.pem", "size": 2}]})
    written = io.BytesIO()
    monkeypatch.setattr("sys.stdout", io.TextIOWrapper(written, encoding="cp1252"))

    assert cli.main(["attachments", UUID, "--directory", str(kept)]) == 0

    sys.stdout.flush()
    lines = written.getvalue().decode("cp437").splitlines()
    assert [line.split()[-1] for line in lines] == ["ä.pem", "\\u20ac.pem"]


def test_the_error_handler_pythonioencoding_names_is_kept_for_input_and_output(
    monkeypatch: pytest.MonkeyPatch,
) -> None:
    # Standard error keeps its escapes whatever the variable names, as Python's own reading of it
    # has it, so a sentence about a failure never fails to be written.
    monkeypatch.setenv("PYTHONIOENCODING", "ascii:replace")
    for name in ("stdin", "stdout", "stderr"):
        monkeypatch.setattr(f"sys.{name}", io.TextIOWrapper(io.BytesIO(), encoding="utf-8"))

    cli._encoded_streams()

    assert (sys.stdin.encoding, sys.stdin.errors) == ("ascii", "replace")
    assert (sys.stdout.encoding, sys.stdout.errors) == ("ascii", "replace")
    # Python's own name for the handler: cspell:ignore backslashreplace
    assert (sys.stderr.encoding, sys.stderr.errors) == ("ascii", "backslashreplace")


def test_utf8_named_by_pythonioencoding_is_read_as_utf8_is_without_it(
    monkeypatch: pytest.MonkeyPatch,
) -> None:
    # A byte order mark, which Windows PowerShell puts before what it sends as UTF-8, is still
    # dropped from input, and output still starts with none.
    monkeypatch.setenv("PYTHONIOENCODING", "UTF8")
    piped = "\N{BYTE ORDER MARK}rotated\n".encode()
    monkeypatch.setattr("sys.stdin", io.TextIOWrapper(io.BytesIO(piped), encoding="cp1252"))
    monkeypatch.setattr("sys.stdout", io.TextIOWrapper(io.BytesIO(), encoding="cp1252"))

    cli._encoded_streams()

    assert sys.stdin.read() == "rotated\n"
    assert sys.stdout.encoding == "utf-8"


@pytest.mark.parametrize(
    ("flags", "encoding"), [([], "cp437"), (["-I"], "utf-8")], ids=["read", "ignored"]
)
def test_pythonioencoding_is_read_as_python_reads_it(flags: list[str], encoding: str) -> None:
    # Ignored under `-E` and `-I`, which tell the interpreter to ignore the environment, and then
    # it decides nothing here either. In an interpreter of its own, since the flag is read as the
    # interpreter starts.
    script = (
        "import sys\n"
        "from pykeepassgate import cli\n"
        "cli._encoded_streams()\n"
        "print(sys.stdout.encoding, file=sys.stderr)\n"
    )
    environment = {**os.environ, "PYTHONIOENCODING": "cp437"}

    ended = subprocess.run(  # noqa: S603
        [sys.executable, *flags, "-c", script],
        env=environment,
        capture_output=True,
        text=True,
        check=False,
    )

    assert ended.stderr.strip() == encoding, ended.stderr


def test_pythonioencoding_names_nothing_to_an_interpreter_ignoring_the_environment(
    monkeypatch: pytest.MonkeyPatch,
) -> None:
    # The same as the interpreter of its own above, read here as well so that this process runs the
    # path too: the flags are replaced for this question alone.
    monkeypatch.setenv("PYTHONIOENCODING", "cp437:replace")
    monkeypatch.setattr("sys.flags", types.SimpleNamespace(ignore_environment=1))

    assert cli._io_encoding() == (None, None)


def test_plain_output_for_a_console_that_decodes_otherwise_is_warned_about_once(
    gate: FakeGate,
    kept: Path,
    monkeypatch: pytest.MonkeyPatch,
    capsys: pytest.CaptureFixture[str],
) -> None:
    # PowerShell decodes a program's output with the console's code page, so a name beyond ASCII
    # captured into a variable arrives as other text and nothing fails. The output is printed all
    # the same, and the warning names both ways of making the two agree.
    monkeypatch.setattr(cli, "_other_reader", lambda: "cp437")
    answering(gate, {"attachments": [{"name": "ä.pem", "size": 1}, {"name": "Ω.pem", "size": 2}]})

    assert cli.main(["attachments", UUID, "--directory", str(kept)]) == 0

    out, err = capsys.readouterr()
    assert [line.split()[-1] for line in out.splitlines()] == ["ä.pem", "Ω.pem"]
    assert err.count("OutputEncodingWarning: ") == 1
    assert "this console decodes cp437" in err
    assert "[Console]::OutputEncoding" in err
    assert "PYTHONIOENCODING=cp437" in err


@pytest.mark.parametrize(
    ("output", "name"), [("plain", "key.pem"), ("json", "ä.pem")], ids=["ascii", "json"]
)
def test_output_every_code_page_reads_alike_is_not_warned_about(
    gate: FakeGate,
    kept: Path,
    monkeypatch: pytest.MonkeyPatch,
    capsys: pytest.CaptureFixture[str],
    output: str,
    name: str,
) -> None:
    # `json` is only ASCII, whatever the names in it.
    monkeypatch.setattr(cli, "_other_reader", lambda: "cp437")
    answering(gate, {"attachments": [{"name": name, "size": 1}]})

    assert cli.main(["attachments", UUID, "--directory", str(kept), "-o", output]) == 0

    assert "OutputEncodingWarning" not in capsys.readouterr().err


def test_the_warning_is_pythons_own_and_a_filter_silences_it(
    gate: FakeGate,
    kept: Path,
    monkeypatch: pytest.MonkeyPatch,
    capsys: pytest.CaptureFixture[str],
) -> None:
    # Raised through `warnings`, so a program running the command line with its own filters, or
    # `-W`, decides whether it is shown.
    monkeypatch.setattr(cli, "_other_reader", lambda: "cp437")
    with pytest.warns(cli.OutputEncodingWarning, match="cp437"):
        cli._misread(["key.pem", "ä.pem"])

    answering(gate, {"attachments": [{"name": "ä.pem", "size": 1}]})
    with warnings.catch_warnings():
        warnings.simplefilter("ignore", cli.OutputEncodingWarning)
        assert cli.main(["attachments", UUID, "--directory", str(kept)]) == 0

    assert "OutputEncodingWarning" not in capsys.readouterr().err


def test_a_warning_is_shown_as_a_failure_is_and_never_fails_itself(
    monkeypatch: pytest.MonkeyPatch,
) -> None:
    shown = io.StringIO()
    cli._warned("decodes otherwise", cli.OutputEncodingWarning, "cli.py", 1, shown)
    assert shown.getvalue() == "OutputEncodingWarning: decodes otherwise\n"

    class Gone(io.StringIO):
        def write(self, text: str) -> int:
            raise BrokenPipeError

    cli._warned("cannot be written", UserWarning, "cli.py", 1, Gone())

    # No standard error at all, as under `pythonw`. cspell:ignore pythonw
    monkeypatch.setattr("sys.stderr", None)
    cli._warned("nowhere to go", UserWarning, "cli.py", 1)


@pytest.mark.skipif(os.name != "nt", reason="a console decodes with a code page on Windows")
@pytest.mark.parametrize(
    ("page", "expected"),
    [(437, "cp437"), (65001, None), (0, None), (99999, None)],
    ids=["another", "utf-8", "no console", "no codec"],
)
def test_a_file_is_read_with_the_consoles_code_page_where_it_differs(
    tmp_path: Path, monkeypatch: pytest.MonkeyPatch, page: int, expected: str | None
) -> None:
    monkeypatch.setattr(cli, "_console_code_page", lambda: page)
    with (tmp_path / "out.txt").open("w", encoding="utf-8") as file:
        monkeypatch.setattr("sys.stdout", file)
        assert cli._other_reader() == expected


@pytest.mark.skipif(os.name != "nt", reason="a console decodes with a code page on Windows")
def test_a_stream_nobody_reads_through_the_console_has_no_reader(
    tmp_path: Path, monkeypatch: pytest.MonkeyPatch
) -> None:
    # The null device is read by nobody, and a stream with no descriptor, or a stream that is no
    # text wrapper at all, as a capture may be, is no file or pipe.
    monkeypatch.setattr(cli, "_console_code_page", lambda: 437)
    with Path(os.devnull).open("w", encoding="utf-8") as null:
        monkeypatch.setattr("sys.stdout", null)
        assert cli._other_reader() is None

    monkeypatch.setattr("sys.stdout", io.TextIOWrapper(io.BytesIO(), encoding="utf-8"))
    assert cli._other_reader() is None

    monkeypatch.setattr("sys.stdout", io.StringIO())
    assert cli._other_reader() is None


@pytest.mark.skipif(os.name != "nt", reason="a console decodes with a code page on Windows")
def test_the_consoles_code_page_is_asked_of_windows() -> None:
    # Whatever runs the tests may have a console or none, so only the answer's form is known.
    assert cli._console_code_page() >= 0


@pytest.mark.skipif(os.name == "nt", reason="elsewhere no console decodes with a code page")
def test_elsewhere_no_reader_is_warned_about(
    tmp_path: Path, monkeypatch: pytest.MonkeyPatch
) -> None:
    with (tmp_path / "out.txt").open("w", encoding="utf-8") as file:
        monkeypatch.setattr("sys.stdout", file)
        assert cli._other_reader() is None


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
