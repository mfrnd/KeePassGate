"""The Security tab kept to what the code suppresses: every suppression listed there, as dismissed.

On `master` and on the weekly run everything is uploaded, the findings the code suppresses among the
rest, so each of those is an alert there, and this dismisses it, with the reason the comment gives,
as "won't fix" by the workflow. The dismissed alerts then list exactly the suppressions in the code,
which is the record a pull request's upload leaves out.

**It is kept exact by opening again what it dismissed and the code no longer suppresses.** GitHub
leaves an alert dismissed when its finding goes away, noting only when, so without this the list
would keep every suppression that ever was. Opened again, an alert whose finding is gone settles as
fixed at once, and one whose finding stayed when its comment went is open, as it should be. Only
dismissals this made are touched, which its comments begin with `Suppressed in code:` to mark; one
somebody made by hand stays theirs.

**A finding is matched to its alert within the one analysis just uploaded**: the same rule, file and
line, on an alert whose latest instance is that commit's, in that language. An alert's identity
changes when the line it is on changes, or a line after it, so a suppressed finding on an edited
line arrives as a new alert, dismissed here within seconds, while the old one is opened again and
settles as fixed. GitHub refuses a dismissal comment longer than 280 characters rather than cutting
it, so a long reason is cut here, and the code keeps it whole.
"""

from __future__ import annotations

import argparse
import os
import sys
from collections.abc import Iterable
from pathlib import Path
from typing import Any, Protocol

import code_scanning
import suppressions
from suppression_review import GitHub

PREFIX = "Suppressed in code: "
LIMIT = 280


class Alerts(Protocol):
    """What the dismissals ask of GitHub, which the tests answer with a stand-in."""

    def alerts(self, ref: str, state: str) -> list[dict[str, Any]]:
        """Every CodeQL alert in one state on one ref."""

    def change_alert(self, number: int, fields: dict[str, Any]) -> dict[str, Any]:
        """One alert dismissed or opened again, and what GitHub made of it."""


def reasons(scanned: Iterable[suppressions.Scanned]) -> dict[tuple[str, int, str], str]:
    """The reason each suppression gives, by the file, the line it covers and its rule."""
    found: dict[tuple[str, int, str], str] = {}
    for name, _, every in scanned:
        for suppression in every:
            for marker in suppression.markers:
                for line in marker.lines:
                    found.setdefault((name, line, marker.rule), suppression.reason)
    return found


def _place(alert: dict[str, Any]) -> tuple[str, int, str]:
    location = alert["most_recent_instance"]["location"]
    return location["path"], location["start_line"], alert["rule"]["id"]


def _reason(given: dict[tuple[str, int, str], str], place: tuple[str, int, str]) -> str:
    path, line, _ = place
    return given.get(place) or given.get((path, line, "*")) or "no reason given"


def sync(github: Alerts, ref: str, commit: str, category: str,
         suppressed: set[tuple[str, int, str]], given: dict[tuple[str, int, str], str],
         ) -> tuple[list[int], list[int]]:
    """Dismiss what one language's analysis suppresses, and open again what it no longer does."""
    dismissed: list[int] = []
    for alert in github.alerts(ref, "open"):
        instance = alert["most_recent_instance"]
        if instance.get("category") != category or instance.get("commit_sha") != commit:
            continue
        place = _place(alert)
        if place in suppressed:
            comment = (PREFIX + _reason(given, place))[:LIMIT]
            github.change_alert(alert["number"], {"state": "dismissed",
                                                  "dismissed_reason": "won't fix",
                                                  "dismissed_comment": comment})
            dismissed.append(alert["number"])
    opened: list[int] = []
    for alert in github.alerts(ref, "dismissed"):
        instance = alert["most_recent_instance"]
        ours = str(alert.get("dismissed_comment") or "").startswith(PREFIX)
        if not ours or instance.get("category") != category:
            continue
        if instance.get("commit_sha") == commit and _place(alert) in suppressed:
            continue
        github.change_alert(alert["number"], {"state": "open"})
        opened.append(alert["number"])
    return dismissed, opened


def main(argv: list[str] | None = None) -> int:
    """Bring the Security tab to what the code at `--commit` suppresses, one language at a time."""
    parser = argparse.ArgumentParser(description="Keep the Security tab to the suppressions.")
    parser.add_argument("--analyzed", type=Path, required=True, help="the SARIF CodeQL wrote")
    parser.add_argument("--root", type=Path, default=Path.cwd())
    parser.add_argument("--repository", required=True)
    parser.add_argument("--commit", required=True, help="the commit just uploaded")
    parser.add_argument("--ref", required=True, help="the branch it was uploaded for")
    arguments = parser.parse_args(argv)
    root: Path = arguments.root.resolve()
    given = reasons(suppressions.scan(root, suppressions.tracked(root)))
    github = GitHub(arguments.repository, os.environ.get("GITHUB_TOKEN", ""))
    for log in code_scanning.logs(arguments.analyzed):
        category = f"/language:{log.stem}"
        found = code_scanning.findings(code_scanning.read(log))
        suppressed = {(f.path, f.line, f.rule) for f in found if f.suppressed}
        dismissed, opened = sync(github, arguments.ref, arguments.commit, category, suppressed,
                                 given)
        print(f"  {category}: {len(suppressed)} suppressed in the code, {len(dismissed)} alert(s) "
              f"dismissed now, {len(opened)} opened again.")
    return 0


if __name__ == "__main__":
    sys.exit(main())
