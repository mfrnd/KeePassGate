"""What `pykeepass` actually requires, measured rather than asserted.

`RESULTS.md` says what a `pykeepass` user can and cannot expect from KeePassGate. Every claim it
makes about somebody else's library is measured here, so that a release of `pykeepass` which
changes one of them fails a test rather than leaving the document quietly wrong.

Run it with nothing arranged first:

    uv run --no-project --with pykeepass --with pytest python -m pytest test_pykeepass.py -q
"""

from __future__ import annotations

import inspect
from pathlib import Path

import pytest

pykeepass = pytest.importorskip("pykeepass")


def test_opening_a_database_needs_a_file_and_a_password() -> None:
    # The whole of the compatibility question in one signature. There is no constructor that takes
    # entries, or a connection, or anything a gate could hand over.
    signature = inspect.signature(pykeepass.PyKeePass.__init__)

    assert "filename" in signature.parameters
    assert "password" in signature.parameters
    assert signature.parameters["filename"].default is inspect.Parameter.empty


def test_a_database_can_be_made_from_nothing_and_read_back(tmp_path: Path) -> None:
    # So synthesizing one is possible, which is what makes the trap worth writing down rather than
    # simply saying it cannot be done.
    path = tmp_path / "invented.kdbx"
    made = pykeepass.create_database(str(path), password="not-a-real-password")
    made.add_entry(made.root_group, "invented", "somebody", "something")
    made.save()

    reopened = pykeepass.PyKeePass(str(path), password="not-a-real-password")
    assert [entry.title for entry in reopened.entries] == ["invented"]


def test_saving_writes_to_the_file_it_was_opened_from(tmp_path: Path) -> None:
    # The trap, measured. A synthesized database is a file, and `save` writes to that file. Nothing
    # about it reaches whatever the entries came from, so a user who edited one and saved it would
    # have changed a copy and nothing else.
    path = tmp_path / "invented.kdbx"
    made = pykeepass.create_database(str(path), password="not-a-real-password")
    made.add_entry(made.root_group, "invented", "somebody", "something")
    made.save()

    before = path.read_bytes()
    made.add_entry(made.root_group, "second", "somebody", "something")
    made.save()

    assert path.read_bytes() != before, "the copy changed"

    reopened = pykeepass.PyKeePass(str(path), password="not-a-real-password")
    assert len(reopened.entries) == 2, "and the change is only in the copy"


def test_the_password_a_synthesized_database_has_is_not_the_database_s_own(tmp_path: Path) -> None:
    # Whoever synthesizes it chooses the password, because the real one is exactly what the gate
    # exists not to hand over. So the object a user would be given is protected by a secret this
    # client invented, which is worse than no protection because it looks like protection.
    path = tmp_path / "invented.kdbx"
    pykeepass.create_database(str(path), password="chosen-here")

    with pytest.raises(pykeepass.exceptions.CredentialsError):
        pykeepass.PyKeePass(str(path), password="the-database-s-own")
