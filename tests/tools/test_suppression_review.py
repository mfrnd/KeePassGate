"""The review of the suppressions a change touches, over a real git history and a stand-in GitHub.

What is held is what decides that nobody merges a suppression unseen: that every suppression a
change adds, changes or removes is found by comparing the two commits' comments rather than their
line numbers, that a warning goes on a line only when the change touched it and into the summary
always, that a review thread already posted is not posted again, and that a pull request that
cannot be written to fails the job instead of losing what it would have shown.
"""

from __future__ import annotations

import email.message
import io
import json
import subprocess
import threading
import urllib.error
import urllib.parse
from collections import Counter
from collections.abc import Callable
from http.server import BaseHTTPRequestHandler, HTTPServer
from pathlib import Path
from typing import Any, ClassVar

import pytest

import suppression_review as review
import suppressions
from code_scanning import Finding


def _marker(rule: str = "cs/a", *, line: int = 3, comment: str = "codeql[cs/a] why",
            target: str = "Call();", path: str = "A.cs") -> review.Placed:
    return review.Placed(path, rule, line, line + 1, comment, target, "why")


def _finding(rule: str = "cs/a", line: int = 4, *, path: str = "A.cs", suppressed: bool = True,
             severity: str | None = None) -> Finding:
    return Finding(rule, path, line, "a finding", severity, suppressed)


PATCH = """\
@@ -3 +3,2 @@ class A
-old line
+new line
+another
@@ -10,3 +11,4 @@
 kept
--- a removed line that looks like a header
+++ an added line that looks like one
+added at thirteen
 kept
\\ No newline at end of file
"""


def test_a_patch_gives_its_hunks_and_the_lines_it_changes_on_either_side() -> None:
    read = review.read_patch(PATCH)
    assert read.hunks == {"LEFT": [(3, 3), (10, 12)], "RIGHT": [(3, 4), (11, 14)]}
    assert read.changed == {"LEFT": {3, 11}, "RIGHT": {3, 4, 12, 13}}
    assert review.read_patch("@@ -0,0 +1,2 @@\n+a\n+b\n").hunks == {"LEFT": [], "RIGHT": [(1, 2)]}
    assert review.read_patch("") == review.Patch()


def test_a_marker_is_placed_with_the_comment_and_the_line_it_covers() -> None:
    text = "class C\n{\n    // codeql[cs/a] codeql[cs/b] why\n    void M() { }\n    // lgtm\n}\n"
    source = suppressions.read(text, "csharp")
    found = suppressions.suppressions(source, "csharp", "C.cs")
    assert review.placed("C.cs", source, found) == [
        review.Placed("C.cs", "cs/a", 3, 4, "codeql[cs/a] codeql[cs/b] why", "void M() { }", "why"),
        review.Placed("C.cs", "cs/b", 3, 4, "codeql[cs/a] codeql[cs/b] why", "void M() { }", "why"),
    ]


def test_a_marker_that_only_moved_is_neither_added_nor_removed() -> None:
    assert review.compare([_marker(line=3)], [_marker(line=30)]) == ([], [])


def test_a_new_marker_is_added_and_a_lost_one_removed() -> None:
    kept, lost = _marker("cs/a"), _marker("cs/gone", comment="g")
    new = _marker("cs/new", comment="n")
    assert review.compare([kept, lost], [kept, new]) == ([new], [lost])


@pytest.mark.parametrize("changed", [
    _marker(comment="codeql[cs/a] a better reason"),
    _marker(target="CallSomethingElse();"),
])
def test_a_changed_reason_or_line_is_a_change_and_no_removal(changed: review.Placed) -> None:
    assert review.compare([_marker()], [changed]) == ([changed], [])


def test_a_marker_changed_in_both_is_added_and_its_old_one_removed() -> None:
    old, new = _marker(), _marker(comment="codeql[cs/a] other", target="Other();")
    assert review.compare([old], [new]) == ([new], [old])


def test_a_marker_suppressing_nothing_of_its_rule_is_stale() -> None:
    markers = [_marker("cs/a"), _marker("cs/b", line=8), _marker("cs/c", line=20)]
    findings = [_finding("cs/a", 4), _finding("cs/other", 9),
                _finding("cs/c", 21, suppressed=False)]
    assert review.stale(markers, findings) == markers[1:]


def test_a_suppression_no_comment_accounts_for_is_unaccounted(tmp_path: Path) -> None:
    text = "class C\n{\n    // codeql[cs/a] why\n    void M() { }\n    void N() { } // lgtm\n}\n"
    source = suppressions.read(text, "csharp")
    scanned = [("C.cs", source, suppressions.suppressions(source, "csharp", "C.cs"))]
    covered, starred = _finding("cs/a", 4, path="C.cs"), _finding("cs/any", 5, path="C.cs")
    stray, open_one = _finding("cs/b", 4, path="C.cs"), _finding("cs/c", 2, path="C.cs",
                                                                   suppressed=False)
    elsewhere = _finding("cs/a", 4, path="D.cs")
    found = review.unaccounted(scanned, [covered, starred, stray, open_one, elsewhere])
    assert found == [stray, elsewhere]


def test_notes_go_on_lines_the_change_touched_and_nowhere_else() -> None:
    added, removed = _marker("cs/new", line=3), _marker("cs/gone", line=7, comment="g")
    away = _marker("cs/old", line=40, comment="o")
    stale_here, stale_away = _marker("cs/s", line=10, comment="s"), _marker("cs/t", line=50)
    unaccounted_here = _finding("cs/u", 12)
    found = review.Review(added=[added], removed=[removed, away], stale=[stale_here, stale_away],
                          unaccounted=[unaccounted_here, _finding("cs/v", 60)],
                          findings=[_finding("cs/new", 4, severity="7.5")])
    placed = review.notes(found, _view(right={3, 4, 11, 12}, left={7}))
    assert [(n.kind, n.line, n.side) for n in placed] == [
        ("added", 3, "RIGHT"), ("removed", 7, "LEFT"), ("stale", 10, "RIGHT"),
        ("unaccounted", 12, "RIGHT")]
    assert "(security, 7.5)" in placed[0].body
    assert placed[0].key != placed[2].key


def test_the_summary_lists_and_counts_what_is_suppressed() -> None:
    added = _marker("cs/a")
    added_with_bar = review.Placed("B.cs", "cs/b", 1, 2, "codeql[cs/b] a | b", "X();", "a | b")
    found = review.Review(added=[added, added_with_bar], removed=[_marker("cs/c", comment="c")],
                   stale=[_marker("cs/d", line=8)], unaccounted=[_finding("cs/e", 9)],
                   totals=Counter({"cs/a": 3, "cs/b": 1}))
    text = review.summary(found, [_finding("cs/a", 4, severity="9.1"),
                                  _finding("cs/b", 2, path="B.cs")], pull_request=True)
    assert "**Newly suppressed in this pull request: 2**, 1 of them security findings" in text
    assert "| `cs/a` | security, 9.1 | `A.cs:4` | why |" in text
    assert "| `cs/b` | quality | `B.cs:2` | a \\| b |" in text
    assert "- `cs/c` at `A.cs:4`" in text
    assert "| `cs/a` | 3 (+1) |" in text
    assert "**Suppressions that suppress nothing: 1**" in text
    assert "- `cs/e` at `A.cs:9`" in text
    assert text.endswith(review.SUMMARY)


def test_without_a_pull_request_the_summary_counts_and_warns_only() -> None:
    found = review.Review(added=[_marker()], totals=Counter({"cs/a": 2}))
    text = review.summary(found, [], pull_request=False)
    assert "Newly suppressed" not in text
    assert "| `cs/a` | 2 |" in text


def test_every_warning_is_annotated_wherever_it_is() -> None:
    found = review.Review(stale=[_marker("cs/d", line=8, path="x,y.cs")],
                   unaccounted=[_finding("cs/e", 9)])
    assert review.warnings(found) == [
        "::warning file=x%2Cy.cs,line=8,title=suppresses nothing::CodeQL found no cs/d on the "
        + "line under this suppression.",
        "::warning file=A.cs,line=9,title=unaccounted suppression::CodeQL suppressed this cs/e "
        + "finding, and no comment read here accounts for it.",
    ]


def _shape(comment: dict[str, Any]) -> dict[str, Any]:
    """A thread as the stand-in keeps it, from REST's comment or GraphQL's input alike."""
    if "startLine" in comment:
        return {"path": comment["path"], "start": comment["startLine"], "line": comment["line"],
                "side": comment["side"], "file": comment["subjectType"] == "FILE",
                "body": comment["body"]}
    return {"path": comment["path"], "start": comment.get("start_line"), "line": comment["line"],
            "side": comment["side"], "file": False, "body": comment["body"]}


def _as_rest(thread: dict[str, Any]) -> dict[str, Any]:
    """A thread as REST lists a review comment."""
    return {"path": thread["path"], "line": thread["line"], "side": thread["side"],
            "body": thread["body"], "subject_type": "file" if thread["file"] else "line"}


class FakeGitHub:
    """A pull request's comments, files and reviews, kept in memory.

    `reviews` are the ones the review opened, each with its threads in `_shape`'s form. GitHub
    refuses a range on a path in `no_ranges`, a line on one in `no_lines` and a whole file in
    `no_files`, and `left` are pending reviews a run before this one left behind.
    """

    def __init__(self, reviewed: list[str] | None = None, summary: str | None = None,
                 files: list[dict[str, Any]] | None = None,
                 resolved: set[int] | None = None) -> None:
        self.reviews: list[dict[str, Any]] = []
        self.reviewed: list[dict[str, Any]] = [{"body": body} for body in reviewed or []]
        self.conversation = [] if summary is None else [{"id": 7, "body": summary}]
        self.written: list[tuple[int, int | None, str]] = []
        self.files = files or []
        self.on_files: list[tuple[str, str]] = []
        self.resolved = resolved or set()
        self.deleted: list[int] = []
        self.no_ranges: set[str] = set()
        self.no_lines: set[str] = set()
        self.no_files: set[str] = set()
        self.left: list[tuple[int, str]] = []
        self.deleted_reviews: list[int] = []

    def review_comments(self, number: int) -> list[dict[str, Any]]:
        return self.reviewed

    def _takes(self, thread: dict[str, Any]) -> bool:
        if thread["file"]:
            return thread["path"] not in self.no_files
        if thread["start"] is not None and thread["path"] in self.no_ranges:
            return False
        return thread["path"] not in self.no_lines

    def _review(self, key: str, value: object) -> dict[str, Any]:
        return next(r for r in self.reviews if r[key] == value)

    def open_review(self, number: int, commit: str, comment: dict[str, Any]) -> tuple[int, str]:
        if not self._takes(_shape(comment)):
            raise review.Refused(422, "Line could not be resolved")
        index = len(self.reviews)
        self.reviews.append({"id": 100 + index, "node": f"R{index}", "commit": commit,
                             "state": "PENDING", "threads": [_shape(comment)]})
        return 100 + index, f"R{index}"

    def add_thread(self, review: str, thread: dict[str, Any]) -> bool:
        if not self._takes(_shape(thread)):
            return False
        self._review("node", review)["threads"].append(_shape(thread))
        return True

    def submit_review(self, review: str) -> None:
        submitted = self._review("node", review)
        submitted["state"] = "COMMENTED"
        self.reviewed.extend(_as_rest(t) for t in submitted["threads"])

    def review_state(self, number: int, review: int) -> str:
        return str(self._review("id", review)["state"])

    def pending_reviews(self, number: int) -> list[tuple[int, str]]:
        return [*self.left, *((r["id"], r["node"]) for r in self.reviews
                              if r["state"] == "PENDING")]

    def delete_review(self, number: int, review: int) -> None:
        self.deleted_reviews.append(review)
        self.left = [p for p in self.left if p[0] != review]

    def review_thread_comments(self, number: int, review: int) -> list[dict[str, Any]]:
        return [_as_rest(t) for t in self._review("id", review)["threads"]]

    def comment_on_file(self, number: int, commit: str, path: str, body: str) -> None:
        if path in self.no_files:
            raise review.Refused(422, f"Diff entry {path} diff is too large")
        self.on_files.append((path, body))

    def pull_files(self, number: int) -> list[dict[str, Any]]:
        return self.files

    def issue_comments(self, number: int) -> list[dict[str, Any]]:
        return self.conversation

    def write_comment(self, number: int, existing: int | None, body: str) -> None:
        self.written.append((number, existing, body))

    def resolved_threads(self, number: int) -> set[int]:
        return self.resolved

    def delete_comment(self, comment: int) -> None:
        self.deleted.append(comment)
        self.reviewed = [c for c in self.reviewed if c.get("id") != comment]


def test_a_note_already_on_the_pull_request_is_not_posted_again(waits: list[float]) -> None:
    old = review.Note("added", "A.cs", 3, "RIGHT", "old", "k1")
    new = review.Note("stale", "A.cs", 9, "RIGHT", "new", "k2")
    github = FakeGitHub(reviewed=["old\n\n<!-- code-scanning: k1 -->"])
    assert review.post(github, 5, "abc", [old, new], "the summary") == 1
    assert [(r["commit"], r["state"], r["threads"]) for r in github.reviews] == [
        ("abc", "COMMENTED", [{"path": "A.cs", "start": None, "line": 9, "side": "RIGHT",
                               "file": False, "body": "new\n\n<!-- code-scanning: k2 -->"}])]
    assert github.written == [(5, None, "the summary")]


def test_the_summary_is_written_over_the_one_comment_it_keeps() -> None:
    github = FakeGitHub(summary=f"earlier\n{review.SUMMARY}")
    assert review.post(github, 5, "abc", [], "now") == 0
    assert github.reviews == []
    assert github.written == [(5, 7, "now")]


class _Api(BaseHTTPRequestHandler):
    calls: ClassVar[list[tuple[str, str, Any, str]]] = []

    def _answer(self, status: int, body: Any, link: str = "") -> None:
        data = json.dumps(body).encode()
        self.send_response(status)
        self.send_header("Content-Type", "application/json")
        self.send_header("Content-Length", str(len(data)))
        if link:
            self.send_header("Link", link)
        self.end_headers()
        self.wfile.write(data)

    def _record(self) -> None:
        length = int(self.headers.get("Content-Length", "0"))
        body = json.loads(self.rfile.read(length)) if length else None
        self.calls.append((self.command, self.path, body, self.headers.get("Authorization", "")))

    def do_GET(self) -> None:
        self._record()
        if "/code-scanning/alerts" in self.path:
            self._answer(200, [{"number": 9}])
        elif "/pulls/5/reviews/9/comments" in self.path:
            self._answer(200, [{"id": 21, "path": "A.cs", "line": 3, "body": "c"}])
        elif self.path.endswith("/pulls/5/reviews/9"):
            self._answer(200, {"id": 9, "state": "PENDING"})
        elif "/pulls/5/reviews?" in self.path:
            self._answer(200, [{"id": 9, "node_id": "N9", "state": "PENDING"},
                               {"id": 8, "node_id": "N8", "state": "COMMENTED"}])
        elif self.path.endswith("page=2"):
            self._answer(200, [{"id": 2, "body": "second"}])
        else:
            port = self.server.server_port  # type: ignore[attr-defined]
            nxt = f'<http://127.0.0.1:{port}/repos/o/r/issues/5/comments?page=2>; rel="next"'
            self._answer(200, [{"id": 1, "body": "first"}], nxt)

    def do_POST(self) -> None:
        self._record()
        if self.path == "/graphql":
            self._graph(self.calls[-1][2]["query"], self.calls[-1][2]["variables"])
        elif "refuse" in self.path:
            self._answer(403, {"message": "Resource not accessible by integration"})
        else:
            self._answer(201, {"id": 3, "node_id": "N3"})

    def _graph(self, query: str, asked: dict[str, Any]) -> None:
        # A thread made or not by its path, a submission, two pages of threads, or GraphQL's
        # refusals, which come with 200, by the repository.
        if "addPullRequestReviewThread" in query:
            made = None if asked["path"] == "outside.cs" else {"id": "T1"}
            self._answer(200, {"data": {"addPullRequestReviewThread": {"thread": made}}})
        elif "submitPullRequestReview" in query:
            self._answer(200, {"data": {"submitPullRequestReview": {
                "pullRequestReview": {"id": asked["review"]}}}})
        elif asked["name"] == "refuse":
            self._answer(200, {"errors": [{"type": "FORBIDDEN",
                                           "message": "Resource not accessible by integration"}]})
        elif asked["name"] == "odd":
            self._answer(200, {"errors": [{"type": "NOT_FOUND", "message": "Could not resolve"}]})
        elif asked["name"] == "empty":
            self.send_response(200)
            self.send_header("Content-Length", "0")
            self.end_headers()
        else:
            first = asked["after"] is None

            def thread(resolved: bool, *numbers: int) -> dict[str, Any]:
                nodes = [{"databaseId": number} for number in numbers]
                return {"isResolved": resolved, "comments": {"nodes": nodes}}

            threads = {"pageInfo": {"hasNextPage": first, "endCursor": "c1" if first else None},
                       "nodes": [thread(True, 11), thread(False, 12)] if first
                       else [thread(True, 13), thread(True)]}
            self._answer(200, {"data": {"repository": {"pullRequest": {"reviewThreads": threads}}}})

    def do_PATCH(self) -> None:
        self._record()
        self._answer(200, {"id": 7})

    def do_DELETE(self) -> None:
        self._record()
        self.send_response(204)
        self.end_headers()

    def log_message(self, format: str, *args: Any) -> None:
        return


@pytest.fixture
def api() -> Any:
    _Api.calls = []
    server = HTTPServer(("127.0.0.1", 0), _Api)
    thread = threading.Thread(target=server.serve_forever, daemon=True)
    thread.start()
    yield f"http://127.0.0.1:{server.server_port}"
    server.shutdown()
    server.server_close()


def test_the_client_follows_every_page_and_sends_the_token(api: str) -> None:
    github = review.GitHub("o/r", "secret", api)
    assert [c["id"] for c in github.issue_comments(5)] == [1, 2]
    assert _Api.calls[0][:2] == ("GET", "/repos/o/r/issues/5/comments?per_page=100")
    assert all(call[3] == "Bearer secret" for call in _Api.calls)


def test_the_client_writes_reviews_and_comments_where_github_takes_them(api: str) -> None:
    github = review.GitHub("o/r", "secret", api)
    assert github.open_review(5, "abc", {"path": "A.cs", "line": 3, "side": "RIGHT",
                                         "body": "b"}) == (3, "N3")
    github.write_comment(5, None, "new")
    github.write_comment(5, 7, "over")
    assert [(c[0], c[1], c[2]) for c in _Api.calls] == [
        ("POST", "/repos/o/r/pulls/5/reviews", {"commit_id": "abc",
                                                "comments": [{"path": "A.cs", "line": 3,
                                                              "side": "RIGHT", "body": "b"}]}),
        ("POST", "/repos/o/r/issues/5/comments", {"body": "new"}),
        ("PATCH", "/repos/o/r/issues/comments/7", {"body": "over"}),
    ]


def test_the_client_adds_threads_to_a_pending_review_and_submits_it(api: str) -> None:
    github = review.GitHub("o/r", "secret", api)
    thread = {"path": "A.cs", "line": 9, "side": "RIGHT", "startLine": 3, "startSide": "RIGHT",
              "subjectType": None, "body": "b"}
    assert github.add_thread("N3", thread) is True
    assert github.add_thread("N3", {**thread, "path": "outside.cs"}) is False
    github.submit_review("N3")
    assert [c[2]["variables"] for c in _Api.calls] == [
        {"review": "N3", **thread}, {"review": "N3", **thread, "path": "outside.cs"},
        {"review": "N3"}]
    assert "addPullRequestReviewThread(input:" in _Api.calls[0][2]["query"]
    assert "event: COMMENT" in _Api.calls[2][2]["query"]


def test_the_client_finds_reads_and_deletes_its_pending_reviews(api: str) -> None:
    github = review.GitHub("o/r", "secret", api)
    assert github.pending_reviews(5) == [(9, "N9")]
    assert github.review_state(5, 9) == "PENDING"
    assert [c["id"] for c in github.review_thread_comments(5, 9)] == [21]
    github.delete_review(5, 9)
    assert [c[:2] for c in _Api.calls] == [
        ("GET", "/repos/o/r/pulls/5/reviews?per_page=100"), ("GET", "/repos/o/r/pulls/5/reviews/9"),
        ("GET", "/repos/o/r/pulls/5/reviews/9/comments?per_page=100"),
        ("DELETE", "/repos/o/r/pulls/5/reviews/9")]


def test_the_client_asks_for_alerts_by_ref_and_changes_one(api: str) -> None:
    github = review.GitHub("o/r", "secret", api)
    assert github.alerts("refs/heads/master", "open") == [{"number": 9}]
    assert github.change_alert(9, {"state": "open"}) == {"id": 7}
    asked = urllib.parse.urlsplit(_Api.calls[0][1])
    assert (_Api.calls[0][0], asked.path) == ("GET", "/repos/o/r/code-scanning/alerts")
    assert urllib.parse.parse_qs(asked.query) == {
        "ref": ["refs/heads/master"], "state": ["open"], "tool_name": ["CodeQL"],
        "per_page": ["100"]}
    assert "%2F" in asked.query
    assert _Api.calls[1][:3] == ("PATCH", "/repos/o/r/code-scanning/alerts/9", {"state": "open"})


def test_the_client_reads_which_threads_are_resolved_page_by_page(api: str) -> None:
    assert review.GitHub("o/r", "secret", api).resolved_threads(5) == {11, 13}
    assert [(c[0], c[1], c[2]["variables"]) for c in _Api.calls] == [
        ("POST", "/graphql", {"owner": "o", "name": "r", "number": 5, "after": None}),
        ("POST", "/graphql", {"owner": "o", "name": "r", "number": 5, "after": "c1"})]
    assert "reviewThreads(first: 100, after: $after)" in _Api.calls[0][2]["query"]


def test_the_client_deletes_a_review_comment(api: str) -> None:
    review.GitHub("o/r", "secret", api).delete_comment(11)
    assert _Api.calls[0][:2] == ("DELETE", "/repos/o/r/pulls/comments/11")


@pytest.mark.parametrize(("name", "refusal"), [
    ("refuse", r"^403: Resource not accessible by integration$"),
    ("odd", r"^422: Could not resolve$"),
    ("empty", r"^422: GitHub's GraphQL API answered with nothing$"),
])
def test_a_graphql_refusal_reaches_the_caller(api: str, name: str, refusal: str) -> None:
    with pytest.raises(review.Refused, match=refusal):
        review.GitHub(f"o/{name}", "secret", api).resolved_threads(5)


def test_a_refusal_reaches_the_caller(api: str) -> None:
    with pytest.raises(review.Refused, match=r"^403: Resource not accessible by integration$"):
        review.GitHub("o/refuse", "secret", api).write_comment(5, None, "x")


def _git(root: Path, *arguments: str) -> None:
    subprocess.run(["git", "-c", "user.name=t", "-c", "user.email=t@example.com", *arguments],  # noqa: S603, S607
                   cwd=root, check=True, capture_output=True)


def _history(root: Path) -> Path:
    """Two commits: the base suppresses one finding, the head another, both in C#."""
    base = "class A\n{\n    // codeql[cs/a] why\n    void M() { }\n}\n"
    head = "class A\n{\n    void M() { }\n    // codeql[cs/b] the new one\n    void N() { }\n}\n"
    root.mkdir()
    _git(root, "init", "-q")
    (root / "A.cs").write_text(base, encoding="utf-8", newline="\n")
    (root / "keep.py").write_text("x = 1\n", encoding="utf-8", newline="\n")
    _git(root, "add", "A.cs", "keep.py")
    _git(root, "commit", "-q", "-m", "base")
    (root / "A.cs").write_text(head, encoding="utf-8", newline="\n")
    _git(root, "commit", "-q", "-am", "head")
    return root


def _analysis(folder: Path, *results: tuple[str, int, bool]) -> Path:
    folder.mkdir()
    runs = [{"tool": {"driver": {"name": "CodeQL", "rules": []}}, "results": [
        {"ruleId": rule, "message": {"text": "m"}, "locations": [{"physicalLocation": {
            "artifactLocation": {"uri": "A.cs"}, "region": {"startLine": line}}}],
         **({"suppressions": [{"kind": "inSource"}]} if suppressed else {})}
        for rule, line, suppressed in results]}]
    (folder / "csharp.sarif").write_text(json.dumps({"runs": runs}), encoding="utf-8")
    return folder


def test_gather_finds_what_a_commit_adds_and_removes(tmp_path: Path) -> None:
    root = _history(tmp_path / "repository")
    findings = [_finding("cs/b", 5)]
    found, placed = review.gather(root, findings, "HEAD^1")
    assert [(m.rule, m.line) for m in found.added] == [("cs/b", 4)]
    assert [(m.rule, m.line) for m in found.removed] == [("cs/a", 3)]
    assert found.stale == [] and found.unaccounted == []
    assert [(n.kind, n.line, n.side, n.span) for n in placed] == [
        ("added", 4, "RIGHT", (1, 6)), ("removed", 3, "LEFT", (1, 5))]


def test_on_master_the_review_writes_the_run_summary_and_passes(
        tmp_path: Path, monkeypatch: pytest.MonkeyPatch,
        capsys: pytest.CaptureFixture[str]) -> None:
    root = _history(tmp_path / "repository")
    analyzed = _analysis(tmp_path / "analyzed", ("cs/b", 5, True), ("cs/c", 2, True))
    written = tmp_path / "summary.md"
    monkeypatch.setenv("GITHUB_STEP_SUMMARY", str(written))
    assert review.main(["--analyzed", str(analyzed), "--root", str(root)]) == 0
    printed = capsys.readouterr().out
    assert "::warning file=A.cs,line=2,title=unaccounted suppression::" in printed
    assert "**Suppressed in total: 2**" in written.read_text(encoding="utf-8")


def test_on_a_pull_request_the_review_posts_its_notes(
        tmp_path: Path, monkeypatch: pytest.MonkeyPatch,
        capsys: pytest.CaptureFixture[str], waits: list[float]) -> None:
    root = _history(tmp_path / "repository")
    analyzed = _analysis(tmp_path / "analyzed", ("cs/b", 5, True))
    github = FakeGitHub(files=[{"filename": "A.cs", "status": "modified"}])
    monkeypatch.setattr(review, "GitHub", lambda repository, token: github)
    monkeypatch.setenv("GITHUB_STEP_SUMMARY", str(tmp_path / "summary.md"))
    arguments = ["--analyzed", str(analyzed), "--root", str(root), "--repository", "o/r",
                 "--pull-request", "5", "--commit", "abc"]
    assert review.main(arguments) == 0
    assert [(t["start"], t["line"], t["side"]) for t in github.reviews[0]["threads"]] == [
        (1, 6, "RIGHT"), (1, 5, "LEFT")]
    assert "2 new thread(s)" in capsys.readouterr().out
    assert github.written[0][2] in (tmp_path / "summary.md").read_text(encoding="utf-8")


def test_a_pull_request_that_cannot_be_written_to_fails_the_review(
        tmp_path: Path, monkeypatch: pytest.MonkeyPatch,
        capsys: pytest.CaptureFixture[str]) -> None:
    root = _history(tmp_path / "repository")
    analyzed = _analysis(tmp_path / "analyzed", ("cs/b", 5, True))

    class Refusing(FakeGitHub):
        def open_review(self, number: int, commit: str,
                        comment: dict[str, Any]) -> tuple[int, str]:
            raise review.Refused(403, "Resource not accessible by integration", "R1")

    refusing = Refusing(files=[{"filename": "A.cs", "status": "modified"}])
    monkeypatch.setattr(review, "GitHub", lambda repository, token: refusing)
    arguments = ["--analyzed", str(analyzed), "--root", str(root), "--repository", "o/r",
                 "--pull-request", "5", "--commit", "abc"]
    assert review.main(arguments) == 1
    assert ("::error::the review could not be written to the pull request (403: Resource not "
            "accessible by integration, request R1)") in capsys.readouterr().out


def _notes(count: int) -> list[review.Note]:
    return [review.Note("added", "A.cs", line, "RIGHT", f"n{line}", f"k{line}")
            for line in range(1, count + 1)]


@pytest.fixture
def waits(monkeypatch: pytest.MonkeyPatch) -> list[float]:
    """Every pause the review takes, which it then does not take."""
    waited: list[float] = []
    monkeypatch.setattr(review, "_wait", waited.append)
    return waited


class Fumbling(FakeGitHub):
    """A pull request on which GitHub answers the review's calls with the refusals given in turn.

    With `made`, a 5xx comes after the call was carried out, as GitHub answers a large review.
    """

    def __init__(self, *, opening: list[review.Refused | None] | None = None,
                 adding: list[review.Refused | None] | None = None,
                 submitting: list[review.Refused | None] | None = None,
                 made: bool = False) -> None:
        super().__init__()
        self.answers = {"open": list(opening or []), "add": list(adding or []),
                        "submit": list(submitting or [])}
        self.made = made

    def _next(self, call: str) -> tuple[review.Refused | None, bool]:
        answer = self.answers[call].pop(0) if self.answers[call] else None
        return answer, answer is None or (self.made and answer.status >= 500)

    def open_review(self, number: int, commit: str, comment: dict[str, Any]) -> tuple[int, str]:
        answer, carried = self._next("open")
        opened = super().open_review(number, commit, comment) if carried else (0, "")
        if answer is not None:
            raise answer
        return opened

    def add_thread(self, review: str, thread: dict[str, Any]) -> bool:
        answer, carried = self._next("add")
        made = super().add_thread(review, thread) if carried else False
        if answer is not None:
            raise answer
        return made

    def submit_review(self, review: str) -> None:
        answer, carried = self._next("submit")
        if carried:
            super().submit_review(review)
        if answer is not None:
            raise answer


LIMITED = "You have exceeded a secondary rate limit and have been temporarily blocked"
SERVER = review.Refused(502, "Server Error")
GAP = review.THREAD_GAP


def test_a_push_s_threads_go_out_as_one_review_a_second_apart(waits: list[float]) -> None:
    github = FakeGitHub()
    assert review.post(github, 5, "abc", _notes(45), "the summary") == 45
    assert [(r["state"], len(r["threads"])) for r in github.reviews] == [("COMMENTED", 45)]
    assert waits == [GAP] * 45
    assert github.written == [(5, None, "the summary")]


def test_a_pending_review_an_earlier_run_left_is_deleted_before_the_new_one(
        waits: list[float]) -> None:
    github = FakeGitHub()
    github.left = [(77, "R77")]
    assert review.post(github, 5, "abc", _notes(2), "s") == 2
    assert github.deleted_reviews == [77]
    assert [r["state"] for r in github.reviews] == ["COMMENTED"]
    assert waits == [GAP] * 3


def test_an_opening_answered_502_is_read_back_and_not_opened_again(waits: list[float]) -> None:
    github = Fumbling(opening=[SERVER], made=True)
    assert review.post(github, 5, "abc", _notes(3), "s") == 3
    assert [len(r["threads"]) for r in github.reviews] == [3]
    assert waits == [GAP] * 3


def test_an_opening_5xx_that_opened_nothing_is_tried_again_after_a_wait(
        waits: list[float]) -> None:
    github = Fumbling(opening=[SERVER])
    assert review.post(github, 5, "abc", _notes(2), "s") == 2
    assert [len(r["threads"]) for r in github.reviews] == [2]
    assert waits == [60.0, GAP, GAP]


def test_a_thread_answered_502_is_read_back_and_not_added_again(waits: list[float]) -> None:
    github = Fumbling(adding=[SERVER], made=True)
    assert review.post(github, 5, "abc", _notes(3), "s") == 3
    assert [len(r["threads"]) for r in github.reviews] == [3]
    assert waits == [GAP] * 3


def test_a_twin_in_the_review_does_not_pass_for_one_a_5xx_left_unmade(waits: list[float]) -> None:
    twin = review.Note("added", "A.cs", 99, "RIGHT", "n1", "k1")
    github = Fumbling(adding=[None, SERVER])
    assert review.post(github, 5, "abc", [*_notes(2), twin], "s") == 3
    assert [t["line"] for t in github.reviews[0]["threads"]] == [1, 2, 99]
    assert waits == [GAP, GAP, 60.0, GAP]


def test_a_submission_answered_502_is_read_back_rather_than_sent_again(
        waits: list[float]) -> None:
    github = Fumbling(submitting=[SERVER], made=True)
    assert review.post(github, 5, "abc", _notes(2), "s") == 2
    assert [r["state"] for r in github.reviews] == ["COMMENTED"]
    assert waits == [GAP, GAP]


def test_a_submission_a_5xx_left_pending_is_sent_again_after_a_wait(waits: list[float]) -> None:
    github = Fumbling(submitting=[SERVER])
    assert review.post(github, 5, "abc", _notes(2), "s") == 2
    assert [r["state"] for r in github.reviews] == ["COMMENTED"]
    assert waits == [GAP, GAP, 60.0]


def test_a_refusal_for_rate_is_waited_out_as_long_as_github_asks(waits: list[float]) -> None:
    github = Fumbling(adding=[review.Refused(403, LIMITED),
                              review.Refused(429, LIMITED, wait=7.0)])
    assert review.post(github, 5, "abc", _notes(3), "s") == 3
    assert waits == [GAP, 60.0, 7.0, GAP, GAP]


def test_a_refusal_for_rate_past_the_last_wait_stands(waits: list[float]) -> None:
    github = Fumbling(opening=[review.Refused(403, LIMITED)] * 4)
    with pytest.raises(review.Refused, match="secondary rate limit"):
        review.post(github, 5, "abc", _notes(3), "s")
    assert waits == [60.0, 120.0, 240.0]
    assert github.reviews == []


def test_any_other_refusal_stands_at_once(waits: list[float]) -> None:
    github = Fumbling(opening=[review.Refused(403, "Resource not accessible by integration")])
    with pytest.raises(review.Refused, match=r"^403: Resource not accessible"):
        review.post(github, 5, "abc", _notes(3), "s")
    assert waits == []


def test_a_summary_answered_502_is_found_and_written_over(waits: list[float]) -> None:
    class Writing(FakeGitHub):
        def write_comment(self, number: int, existing: int | None, body: str) -> None:
            super().write_comment(number, existing, body)
            if existing is None:
                self.conversation.append({"id": 8, "body": body})
                raise review.Refused(502, "Server Error")

    github = Writing()
    review.post(github, 5, "abc", [], f"now\n{review.SUMMARY}")
    assert [existing for _, existing, _ in github.written] == [None, 8]
    assert waits == [60.0]


def _answer(status: int, body: str, **headers: str) -> urllib.error.HTTPError:
    message = email.message.Message()
    for name, value in headers.items():
        message[name.replace("_", "-")] = value
    return urllib.error.HTTPError("u", status, "reason", message, io.BytesIO(body.encode()))


def test_github_s_answer_is_read_into_the_refusal() -> None:
    limited = review.refused(_answer(403, '{"message": "' + LIMITED + '"}',
                                     x_github_request_id="R1"))
    assert (limited.status, limited.message, limited.request, limited.wait) == (
        403, LIMITED, "R1", None)
    assert limited.for_rate
    assert review.refused(_answer(429, "{}", retry_after="30")).wait == 30.0
    spent = review.refused(_answer(403, "not JSON", x_ratelimit_remaining="0",
                                   x_ratelimit_reset="9999999999"))
    assert spent.message == "not JSON"
    assert (spent.wait or 0) > 1e9
    assert not review.refused(_answer(404, '{"message": "Not Found"}')).for_rate
    assert review.refused(_answer(500, "")).message == "reason"


@pytest.mark.parametrize(("line", "covered"), [
    (62, (59, 65)), (60, (59, 63)), (2, (1, 5)), (130, (127, 130)), (70, None), (131, None),
    (0, None)])
def test_a_thread_covers_three_lines_either_side_inside_the_file_and_its_hunk(
        line: int, covered: tuple[int, int] | None) -> None:
    assert review.span(line, [(1, 5), (59, 65), (127, 130)], 130) == covered


def test_below_its_line_a_thread_reaches_the_last_flagged_line_as_far_as_hunk_and_file_go() -> None:
    assert review.span(62, [(59, 80)], 130, through=75) == (59, 75)
    assert review.span(62, [(59, 65)], 130, through=75) == (59, 65)
    assert review.span(62, [(59, 80)], 70, through=75) == (59, 70)
    assert review.span(62, [(59, 80)], 130, through=63) == (59, 65)


def _view(*, right: set[int] | None = None, left: set[int] | None = None,
          lines: list[str] | None = None,
          hunks: list[tuple[int, int]] | None = None) -> review.View:
    patch = review.Patch({"LEFT": list(hunks or []), "RIGHT": list(hunks or [])},
                         {"LEFT": set(left or set()), "RIGHT": set(right or set())})
    return review.View({"A.cs": patch}, lambda path, side: list(lines or []))


def test_a_thread_names_the_rule_and_quotes_code_that_runs_past_its_lines() -> None:
    lines = [f"        line {n};" for n in range(1, 31)]
    lines[9:16] = ["        // codeql[cs/a] why", "        catch (Exception error)", "        {",
                   "            Report(error);", "            Again();", "            More();",
                   "        }"]
    finding = Finding("cs/a", "A.cs", 11, "Generic catch clause.", None, True,
                      name="Generic catch clause", end_line=16)
    found = review.Review(added=[_marker(line=10)], findings=[finding])
    note = review.notes(found, _view(right={10}, lines=lines, hunks=[(7, 13)]))[0]
    assert note.span == (7, 13)
    assert note.body.startswith("**Suppresses `cs/a`**, Generic catch clause (quality): why\n\n")
    assert "CodeQL:" not in note.body
    assert ("```csharp\ncatch (Exception error)\n{\n    Report(error);\n    Again();\n"
            "    More();\n}\n```") in note.body
    whole = review.notes(found, _view(right={10}, lines=lines, hunks=[(1, 30)]))[0]
    assert whole.span == (7, 16)
    assert "```" not in whole.body


def test_codeql_s_links_become_lines_and_code_inside_the_thread_is_not_quoted() -> None:
    lines = [f"        step({n});" for n in range(1, 21)]
    finding = Finding("cs/a", "A.cs", 4, "Disposed [here](1) and [there](2), not [lost](3).", None,
                      True, name="Missed using", related=((1, "A.cs", 9), (2, "B.cs", 2)))
    found = review.Review(added=[_marker(line=3)], findings=[finding])
    note = review.notes(found, _view(right={3}, lines=lines, hunks=[(1, 6)]))[0]
    assert "\n\nCodeQL: Disposed here (line 9) and there (B.cs:2), not lost.\n\n" in note.body
    assert "```" not in note.body


def test_long_code_is_cut_and_counted() -> None:
    lines = [f"        step({n});" for n in range(1, 41)]
    finding = Finding("cs/a", "A.cs", 4, "m", None, True, name="M", end_line=30)
    found = review.Review(added=[_marker(line=3)], findings=[finding])
    note = review.notes(found, _view(right={3}, lines=lines, hunks=[(1, 6)]))[0]
    assert note.body.count("step(") == review.EXCERPT
    assert "*12 more lines, to line 30.*" in note.body


def test_outside_every_hunk_a_thread_sits_on_its_line_alone() -> None:
    finding = Finding("cs/a", "A.cs", 4, "m", None, True, name="M")
    found = review.Review(added=[_marker(line=3)], findings=[finding])
    note = review.notes(found, _view(right={3}))[0]
    assert note.span is None
    assert "```" not in note.body


def test_a_removed_suppression_covers_the_lines_it_was_taken_from() -> None:
    lines = [f"line {n}" for n in range(1, 21)]
    found = review.Review(removed=[_marker(line=8)])
    note = review.notes(found, _view(left={8}, lines=lines, hunks=[(5, 11)]))[0]
    assert (note.side, note.span) == ("LEFT", (5, 11))


def _spans() -> list[review.Note]:
    return [review.Note("added", "A.cs", 10, "RIGHT", "n", "k", (7, 13)),
            review.Note("added", "A.cs", 30, "RIGHT", "m", "j", (27, 33))]


def test_a_thread_is_posted_over_its_span_as_the_review_opens_and_after(
        waits: list[float]) -> None:
    github = FakeGitHub()
    assert review.post(github, 5, "abc", _spans(), "s") == 2
    assert github.reviews[0]["threads"] == [
        {"path": "A.cs", "start": 7, "line": 13, "side": "RIGHT", "file": False,
         "body": "n\n\n<!-- code-scanning: k -->"},
        {"path": "A.cs", "start": 27, "line": 33, "side": "RIGHT", "file": False,
         "body": "m\n\n<!-- code-scanning: j -->"}]


def test_a_span_github_refuses_goes_again_on_its_own_line(waits: list[float]) -> None:
    github = FakeGitHub()
    github.no_ranges = {"A.cs"}
    assert review.post(github, 5, "abc", _spans(), "s") == 2
    assert [(t["start"], t["line"]) for t in github.reviews[0]["threads"]] == [
        (None, 10), (None, 30)]
    assert waits == [GAP] * 4


def test_a_thread_refused_with_an_error_goes_again_on_its_own_line(waits: list[float]) -> None:
    github = Fumbling(adding=[review.Refused(422, "Line must be part of the diff")])
    assert review.post(github, 5, "abc", _spans(), "s") == 2
    assert [(t["start"], t["line"]) for t in github.reviews[0]["threads"]] == [
        (7, 13), (None, 30)]
    assert waits == [GAP] * 3


def test_a_leftover_review_answered_502_as_it_is_deleted_is_read_back(
        waits: list[float]) -> None:
    class Slow(FakeGitHub):
        def delete_review(self, number: int, review: int) -> None:
            super().delete_review(number, review)
            raise SERVER

    github = Slow()
    github.left = [(77, "R77")]
    assert review.post(github, 5, "abc", _notes(1), "s") == 1
    assert github.deleted_reviews == [77]
    assert waits == [GAP, GAP]


def test_a_line_moves_by_every_change_above_it_and_is_lost_to_one_over_it() -> None:
    found = review.changes("diff --git a/A.cs b/A.cs\n--- a/A.cs\n+++ b/A.cs\n"
                           "@@ -1,3 +0,0 @@\n-a\n-b\n-c\n@@ -8,0 +6,2 @@\n+d\n+e\n"
                           "@@ -12 +10 @@\n-f\n+g\n")
    assert found == [(1, 3, 0, 0), (8, 0, 6, 2), (12, 1, 10, 1)]
    assert [review.moved(line, found) for line in (2, 4, 8, 9, 12, 13)] == [
        None, 1, 5, 8, None, 12]
    assert review.moved(7, []) == 7


def _behind(root: Path) -> str:
    """A branch adding a suppression while the one it merges into puts three lines above it.

    The checkout is the commit merging the two, first parent first, as GitHub checks one out for a
    pull request, and what comes back is the branch's own head.
    """
    body = [f"    void M{n}() {{ }}" for n in range(1, 11)]
    root.mkdir()
    _git(root, "init", "-q", "-b", "master")
    (root / "A.cs").write_text("\n".join(["class A", "{", *body, "}"]) + "\n", encoding="utf-8",
                               newline="\n")
    _git(root, "add", "A.cs")
    _git(root, "commit", "-q", "-m", "base")
    _git(root, "checkout", "-q", "-b", "pr")
    suppressed = [*body[:4], "    // codeql[cs/b] the new one", *body[4:]]
    (root / "A.cs").write_text("\n".join(["class A", "{", *suppressed, "}"]) + "\n",
                               encoding="utf-8", newline="\n")
    _git(root, "commit", "-q", "-am", "head")
    head = subprocess.run(["git", "rev-parse", "HEAD"], cwd=root, capture_output=True, text=True,  # noqa: S607
                          check=True).stdout.strip()
    _git(root, "checkout", "-q", "master")
    (root / "A.cs").write_text("\n".join(["// one", "// two", "// three", "class A", "{", *body,
                                          "}"]) + "\n", encoding="utf-8", newline="\n")
    _git(root, "commit", "-q", "-am", "master moves")
    _git(root, "merge", "-q", "--no-ff", "-m", "merge", "pr")
    return head


def test_a_branch_behind_the_one_it_merges_into_gets_its_threads_on_its_own_lines(
        tmp_path: Path) -> None:
    root = tmp_path / "repository"
    head = _behind(root)
    findings = [_finding("cs/b", 11)]
    found, placed = review.gather(root, findings, "HEAD^1", head)
    assert [(m.line, m.covered) for m in found.added] == [(7, 8)]
    assert [(n.kind, n.line, n.span) for n in placed] == [("added", 7, (4, 10))]
    assert "`A.cs:8`" in review.summary(found, findings, True)


def test_a_twin_added_later_gets_a_thread_of_its_own_on_its_own_line(waits: list[float]) -> None:
    first = review.Note("added", "A.cs", 3, "RIGHT", "n", "k", (1, 6))
    twin = review.Note("added", "A.cs", 9, "RIGHT", "n", "k", (6, 12))
    github = FakeGitHub()
    github.reviewed = [{"path": "A.cs", "line": 6, "body": "n\n\n<!-- code-scanning: k -->"}]
    assert review.post(github, 5, "abc", [first, twin], "s") == 1
    assert [(t["start"], t["line"]) for t in github.reviews[0]["threads"]] == [(6, 12)]
    assert review.post(github, 5, "abc", [first], "s") == 0


SUPPRESSED = "class A\n{\n    // codeql[cs/a] why\n    void M() { }\n}\n"


def _pull(root: Path, base: dict[str, str], head: dict[str, str | None],
          index_only: dict[str, str] | None = None) -> None:
    """A repository whose two commits are `base` and `head`, by file name and the file's text.

    A name the head maps to None is deleted there, and `index_only` goes into the head through the
    index alone, for a name Windows cannot write.
    """
    root.mkdir()
    _git(root, "init", "-q", "-b", "master")
    for name, text in base.items():
        (root / name).write_text(text, encoding="utf-8", newline="\n")
    _git(root, "add", "--", *base)
    _git(root, "commit", "-q", "-m", "base")
    for name, after in head.items():
        if after is None:
            _git(root, "rm", "-q", "--", name)
        else:
            (root / name).write_text(after, encoding="utf-8", newline="\n")
            _git(root, "add", "--", name)
    for name, text in (index_only or {}).items():
        # git's own names for what this asks of it: cspell:ignore NTFS cacheinfo
        blob = subprocess.run(["git", "hash-object", "-w", "--stdin"], cwd=root,  # noqa: S607
                              input=text.encode(), capture_output=True,
                              check=True).stdout.decode().strip()
        _git(root, "-c", "core.protectNTFS=false", "update-index", "--add", "--cacheinfo",
             f"100644,{blob},{name}")
    _git(root, "commit", "-q", "-m", "head")


def _gathered(root: Path, *findings: Finding,
              files: list[review.Changed] | None = None) -> tuple[review.Review, list[review.Note]]:
    return review.gather(root, list(findings), "HEAD^1", "HEAD", files)


def test_a_file_renamed_with_its_suppression_changes_none_of_them(tmp_path: Path) -> None:
    _pull(tmp_path / "r", {"A.cs": SUPPRESSED}, {"A.cs": None, "B.cs": SUPPRESSED})
    found, placed = _gathered(tmp_path / "r", _finding("cs/a", 4, path="B.cs"))
    assert (found.added, found.removed, placed) == ([], [], [])


def test_a_suppression_added_to_a_renamed_file_is_on_its_new_name(tmp_path: Path) -> None:
    body = "".join(f"    void M{n}() {{ }}\n" for n in range(1, 11))
    new = body.replace("    void M5", "    // codeql[cs/b] new\n    void M5")
    _pull(tmp_path / "r", {"A.cs": f"class A\n{{\n{body}}}\n"},
          {"A.cs": None, "B.cs": f"class A\n{{\n{new}}}\n"})
    found, placed = _gathered(tmp_path / "r", _finding("cs/b", 8, path="B.cs"))
    assert [(n.kind, n.path, n.line) for n in placed] == [("added", "B.cs", 7)]
    assert found.removed == []


def test_a_suppression_moved_to_another_file_is_added_there_and_removed_nowhere(
        tmp_path: Path) -> None:
    _pull(tmp_path / "r", {"A.cs": SUPPRESSED, "B.cs": "class B\n{\n    void M() { }\n}\n"},
          {"A.cs": "class A\n{\n    void N() { }\n}\n",
           "B.cs": "class B\n{\n    // codeql[cs/a] why\n    void M() { }\n}\n"})
    found, placed = _gathered(tmp_path / "r", _finding("cs/a", 4, path="B.cs"))
    assert [(n.kind, n.path) for n in placed] == [("added", "B.cs")]
    assert found.removed == []


def test_names_git_quotes_are_read_as_they_are(tmp_path: Path) -> None:
    _pull(tmp_path / "r", {"keep.cs": "class K { }\n"},
          {"a b.cs": SUPPRESSED, "ü.cs": SUPPRESSED}, index_only={'q"t.cs': SUPPRESSED})
    _, placed = _gathered(tmp_path / "r", _finding("cs/a", 4, path="a b.cs"),
                              _finding("cs/a", 4, path="ü.cs"))
    assert sorted((n.kind, n.path, n.line) for n in placed) == [
        ("added", "a b.cs", 3), ("added", 'q"t.cs', 3), ("added", "ü.cs", 3)]


def test_a_twin_added_beside_one_already_there_is_the_one_added(tmp_path: Path) -> None:
    twins = "class A\n{\n" + "    // codeql[cs/a] why\n    void M() { }\n" * 2 + "}\n"
    _pull(tmp_path / "r", {"A.cs": SUPPRESSED}, {"A.cs": twins})
    _, placed = _gathered(tmp_path / "r", _finding("cs/a", 4), _finding("cs/a", 6))
    assert [(n.kind, n.line) for n in placed] == [("added", 5)]


def test_two_rules_in_one_comment_are_two_threads_on_its_line(tmp_path: Path) -> None:
    _pull(tmp_path / "r", {"A.cs": "class A\n{\n    void M() { }\n}\n"},
          {"A.cs": "class A\n{\n    // codeql[cs/a] codeql[cs/b] why\n    void M() { }\n}\n"})
    _, placed = _gathered(tmp_path / "r", _finding("cs/a", 4), _finding("cs/b", 4))
    assert [(n.kind, n.line, n.key.split(":")[2]) for n in placed] == [
        ("added", 3, "cs/a"), ("added", 3, "cs/b")]


def test_a_python_suppression_is_reviewed_as_a_c_sharp_one_is(tmp_path: Path) -> None:
    _pull(tmp_path / "r", {"x.py": "import os\n"},
          {"x.py": "import os\n# codeql[py/a] why\nimport sys\n"})
    _, placed = _gathered(tmp_path / "r", _finding("py/a", 3, path="x.py"))
    assert [(n.kind, n.path, n.line, n.span) for n in placed] == [("added", "x.py", 2, (1, 3))]


def test_a_deleted_file_s_suppression_is_removed_with_the_line_it_covered(tmp_path: Path) -> None:
    _pull(tmp_path / "r", {"A.cs": SUPPRESSED, "keep.cs": "class K { }\n"}, {"A.cs": None})
    _, placed = _gathered(tmp_path / "r")
    assert [(n.kind, n.side, n.line, n.span) for n in placed] == [("removed", "LEFT", 3, (1, 5))]
    assert "removed, with the line it covered" in placed[0].body


def test_a_last_line_with_no_newline_is_a_line_like_any_other(tmp_path: Path) -> None:
    _pull(tmp_path / "r", {"A.cs": "class A\n{\n    void M() { }"},
          {"A.cs": "class A\n{\n    // codeql[cs/a] why\n    void M() { }"})
    _, placed = _gathered(tmp_path / "r", _finding("cs/a", 4))
    assert [(n.kind, n.line, n.span) for n in placed] == [("added", 3, (1, 4))]


def test_github_s_own_patch_is_where_its_threads_go(tmp_path: Path) -> None:
    root = _history(tmp_path / "repository")
    patch = ("@@ -3,2 +3,3 @@\n-    // codeql[cs/a] why\n     void M() { }\n"
             "+    // codeql[cs/b] the new one\n+    void N() { }\n")
    _, placed = _gathered(root, _finding("cs/b", 5),
                              files=[review.Changed("A.cs", "A.cs", "modified", patch)])
    assert [(n.kind, n.line, n.span) for n in placed] == [("added", 4, (3, 5)),
                                                          ("removed", 3, (3, 4))]


def test_the_files_github_lists_keep_their_old_names() -> None:
    listed = review.files_of([{"filename": "B.cs", "previous_filename": "A.cs",
                               "status": "renamed", "patch": "@@ -1 +1 @@\n-a\n+b\n"},
                              {"filename": "big.txt", "status": "added"}])
    assert listed == [review.Changed("B.cs", "A.cs", "renamed", "@@ -1 +1 @@\n-a\n+b\n"),
                      review.Changed("big.txt", "big.txt", "added", None)]


def test_a_file_the_branch_merged_into_renamed_keeps_its_findings(tmp_path: Path) -> None:
    root = tmp_path / "repository"
    head = _behind(root)
    _git(root, "checkout", "-q", "--detach", "HEAD^1")
    _git(root, "mv", "A.cs", "C.cs")
    _git(root, "commit", "-q", "-m", "master renames")
    _git(root, "merge", "-q", "--no-ff", "-m", "merge", head)
    _, placed = review.gather(root, [_finding("cs/b", 11, path="C.cs")], "HEAD^1", head)
    assert [(n.kind, n.path, n.line) for n in placed] == [("added", "A.cs", 7)]
    assert "(quality)" in placed[0].body


def _summaries(written: list[str]) -> Callable[[review.Posted], str]:
    def compose(posted: review.Posted) -> str:
        written.append(review.summary(review.Review(), [], True, posted))
        return written[-1]
    return compose


def _two() -> list[review.Note]:
    return [review.Note("added", "A.cs", 3, "RIGHT", "a", "ka"),
            review.Note("added", "big.txt", 1, "RIGHT", "b", "kb")]


def test_where_github_takes_no_thread_on_a_line_the_file_gets_one_in_the_review(
        waits: list[float]) -> None:
    github, written = FakeGitHub(), list[str]()
    github.no_lines = {"big.txt"}
    assert review.post(github, 5, "abc", _two(), _summaries(written)) == 2
    assert [(t["path"], t["file"]) for t in github.reviews[0]["threads"]] == [
        ("A.cs", False), ("big.txt", True)]
    assert github.on_files == []
    assert "**On their whole file, since GitHub took no thread on their lines: 1**" in written[-1]
    assert waits == [GAP, GAP, GAP]


def test_where_github_takes_no_thread_at_all_the_summary_names_it(waits: list[float]) -> None:
    github, written = FakeGitHub(), list[str]()
    github.no_lines = github.no_files = {"big.txt"}
    assert review.post(github, 5, "abc", _two(), _summaries(written)) == 1
    assert "**Where GitHub took no thread at all: 1**" in written[-1]
    assert "`big.txt:1`, added: GitHub took no thread on its lines or its file" in written[-1]


def test_a_first_note_no_line_takes_leaves_the_opening_to_the_next(waits: list[float]) -> None:
    github = FakeGitHub()
    github.no_lines = {"big.txt"}
    assert review.post(github, 5, "abc", _two()[::-1], "s") == 2
    assert [(t["path"], t["file"]) for t in github.reviews[0]["threads"]] == [
        ("A.cs", False), ("big.txt", True)]


def test_with_no_note_to_open_a_review_each_goes_on_its_file_alone(waits: list[float]) -> None:
    notes = [review.Note("added", "big.txt", 1, "RIGHT", "b", "kb"),
             review.Note("added", "big.txt", 5, "RIGHT", "c", "kc")]
    github = FakeGitHub()
    github.no_lines = {"big.txt"}
    assert review.post(github, 5, "abc", notes, "s") == 2
    assert github.reviews == []
    assert github.on_files == [("big.txt", "b\n\n<!-- code-scanning: kb -->"),
                               ("big.txt", "c\n\n<!-- code-scanning: kc -->")]
    assert waits == [GAP] * 3


def test_with_no_review_a_file_github_refuses_is_named_with_its_answer(
        waits: list[float]) -> None:
    github, written = FakeGitHub(), list[str]()
    github.no_lines = github.no_files = {"big.txt"}
    assert review.post(github, 5, "abc", _two()[1:], _summaries(written)) == 0
    assert "`big.txt:1`, added: Diff entry big.txt diff is too large" in written[-1]


def _gone(number: int, line: int, **more: Any) -> dict[str, Any]:
    """A thread the review posted, on `A.cs`, for a suppression no note is made for any more."""
    return {"id": number, "path": "A.cs", "line": line,
            "body": f"old\n\n<!-- code-scanning: gone{number} -->", **more}


def test_a_thread_whose_suppression_is_gone_is_deleted_and_listed(waits: list[float]) -> None:
    github, written = FakeGitHub(), list[str]()
    github.reviewed = [_gone(11, 9), _gone(12, 12)]
    review.post(github, 5, "abc", _two()[:1], _summaries(written))
    assert github.deleted == [11, 12]
    assert waits == [GAP] * 3
    assert "**Threads whose suppression is no longer in this pull request: 2**" in written[-1]
    assert "- `A.cs:9`, deleted" in written[-1]
    assert "- `A.cs:12`, deleted" in written[-1]


def test_a_gone_thread_somebody_answered_in_or_resolved_is_kept(waits: list[float]) -> None:
    github, written = FakeGitHub(resolved={12}), list[str]()
    github.reviewed = [_gone(11, 9), _gone(12, 12),
                       {"id": 13, "in_reply_to_id": 11, "path": "A.cs", "line": 9, "body": "why?"}]
    review.post(github, 5, "abc", _two()[:1], _summaries(written))
    assert github.deleted == []
    assert "- `A.cs:9`, kept: somebody answered in it" in written[-1]
    assert "- `A.cs:12`, kept: resolved" in written[-1]


def test_a_thread_whose_suppression_remains_is_not_deleted() -> None:
    github = FakeGitHub(reviewed=["a\n\n<!-- code-scanning: ka -->"])
    assert review.post(github, 5, "abc", _two()[:1], "the summary") == 0
    assert github.deleted == []


def test_when_which_threads_are_resolved_cannot_be_read_none_is_deleted(
        waits: list[float]) -> None:
    class Unreadable(FakeGitHub):
        def resolved_threads(self, number: int) -> set[int]:
            raise review.Refused(403, "Resource not accessible by integration")

    github, written = Unreadable(), list[str]()
    github.reviewed = [_gone(11, 9)]
    review.post(github, 5, "abc", _two()[:1], _summaries(written))
    assert github.deleted == []
    assert ("- `A.cs:9`, kept: which threads are resolved could not be read: Resource not "
            "accessible by integration") in written[-1]


def test_a_refused_deletion_keeps_the_thread_and_the_review_goes_on(waits: list[float]) -> None:
    class Stubborn(FakeGitHub):
        def delete_comment(self, comment: int) -> None:
            if comment == 11:
                raise review.Refused(403, "Resource not accessible by integration")
            super().delete_comment(comment)

    github, written = Stubborn(), list[str]()
    github.reviewed = [_gone(11, 9), _gone(12, 12)]
    review.post(github, 5, "abc", _two()[:1], _summaries(written))
    assert github.deleted == [12]
    assert waits == [GAP] * 3
    assert ("- `A.cs:9`, kept: GitHub refused to delete it: Resource not accessible by "
            "integration") in written[-1]
    assert "- `A.cs:12`, deleted" in written[-1]


def test_a_comment_already_gone_counts_as_deleted(waits: list[float]) -> None:
    class Vanished(FakeGitHub):
        def delete_comment(self, comment: int) -> None:
            raise review.Refused(404, "Not Found")

    github, written = Vanished(), list[str]()
    github.reviewed = [_gone(11, 9)]
    review.post(github, 5, "abc", _two()[:1], _summaries(written))
    assert "- `A.cs:9`, deleted" in written[-1]


def test_a_deletion_answered_502_is_read_back_rather_than_tried_again(waits: list[float]) -> None:
    class Slow(FakeGitHub):
        def delete_comment(self, comment: int) -> None:
            super().delete_comment(comment)
            raise review.Refused(502, "Bad Gateway")

    github, written = Slow(), list[str]()
    github.reviewed = [_gone(11, 9)]
    review.post(github, 5, "abc", _two()[:1], _summaries(written))
    assert github.deleted == [11]
    assert waits == [GAP, GAP]
    assert "- `A.cs:9`, deleted" in written[-1]


def test_a_reply_quoting_a_mark_is_no_thread_and_nothing_gone(waits: list[float]) -> None:
    github, written = FakeGitHub(), list[str]()
    github.reviewed = [{"path": "A.cs", "line": 3, "in_reply_to_id": 1,
                        "body": "> a\n\n<!-- code-scanning: ka --> and <!-- code-scanning: x -->"}]
    assert review.post(github, 5, "abc", _two()[:1], _summaries(written)) == 1
    assert "no longer in this pull request" not in written[-1]
