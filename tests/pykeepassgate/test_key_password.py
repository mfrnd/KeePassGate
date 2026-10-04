"""A password on the client's key: stored under it, opened with it, asked for at a terminal.

What it protects is a copy of the directory taken somewhere else, and nothing against a process run
as the same user. So what these hold is that it is applied where it is asked for, asked for only
where a key needs one and only once, and printed nowhere.
"""

from __future__ import annotations

import base64
import json
import subprocess
import sys
from collections.abc import Callable
from pathlib import Path
from typing import Any, cast

import pytest
from cryptography import x509
from cryptography.hazmat.primitives import serialization

from pykeepassgate import api, cli
from pykeepassgate import credentials as credentials_module
from pykeepassgate.client import GateClient
from pykeepassgate.errors import MalformedToken, PairingError, StorageError
from pykeepassgate.testing import Authority, FakeGate, Issued
from test_pairing import INVITATION
from test_pairing import Gate as PairingGate
from test_renewal import Gate as RenewalGate
from test_renewal import bundle_for

# The encryption scheme PKCS#5 names for a key under a password, named the way it names it.
# cspell:ignore PBES

#: Invented, and looked for in everything printed.
PASSWORD = "an invented key password"

STATUS = json.dumps({"ok": True, "database": "abcdef01", "version": "0.1.0", "revision": ""})


def serving_status(gate: FakeGate) -> None:
    """Answer every request with a status, which is all a test of opening a key needs."""
    gate.handler = lambda path, body, client: (200, STATUS)


def encrypted(path: Path) -> bool:
    """Whether the key in this identity file is stored under a password, read off the file."""
    written = path.read_bytes()
    return b"ENCRYPTED PRIVATE KEY" in written and b"BEGIN PRIVATE KEY" not in written


def opens_with(path: Path, password: bytes | None) -> bool:
    """Whether `cryptography` reads the key in this file back with this password."""
    pem = path.read_bytes()
    start = pem.index(b"-----BEGIN ", pem.index(b"END CERTIFICATE"))
    try:
        serialization.load_pem_private_key(pem[start:], password=password)
    except (TypeError, ValueError):
        return False

    return True


class Counting:
    """A key password given as a callable, which counts how often it was asked."""

    def __init__(self, answer: str | bytes = PASSWORD) -> None:
        self.answer = answer
        self.asked = 0

    def __call__(self) -> str | bytes:
        self.asked += 1
        return self.answer


class TestTheKeyOnDisk:
    """What `save` writes, and what a client opens it with."""

    def test_a_key_stored_under_a_password_is_encrypted_and_opens_with_it(
        self, tmp_path: Path, bundle: dict[str, Any], gate: FakeGate
    ) -> None:
        stored = credentials_module.save(
            tmp_path / "kept", gate.port, bundle, key_password=PASSWORD
        )
        serving_status(gate)

        assert encrypted(stored.identity)
        assert opens_with(stored.identity, PASSWORD.encode())
        assert GateClient(stored, connection_timeout=10.0, key_password=PASSWORD).status().ok

    def test_a_key_stored_without_one_is_not(
        self, tmp_path: Path, bundle: dict[str, Any], gate: FakeGate
    ) -> None:
        stored = credentials_module.save(tmp_path / "kept", gate.port, bundle)

        assert not encrypted(stored.identity)
        assert opens_with(stored.identity, None)

    @pytest.mark.parametrize(
        "given",
        [PASSWORD, PASSWORD.encode(), Counting(), Counting(PASSWORD.encode())],
        ids=["text", "bytes", "a callable returning text", "a callable returning bytes"],
    )
    def test_a_password_is_text_bytes_or_a_callable_returning_either(
        self, given: api.KeyPassword, tmp_path: Path, bundle: dict[str, Any], gate: FakeGate
    ) -> None:
        # Text is UTF-8 on both sides, which is what `ssl` does with a text password, so a key
        # stored under one spelling opens under the other.
        stored = credentials_module.save(tmp_path / "kept", gate.port, bundle, key_password=given)
        serving_status(gate)

        assert GateClient(stored, connection_timeout=10.0, key_password=PASSWORD.encode()).status()
        assert GateClient(stored, connection_timeout=10.0, key_password=PASSWORD).status()

    def test_a_password_outside_ascii_opens_what_it_stored(
        self, tmp_path: Path, bundle: dict[str, Any], gate: FakeGate
    ) -> None:
        # Built from its code point, so this file stays ASCII and the password does not.
        typed = "an invented " + chr(0xE4) + " password"
        stored = credentials_module.save(tmp_path / "kept", gate.port, bundle, key_password=typed)
        serving_status(gate)

        assert GateClient(stored, connection_timeout=10.0, key_password=typed).status().ok


class TestOpeningTheKey:
    """What a client does with a key that needs a password, and with one that does not."""

    @pytest.fixture
    def stored(
        self, tmp_path: Path, bundle: dict[str, Any], gate: FakeGate
    ) -> credentials_module.Credentials:
        serving_status(gate)
        return credentials_module.save(tmp_path / "kept", gate.port, bundle, key_password=PASSWORD)

    def test_an_encrypted_key_with_no_password_names_the_file_and_dials_nothing(
        self, stored: credentials_module.Credentials, gate: FakeGate
    ) -> None:
        # Never OpenSSL's own prompt, which would read a terminal from inside a library call.
        with pytest.raises(StorageError, match=r"identity\.pem holds an encrypted key.*no key"):
            GateClient(stored, connection_timeout=10.0)

        assert gate.names == []

    def test_a_password_that_does_not_open_the_key_names_the_file(
        self, stored: credentials_module.Credentials, gate: FakeGate
    ) -> None:
        with pytest.raises(StorageError, match=r"identity\.pem.*does not open") as refused:
            GateClient(stored, connection_timeout=10.0, key_password="not the one")

        assert "not the one" not in str(refused.value)
        assert gate.names == []

    def test_a_callable_is_asked_once_whatever_the_client_does(
        self, stored: credentials_module.Credentials
    ) -> None:
        asked = Counting()
        client = GateClient(stored, connection_timeout=10.0, key_password=asked)

        client.status()
        client.status()

        assert asked.asked == 1

    def test_a_key_that_is_not_encrypted_never_asks(
        self, tmp_path: Path, bundle: dict[str, Any], gate: FakeGate
    ) -> None:
        # So a password given for one is not used, and is not an error either.
        plain = credentials_module.save(tmp_path / "plain", gate.port, bundle)
        serving_status(gate)
        asked = Counting()

        assert GateClient(plain, connection_timeout=10.0, key_password=asked).status().ok
        assert GateClient(plain, connection_timeout=10.0, key_password="unused").status().ok
        assert asked.asked == 0

    def test_what_a_callable_raises_comes_out_as_it_was_raised(
        self, stored: credentials_module.Credentials
    ) -> None:
        # How the command line refuses to ask where there is no terminal, among other things.
        class Declined(Exception):
            pass

        def declining() -> str:
            raise Declined

        with pytest.raises(Declined):
            GateClient(stored, connection_timeout=10.0, key_password=declining)

    def test_opening_from_disk_takes_the_password_too(
        self, stored: credentials_module.Credentials
    ) -> None:
        opened = GateClient.open(stored.directory, connection_timeout=10.0, key_password=PASSWORD)

        assert opened.status().ok


@pytest.mark.parametrize(
    "given",
    [7, "", b"", ["a list"], "x" * 1024, b"x" * 1024, "\u00e9" * 512, "ab\udc80cd"],
    ids=[
        "a number", "empty text", "empty bytes", "a list", "1024 characters", "1024 bytes",
        "512 characters of two bytes each", "a lone surrogate",
    ],
)
def test_a_key_password_that_is_none_is_refused_where_it_is_given(
    given: Any, tmp_path: Path, bundle: dict[str, Any], gate: FakeGate
) -> None:
    stored = credentials_module.save(tmp_path / "kept", gate.port, bundle)

    with pytest.raises(ValueError, match="key_password") as refused:
        GateClient(stored, key_password=given)

    assert " a int" not in str(refused.value), "a type is named so that it reads"

    with pytest.raises(ValueError, match="key_password"):
        api.pair(
            port=gate.port,
            invitation=INVITATION,
            name="agent-invented",
            directory=tmp_path / "paired",
            key_password=given,
        )

    assert not (tmp_path / "paired").exists()
    assert gate.names == []


def test_the_longest_key_password_a_key_can_be_stored_under_is_taken(
    tmp_path: Path, bundle: dict[str, Any], gate: FakeGate
) -> None:
    # 1023 bytes is what `cryptography` encrypts under and `ssl` opens with, and one more is
    # refused where it is given rather than after a pairing has spent its invitation.
    longest = "\u00e9" * 511 + "x"
    stored = credentials_module.save(tmp_path / "kept", gate.port, bundle, key_password=longest)
    serving_status(gate)

    assert GateClient(stored, connection_timeout=10.0, key_password=longest).status().ok


@pytest.mark.parametrize(
    "answer",
    ["x" * 1024, "ab\udc80cd"],
    ids=["1024 characters", "a lone surrogate"],
)
def test_a_callable_that_gives_a_password_no_key_can_have_is_refused_without_it(
    answer: str, tmp_path: Path, bundle: dict[str, Any], gate: FakeGate
) -> None:
    stored = credentials_module.save(tmp_path / "kept", gate.port, bundle, key_password=PASSWORD)

    with pytest.raises(ValueError, match="key_password") as refused:
        GateClient(stored, key_password=lambda: answer)

    assert "\udc80" not in str(refused.value)
    assert answer not in str(refused.value)


@pytest.mark.parametrize("answer", ["", b"", None, 7], ids=["empty", "empty bytes", "None", "7"])
def test_a_callable_that_gives_no_password_is_refused(
    answer: Any, tmp_path: Path, bundle: dict[str, Any], gate: FakeGate
) -> None:
    stored = credentials_module.save(tmp_path / "kept", gate.port, bundle, key_password=PASSWORD)

    with pytest.raises(ValueError, match="key_password gave no password"):
        GateClient(stored, key_password=lambda: answer)


class TestPairingUnderAPassword:
    """`api.pair` stores the key under it, and asks a callable before anything is spent."""

    def test_the_key_pairing_stores_is_encrypted_under_it(
        self, gate: FakeGate, host: Issued, tmp_path: Path, bundle: dict[str, Any]
    ) -> None:
        gate.handler = PairingGate(gate, host, bundle=bundle)

        stored = api.pair(
            port=gate.port,
            invitation=INVITATION,
            name="agent-invented",
            directory=tmp_path / "kept",
            key_password=PASSWORD,
        )

        assert encrypted(stored.identity)
        assert opens_with(stored.identity, PASSWORD.encode())

    def test_a_callable_is_asked_once_and_before_the_hello(
        self,
        gate: FakeGate,
        host: Issued,
        tmp_path: Path,
        bundle: dict[str, Any],
        monkeypatch: pytest.MonkeyPatch,
    ) -> None:
        # Once, though the exchange is tried twice; and first, because a password that cannot
        # be had after the invitation is spent loses an identity nobody can ask for again.
        monkeypatch.setattr("pykeepassgate.transport.pause", lambda seconds: None)
        answering = PairingGate(gate, host, bundle=bundle)
        answering.request_refusals = [(503, "busy")]
        gate.handler = answering
        sent_before: list[int] = []

        def asked() -> str:
            sent_before.append(len(gate.requests))
            return PASSWORD

        api.pair(
            port=gate.port,
            invitation=INVITATION,
            name="agent-invented",
            directory=tmp_path / "kept",
            retry_busy_timeout=30.0,
            key_password=asked,
        )

        assert sent_before == [0]
        assert len(gate.requests) == 4, "two exchanges, and the password asked for once"

    def test_nobody_is_asked_for_a_password_a_pairing_refused_anyway_would_not_use(
        self, gate: FakeGate, tmp_path: Path
    ) -> None:
        # Asked last of everything checked before the hello, because a callable may be a person
        # at a prompt, and asking one and then refusing a setting wastes their answer.
        asked = Counting()

        with pytest.raises(ValueError, match="not one directory's name"):
            api.pair(
                port=gate.port,
                invitation=INVITATION,
                name="agent-invented",
                profile="../escaped",
                key_password=asked,
            )

        with pytest.raises(ValueError, match="retry_busy_timeout"):
            api.pair(
                port=gate.port,
                invitation=INVITATION,
                name="agent-invented",
                directory=tmp_path / "kept",
                retry_busy_timeout=-1.0,
                key_password=asked,
            )

        assert asked.asked == 0
        assert gate.names == []

    def test_nobody_is_asked_for_a_password_over_an_invitation_or_a_directory_refused_anyway(
        self, gate: FakeGate, tmp_path: Path
    ) -> None:
        # The invitation, the name and the profiles are read, and the directory is made, before a
        # person is asked: each refuses with nothing spent, and an answer typed first is wasted.
        asked = Counting()
        (tmp_path / "a file").write_text("in the way")
        cases: list[tuple[type[Exception], dict[str, Any]]] = [
            (MalformedToken, {"invitation": "not an invitation"}),
            (PairingError, {"name": " agent"}),
            (PairingError, {"profiles": ["default"]}),
            (StorageError, {"directory": tmp_path / "a file" / "kept"}),
        ]

        for raised, changed in cases:
            given: dict[str, Any] = {
                "port": gate.port,
                "invitation": INVITATION,
                "name": "agent-invented",
                "directory": tmp_path / "kept",
                **changed,
            }
            with pytest.raises(raised):
                api.pair(**given, key_password=asked)

        assert asked.asked == 0
        assert gate.names == []


def test_the_key_is_stored_under_the_scheme_the_documentation_names(
    tmp_path: Path, bundle: dict[str, Any]
) -> None:
    # PBES2, PBKDF2 with HMAC-SHA256 at 2048 iterations, and AES-256-CBC, read out of the stored
    # key's own encoding, so a release of `cryptography` that changed any of it fails here rather
    # than leaving the pages describing a protection the key no longer has.
    stored = credentials_module.save(tmp_path / "kept", 45678, bundle, key_password=PASSWORD)
    pem = stored.identity.read_text("ascii")
    body = pem.split("-----BEGIN ENCRYPTED PRIVATE KEY-----")[1].split("-----END")[0]
    encoded = base64.b64decode("".join(body.split()))

    def oid(*arcs: int) -> bytes:
        """An object identifier as DER writes it, for the two arcs and the rest of each."""
        written = bytes([40 * arcs[0] + arcs[1]])
        for arc in arcs[2:]:
            part = [arc & 0x7F]
            arc >>= 7
            while arc:
                part.insert(0, 0x80 | (arc & 0x7F))
                arc >>= 7
            written += bytes(part)
        return bytes([0x06, len(written)]) + written

    assert oid(1, 2, 840, 113549, 1, 5, 13) in encoded, "PBES2"
    assert oid(1, 2, 840, 113549, 1, 5, 12) in encoded, "PBKDF2"
    assert oid(1, 2, 840, 113549, 2, 9) in encoded, "HMAC-SHA256"
    assert oid(2, 16, 840, 1, 101, 3, 4, 1, 42) in encoded, "AES-256-CBC"
    assert bytes([0x02, 0x02, 0x08, 0x00]) in encoded, "2048 iterations"


class TestRenewingUnderAPassword:
    """The new key is stored as the old one was, and nobody is asked twice."""

    @pytest.fixture
    def renewing(
        self, gate: FakeGate, authority: Authority, renewed_certificate: Issued
    ) -> RenewalGate:
        answering = RenewalGate(bundle_for(authority, renewed_certificate, "2127-01-02T03:04:05Z"))
        gate.handler = answering
        return answering

    @pytest.fixture
    def renewed_certificate(self, authority: Authority) -> Issued:
        return authority.issue(
            "agent-invented", x509.oid.ExtendedKeyUsageOID.CLIENT_AUTH, "renewed.pem"
        )

    def test_the_new_key_is_stored_under_the_password_that_opened_the_old(
        self,
        tmp_path: Path,
        bundle: dict[str, Any],
        gate: FakeGate,
        renewing: RenewalGate,
    ) -> None:
        stored = credentials_module.save(
            tmp_path / "profiles" / "default", gate.port, bundle, key_password=PASSWORD
        )
        asked = Counting()
        client = GateClient(stored, connection_timeout=10.0, key_password=asked)

        renewal = client.renew()

        assert renewal.retired is None, "the old one was given up, which took opening it"
        assert encrypted(renewal.credentials.identity)
        assert opens_with(renewal.credentials.identity, PASSWORD.encode())
        assert [path for path, _, _ in renewing.seen] == ["/gate/renew", "/gate/certificate"]
        client.status()
        assert asked.asked == 1, "asked once, for opening, storing, giving up and carrying on"

    def test_a_key_that_needed_no_password_renews_to_one_that_needs_none(
        self,
        tmp_path: Path,
        bundle: dict[str, Any],
        gate: FakeGate,
        renewing: RenewalGate,
    ) -> None:
        stored = credentials_module.save(tmp_path / "profiles" / "default", gate.port, bundle)

        renewal = GateClient(stored, connection_timeout=10.0, key_password=PASSWORD).renew()

        assert not encrypted(renewal.credentials.identity)


class _Terminal:
    """Standard input that is a terminal, which nothing may read a line from."""

    def isatty(self) -> bool:
        return True

    def readline(self) -> str:
        raise AssertionError("a terminal is asked through getpass, not read a line from")


class _Pipe:
    """Standard input that is not a terminal, and must not be read either."""

    def isatty(self) -> bool:
        return False

    def readline(self) -> str:
        raise AssertionError("nothing is read once the pairing is refused")


def typing(monkeypatch: pytest.MonkeyPatch, *answers: str) -> list[str]:
    """Answer each `getpass` prompt with the next of these, recording the prompts."""
    prompts: list[str] = []
    left = list(answers)

    def typed(
        prompt: str = "Password: ", stream: object = None, *, echo_char: str | None = None
    ) -> str:
        # A key's password shows nothing as it is typed, not even a star per character.
        assert echo_char is None
        prompts.append(prompt)
        return left.pop(0)

    monkeypatch.setattr("getpass.getpass", typed)
    return prompts


class TestTheCommandLine:
    """It asks at a terminal and nowhere else, and takes the password as an argument never."""

    @staticmethod
    def _pair(gate: FakeGate, where: Path) -> int:
        return cli.main(
            [
                "pair",
                "--port",
                str(gate.port),
                "--name",
                "agent-invented",
                "--invitation",
                INVITATION,
                "--directory",
                str(where),
                "--key-password",
            ]
        )

    def test_pairing_asks_twice_and_stores_the_key_under_the_answer(
        self,
        gate: FakeGate,
        host: Issued,
        tmp_path: Path,
        bundle: dict[str, Any],
        monkeypatch: pytest.MonkeyPatch,
        capsys: pytest.CaptureFixture[str],
    ) -> None:
        gate.handler = PairingGate(gate, host, bundle=bundle)
        monkeypatch.setattr("sys.stdin", _Terminal())
        prompts = typing(monkeypatch, PASSWORD, PASSWORD)

        assert self._pair(gate, tmp_path / "kept") == 0

        assert prompts == ["Key password: ", "The same again: "]
        assert opens_with(tmp_path / "kept" / credentials_module.IDENTITY_FILE, PASSWORD.encode())
        captured = capsys.readouterr()
        assert PASSWORD not in captured.out + captured.err

    @pytest.mark.parametrize(
        ("answers", "said"),
        [
            ((PASSWORD, "something else"), "differ"),
            (("", ""), "empty"),
            (("x" * 1024, "x" * 1024), "at most 1023 bytes"),
        ],
        ids=["two that differ", "an empty one", "one no key can be stored under"],
    )
    def test_pairing_refuses_a_password_it_could_not_trust_and_sends_nothing(
        self,
        answers: tuple[str, str],
        said: str,
        gate: FakeGate,
        tmp_path: Path,
        monkeypatch: pytest.MonkeyPatch,
        capsys: pytest.CaptureFixture[str],
    ) -> None:
        monkeypatch.setattr("sys.stdin", _Terminal())
        typing(monkeypatch, *answers)

        assert self._pair(gate, tmp_path / "kept") == 2

        assert said in capsys.readouterr().err
        assert gate.names == []
        assert not (tmp_path / "kept").exists()

    def test_pairing_with_no_terminal_reads_nothing_and_sends_nothing(
        self,
        gate: FakeGate,
        tmp_path: Path,
        monkeypatch: pytest.MonkeyPatch,
        capsys: pytest.CaptureFixture[str],
    ) -> None:
        monkeypatch.setattr("sys.stdin", _Pipe())

        assert self._pair(gate, tmp_path / "kept") == 2

        assert "key_password=" in capsys.readouterr().err, "it says how a program gives one"
        assert gate.names == []

    @pytest.mark.parametrize(
        ("arguments", "code", "said"),
        [
            (["--invitation", INVITATION, "--key-password"], 2, "there is none here"),
            ([], 1, "MalformedToken"),
        ],
    )
    def test_input_from_the_null_device_is_no_terminal(
        self, gate: FakeGate, tmp_path: Path, arguments: list[str], code: int, said: str
    ) -> None:
        # On Windows `isatty` is true for NUL, which is where automation points a command's
        # input, and `getpass` then waits on the console for good instead of refusing at once.
        # A real interpreter, because only a real descriptor is the device being asked about.
        # Every argument is this test's own, which is what `S603` asks about.
        ended = subprocess.run(  # noqa: S603
            [
                sys.executable, "-m", "pykeepassgate.cli", "pair", "--port", str(gate.port),
                "--name", "agent-invented", "--directory", str(tmp_path / "kept"), *arguments,
            ],
            stdin=subprocess.DEVNULL,
            capture_output=True,
            text=True,
            timeout=60,
            check=False,
        )

        assert ended.returncode == code, ended.stderr
        assert said in ended.stderr
        assert gate.names == []

    def test_a_pairing_refused_anyway_asks_for_no_password(
        self,
        gate: FakeGate,
        tmp_path: Path,
        monkeypatch: pytest.MonkeyPatch,
        capsys: pytest.CaptureFixture[str],
    ) -> None:
        # Typed twice and thrown away for an invitation that is not one would be a person's answer
        # wasted, so the invitation is read first.
        monkeypatch.setattr("sys.stdin", _Terminal())
        prompts = typing(monkeypatch)

        code = cli.main(
            [
                "pair", "--port", str(gate.port), "--name", "agent-invented",
                "--invitation", "not an invitation", "--directory", str(tmp_path / "kept"),
                "--key-password",
            ]
        )

        assert code == cli.FAILED
        assert "MalformedToken" in capsys.readouterr().err
        assert prompts == []
        assert gate.names == []

    def test_there_is_no_option_that_takes_the_password(self) -> None:
        # The flag is a switch: anything after it is the next argument, never its value, so a
        # password typed there is refused as an argument rather than taken.
        with pytest.raises(SystemExit) as raised:
            cli.main(["pair", "--port", "1", "--name", "agent", "--key-password=" + PASSWORD])

        assert raised.value.code == 2

    @pytest.fixture
    def kept(self, tmp_path: Path, bundle: dict[str, Any], gate: FakeGate) -> Path:
        serving_status(gate)
        credentials_module.save(tmp_path / "kept", gate.port, bundle, key_password=PASSWORD)
        return tmp_path / "kept"

    def test_a_command_asks_for_the_password_of_an_encrypted_key_once(
        self, kept: Path, monkeypatch: pytest.MonkeyPatch
    ) -> None:
        monkeypatch.setattr("sys.stdin", _Terminal())
        prompts = typing(monkeypatch, PASSWORD)

        assert cli.main(["status", "--directory", str(kept)]) == 0
        assert prompts == ["Key password: "]

    def test_a_command_never_asks_about_a_key_that_is_not_encrypted(
        self,
        tmp_path: Path,
        bundle: dict[str, Any],
        gate: FakeGate,
        monkeypatch: pytest.MonkeyPatch,
    ) -> None:
        serving_status(gate)
        credentials_module.save(tmp_path / "plain", gate.port, bundle)
        monkeypatch.setattr("sys.stdin", _Terminal())
        prompts = typing(monkeypatch)

        assert cli.main(["status", "--directory", str(tmp_path / "plain")]) == 0
        assert prompts == []

    @pytest.mark.parametrize(
        ("stdin", "answers", "said"),
        [
            (_Pipe(), (), "asked for only at a terminal"),
            (_Terminal(), ("not the one",), "does not open"),
            (_Terminal(), ("",), "no password was typed"),
        ],
        ids=["no terminal", "the wrong one", "none typed"],
    )
    def test_a_key_that_will_not_open_is_an_exit_code_naming_the_file(
        self,
        stdin: object,
        answers: tuple[str, ...],
        said: str,
        kept: Path,
        gate: FakeGate,
        monkeypatch: pytest.MonkeyPatch,
        capsys: pytest.CaptureFixture[str],
    ) -> None:
        monkeypatch.setattr("sys.stdin", stdin)
        typing(monkeypatch, *answers)

        assert cli.main(["status", "--directory", str(kept)]) == 1

        printed = capsys.readouterr().err
        assert printed.startswith("StorageError: ")
        assert credentials_module.IDENTITY_FILE in printed
        assert said in printed
        assert gate.names == []

    def test_an_encrypted_key_with_input_from_the_null_device_is_refused_at_once(
        self, kept: Path, gate: FakeGate
    ) -> None:
        # The other half of NUL counting as a terminal: the key's password is asked for at a
        # terminal only, and a command run with its input from NUL has none.
        # Every argument is this test's own, which is what `S603` asks about.
        ended = subprocess.run(  # noqa: S603
            [sys.executable, "-m", "pykeepassgate.cli", "status", "--directory", str(kept)],
            stdin=subprocess.DEVNULL,
            capture_output=True,
            text=True,
            timeout=60,
            check=False,
        )

        assert ended.returncode == 1, ended.stderr
        assert "asked for only at a terminal" in ended.stderr
        assert gate.names == []


def test_the_password_is_printed_by_nothing_that_refuses_it(
    tmp_path: Path, bundle: dict[str, Any], gate: FakeGate
) -> None:
    # A wrong one, one inside something that is not a password, and one a callable hands back
    # inside something that is not a password: none of them is put in the message.
    stored = credentials_module.save(
        tmp_path / "kept", gate.port, bundle, key_password=PASSWORD
    )
    attempts: list[Callable[[], object]] = [
        lambda: GateClient(stored, key_password=PASSWORD[:-1]),
        lambda: GateClient(stored, key_password=cast(Any, [PASSWORD])),
        lambda: GateClient(stored, key_password=cast(Any, lambda: [PASSWORD])),
    ]

    for attempt in attempts:
        with pytest.raises((StorageError, ValueError)) as refused:
            attempt()

        assert PASSWORD not in str(refused.value)
        assert PASSWORD not in repr(refused.value)
