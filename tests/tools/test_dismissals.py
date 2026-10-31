"""The Security tab kept to what the code suppresses, over a stand-in for GitHub's alerts.

The cases are the ones measured on `spikes/codeql-suppressions`: a suppressed finding's alert is
dismissed with the comment's reason, cut where GitHub would refuse it; an alert this dismissed is
opened again once its finding is no longer suppressed in the analysis just uploaded, whether the
finding went away or only its comment did; and nothing is touched that belongs to another language,
an older commit, or a person who dismissed it by hand.
"""

from __future__ import annotations

import json
import subprocess
from pathlib import Path
from typing import Any

import pytest

import dismissals
import suppressions

COMMIT = "c" * 40
CATEGORY = "/language:csharp"


def _alert(number: int, line: int, *, rule: str = "cs/a", commit: str = COMMIT,
           category: str = CATEGORY, comment: str | None = None) -> dict[str, Any]:
    location = {"path": "A.cs", "start_line": line}
    instance = {"category": category, "commit_sha": commit, "location": location}
    return {"number": number, "rule": {"id": rule}, "most_recent_instance": instance,
            "dismissed_comment": comment}


class FakeAlerts:
    """Alerts by state, and every change asked for."""

    def __init__(self, opened: list[dict[str, Any]], dismissed: list[dict[str, Any]]) -> None:
        self.by_state = {"open": opened, "dismissed": dismissed}
        self.changes: list[tuple[int, dict[str, Any]]] = []

    def alerts(self, ref: str, state: str) -> list[dict[str, Any]]:
        assert ref == "refs/heads/master"
        return self.by_state[state]

    def change_alert(self, number: int, fields: dict[str, Any]) -> dict[str, Any]:
        self.changes.append((number, fields))
        return {"number": number, "state": fields["state"]}


def _sync(github: FakeAlerts, suppressed: set[tuple[str, int, str]],
          given: dict[tuple[str, int, str], str] | None = None) -> tuple[list[int], list[int]]:
    return dismissals.sync(github, "refs/heads/master", COMMIT, CATEGORY, suppressed, given or {})


def test_an_open_alert_the_code_suppresses_is_dismissed_with_the_reason() -> None:
    github = FakeAlerts([_alert(1, 4), _alert(2, 9)], [])
    given = {("A.cs", 4, "cs/a"): "it has to be so"}
    assert _sync(github, {("A.cs", 4, "cs/a")}, given) == ([1], [])
    assert github.changes == [(1, {"state": "dismissed", "dismissed_reason": "won't fix",
                                   "dismissed_comment": "Suppressed in code: it has to be so"})]


def test_a_reason_longer_than_github_takes_is_cut_to_what_it_takes() -> None:
    github = FakeAlerts([_alert(1, 4)], [])
    _sync(github, {("A.cs", 4, "cs/a")}, {("A.cs", 4, "cs/a"): "why " * 100})
    comment = github.changes[0][1]["dismissed_comment"]
    assert len(comment) == dismissals.LIMIT
    assert comment.startswith(dismissals.PREFIX)


def test_a_suppression_of_every_rule_lends_its_reason_and_none_is_named_as_none() -> None:
    github = FakeAlerts([_alert(1, 4), _alert(2, 6)], [])
    _sync(github, {("A.cs", 4, "cs/a"), ("A.cs", 6, "cs/a")}, {("A.cs", 4, "*"): "every rule"})
    assert [c[1]["dismissed_comment"] for c in github.changes] == [
        "Suppressed in code: every rule", "Suppressed in code: no reason given"]


def test_another_language_or_an_older_commit_is_left_alone() -> None:
    python = "/language:python"
    github = FakeAlerts([_alert(1, 4, category=python), _alert(2, 4, commit="o" * 40)],
                        [_alert(3, 4, category=python, comment="Suppressed in code: x")])
    assert _sync(github, {("A.cs", 4, "cs/a")}) == ([], [])
    assert github.changes == []


def test_a_dismissal_still_suppressed_stays_and_one_no_longer_is_opened_again() -> None:
    kept = _alert(1, 4, comment="Suppressed in code: still")
    comment_gone = _alert(2, 9, comment="Suppressed in code: was")
    finding_gone = _alert(3, 4, commit="o" * 40, comment="Suppressed in code: was")
    by_hand = _alert(4, 12, comment="a person's own reason")
    github = FakeAlerts([], [kept, comment_gone, finding_gone, by_hand])
    assert _sync(github, {("A.cs", 4, "cs/a")}) == ([], [2, 3])
    assert github.changes == [(2, {"state": "open"}), (3, {"state": "open"})]


def test_the_reasons_come_from_every_marker_by_the_line_it_covers() -> None:
    text = "class C\n{\n    // codeql[cs/a] codeql[cs/b] why\n    void M() { } // lgtm\n}\n"
    source = suppressions.read(text, "csharp")
    scanned = [("C.cs", source, suppressions.suppressions(source, "csharp", "C.cs"))]
    assert dismissals.reasons(scanned) == {("C.cs", 4, "cs/a"): "why", ("C.cs", 4, "cs/b"): "why",
                                           ("C.cs", 4, "*"): ""}


def test_main_brings_each_language_to_what_its_analysis_suppresses(
        tmp_path: Path, monkeypatch: pytest.MonkeyPatch,
        capsys: pytest.CaptureFixture[str]) -> None:
    root = tmp_path / "repository"
    root.mkdir()
    (root / "A.cs").write_text("class A\n{\n    // codeql[cs/a] the reason\n    void M() { }\n}\n",
                               encoding="utf-8", newline="\n")
    subprocess.run(["git", "init", "-q"], cwd=root, check=True)  # noqa: S607
    subprocess.run(["git", "add", "A.cs"], cwd=root, check=True)  # noqa: S607
    analyzed = tmp_path / "analyzed"
    analyzed.mkdir()
    result = {"ruleId": "cs/a", "message": {"text": "m"}, "suppressions": [{"kind": "inSource"}],
              "locations": [{"physicalLocation": {"artifactLocation": {"uri": "A.cs"},
                                                  "region": {"startLine": 4}}}]}
    log = {"runs": [{"tool": {"driver": {"name": "CodeQL", "rules": []}}, "results": [result]}]}
    (analyzed / "csharp.sarif").write_text(json.dumps(log), encoding="utf-8")
    github = FakeAlerts([_alert(1, 4)], [])
    monkeypatch.setattr(dismissals, "GitHub", lambda repository, token: github)
    assert dismissals.main(["--analyzed", str(analyzed), "--root", str(root), "--repository", "o/r",
                            "--commit", COMMIT, "--ref", "refs/heads/master"]) == 0
    assert github.changes[0][1]["dismissed_comment"] == "Suppressed in code: the reason"
    printed = capsys.readouterr().out
    assert "/language:csharp: 1 suppressed in the code, 1 alert(s) dismissed now" in printed
