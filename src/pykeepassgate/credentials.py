"""What a paired client keeps, and where it keeps it.

Pairing hands over two secrets, a trust anchor and some text about them. The authority is a
certificate anybody may see; what has to stay unread is the client's own key and the gate key. This
writes each of the three to a file of its own and the text to a fourth, so that nothing secret is in
the file a person is most likely to open, paste or attach.

**The client's key is on the disk, and that is not the contradiction it looks like.** The plugin
goes to some trouble to keep the *host* key out of the user's profile, because a password manager
holding the database can hold its own key in memory for as long as it runs. A client is a program
that has to still be the same client tomorrow, so its key has to survive it exiting. What the
design concedes throughout, and concedes here as well, is local code execution as the user: nothing
in this file defends against it, and nothing claims to.

What it does do is refuse to be careless. The files are *created* with owner-only permissions
where the platform has them, rather than created and then narrowed, and the key is written once
rather than through a temporary file that a crash could leave behind. A directory cannot be made
that way, because `mkdir` gives its mode to the last directory alone, so each one this client
makes is narrowed straight after it, and one that was already there keeps the mode it had.
Nothing is written at all until the whole bundle has been read and found to be what it claims.

**The four files are opened before the exchange and written through those handles after it**, so
nothing that could take away the right to write them, a permission or attribute changed or the
file taken by somebody else, can do so between the check and the write. That is all the handles
promise. Nothing reserves space and nothing is written aside and swapped in, which was weighed and
declined to keep this simple: **a disk that fills between the four writes, or a crash part way
through them, still leaves an identity that is part new and part old**, and that is the accepted
cost of writing in place.
"""

from __future__ import annotations

import base64
import contextlib
import hashlib
import json
import os
import stat
from dataclasses import dataclass
from datetime import UTC, datetime, timedelta
from pathlib import Path
from types import TracebackType
from typing import Any

from cryptography import x509
from cryptography.hazmat.primitives import serialization
from cryptography.hazmat.primitives.serialization import pkcs12

from . import sni, transport
from .errors import NotPaired, ProtocolError, StorageError

IDENTITY_FILE = "identity.pem"
AUTHORITY_FILE = "authority.pem"
GATE_KEY_FILE = "gate.key"
DETAILS_FILE = "client.json"

#: The four files an identity is, in the order they are written, so that `client.json`, which
#: `load` reads first and refuses without, is the last thing to change. Nothing else in a directory
#: is this client's: a renewal moves these four and leaves anything beside them where it is.
FILES = (IDENTITY_FILE, GATE_KEY_FILE, AUTHORITY_FILE, DETAILS_FILE)

#: The reparse tags of a link on Windows, `IO_REPARSE_TAG_SYMLINK` and
#: `IO_REPARSE_TAG_MOUNT_POINT`, which is a junction. Written out because `stat` names them only on
#: Windows, and a type checker told it runs elsewhere calls the names undefined.
_LINK_TAGS = (0xA000000C, 0xA0000003)

#: Owner read and write, and nothing for anybody else. Meaningless on Windows, where the file's
#: inherited ACL is what actually decides, so it is applied anyway and relied on nowhere.
_OWNER_ONLY = stat.S_IRUSR | stat.S_IWUSR

#: What a profile's name cannot hold, because it is joined as one directory: either separator, on
#: every platform so that a name means the same wherever it is used, the colon that names a drive,
#: and the one character no file name anywhere can carry.
_NOT_IN_A_PROFILE = frozenset("/\\:\0")


def default_directory(name: str = "default") -> Path:
    """Where credentials live when nobody says otherwise.

    Per profile rather than one place, because a client that has paired with two databases holds
    two identities and they are not interchangeable.

    **A profile is one directory's name**, and anything else is refused with `ValueError` rather
    than joined. A separator or `..` would put the identity beside or above the others, an absolute
    path would replace the place altogether, and `whoami` would read it back from there as though
    it were a profile. A name ending in a dot or a space is refused on every platform because
    Windows stores it without them, which makes `work.` and `work` one identity and `...` the
    directory every profile lives in. A path is what a `directory` is for.
    """
    if not name or name[-1] in ". " or not _NOT_IN_A_PROFILE.isdisjoint(name):
        raise ValueError(
            f"profile {name!r} is not one directory's name: a profile holds no separator, colon "
            "or null and ends in neither a dot nor a space"
        )

    # `os.name` rather than `sys.platform`, which a type checker narrows to whichever platform it
    # happens to be running on and then calls the other branch unreachable.
    if os.name == "nt":
        # Local rather than roaming. What goes in here is an unencrypted private key and
        # a gate key, and `%APPDATA%` is copied to a domain file server at logoff on a
        # machine with a roaming profile or folder redirection, which are the ordinary
        # enterprise settings. A credential that identifies this client should not
        # follow the person to every machine they sign into.
        local = os.environ.get("LOCALAPPDATA")
        base = Path(local) if local else Path.home() / "AppData" / "Local"
    else:
        # A relative value is ignored, as the XDG Base Directory specification says it is to be:
        # it would name another directory from every working directory, so a `pair` run in one
        # would store what a `whoami` run in another could not find.
        config = os.environ.get("XDG_CONFIG_HOME")
        base = Path(config) if config and Path(config).is_absolute() else Path.home() / ".config"

    return base / "pykeepassgate" / name


@dataclass(frozen=True)
class Credentials:
    """One paired identity: where it is stored, and what it is for."""

    directory: Path
    port: int
    subject: str
    database: str
    profiles: tuple[str, ...]
    expires: str

    @property
    def identity(self) -> Path:
        """The client's own certificate and key, which `ssl` loads as one file."""
        return self.directory / IDENTITY_FILE

    @property
    def authority(self) -> Path:
        """The one certificate this client trusts to have issued a host certificate."""
        return self.directory / AUTHORITY_FILE

    @property
    def gate_key(self) -> bytes:
        """The key every connection after pairing is named from."""
        try:
            return (self.directory / GATE_KEY_FILE).read_bytes()
        except OSError as error:
            # Every one of them, a file that has gone among them, which is damage here rather
            # than a client that never paired: `load` found `client.json` beside it. Left
            # alone they reach the caller mid request as a bare `OSError`: a file locked by a
            # backup agent, a directory locked down after this client opened.
            raise StorageError(f"the gate key in {self.directory} will not read") from error

    @property
    def expired(self) -> bool:
        """Whether the certificate has expired, by `expires` and this machine's clock.

        The gate refuses an expired certificate as it refuses a stranger, with nothing to say
        why, so a client that knows it has expired says so itself. False for an `expires` this
        cannot read as well, which leaves the gate to say: a client refused on a guess would be
        one refused for nothing.
        """
        return self.expired_by(timedelta(0))

    def expired_by(self, margin: timedelta) -> bool:
        """Whether the certificate expired more than `margin` ago by this machine's clock.

        What a client asks before refusing to dial, with the leeway a clock running ahead of the
        gate's is owed; False for an `expires` this cannot read, as `expired` is.
        """
        try:
            ends = datetime.strptime(self.expires, "%Y-%m-%dT%H:%M:%SZ").replace(tzinfo=UTC)
        except ValueError:
            return False

        return ends + margin < datetime.now(UTC)

    @property
    def admission_key(self) -> str:
        """SHA-256 over this client's certificate, in lower case hex: what the gate knows it by.

        The plugin keys this client's admission record on the same digest, and its audit file
        names this client by it on every line, so a person holding both can tell which lines are
        this client's. Read off the certificate in `identity.pem` rather than stored beside it, so
        it cannot disagree with the certificate this client presents; nothing about the key is
        read, so an encrypted one needs no password here.
        """
        try:
            certificate = x509.load_pem_x509_certificate(self.identity.read_bytes())
        except (OSError, ValueError) as error:
            raise StorageError(
                f"{self.identity} holds no certificate this client can read"
            ) from error

        return hashlib.sha256(certificate.public_bytes(serialization.Encoding.DER)).hexdigest()


def forget(directory: Path) -> None:
    """Take away what pairing stored in a directory, and the directory if that empties it.

    For an identity nothing can use any more, which is one whose certificate has been revoked: a
    private key kept for a certificate the gate refuses protects nothing and is one more copy of a
    key to look after. Only the four files this client writes are touched, so a directory holding
    anything else is left holding it, and a file already gone is not an error.
    """
    for name in FILES:
        path = directory / name
        try:
            path.unlink()
        except FileNotFoundError:
            continue
        except OSError as error:
            raise StorageError(f"{path} could not be removed: {error}") from error

    # Only an empty one, which `rmdir` refuses to be anything else about.
    with contextlib.suppress(OSError):
        directory.rmdir()


def save(
    directory: Path,
    port: int,
    bundle: dict[str, Any],
    allow_shared_directory: bool = False,
) -> Credentials:
    """Write what pairing handed over, and hand back what a client needs to use it.

    The PKCS#12 the gate sends is converted to PEM here rather than at every connection, because
    `ssl` reads PEM from a path and nothing else, and because converting once means the key is
    handled once.

    **Everything is read before anything is written.** A bundle refused halfway destroys a
    working identity: the key and the gate key would already be the new ones while the anchor
    and the details still described the old, so `load` would report a subject that does not
    belong to the key beside it. On a first pairing it would leave an unencrypted key with no
    `client.json`, which `load` refuses, so nothing would ever clean it up.

    `allow_shared_directory` is `prepare`'s, and means the same here.
    """
    # Everything is read before anything is written. See the note in the docstring above: a
    # bundle refused halfway is a working identity destroyed, and the invitation that produced
    # it is spent either way, so there is nothing left to try again with.
    contents = _contents(port, bundle)

    with prepare(directory, allow_shared_directory=allow_shared_directory) as prepared:
        prepared.write(contents)

    return load(directory)


class Prepared:
    """The four files `prepare` opened, held until the write and let go of after it.

    Leaving the `with` closes every handle and, if nothing was written by then, takes away again
    each file that opening created: a pairing that did not complete leaves no empty identity
    behind, and a file that was already there is left exactly as it was.
    """

    def __init__(self, directory: Path) -> None:
        """Hold nothing yet: `prepare` opens the files, and closes them again on a refusal."""
        self.directory = directory
        self._handles: dict[str, int] = {}
        self._created: list[str] = []
        self._written = False

    def save(self, port: int, bundle: dict[str, Any]) -> Credentials:
        """What `save` does, through the handles this already holds."""
        self.write(_contents(port, bundle))
        return load(self.directory)

    def write(self, contents: dict[str, bytes]) -> None:
        """Replace all four files, truncating each only as it is written.

        Not before: the handles are opened while the pairing may yet be refused, and an existing
        identity is the working one until there is another to put in its place.
        """
        for name in FILES:
            _write(self.directory / name, self._handles[name], contents[name])

        self._written = True

    def __enter__(self) -> Prepared:
        """Hand back the same handles, which `prepare` has already opened."""
        return self

    def __exit__(
        self,
        kind: type[BaseException] | None,
        error: BaseException | None,
        trace: TracebackType | None,
    ) -> None:
        """Close whether the exchange completed or not, which `close` itself tells apart."""
        self.close()

    def close(self) -> None:
        """Let go of every handle, and take away what opening created if nothing was written."""
        for descriptor in self._handles.values():
            with contextlib.suppress(OSError):
                os.close(descriptor)
        self._handles.clear()

        if not self._written:
            for name in self._created:
                with contextlib.suppress(OSError):
                    (self.directory / name).unlink()
        self._created.clear()

    def open(self, name: str) -> None:
        """Open one file for writing without truncating it, creating it if it is not there.

        **A link at the file's name is refused rather than followed**, or whoever planted it
        would choose which file receives the key and then has it narrowed to this user's.
        `prepare` refuses one when it looks; `O_NOFOLLOW` is the same answer for the instant
        between looking and opening, where the platform has it. Windows does not, and a link can
        be made there by any account in Developer Mode and by an elevated one, so on every
        platform the file opened is then held to the file at the name, by device and inode,
        before anything is written through it; after that the handle is what is written.
        """
        path = self.directory / name
        flags = os.O_WRONLY | getattr(os, "O_NOFOLLOW", 0) | getattr(os, "O_BINARY", 0)
        try:
            try:
                descriptor = os.open(path, flags | os.O_CREAT | os.O_EXCL, _OWNER_ONLY)
                self._created.append(name)
            except FileExistsError:
                descriptor = os.open(path, flags)
        except OSError as error:
            raise StorageError(f"{path} could not be opened for writing: {error}") from error

        self._handles[name] = descriptor
        # cspell:ignore fstat
        try:
            opened, there = os.fstat(descriptor), path.lstat()
        except OSError as error:
            raise StorageError(f"{path} could not be opened for writing: {error}") from error

        if _linked(there) or (opened.st_dev, opened.st_ino) != (there.st_dev, there.st_ino):
            raise StorageError(
                f"{path} could not be opened for writing: it is a link, which is not followed"
            )


def _contents(port: int, bundle: dict[str, Any]) -> dict[str, bytes]:
    """All four files' contents, read out of the bundle before any of them is touched."""
    # The port as well, which is not the bundle's: one TCP does not have would be written into
    # `client.json`, and `load` would then refuse the identity stored beside it.
    transport.port_number(port)

    return {
        IDENTITY_FILE: _identity(_text(bundle, "identity")),
        AUTHORITY_FILE: _anchor(_text(bundle, "authority")),
        GATE_KEY_FILE: _gate_key(_text(bundle, "gateKey")),
        DETAILS_FILE: _details(port, bundle),
    }


def load(directory: Path) -> Credentials:
    """Read back what `save` wrote.

    This is the one place that reads a file somebody could have edited, truncated or filled a
    disk halfway through, so it is the one place that refuses in this library's own terms. Left
    alone, a `client.json` that will not parse arrives as `JSONDecodeError`, one missing its
    port as `KeyError`, one holding a JSON array as `TypeError` and one whose port is not a
    number as `ValueError`. That is four ways past a caller who was promised one root to catch,
    and four tracebacks out of a command line that documents an exit code instead.

    **No `client.json` at all is `NotPaired`**, a `StorageError` rather than a
    `FileNotFoundError`: nothing is stored here, which is a state and not damage, and it is the
    one a caller meets first, so it is this library's to name like every other.

    **Every member is held to what `save` writes**, and the text ones and the profiles are not
    converted into it: `str` makes a `null` the four letters `None`, and `tuple` spells a string
    out one character at a time, which `whoami` would then show as that many profiles approved.
    """
    details_path = directory / DETAILS_FILE
    if not details_path.exists():
        raise NotPaired(
            f"no paired client in {directory}: pair first, or name the profile or the directory "
            "the pairing used"
        )

    try:
        details = json.loads(details_path.read_text("utf-8"))
    except (OSError, ValueError) as error:
        raise StorageError(f"{details_path} will not read: {error}") from error

    if not isinstance(details, dict):
        raise StorageError(f"{details_path} is not what this client wrote")

    # A whole number TCP has, as `save` writes one. `int` would take `"45678"`, `45678.9` and
    # `true`, and a number TCP does not have would be dialed and leave the socket as an
    # `OverflowError`, which is not this library's to raise.
    port = details.get("port")
    if isinstance(port, bool) or not isinstance(port, int) or not 0 < port <= 65535:
        raise StorageError(f"{details_path} names no port this client can use")

    for key in ("subject", "database", "expires"):
        if not isinstance(details.get(key), str):
            raise StorageError(f"{details_path} names no {key} this client wrote")

    profiles = details.get("profiles")
    if not isinstance(profiles, list) or not all(isinstance(name, str) for name in profiles):
        raise StorageError(f"{details_path} names its profiles as something other than names")

    return Credentials(
        directory=directory,
        port=port,
        subject=details["subject"],
        database=details["database"],
        profiles=tuple(profiles),
        expires=details["expires"],
    )


def _identity(value: str) -> bytes:
    """The PKCS#12 the gate sent, as the PEM `ssl` will read."""
    try:
        key, certificate, _ = pkcs12.load_key_and_certificates(
            _base64(value, "identity"),
            # The gate writes the PKCS#12 with no password: what protects it in the database is
            # the database, and what protects it here is the file.
            password=None,
        )
    except ValueError as error:
        raise ProtocolError("the bundle's identity is not a PKCS#12") from error

    if key is None or certificate is None:
        raise ProtocolError("the bundle's identity carries no certificate and key")

    return certificate.public_bytes(serialization.Encoding.PEM) + key.private_bytes(
        encoding=serialization.Encoding.PEM,
        format=serialization.PrivateFormat.PKCS8,
        encryption_algorithm=serialization.NoEncryption(),
    )


def _anchor(value: str) -> bytes:
    """The trust anchor, parsed rather than wrapped in delimiters.

    Writing the PEM markers around whatever arrived stores a base64 blob that need not be a
    certificate at all, and the refusal then turns up at the next connection out of
    `SSLContext.load_verify_locations` as an `SSLError`, which is an `OSError` and so is not a
    `GateError`. The identity beside it is parsed; this is the same care for the half of the
    bundle that decides who this client will trust from here on.
    """
    try:
        certificate = x509.load_der_x509_certificate(_base64(value, "authority"))
    except ValueError as error:
        raise ProtocolError("the bundle's authority is not a certificate") from error

    return certificate.public_bytes(serialization.Encoding.PEM)


def _gate_key(value: str) -> bytes:
    """The key every name after pairing is built from, at the one length it can be."""
    key = _base64(value, "gateKey")
    if len(key) != sni.KEY_LENGTH:
        raise ProtocolError(f"a gate key is {sni.KEY_LENGTH} bytes, not {len(key)}")

    return key


def _details(port: int, bundle: dict[str, Any]) -> bytes:
    """The text about the identity, which is the part of the bundle nothing else checks."""
    profiles = bundle.get("profiles") or []
    if not isinstance(profiles, list) or not all(isinstance(name, str) for name in profiles):
        # `list()` over a string spells it out one character at a time, and the operator is
        # then shown six profiles nobody approved.
        raise ProtocolError("the bundle's profiles are not a list of names")

    details = {
        "port": port,
        "subject": _optional(bundle, "subject"),
        "database": _optional(bundle, "database"),
        "expires": _optional(bundle, "expires"),
        "profiles": list(profiles),
    }

    return json.dumps(details, indent=2).encode("utf-8") + b"\n"


def _optional(bundle: dict[str, Any], key: str) -> str:
    """Text the bundle may leave out, which is not the same as text it set to null.

    `get` with a default fires only on absence, so a JSON `null` travels all the way to `str()`
    and comes back as the four letters `None`.
    """
    value = bundle.get(key)
    return value if isinstance(value, str) else ""


def prepare(directory: Path, allow_shared_directory: bool = False) -> Prepared:
    """Make the directory and open the four files, so that `save` can write into them later.

    **Called before the exchange, because afterwards is too late.** Once the gate has answered,
    the invitation is spent and the admission record is written and saved, so a directory that
    turns out to be a file, to sit under a parent this user cannot create in, or to refuse
    writes, loses an identity that cannot be asked for again and leaves a record for a
    certificate nobody holds. None of that needs the bundle to find out, so it is found out
    first, and a refusal here has cost nothing.

    **What comes back holds the four files open**, and is a context manager around the exchange:
    `Prepared.save` writes through it, and leaving it closes them. Opening is the only honest
    answer to whether a file can be created or replaced, since `os.access` asks the mode bits,
    and on Windows only the read-only attribute, so it says yes to a directory whose ACL refuses
    the write that matters. Opening truncates nothing: an identity already there is left exactly
    as it was until the write, and a file that opening created is taken away again if the write
    never comes. `save` called on its own prepares for itself.

    **`allow_shared_directory` waives one question and only one**: whether a directory that
    already existed belongs to another account or may be written by one. It is for a caller who
    keeps credentials somewhere shared on purpose and has said so. A directory that cannot be
    made or written to, and a name taken by a link or by anything but a writable regular file,
    are refused whatever it says, and opening still refuses to follow a link: the flag says
    others can reach this directory, not that nothing should be checked.

    **The directory itself is no link either**, a symbolic link or a junction, asked before it is
    made and again after, so one planted between the two is refused as well: every other check
    here would follow it, and the identity would be written into whichever directory it named.
    """
    refuse_link(directory)
    try:
        _make(directory)
    except OSError as error:
        raise StorageError(f"{directory} cannot hold credentials: {error}") from error

    refuse_link(directory)

    # Looked up rather than named: Windows has no `geteuid`, and a type checker told which
    # platform it runs on calls a branch on `os.name` unreachable or its call undefined.
    me = getattr(os, "geteuid", None)
    if me is not None and not allow_shared_directory:
        status = directory.stat()
        reason = _shared(status.st_mode, status.st_uid, me())
        if reason:
            raise StorageError(
                f"{directory} cannot hold credentials: {reason}. If that is intended, "
                "allow_shared_directory, or --allow-shared-directory on pair, accepts it"
            )

    for name in FILES:
        _replaceable(directory / name)

    prepared = Prepared(directory)
    try:
        for name in FILES:
            prepared.open(name)
    except BaseException:
        prepared.close()
        raise

    return prepared


def refuse_link(path: Path) -> None:
    """Refuse a name taken by a symbolic link or a junction, which this client never follows."""
    try:
        status = path.lstat()
    except FileNotFoundError:
        return
    except OSError as error:
        raise StorageError(f"{path} cannot hold credentials: {error}") from error

    if _linked(status):
        raise StorageError(
            f"{path} is a link, and credentials are kept where they are named rather than "
            "wherever a link points"
        )


def _linked(status: os.stat_result) -> bool:
    """Whether what `lstat` described is a link: a symbolic one, or a junction on Windows.

    A junction is not a symbolic link to `stat`, and Windows says it is one only through the
    reparse tag, which exists on no other platform. Other reparse points, a file a cloud service
    keeps as a placeholder among them, are no link and are left alone.
    """
    return stat.S_ISLNK(status.st_mode) or getattr(status, "st_reparse_tag", 0) in _LINK_TAGS


def _replaceable(path: Path) -> None:
    """Refuse a name taken by anything but a regular file this client may write, if it is taken."""
    try:
        status = path.lstat()
    except FileNotFoundError:
        return
    except OSError as error:
        raise StorageError(f"{path} cannot be replaced: {error}") from error

    if not stat.S_ISREG(status.st_mode) or not os.access(path, os.W_OK):
        raise StorageError(f"{path} is not a file this client can replace")


def _shared(mode: int, owner: int, me: int) -> str:
    """Why a directory that already existed is not this client's to write a key into, if it is not.

    A directory somebody else owns, or one another account may write to, lets that account put
    a file of its own choosing where the key is about to go, or swap `authority.pem` afterwards
    for an anchor it holds the key to. Local code execution as this user is conceded throughout;
    as some other user it is not. Asked only where the platform has modes: on Windows the ACL
    decides, and nothing here can read it.
    """
    if owner != me:
        return "it belongs to another account"

    if mode & (stat.S_IWGRP | stat.S_IWOTH):
        return f"other accounts may write to it (mode {stat.S_IMODE(mode):o})"

    return ""


def _make(directory: Path) -> None:
    """Create the directory and whatever it needs above it, narrowing only what this created.

    The narrowing has to happen after the fact rather than in the call: `mkdir` applies its
    `mode` to the leaf alone and creates everything above it with the process umask, so a
    `pykeepassgate` directory this call brings into existence would name which profiles exist
    to anybody while the files inside it are not readable at all.

    **What was already there keeps the mode it had**, and that is the whole of what this
    refuses to do. A directory the caller named is the caller's, and its parent is nobody's
    business here: `--directory ~/agent` would otherwise change the mode of a home directory,
    and a parent this process does not own, `/tmp` among them, would refuse the change and
    raise. `prepare` is the only caller and runs this once, before the exchange, so a refusal
    then costs nothing; what is written afterwards goes through the handles `prepare` opened, and
    `save` on its own prepares for itself.
    """
    # Collected before the call, because afterwards there is no way to tell what it made.
    fresh = [path for path in (directory, *directory.parents) if not path.exists()]
    directory.mkdir(parents=True, exist_ok=True, mode=stat.S_IRWXU)
    for path in fresh:
        _own(path, stat.S_IRWXU)


def _write(path: Path, descriptor: int, content: bytes) -> None:
    """Replace what the handle `prepare` opened holds, truncating it only now.

    The file was created closed, with the mode in the call, rather than created open and
    narrowed after: `Path.write_bytes` creates with the process umask, which is world-readable
    under the usual one, and the client's key would exist at 0644 until a `chmod` caught up.
    """
    try:
        os.ftruncate(descriptor, 0)
        os.lseek(descriptor, 0, os.SEEK_SET)
        view = memoryview(content)
        while view:
            view = view[os.write(descriptor, view) :]
    except OSError as error:
        raise StorageError(f"{path} could not be written: {error}") from error

    # The mode at opening applies only where the file is created, so an overwrite keeps
    # whatever the old one had. A re-pair is an overwrite. Narrowed through the handle,
    # because the name may be somebody else's link by now.
    _own(path, _OWNER_ONLY, descriptor)


def _own(path: Path, mode: int, descriptor: int | None = None) -> None:
    """Narrow what a platform with permissions will honor.

    Suppressed on Windows only, where the inherited ACL is what actually decides and a
    `chmod` means almost nothing either way. Everywhere else a refusal means the key is
    still readable by whoever could read it before, which is the one thing this function
    exists to prevent, so it is reported rather than swallowed.
    """
    if os.name == "nt":
        with contextlib.suppress(OSError):
            path.chmod(mode)
        return

    try:
        if descriptor is None:
            path.chmod(mode)
        else:
            # A descriptor has no `Path` to ask, and the name may be somebody else's link.
            os.chmod(descriptor, mode)  # noqa: PTH101
    except OSError as error:
        raise StorageError(f"{path} could not be made owner-only: {error}") from error


def _text(bundle: dict[str, Any], key: str) -> str:
    value = bundle.get(key)
    if not isinstance(value, str) or not value:
        raise ProtocolError(f"the bundle is missing {key}")

    return value


def _base64(value: str, key: str) -> bytes:
    try:
        return base64.b64decode(value, validate=True)
    except ValueError as error:
        raise ProtocolError(f"the bundle's {key} is not base64") from error
