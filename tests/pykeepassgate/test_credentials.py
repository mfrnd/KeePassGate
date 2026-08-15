"""What a paired client keeps, where it keeps it, and what it refuses to keep."""

from __future__ import annotations

import base64
import importlib
import json
import os
import stat
import sys
from pathlib import Path
from typing import Any

import pytest
from cryptography import x509
from cryptography.hazmat.primitives.serialization import Encoding

from pykeepassgate import credentials
from pykeepassgate.errors import ProtocolError, StorageError


def test_what_pairing_hands_over_is_written_where_ssl_can_read_it(
    tmp_path: Path, bundle: dict[str, Any]
) -> None:
    stored = credentials.save(tmp_path / "kept", 45678, bundle)

    # `ssl` reads a certificate and its key from one PEM file and nothing else, which is why the
    # PKCS#12 is converted once here rather than at every connection.
    identity = stored.identity.read_text("utf-8")
    assert "BEGIN CERTIFICATE" in identity
    assert "BEGIN PRIVATE KEY" in identity
    assert "BEGIN CERTIFICATE" in stored.authority.read_text("utf-8")


def test_the_details_a_person_reads_carry_nothing_secret(
    tmp_path: Path, bundle: dict[str, Any]
) -> None:
    # The file most likely to be opened, pasted or attached. The key and the gate key are in their
    # own files precisely so that this one can be shown to somebody.
    stored = credentials.save(tmp_path / "kept", 45678, bundle)
    written = json.loads((stored.directory / credentials.DETAILS_FILE).read_text("utf-8"))

    assert set(written) == {"port", "subject", "database", "expires", "profiles"}
    assert bundle["gateKey"] not in json.dumps(written)
    assert bundle["identity"] not in json.dumps(written)


def test_the_gate_key_survives_the_round_trip(tmp_path: Path, bundle: dict[str, Any]) -> None:
    stored = credentials.save(tmp_path / "kept", 45678, bundle)
    assert stored.gate_key == bytes(range(32))


def test_what_was_saved_is_what_is_loaded(tmp_path: Path, bundle: dict[str, Any]) -> None:
    saved = credentials.save(tmp_path / "kept", 45678, bundle)
    loaded = credentials.load(tmp_path / "kept")

    assert loaded == saved
    assert loaded.subject == "CN=agent-invented"
    assert loaded.profiles == ("reader",)
    assert loaded.port == 45678


def test_loading_from_nowhere_says_where_it_looked(tmp_path: Path) -> None:
    with pytest.raises(FileNotFoundError, match="no paired client"):
        credentials.load(tmp_path / "nothing")


@pytest.mark.skipif(sys.platform == "win32", reason="Windows decides this with an ACL instead")
def test_the_key_is_readable_by_its_owner_and_nobody_else(
    tmp_path: Path, bundle: dict[str, Any]
) -> None:
    stored = credentials.save(tmp_path / "kept", 45678, bundle)
    assert stat.S_IMODE(stored.identity.stat().st_mode) == 0o600
    assert stat.S_IMODE(stored.directory.stat().st_mode) == 0o700


def test_only_the_directories_this_created_are_narrowed(
    tmp_path: Path, bundle: dict[str, Any], monkeypatch: pytest.MonkeyPatch
) -> None:
    # The parent of whatever was named used to be narrowed as well, so `--directory ~/agent`
    # changed the mode of a home directory and `--directory /tmp/kept` raised out of `chmod`
    # with the invitation already spent and the record already saved. Asserted on the call
    # rather than on the mode, because Windows decides a directory with an ACL, reports every
    # one of them alike, and is the only platform CI runs on.
    narrowed: list[Path] = []
    chmod = Path.chmod

    def record(self: Path, mode: int, *, follow_symlinks: bool = True) -> None:
        narrowed.append(self)
        chmod(self, mode, follow_symlinks=follow_symlinks)

    monkeypatch.setattr(Path, "chmod", record)
    beside = credentials.save(tmp_path / "kept", 45678, bundle)
    under = credentials.save(tmp_path / "made" / "kept", 45678, bundle)

    assert tmp_path not in narrowed, "the directory the caller pointed at is not this one to change"
    assert beside.directory in narrowed
    assert tmp_path / "made" in narrowed, "a directory nothing else knows about is still this one's"
    assert under.directory in narrowed


@pytest.mark.skipif(sys.platform == "win32", reason="Windows decides this with an ACL instead")
def test_a_parent_that_was_already_there_keeps_the_mode_it_had(
    tmp_path: Path, bundle: dict[str, Any]
) -> None:
    # The same claim where the platform can be asked directly. A parent this process cannot
    # narrow is the same case: nothing is attempted on it, so nothing fails on it either.
    parent = tmp_path / "shared"
    parent.mkdir()
    parent.chmod(0o755)

    credentials.save(parent / "kept", 45678, bundle)

    assert stat.S_IMODE(parent.stat().st_mode) == 0o755


@pytest.mark.parametrize("missing", ["identity", "authority", "gateKey"])
def test_a_bundle_missing_a_secret_is_refused_rather_than_half_written(
    tmp_path: Path, bundle: dict[str, Any], missing: str
) -> None:
    del bundle[missing]

    with pytest.raises(ProtocolError, match=missing):
        credentials.save(tmp_path / "kept", 45678, bundle)


def test_a_bundle_whose_identity_is_not_base64_is_refused(
    tmp_path: Path, bundle: dict[str, Any]
) -> None:
    bundle["identity"] = "not base64 at all"

    with pytest.raises(ProtocolError, match="not base64"):
        credentials.save(tmp_path / "kept", 45678, bundle)


def test_credentials_live_somewhere_per_profile(monkeypatch: pytest.MonkeyPatch) -> None:
    # A client paired with two databases holds two identities, and they are not interchangeable.
    one = credentials.default_directory("one")
    other = credentials.default_directory("other")

    assert one != other
    assert one.parent == other.parent


@pytest.mark.parametrize(
    "name",
    [
        "",
        ".",
        "..",
        "...",
        "../escaped",
        "a/b",
        "a\\b",
        "/elsewhere/entirely",
        "C:elsewhere",
        "a\0b",
        "work.",
        "work ",
    ],
)
def test_a_profile_that_is_not_one_directory_is_refused_rather_than_joined(name: str) -> None:
    # Joined, `..` or a separator stored the identity beside or above the other profiles, an
    # absolute path replaced the place altogether, and `whoami` read it back from there as
    # though it were a profile. Windows stores a name without a trailing dot or space, which
    # makes `...` the directory every profile lives in and `work.` the same identity as `work`.
    with pytest.raises(ValueError, match="not one directory's name"):
        credentials.default_directory(name)


def test_every_name_that_is_one_directory_is_a_profile() -> None:
    for name in ("default", "work", "agent one", "build.v2", ".hidden"):
        assert credentials.default_directory(name).name == name


def test_an_authority_that_is_not_a_certificate_leaves_the_old_identity_alone(
    tmp_path: Path, bundle: dict[str, Any]
) -> None:
    # Everything is read before anything is written, because a bundle refused halfway destroys a
    # working identity and the invitation that produced it is spent either way.
    where = tmp_path / "kept"
    first = credentials.save(where, 45678, bundle)
    before = first.identity.read_bytes()

    broken = dict(bundle)
    broken["authority"] = base64.b64encode(b"not a certificate at all").decode()

    with pytest.raises(ProtocolError, match="not a certificate"):
        credentials.save(where, 45678, broken)

    assert first.identity.read_bytes() == before
    assert credentials.load(where).subject == first.subject


def test_a_gate_key_that_is_not_base64_leaves_the_old_identity_alone(
    tmp_path: Path, bundle: dict[str, Any]
) -> None:
    # The second and narrower window: `gateKey` used to be validated by the very call that wrote
    # it, one line after the identity had already been replaced.
    where = tmp_path / "kept"
    before = credentials.save(where, 45678, bundle).identity.read_bytes()

    broken = dict(bundle)
    broken["gateKey"] = "not base64 at all"

    with pytest.raises(ProtocolError, match="not base64"):
        credentials.save(where, 45678, broken)

    assert (where / credentials.IDENTITY_FILE).read_bytes() == before


def test_a_first_pairing_that_is_refused_leaves_no_orphaned_key(
    tmp_path: Path, bundle: dict[str, Any]
) -> None:
    # Nothing would ever clean this up: `load` refuses a directory with no `client.json`, and the
    # invitation is spent.
    where = tmp_path / "fresh"
    broken = dict(bundle)
    broken["authority"] = base64.b64encode(b"not a certificate at all").decode()

    with pytest.raises(ProtocolError):
        credentials.save(where, 45678, broken)

    assert not where.exists() or list(where.iterdir()) == []


def test_a_gate_key_of_the_wrong_length_is_refused(tmp_path: Path, bundle: dict[str, Any]) -> None:
    # `CA.md` 204, and `AuthorityMaterial.SniKeyLength` refusing anything else on the other side. A
    # short one names every connection this client makes, and fails much later as a handshake
    # nobody can account for.
    bundle["gateKey"] = base64.b64encode(bytes(1)).decode()

    with pytest.raises(ProtocolError, match="32 bytes, not 1"):
        credentials.save(tmp_path / "kept", 45678, bundle)


def test_profiles_that_are_not_a_list_are_refused(tmp_path: Path, bundle: dict[str, Any]) -> None:
    # `list("reader")` is six profiles, and `whoami` would show the operator all six as though
    # somebody had approved them.
    bundle["profiles"] = "reader"

    with pytest.raises(ProtocolError, match="not a list of names"):
        credentials.save(tmp_path / "kept", 45678, bundle)


def test_a_null_where_text_was_expected_is_stored_as_nothing(
    tmp_path: Path, bundle: dict[str, Any]
) -> None:
    # `get(key, "")` fires only on absence, so a JSON null used to reach `str()` and come back as
    # the four letters `None`.
    bundle["subject"] = None

    assert credentials.save(tmp_path / "kept", 45678, bundle).subject == ""


def test_the_authority_is_written_as_the_certificate_it_parsed(
    tmp_path: Path, bundle: dict[str, Any]
) -> None:
    # Rather than as base64 wrapped in PEM markers, which stores whatever arrived and fails at the
    # next handshake as an `SSLError`. That is an `OSError`, and so outside this library's
    # hierarchy.
    stored = credentials.save(tmp_path / "kept", 45678, bundle)

    written = x509.load_pem_x509_certificate(stored.authority.read_bytes())
    assert written.public_bytes(Encoding.DER) == base64.b64decode(bundle["authority"])


#: What `save` writes, for a case below to spoil one member of.
WRITTEN = {
    "port": 45678,
    "subject": "CN=agent-invented",
    "database": "abcdef01",
    "expires": "2027-08-15T09:00:00Z",
    "profiles": ["reader"],
}


@pytest.mark.parametrize(
    ("written", "because"),
    [
        ("{not json at all", "will not read"),
        ("[]", "not what this client wrote"),
        ('{"subject": "x"}', "names no port"),
        ('{"port": "not a number"}', "names no port"),
        # A port TCP does not have, and three that `int` would have taken for one.
        (json.dumps({**WRITTEN, "port": 0}), "names no port"),
        (json.dumps({**WRITTEN, "port": 65536}), "names no port"),
        (json.dumps({**WRITTEN, "port": "45678"}), "names no port"),
        (json.dumps({**WRITTEN, "port": 45678.5}), "names no port"),
        (json.dumps({**WRITTEN, "port": True}), "names no port"),
        # `tuple` over a string is one profile per letter, and a list of anything else is a
        # `TypeError` out of `whoami`: the two cases `save` refuses on the way in.
        (json.dumps({**WRITTEN, "profiles": "reader"}), "profiles"),
        (json.dumps({**WRITTEN, "profiles": ["reader", 7]}), "profiles"),
        # `str` makes a `null` the four letters `None`.
        (json.dumps({**WRITTEN, "subject": None}), "no subject"),
        (json.dumps({key: value for key, value in WRITTEN.items() if key != "expires"}), "expires"),
    ],
)
def test_a_client_file_that_will_not_read_is_refused_in_this_librarys_terms(
    tmp_path: Path, bundle: dict[str, Any], written: str, because: str
) -> None:
    # A partial write, a full disk, an edit by hand. Left alone these arrive as `JSONDecodeError`,
    # `TypeError`, `KeyError` and `ValueError`: four ways past a caller promised one root.
    where = tmp_path / "kept"
    credentials.save(where, 45678, bundle)
    (where / credentials.DETAILS_FILE).write_text(written, encoding="utf-8")

    with pytest.raises(StorageError, match=because):
        credentials.load(where)


def test_a_gate_key_that_will_not_read_is_refused_the_same_way(
    tmp_path: Path, bundle: dict[str, Any]
) -> None:
    # Read on every request rather than snapshotted, so an `OSError` here reaches a caller in the
    # middle of a session rather than at `load`.
    stored = credentials.save(tmp_path / "kept", 45678, bundle)
    (stored.directory / credentials.GATE_KEY_FILE).unlink()

    with pytest.raises(StorageError, match="will not read"):
        _ = stored.gate_key


@pytest.mark.skipif(os.name != "nt", reason="the roaming distinction is a Windows one")
def test_the_secrets_do_not_follow_a_roaming_profile(monkeypatch: pytest.MonkeyPatch) -> None:
    # An unencrypted private key and a gate key live here, and `%APPDATA%` is copied to a domain
    # file server at logoff wherever a roaming profile or folder redirection is configured.
    monkeypatch.setenv("LOCALAPPDATA", str(Path("C:/local")))
    monkeypatch.setenv("APPDATA", str(Path("C:/roaming")))

    assert credentials.default_directory("one").is_relative_to(Path("C:/local"))


@pytest.mark.skipif(os.name == "nt", reason="XDG_CONFIG_HOME is read where LOCALAPPDATA is not")
def test_a_relative_config_home_is_ignored_as_the_specification_says(
    tmp_path: Path, monkeypatch: pytest.MonkeyPatch
) -> None:
    # A relative value names another directory from every working directory, so a pairing made
    # from one was not found by `whoami` run from another.
    monkeypatch.setenv("HOME", str(tmp_path))

    monkeypatch.setenv("XDG_CONFIG_HOME", "relative/xdg")
    assert credentials.default_directory("one") == tmp_path / ".config" / "pykeepassgate" / "one"

    monkeypatch.setenv("XDG_CONFIG_HOME", str(tmp_path / "xdg"))
    assert credentials.default_directory("one") == tmp_path / "xdg" / "pykeepassgate" / "one"


def test_a_file_that_cannot_be_written_is_refused_in_this_librarys_terms(
    tmp_path: Path, bundle: dict[str, Any], monkeypatch: pytest.MonkeyPatch
) -> None:
    # A full disk, or anything else `prepare` could not see coming. Left alone this is an
    # `OSError` out of `save`, which is neither a `GateError` nor the `FileNotFoundError` the
    # command line catches by name.
    where = tmp_path / "kept"

    def full(descriptor: int, content: Any) -> int:
        raise OSError(28, "No space left on device")

    monkeypatch.setattr(os, "write", full)

    with pytest.raises(StorageError, match="could not be written"):
        credentials.save(where, 45678, bundle)
    assert list(where.iterdir()) == [], "what opening created is taken away again"


def test_a_directory_that_is_a_file_is_refused_in_this_librarys_terms(
    tmp_path: Path, bundle: dict[str, Any]
) -> None:
    # `mkdir(exist_ok=True)` still raises `FileExistsError` when the name is taken by a file,
    # and a parent this user cannot create under raises `PermissionError`: two ways past a
    # caller who was promised one root to catch.
    where = tmp_path / "taken"
    where.write_text("x")

    with pytest.raises(StorageError, match="cannot hold credentials"):
        credentials.prepare(where)
    with pytest.raises(StorageError, match="cannot hold credentials"):
        credentials.save(where, 45678, bundle)


def test_preparing_makes_the_directory_and_writes_nothing_into_it(tmp_path: Path) -> None:
    where = tmp_path / "made" / "kept"

    with credentials.prepare(where):
        assert where.is_dir()
        assert all((where / name).read_bytes() == b"" for name in credentials._FILES)

    assert list(where.iterdir()) == [], "a file opening created goes if nothing was written"


def test_preparing_leaves_an_identity_already_there_as_it_was(
    tmp_path: Path, bundle: dict[str, Any]
) -> None:
    stored = credentials.save(tmp_path / "kept", 45678, bundle)
    before = {path.name: path.read_bytes() for path in stored.directory.iterdir()}

    # Opened for writing and held, and still not truncated: the pairing may yet be refused.
    with credentials.prepare(stored.directory):
        assert {path.name: path.read_bytes() for path in stored.directory.iterdir()} == before

    assert {path.name: path.read_bytes() for path in stored.directory.iterdir()} == before


def test_a_directory_that_refuses_a_file_is_refused_before_anything_is_spent(
    tmp_path: Path, monkeypatch: pytest.MonkeyPatch
) -> None:
    # Asked by opening the files rather than by `os.access`, which on Windows reads only the
    # read-only attribute and says yes to a directory whose ACL says no.
    where = tmp_path / "kept"
    opening = os.open

    def refuse(path: Any, flags: int, mode: int = 0o777) -> int:
        if Path(path).name == credentials.AUTHORITY_FILE:
            raise PermissionError("denied")
        return opening(path, flags, mode)

    monkeypatch.setattr(os, "open", refuse)

    with pytest.raises(StorageError, match="denied"):
        credentials.prepare(where)
    assert list(where.iterdir()) == [], "the two it did open are taken away again"


def test_a_file_already_there_is_left_where_one_beside_it_is_refused(
    tmp_path: Path, bundle: dict[str, Any], monkeypatch: pytest.MonkeyPatch
) -> None:
    stored = credentials.save(tmp_path / "kept", 45678, bundle)
    kept = stored.identity.read_bytes()
    stored.authority.unlink()
    opening = os.open

    def refuse(path: Any, flags: int, mode: int = 0o777) -> int:
        if Path(path).name == credentials.DETAILS_FILE:
            raise PermissionError("denied")
        return opening(path, flags, mode)

    monkeypatch.setattr(os, "open", refuse)

    with pytest.raises(StorageError, match="denied"):
        credentials.prepare(stored.directory)
    assert stored.identity.read_bytes() == kept
    assert not stored.authority.exists(), "created by opening, so taken away again"


def test_a_name_that_is_not_a_file_is_refused_before_anything_is_spent(tmp_path: Path) -> None:
    where = tmp_path / "kept"
    (where / credentials.AUTHORITY_FILE).mkdir(parents=True)

    with pytest.raises(StorageError, match="not a file this client can replace"):
        credentials.prepare(where)


def test_a_file_that_will_not_be_replaced_is_refused_before_anything_is_spent(
    tmp_path: Path, bundle: dict[str, Any]
) -> None:
    stored = credentials.save(tmp_path / "kept", 45678, bundle)
    stored.authority.chmod(stat.S_IRUSR)

    try:
        with pytest.raises(StorageError, match="not a file this client can replace"):
            credentials.prepare(stored.directory)
    finally:
        stored.authority.chmod(stat.S_IRUSR | stat.S_IWUSR)


def test_a_directory_another_account_could_write_into_is_not_this_clients() -> None:
    # Pure, so that the refusal is pinned on the one platform CI runs on, where the check
    # itself is not asked.
    assert credentials._shared(stat.S_IFDIR | 0o700, 1000, 1000) == ""
    assert credentials._shared(stat.S_IFDIR | 0o755, 1000, 1000) == ""
    assert "another account" in credentials._shared(stat.S_IFDIR | 0o700, 0, 1000)
    assert "mode 770" in credentials._shared(stat.S_IFDIR | 0o770, 1000, 1000)
    assert "mode 777" in credentials._shared(stat.S_IFDIR | 0o777, 1000, 1000)


@pytest.mark.skipif(sys.platform == "win32", reason="Windows decides this with an ACL instead")
def test_a_shared_directory_is_refused_where_the_platform_can_say_so(tmp_path: Path) -> None:
    where = tmp_path / "shared"
    where.mkdir()
    where.chmod(0o777)

    with pytest.raises(StorageError, match="other accounts may write to it"):
        credentials.prepare(where)


@pytest.fixture
def stranger(monkeypatch: pytest.MonkeyPatch) -> None:
    """Every directory belongs to another account, which is the one refusal the flag waives.

    Pretended rather than arranged, so that it runs on Windows as well, where `geteuid` does not
    exist and the check is never asked: what this pins is the parameter's plumbing, and the
    tests marked for POSIX pin the check itself.
    """
    monkeypatch.setattr(os, "geteuid", lambda: 4242, raising=False)


def test_the_shared_directory_refusal_names_the_way_past_it(tmp_path: Path, stranger: None) -> None:
    with pytest.raises(StorageError, match="another account") as refused:
        credentials.prepare(tmp_path)

    assert "allow_shared_directory" in str(refused.value)


def test_a_caller_who_says_so_may_keep_credentials_in_a_shared_directory(
    tmp_path: Path, bundle: dict[str, Any], stranger: None
) -> None:
    where = tmp_path / "kept"
    where.mkdir()

    with credentials.prepare(where, allow_shared_directory=True):
        pass
    with pytest.raises(StorageError, match="another account"):
        credentials.save(where, 45678, bundle)

    stored = credentials.save(where, 45678, bundle, allow_shared_directory=True)
    assert stored.identity.is_file()


def test_saying_so_waives_nothing_else(
    tmp_path: Path, monkeypatch: pytest.MonkeyPatch, stranger: None
) -> None:
    taken = tmp_path / "taken"
    taken.write_text("x")
    with pytest.raises(StorageError, match="cannot hold credentials"):
        credentials.prepare(taken, allow_shared_directory=True)

    where = tmp_path / "kept"
    (where / credentials.AUTHORITY_FILE).mkdir(parents=True)
    with pytest.raises(StorageError, match="not a file this client can replace"):
        credentials.prepare(where, allow_shared_directory=True)

    def refuse(path: Any, flags: int, mode: int = 0o777) -> int:
        raise PermissionError("denied")

    monkeypatch.setattr(os, "open", refuse)
    with pytest.raises(StorageError, match="denied"):
        credentials.prepare(tmp_path / "elsewhere", allow_shared_directory=True)


@pytest.mark.skipif(sys.platform == "win32", reason="Windows decides this with an ACL instead")
def test_a_shared_directory_is_accepted_when_the_caller_says_so(
    tmp_path: Path, bundle: dict[str, Any]
) -> None:
    where = tmp_path / "shared"
    where.mkdir()
    where.chmod(0o777)

    stored = credentials.save(where, 45678, bundle, allow_shared_directory=True)

    assert stat.S_IMODE(stored.identity.stat().st_mode) == 0o600
    assert stat.S_IMODE(where.stat().st_mode) == 0o777, "a directory it did not make is left alone"


def linked(name: Path, target: Path) -> None:
    """Make `name` a symbolic link to `target`, or skip where this account may not make one.

    Windows grants it in Developer Mode and to an elevated process, and refuses it otherwise.
    """
    try:
        name.symlink_to(target)
    except OSError as error:  # pragma: no cover, only where links take a privilege
        pytest.skip(f"this account cannot make a symbolic link here: {error}")


def directory_link(name: Path, target: Path) -> None:
    """Make `name` a link to the directory `target`: a junction on Windows, which needs no right."""
    # cspell:ignore winapi
    if sys.platform == "win32":
        importlib.import_module("_winapi").CreateJunction(str(target), str(name))
    else:
        name.symlink_to(target, target_is_directory=True)


@pytest.mark.parametrize("save", [False, True], ids=["prepare", "save"])
def test_a_link_where_the_directory_goes_is_refused_rather_than_followed(
    tmp_path: Path, bundle: dict[str, Any], save: bool
) -> None:
    # Every other check would follow it, and the identity would land in whichever directory of
    # this user's it named, over whatever identity was there.
    victim = tmp_path / "victim"
    credentials.save(victim, 45678, bundle)
    held = (victim / credentials.IDENTITY_FILE).read_bytes()
    where = tmp_path / "kept"
    directory_link(where, victim)

    with pytest.raises(StorageError, match="is a link"):
        if save:
            credentials.save(where, 45679, bundle)
        else:
            credentials.prepare(where)

    assert (victim / credentials.IDENTITY_FILE).read_bytes() == held
    assert credentials.load(victim).port == 45678


def test_a_link_planted_once_the_directory_is_made_is_refused_too(
    tmp_path: Path, monkeypatch: pytest.MonkeyPatch
) -> None:
    victim = tmp_path / "victim"
    victim.mkdir()
    where = tmp_path / "kept"
    make = credentials._make

    def planted(directory: Path) -> None:
        make(directory)
        directory.rmdir()
        directory_link(directory, victim)

    monkeypatch.setattr(credentials, "_make", planted)

    with pytest.raises(StorageError, match="is a link"):
        credentials.prepare(where)

    assert list(victim.iterdir()) == []


def test_a_link_in_a_shared_directory_is_refused_whatever_the_caller_says(
    tmp_path: Path, bundle: dict[str, Any], monkeypatch: pytest.MonkeyPatch
) -> None:
    where = tmp_path / "shared"
    where.mkdir()
    where.chmod(0o777)
    victim = tmp_path / "victim"
    victim.write_text("old contents")
    linked(where / credentials.IDENTITY_FILE, victim)

    with pytest.raises(StorageError, match="not a file this client can replace"):
        credentials.prepare(where, allow_shared_directory=True)

    # And for a link planted after `prepare` looked, which is opening's own refusal.
    monkeypatch.setattr(credentials, "_replaceable", lambda path: None)
    with pytest.raises(StorageError, match="could not be opened for writing"):
        credentials.save(where, 45678, bundle, allow_shared_directory=True)
    assert victim.read_text() == "old contents"


def test_a_link_where_the_key_goes_is_refused_rather_than_followed(
    tmp_path: Path, bundle: dict[str, Any], monkeypatch: pytest.MonkeyPatch
) -> None:
    where = tmp_path / "kept"
    where.mkdir(mode=0o700)
    victim = tmp_path / "victim"
    victim.write_text("old contents")
    linked(where / credentials.IDENTITY_FILE, victim)

    with pytest.raises(StorageError, match="not a file this client can replace"):
        credentials.prepare(where)

    # And for a link planted after `prepare` looked, which is opening's own refusal.
    monkeypatch.setattr(credentials, "_replaceable", lambda path: None)
    with pytest.raises(StorageError, match="could not be opened for writing"):
        credentials.save(where, 45678, bundle)
    assert victim.read_text() == "old contents"
