"""What the CodeQL workflow uploads and how it judges an analysis, over SARIF written here.

The logs are the shape CodeQL writes: one run for each language, the rules under the driver or under
the query packs as extensions, and a suppressed result marked `inSource`. What is held is that a
pull request's upload leaves out exactly the suppressed results and keeps everything else as it was,
that `master` uploads everything, and that the verdict fails on any result nothing suppresses,
marking it at its line.
"""

from __future__ import annotations

import json
from pathlib import Path
from typing import Any

import pytest

import code_scanning


def _result(rule: str, path: str, line: int, *, suppressed: bool = False,
            message: str = "a finding") -> dict[str, Any]:
    where = {"artifactLocation": {"uri": path}, "region": {"startLine": line}}
    result: dict[str, Any] = {"ruleId": rule, "message": {"text": message},
                              "locations": [{"physicalLocation": where}]}
    if suppressed:
        result["suppressions"] = [{"kind": "inSource"}]
    return result


def _log(*results: dict[str, Any]) -> dict[str, Any]:
    driver = {"name": "CodeQL", "rules": [{"id": "cs/quality"}]}
    pack = {"name": "codeql/csharp-queries", "rules": [
        {"id": "cs/security", "properties": {"security-severity": "7.5"}}]}
    run = {"tool": {"driver": driver, "extensions": [pack]}, "results": list(results),
           "automationDetails": {"id": "/language:csharp/"}}
    return {"version": "2.1.0", "runs": [run]}


def _write(folder: Path, name: str, sarif: dict[str, Any]) -> Path:
    folder.mkdir(parents=True, exist_ok=True)
    path = folder / name
    path.write_text(json.dumps(sarif), encoding="utf-8")
    return path


def test_a_finding_carries_its_rule_place_message_and_security_severity() -> None:
    log = _log(_result("cs/quality", "src/A.cs", 3),
               _result("cs/security", "src/B.cs", 9, suppressed=True, message="two\nlines"))
    assert code_scanning.findings(log) == [
        code_scanning.Finding("cs/quality", "src/A.cs", 3, "a finding", None, False),
        code_scanning.Finding("cs/security", "src/B.cs", 9, "two\nlines", "7.5", True),
    ]


def test_only_what_the_code_suppresses_is_left_out() -> None:
    external = _result("cs/quality", "src/C.cs", 4)
    external["suppressions"] = [{"kind": "external"}]
    log = _log(_result("cs/quality", "src/A.cs", 3), _result("cs/security", "src/B.cs", 9,
                                                              suppressed=True), external)
    kept = code_scanning.without_suppressed(log)
    assert [r["locations"][0]["physicalLocation"]["region"]["startLine"]
            for r in kept["runs"][0]["results"]] == [3, 4]
    assert kept["runs"][0]["automationDetails"] == log["runs"][0]["automationDetails"]
    assert len(log["runs"][0]["results"]) == 3


def test_a_pull_request_uploads_nothing_the_code_suppresses(tmp_path: Path) -> None:
    analyzed = tmp_path / "analyzed"
    _write(analyzed / "csharp", "csharp.sarif", _log(
        _result("cs/quality", "src/A.cs", 3),
        _result("cs/security", "src/B.cs", 9, suppressed=True),
        _result("cs/quality", "src/C.cs", 1, suppressed=True)))
    dropped = code_scanning.prepare(analyzed, tmp_path / "upload", pull_request=True)
    assert dropped == ["cs/security at src/B.cs:9 (security, 7.5)",
                       "cs/quality at src/C.cs:1 (quality)"]
    uploaded = json.loads((tmp_path / "upload" / "csharp.sarif").read_text(encoding="utf-8"))
    assert [f.line for f in code_scanning.findings(uploaded)] == [3]


def test_master_uploads_everything_as_codeql_wrote_it(tmp_path: Path) -> None:
    analyzed = tmp_path / "analyzed"
    log = _log(_result("cs/quality", "a.py", 1, suppressed=True))
    written = _write(analyzed, "python.sarif", log)
    assert code_scanning.prepare(analyzed, tmp_path / "upload", pull_request=False) == []
    assert (tmp_path / "upload" / "python.sarif").read_bytes() == written.read_bytes()


def test_an_analysis_that_wrote_nothing_is_refused(tmp_path: Path) -> None:
    (tmp_path / "empty").mkdir()
    with pytest.raises(SystemExit, match="wrote nothing"):
        code_scanning.verdict(tmp_path / "empty")


def test_an_annotation_escapes_what_a_workflow_command_would_read() -> None:
    finding = code_scanning.Finding("cs/x", "src/a,b:c.cs", 7, "100% sure\n2nd line", None, False)
    assert code_scanning.annotation(finding) == (
        "::error file=src/a%2Cb%3Ac.cs,line=7,title=cs/x::100%25 sure%0A2nd line")


def test_the_verdict_fails_on_a_finding_nothing_suppresses_anywhere(
        tmp_path: Path, capsys: pytest.CaptureFixture[str]) -> None:
    analyzed = tmp_path / "analyzed"
    _write(analyzed, "csharp.sarif", _log(_result("cs/quality", "src/A.cs", 3),
                                          _result("cs/security", "src/B.cs", 9, suppressed=True)))
    _write(analyzed, "actions.sarif", _log())
    assert code_scanning.main(["verdict", "--analyzed", str(analyzed)]) == 1
    printed = capsys.readouterr().out
    assert "::error file=src/A.cs,line=3,title=cs/quality::a finding" in printed
    assert "1 finding(s) nothing suppresses, 1 suppressed in the code." in printed
    assert "src/B.cs" not in printed


def test_the_verdict_passes_when_the_code_suppresses_every_finding(
        tmp_path: Path, capsys: pytest.CaptureFixture[str]) -> None:
    analyzed = tmp_path / "analyzed"
    _write(analyzed, "csharp.sarif", _log(_result("cs/security", "src/B.cs", 9, suppressed=True)))
    assert code_scanning.main(["verdict", "--analyzed", str(analyzed)]) == 0
    assert "no finding left, 1 suppressed in the code." in capsys.readouterr().out


@pytest.mark.parametrize(("event", "printed"), [
    ("pull_request", "1 suppressed finding(s) left out of the upload."),
    ("push", "everything uploaded, the suppressed findings included."),
    ("schedule", "everything uploaded, the suppressed findings included."),
])
def test_prepare_names_what_it_did_for_each_event(
        tmp_path: Path, capsys: pytest.CaptureFixture[str], event: str, printed: str) -> None:
    analyzed = tmp_path / "analyzed"
    _write(analyzed, "csharp.sarif", _log(_result("cs/quality", "src/A.cs", 3, suppressed=True)))
    arguments = ["prepare", "--analyzed", str(analyzed), "--upload", str(tmp_path / "up"),
                 "--event", event]
    assert code_scanning.main(arguments) == 0
    assert printed in capsys.readouterr().out


def test_a_finding_carries_its_rule_s_title_its_last_line_and_the_places_it_links_to() -> None:
    result = _result("cs/quality", "src/A.cs", 3, message="Disposed [here](1).")
    result["locations"][0]["physicalLocation"]["region"]["endLine"] = 7
    result["relatedLocations"] = [
        {"id": 1, "physicalLocation": {"artifactLocation": {"uri": "src/A.cs"},
                                       "region": {"startLine": 9}}},
        {"id": 2, "physicalLocation": {"artifactLocation": {"uri": "src/A.cs"}}}]
    log = _log(result)
    log["runs"][0]["tool"]["driver"]["rules"][0]["shortDescription"] = {"text": "Quality"}
    finding = code_scanning.findings(log)[0]
    assert (finding.name, finding.end_line, finding.related) == (
        "Quality", 7, ((1, "src/A.cs", 9),))
