"""The stand-in gate the package ships, held to what its documentation promises a program.

Every other test here uses it; these are about it: that it needs no test framework, that a gate
nobody scripted answers as one with no routes, that a block ends it, and that the arrangement the
documentation shows really is enough to point a client at it.
"""

from __future__ import annotations

import datetime as dt
import json
import socket
import ssl
import subprocess
import sys
from collections.abc import Callable
from pathlib import Path

import pytest
from cryptography.x509.oid import ExtendedKeyUsageOID

from pykeepassgate import api
from pykeepassgate.errors import GateBusy, RouteRefused
from pykeepassgate.testing import Authority, FakeGate, held, written


def test_it_imports_no_test_framework() -> None:
    # A program tests with whatever it tests with, so the module may need nothing of pytest's.
    # A fresh interpreter, because this one has pytest loaded whatever the module imports.
    ended = subprocess.run(
        [
            sys.executable,
            "-c",
            "import sys, pykeepassgate.testing; print('pytest' in sys.modules)",
        ],
        capture_output=True,
        text=True,
        check=False,
    )

    assert ended.returncode == 0, ended.stderr
    assert ended.stdout.strip() == "False"


def test_what_the_documentation_shows_is_enough_to_point_a_client_at_it(tmp_path: Path) -> None:
    # The example in `docs/pykeepassgate.md`, run: an authority, a host and a client under it,
    # a gate key, and credentials naming the gate's port.
    authority = Authority(tmp_path)
    host = authority.issue("host", ExtendedKeyUsageOID.SERVER_AUTH, "host.pem")
    authority.issue("agent-invented", ExtendedKeyUsageOID.CLIENT_AUTH, "identity.pem")
    (tmp_path / "gate.key").write_bytes(bytes(32))

    with FakeGate(host, authority.pem) as gate:
        gate.handler = lambda path, body, client: (503, '{"error":"busy"}')
        credentials = api.Credentials(tmp_path, gate.port, "CN=agent-invented", "", (), "", "")
        client = api.GateClient(credentials, connection_timeout=10.0)

        with pytest.raises(GateBusy):
            client.groups()

    # Not the status route, which the client asks first and the stand-in answers itself.
    assert gate.requests == [("GET", "/groups")]


def test_a_held_approval_is_answered_as_the_plugin_answers_it() -> None:
    # The body `PairingHandler.HeldBody` writes, for a handler to hand to `pair` or `renew`.
    status, body = held(dt.datetime(2026, 10, 14, 11, 6, tzinfo=dt.timezone(dt.timedelta(hours=2))))

    assert status == 503
    assert json.loads(body) == {"error": "busy", "heldUntil": "2026-10-14T09:06:00Z"}


#: What no entity tag is: beyond ASCII, without its quotes, a header break, past its last quote,
#: and a quote inside, which RFC 9110 section 8.8.3 leaves out of a tag's characters.
NO_TAGS = ['"é"', "0a1b", '"a\r\nX-Invented: 1"', '""x', '"a"b"']


def test_a_write_is_answered_with_its_tag_in_the_body_as_the_plugin_answers_it() -> None:
    # `VaultHandlers.Answer`: `ok`, or what a create made, with `etag` beside it when there is one.
    assert written() == (200, '{"ok":true}')
    assert written('"0a1b"') == (200, '{"ok":true,"etag":"\\"0a1b\\""}')
    assert written('"0a1b"', uuid="Account") == (201, '{"uuid":"Account","etag":"\\"0a1b\\""}')
    assert written(uuid="7b1c") == (201, '{"uuid":"7b1c"}')


@pytest.mark.parametrize("tag", NO_TAGS)
def test_a_write_is_not_answered_with_what_is_no_tag(tag: str) -> None:
    # Held as `tag` holds a header's, since the plugin writes one tag in both places.
    with pytest.raises(ValueError, match="not a tag the gate could send"):
        written(tag)


def test_a_gate_nobody_scripted_answers_that_it_has_no_such_route(
    gate: FakeGate, tmp_path: Path, bundle: dict[str, object]
) -> None:
    stored = api.save(tmp_path / "kept", gate.port, dict(bundle))

    with pytest.raises(RouteRefused) as refused:
        api.GateClient(stored, connection_timeout=10.0).groups()

    assert (refused.value.status, refused.value.error) == (404, "no such route")


def test_a_gate_used_as_a_block_stops_listening_when_the_block_ends(
    tmp_path: Path,
) -> None:
    authority = Authority(tmp_path)
    host = authority.issue("host", ExtendedKeyUsageOID.SERVER_AUTH, "host.pem")

    with FakeGate(host, authority.pem) as gate:
        port = gate.port

    # Refused or timed out, as the platform has it: either way nothing is listening there.
    with pytest.raises(OSError):
        socket.create_connection(("127.0.0.1", port), timeout=5).close()


def test_a_connection_that_named_nothing_is_said_to_have_named_nothing(gate: FakeGate) -> None:
    with pytest.raises(AssertionError, match="carried no name"):
        gate.named()


def answered(
    gate: FakeGate,
    authority: Authority,
    request: bytes = b"GET /gate/whoami HTTP/1.1\r\nHost: abc.def.kpg\r\n\r\n",
) -> bytes:
    """The whole answer to one request, as the bytes the stand-in wrote, head and all.

    The request is written as given, so a test can send a head the client never would.
    """
    # Not `create_default_context`, whose strict checks want extensions the stand-in's authority
    # leaves out, as the plugin's does: the client's own context is built the same way.
    context = ssl.SSLContext(ssl.PROTOCOL_TLS_CLIENT)
    context.load_verify_locations(cafile=str(authority.pem))
    context.check_hostname = False
    context.minimum_version = ssl.TLSVersion.TLSv1_3
    context.maximum_version = ssl.TLSVersion.TLSv1_3
    with socket.create_connection(("127.0.0.1", gate.port), timeout=10) as raw, context.wrap_socket(
        raw, server_hostname="abc.def.kpg"
    ) as wrapped:
        wrapped.sendall(request)
        received = b""
        while chunk := wrapped.recv(4096):
            received += chunk

    return received


def test_an_answer_carries_the_head_the_plugin_writes(
    gate: FakeGate, authority: Authority
) -> None:
    # `HttpResponse.Write`: the reason phrase, the charset, and the two headers that keep an
    # answer out of a cache and out of a browser's guessing.
    gate.handler = lambda path, body, client: (404, '{"error":"no such route"}')

    head = answered(gate, authority).split(b"\r\n\r\n")[0].decode("ascii").split("\r\n")

    assert head[0] == "HTTP/1.1 404 Not Found"
    assert "Content-Type: application/json; charset=utf-8" in head
    assert "Cache-Control: no-store" in head
    assert "X-Content-Type-Options: nosniff" in head
    assert not any(line.startswith("Retry-After") for line in head), "only a 503 carries it"


def asked(line: str) -> bytes:
    """A request of a request line and the `Host` every request carries."""
    return f"{line} HTTP/1.1\r\nHost: abc.def.kpg\r\n\r\n".encode("ascii")


@pytest.mark.parametrize(
    "path",
    [
        "/groups",
        "/groups/0123/groups",
        "/groups/0123/entries",
        "/groups/0123/entries?includeExpired=false",
        "/entries",
        "/entries?includeExpired=false",
        "/entries/0123",
        "/entries/0123/fields",
        "/entries/0123/fields/Title",
        "/entries/0123/attachments",
    ],
)
def test_an_answer_that_grows_with_the_database_is_in_chunks_as_the_plugin_answers_it(
    gate: FakeGate, authority: Authority, path: str
) -> None:
    # `HttpResponse.Write` on the plugin's side writes these answers as they are sent, so a
    # client asking this gate for a listing, an entry or a field reads the framing it meets at
    # the plugin, whatever query the path carries.
    gate.handler = lambda path, body, client: (200, '{"ok":true}')

    head, _, body = answered(gate, authority, asked(f"GET {path}")).partition(b"\r\n\r\n")

    assert b"Transfer-Encoding: chunked" in head
    assert b"Content-Length" not in head
    assert body == b'b\r\n{"ok":true}\r\n0\r\n\r\n'


@pytest.mark.parametrize(
    ("line", "status"),
    [
        ("GET /groups/0123", 200),
        ("GET /entries/0123/attachments/key.pem", 200),
        ("GET /entries/0123/access", 200),
        ("GET /gate/whoami", 200),
        ("GET /entries/0123", 404),
        ("DELETE /entries/0123", 200),
    ],
    ids=[
        "a group",
        "an attachment",
        "what the caller may do at an entry",
        "who the caller is",
        "a refused read",
        "a removal",
    ],
)
def test_every_other_answer_is_under_a_length(
    gate: FakeGate, authority: Authority, line: str, status: int
) -> None:
    gate.handler = lambda path, body, client: (status, '{"ok":true}')

    head, _, body = answered(gate, authority, asked(line)).partition(b"\r\n\r\n")

    assert b"Content-Length: 11" in head
    assert b"Transfer-Encoding" not in head
    assert body == b'{"ok":true}'


def test_a_head_of_an_entry_names_no_framing(gate: FakeGate, authority: Authority) -> None:
    gate.handler = lambda path, body, client: (200, '{"ok":true}')

    head, _, body = answered(gate, authority, asked("HEAD /entries/0123")).partition(b"\r\n\r\n")

    assert b"Transfer-Encoding" not in head
    assert b"Content-Length" not in head
    assert body == b""


def test_a_chunk_holds_as_much_as_the_plugin_puts_in_a_chunk(
    gate: FakeGate, authority: Authority
) -> None:
    # `ChunkedContent.Most`: 16376 bytes, which is 3ff8 in hex, and 7248 left over, 1c50.
    gate.handler = lambda path, body, client: (200, "x" * 40000)

    _, _, body = answered(gate, authority, asked("GET /entries/0123")).partition(b"\r\n\r\n")

    whole = b"3ff8\r\n" + b"x" * 16376 + b"\r\n"
    assert body == whole + whole + b"1c50\r\n" + b"x" * 7248 + b"\r\n0\r\n\r\n"


def test_a_declared_length_frames_an_answer_the_plugin_writes_in_chunks(
    gate: FakeGate, authority: Authority
) -> None:
    gate.declare = "11"
    gate.handler = lambda path, body, client: (200, '{"ok":true}')

    head, _, body = answered(gate, authority, asked("GET /entries/0123")).partition(b"\r\n\r\n")

    assert b"Content-Length: 11" in head
    assert b"Transfer-Encoding" not in head
    assert body == b'{"ok":true}'


@pytest.mark.parametrize(("given", "sent"), [("2", "Retry-After: 2"), (None, None)])
def test_a_503_carries_the_retry_after_the_plugin_sends_unless_told_otherwise(
    gate: FakeGate, authority: Authority, given: str | None, sent: str | None
) -> None:
    # The plugin sends `Retry-After: 2` on every `503`, so a program reading it is tested against
    # what it will meet; `None` is a `503` from something older, or a proxy, that sends none.
    gate.handler = lambda path, body, client: (503, '{"error":"busy"}')
    if given is None:
        gate.retry_after = None

    head = answered(gate, authority).split(b"\r\n\r\n")[0].decode("ascii").split("\r\n")

    assert head[0] == "HTTP/1.1 503 Service Unavailable"
    assert [line for line in head if line.startswith("Retry-After")] == ([sent] if sent else [])


@pytest.mark.parametrize("method", ["GET", "HEAD"])
@pytest.mark.parametrize(
    ("status", "answer", "sent"),
    [
        (503, '{"error":"locked"}', ["KPG-Unavailable: locked"]),
        (503, '{"error":"busy","heldUntil":"2026-11-07T10:00:00Z"}', ["KPG-Unavailable: busy"]),
        (503, '{"error":"not a word"}', []),
        (404, '{"error":"busy"}', []),
    ],
)
def test_a_503_names_its_word_in_a_field_as_the_plugin_does(
    gate: FakeGate, authority: Authority, method: str, status: int, answer: str, sent: list[str]
) -> None:
    # The answer to a HEAD has no body, so the field is where a client reads which refusal it
    # was; a GET carries it too, as the plugin's does.
    gate.handler = lambda path, body, client: (status, answer)
    request = f"{method} /gate/whoami HTTP/1.1\r\nHost: abc.def.kpg\r\n\r\n".encode("ascii")

    head = answered(gate, authority, request).split(b"\r\n\r\n")[0].decode("ascii").split("\r\n")

    assert [line for line in head if line.startswith("KPG-Unavailable")] == sent


def test_a_handler_can_ask_which_method_it_is_answering(
    gate: FakeGate, tmp_path: Path, bundle: dict[str, object]
) -> None:
    # One path, two methods, answered apart without counting calls.
    uuid = "A1000000000000000000000000000001"
    asked: list[str] = []

    def entry(path: str, body: str, client: bytes | None) -> tuple[int, str]:
        asked.append(gate.method())
        if gate.method() == "DELETE":
            return 404, '{"error":"no such route"}'
        return 200, json.dumps(
            {"uuid": uuid, "group": "C1" * 16, "fields": {}, "protected": [], "expires": None}
        )

    gate.handler = entry
    client = api.GateClient(
        api.save(tmp_path / "kept", gate.port, dict(bundle)), connection_timeout=10.0
    )

    assert client.entry(uuid).uuid == uuid
    with pytest.raises(RouteRefused):
        client.delete_entry(uuid)

    assert asked == ["GET", "DELETE"]


def test_the_method_is_asked_only_while_a_request_is_answered(gate: FakeGate) -> None:
    with pytest.raises(AssertionError, match="no request is being answered"):
        gate.method()

    with pytest.raises(AssertionError, match="no request is being answered"):
        gate.if_match()

    with pytest.raises(AssertionError, match="no request is being answered"):
        gate.tag('"0a1b"')


HOST = b"Host: abc.def.kpg\r\n"


def request(line: bytes, *headers: bytes, body: bytes = b"") -> bytes:
    """One request as written on the wire, from its request line, its header lines and its body."""
    return line + b"\r\n" + b"".join(headers) + b"\r\n" + body


WHOAMI = b"GET /gate/whoami HTTP/1.1"
POSTED = b"POST /groups HTTP/1.1"
NO_PATH = "the target is not a path this server accepts"


# `HttpRequestReader`, case by case, with the status and the reason it answers each with.
REFUSED = {
    "repeated": (
        request(WHOAMI, HOST, b'If-Match: "0a1b"\r\n', b'If-Match: "2c3d"\r\n'),
        400,
        "a header is repeated",
    ),
    "control": (
        request(WHOAMI, HOST, b"X-Invented: one\x01two\r\n"),
        400,
        "a header value holds a control character",
    ),
    "long head": (
        request(WHOAMI, HOST, b"X-Invented: " + b"a" * 8192 + b"\r\n"),
        431,
        "the head is over 8192 bytes",
    ),
    "not ascii": (
        request(WHOAMI, HOST, "X-Invented: café\r\n".encode()),  # cspell:ignore café
        400,
        "the head is not ascii",
    ),
    "two parts": (request(b"GET /gate/whoami", HOST), 400, "the request line is not three parts"),
    "method": (
        request(b"G(T /gate/whoami HTTP/1.1", HOST),
        400,
        "the method is not a token",
    ),
    "absolute form": (
        request(b"GET https://abc.def.kpg/gate/whoami HTTP/1.1", HOST),
        400,
        "the target is not an absolute path",
    ),
    "long target": (
        request(b"GET /" + b"a" * 2048 + b" HTTP/1.1", HOST),
        414,
        "the target is over 2048 characters",
    ),
    "HTTP/1.0": (request(b"GET /gate/whoami HTTP/1.0", HOST), 505, "only HTTP/1.1 is accepted"),
    "traversal": (request(b"GET /entries/../x HTTP/1.1", HOST), 400, NO_PATH),
    "escaped traversal": (
        request(b"GET /entries/%2e%2E/x HTTP/1.1", HOST),
        400,
        NO_PATH,
    ),
    "unescaped": (request(b"GET /entries/a|b HTTP/1.1", HOST), 400, NO_PATH),
    "escaped control": (
        request(b"GET /entries/a%01b HTTP/1.1", HOST),
        400,
        NO_PATH,
    ),
    "short escape": (request(b"GET /entries/a%0 HTTP/1.1", HOST), 400, NO_PATH),
    "not utf-8": (request(b"GET /entries/%FF HTTP/1.1", HOST), 400, NO_PATH),
    "escaped C1": (request(b"GET /entries/%C2%85 HTTP/1.1", HOST), 400, NO_PATH),
    "query twice": (
        request(b"GET /entries?group=a&group=b HTTP/1.1", HOST),
        400,
        NO_PATH,
    ),
    "empty query name": (
        request(b"GET /entries?=a HTTP/1.1", HOST),
        400,
        NO_PATH,
    ),
    "folded": (request(WHOAMI, HOST, b" folded\r\n"), 400, "folded headers are not accepted"),
    "no name": (request(WHOAMI, HOST, b": value\r\n"), 400, "a header line has no name"),
    "name": (request(WHOAMI, HOST, b"X Invented: 1\r\n"), 400, "a header name is not a token"),
    "chunked": (
        request(WHOAMI, HOST, b"Transfer-Encoding: chunked\r\n"),
        501,
        "transfer encodings are not accepted",
    ),
    "no host": (request(WHOAMI), 400, "a request needs a host header"),
    "length": (
        request(POSTED, HOST, b"Content-Length: 1e3\r\n"),
        400,
        "the content length is not a number",
    ),
    "no length": (request(POSTED, HOST), 411, "a body needs a content length"),
    "long body": (
        request(POSTED, HOST, b"Content-Length: 1048577\r\n", body=b"a" * 1048577),
        413,
        "the body is over 1048576 bytes",
    ),
    "body not utf-8": (
        request(POSTED, HOST, b"Content-Length: 1\r\n", body=b"\xff"),
        400,
        "the body is not utf-8",
    ),
}


@pytest.mark.parametrize(("sent", "status", "reason"), REFUSED.values(), ids=REFUSED.keys())
def test_a_request_the_plugin_refuses_is_refused_here_too(
    gate: FakeGate, authority: Authority, sent: bytes, status: int, reason: str
) -> None:
    # Refused before any route, so a program tested here is not tested against a gate that takes
    # more than the plugin does. A body is taken before the connection closes, as the plugin
    # takes it, or the reset that follows can throw the refusal away unread.
    asked: list[str] = []

    def answer_asked(path: str, body: str, client: bytes | None) -> tuple[int, str]:
        asked.append(path)
        return 200, "{}"

    gate.handler = answer_asked

    head, _, body = answered(gate, authority, sent).partition(b"\r\n\r\n")

    assert head.startswith(b"HTTP/1.1 %d " % status)
    assert json.loads(body) == {"error": reason}
    assert asked == [], "and the handler was never asked"


@pytest.mark.parametrize(
    "sent",
    [
        request(b"HEAD /gate/whoami HTTP/1.1", HOST, b"X-Invented: 1\r\n", b"X-Invented: 2\r\n"),
        request(b"HEAD /gate/whoami HTTP/1.1", HOST, b"X-Invented: " + b"a" * 8192 + b"\r\n"),
        request(b"HEAD /gate/whoami HTTP/1.0", HOST),
    ],
    ids=["repeated", "long head", "HTTP/1.0"],
)
def test_a_head_request_the_plugin_refuses_is_answered_without_content(
    gate: FakeGate, authority: Authority, sent: bytes
) -> None:
    # As the plugin answers it: the method is read off what arrived, so a refusal answers a HEAD
    # with no body and no length, as every other answer to a HEAD does.
    head, _, body = answered(gate, authority, sent).partition(b"\r\n\r\n")

    assert head.startswith((b"HTTP/1.1 400 ", b"HTTP/1.1 431 ", b"HTTP/1.1 505 "))
    assert b"Content-Length" not in head
    assert body == b""


@pytest.mark.parametrize(
    ("ask", "status"),
    [
        (lambda client: client.field("A1" * 16, ".."), 400),
        (lambda client: client.field("A1" * 16, "n" * 2048), 414),
        (lambda client: client.create_attachment("A1" * 16, "big", b"\0" * 800_000), 413),
    ],
    ids=["traversal", "long target", "long body"],
)
def test_what_the_library_sends_and_the_plugin_refuses_is_refused_here_too(
    gate: FakeGate,
    tmp_path: Path,
    bundle: dict[str, object],
    ask: Callable[[api.GateClient], object],
    status: int,
) -> None:
    # Three a typed method can send: a name of `..`, a name that makes the target too long and
    # an attachment whose encoding makes the body too long, each refused by the gate.
    gate.handler = lambda path, body, client: (200, "{}")
    client = api.GateClient(
        api.save(tmp_path / "kept", gate.port, dict(bundle)), connection_timeout=10.0
    )

    with pytest.raises(RouteRefused) as refused:
        ask(client)

    assert refused.value.status == status
    assert gate.requests == []


@pytest.mark.parametrize("tag", NO_TAGS)
def test_what_is_no_tag_is_answered_as_a_defect(
    gate: FakeGate, authority: Authority, tag: str
) -> None:
    # Raised inside the handler, which used to end the thread answering it, so the client saw a
    # connection closed without a word and the test said nothing about why.
    def answer(path: str, body: str, client: bytes | None) -> tuple[int, str]:
        gate.tag(tag)
        return 200, "{}"

    gate.handler = answer

    head = answered(gate, authority).split(b"\r\n\r\n")[0]

    assert head.startswith(b"HTTP/1.1 500 ")
    assert len(gate.failures) == 1
    assert isinstance(gate.failures[0], ValueError)


def test_a_peer_that_goes_quiet_is_let_go(gate: FakeGate) -> None:
    # Bounded, so a quiet peer ends the thread answering it rather than parking it for good: the
    # gate closes its side, which the peer reads as the end of the stream.
    gate.quiet = 0.5

    with socket.create_connection(("127.0.0.1", gate.port), timeout=10) as raw:
        try:
            ended = raw.recv(1)
        except ConnectionResetError:
            ended = b""

    assert ended == b""


def test_a_client_offering_only_tls_12_is_refused(gate: FakeGate, authority: Authority) -> None:
    # A TLS 1.3 server, as the plugin's transport is, and not one that also speaks something
    # older to a client that asks for it: the client itself never offers less, so only a client
    # that does can show it.
    context = ssl.SSLContext(ssl.PROTOCOL_TLS_CLIENT)
    context.load_verify_locations(cafile=str(authority.pem))
    context.check_hostname = False
    context.minimum_version = ssl.TLSVersion.TLSv1_2
    context.maximum_version = ssl.TLSVersion.TLSv1_2

    with socket.create_connection(("127.0.0.1", gate.port), timeout=10) as raw, pytest.raises(
        ssl.SSLError
    ):
        context.wrap_socket(raw, server_hostname="abc.def.kpg")
