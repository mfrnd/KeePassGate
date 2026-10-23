"""The command line's half of writing against what was read: `--if-match` and the tag it prints.

What a condition means is the library's and the plugin's. What is held here is what each command
adds: that a write sends the tag it was given exactly, that a read prints the tag it was answered
with under `-o json` and nowhere in `plain`, and that a write which changed nothing because the
node has changed since fails the command.
"""

from __future__ import annotations

import json
from pathlib import Path
from typing import Any

import pytest

from pykeepassgate import cli
from pykeepassgate import credentials as credentials_module
from pykeepassgate.testing import FakeGate
from tagged import NEXT, TAG, UUID, tagging


@pytest.fixture
def kept(tmp_path: Path, bundle: dict[str, Any], gate: FakeGate) -> Path:
    """A paired identity on disk, pointed at this gate."""
    credentials_module.save(tmp_path / "kept", gate.port, bundle)
    return tmp_path / "kept"


@pytest.mark.parametrize(
    ("command", "tagged"),
    [
        (["update-group", UUID, "--name", "x"], True),
        (["remove-group", UUID], False),
        (["update", UUID, "--title", "x"], True),
        (["remove", UUID], False),
        (["add-field", UUID, "Account", "--value", "x"], True),
        (["update-field", UUID, "Account", "--value", "x"], True),
        (["strip-field", UUID, "Account"], True),
        (["strip-attachment", UUID, "a.txt"], True),
    ],
    ids=lambda value: value[0] if isinstance(value, list) else str(value),
)
def test_a_write_sends_the_tag_it_was_given_and_prints_the_one_it_left(
    gate: FakeGate,
    kept: Path,
    capsys: pytest.CaptureFixture[str],
    command: list[str],
    tagged: bool,
) -> None:
    conditions = tagging(gate, {"ok": True}, NEXT if tagged else None)

    assert cli.main([*command, "--if-match", TAG, "--directory", str(kept), "-o", "json"]) == 0
    printed = json.loads(capsys.readouterr().out)

    assert cli.main([*command, "--directory", str(kept)]) == 0
    plain = capsys.readouterr().out

    assert conditions == [TAG, None], "exactly as given, and nothing when not"
    if tagged:
        assert printed["etag"] == NEXT
    else:
        assert "etag" not in printed, "a removal leaves nothing to tag"

    assert "etag" not in plain and NEXT not in plain, "plain prints what it always printed"


@pytest.mark.parametrize("command", ["add-attachment", "update-attachment"])
def test_an_attachment_from_a_file_is_written_under_the_tag_too(
    gate: FakeGate,
    kept: Path,
    tmp_path: Path,
    capsys: pytest.CaptureFixture[str],
    command: str,
) -> None:
    source = tmp_path / "a.txt"
    source.write_bytes(b"invented")
    conditions = tagging(gate, {"ok": True}, NEXT)

    arguments = [command, UUID, "a.txt", "--from", str(source), "--if-match", TAG]
    assert cli.main([*arguments, "--directory", str(kept), "-o", "json"]) == 0

    assert conditions == [TAG]
    assert json.loads(capsys.readouterr().out)["etag"] == NEXT


@pytest.mark.parametrize(
    ("command", "body"),
    [
        (["group", UUID], {"uuid": UUID, "name": "x", "parent": None, "notes": ""}),
        (
            ["show", UUID],
            {"uuid": UUID, "group": UUID, "fields": {"Title": "x"}, "protected": []},
        ),
        (["field", UUID, "Title"], {"name": "Title", "value": "x", "protected": False}),
    ],
    ids=["group", "show", "field"],
)
def test_a_read_prints_its_tag_in_a_document_and_nowhere_else(
    gate: FakeGate,
    kept: Path,
    capsys: pytest.CaptureFixture[str],
    command: list[str],
    body: dict[str, Any],
) -> None:
    tagging(gate, body, TAG)

    assert cli.main([*command, "--directory", str(kept), "-o", "json"]) == 0
    assert json.loads(capsys.readouterr().out)["etag"] == TAG

    assert cli.main([*command, "--directory", str(kept)]) == 0
    assert TAG not in capsys.readouterr().out


def test_a_read_the_gate_gave_no_tag_for_prints_none(
    gate: FakeGate, kept: Path, capsys: pytest.CaptureFixture[str]
) -> None:
    tagging(gate, {"uuid": UUID, "name": "x", "parent": None, "notes": ""}, None)

    assert cli.main(["group", UUID, "--directory", str(kept), "-o", "json"]) == 0

    assert "etag" not in json.loads(capsys.readouterr().out)


def test_fetch_prints_the_tag_of_the_entry_it_read_from(
    gate: FakeGate, kept: Path, tmp_path: Path, capsys: pytest.CaptureFixture[str]
) -> None:
    tagging(gate, {"name": "a.txt", "size": 1, "content": "eA=="}, TAG)

    target = tmp_path / "out.bin"
    command = ["fetch", UUID, "a.txt", "--to", str(target), "--directory", str(kept), "-o", "json"]
    assert cli.main(command) == 0

    assert json.loads(capsys.readouterr().out)["etag"] == TAG


@pytest.mark.parametrize(
    ("command", "body"),
    [
        (["fields", UUID], {"fields": [{"name": "Title", "protected": False}]}),
        (["attachments", UUID], {"attachments": [{"name": "a.txt", "size": 1}]}),
    ],
    ids=["fields", "attachments"],
)
def test_an_entry_s_listing_prints_the_entry_s_tag_beside_each_name(
    gate: FakeGate,
    kept: Path,
    capsys: pytest.CaptureFixture[str],
    command: list[str],
    body: dict[str, Any],
) -> None:
    # So a name a listing printed can be taken off under that tag without reading the value.
    tagging(gate, body, TAG)

    assert cli.main([*command, "--directory", str(kept), "-o", "json"]) == 0
    assert [item["etag"] for item in json.loads(capsys.readouterr().out)] == [TAG]

    tagging(gate, body, None)
    assert cli.main([*command, "--directory", str(kept), "-o", "json"]) == 0
    assert ["etag" in item for item in json.loads(capsys.readouterr().out)] == [False]


@pytest.mark.parametrize("tag", ["", '"a"\x00', '"é"'])
def test_a_tag_that_could_not_be_sent_is_refused_as_an_argument_before_anything_is_dialed(
    gate: FakeGate, kept: Path, capsys: pytest.CaptureFixture[str], tag: str
) -> None:
    # Exit 2, as every argument this cannot use is refused, and before a prompt or a connection.
    tagging(gate, {"ok": True}, None)

    with pytest.raises(SystemExit) as exited:
        cli.main(["update", UUID, "--title", "x", "--if-match", tag, "--directory", str(kept)])

    assert exited.value.code == 2
    assert "not a tag" in capsys.readouterr().err
    assert gate.names == [], "nothing was dialed"


def test_a_write_against_a_node_that_has_changed_since_fails_the_command(
    gate: FakeGate, kept: Path, capsys: pytest.CaptureFixture[str]
) -> None:
    tagging(gate, {"error": "precondition failed"}, None, status=412)

    command = ["update", UUID, "--title", "x", "--if-match", TAG, "--directory", str(kept)]
    assert cli.main(command) == 1

    captured = capsys.readouterr()
    assert captured.out == ""
    assert "412" in captured.err
