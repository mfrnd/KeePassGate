"""The suppression tool, held to what CodeQL itself makes of the same comments.

`SuppressionVectors.json` beside this file was made by CodeQL, over the cases
`tools/suppression_vectors.py` writes down, and every finding in it records whether CodeQL
suppressed it. The tool has to give the same answer for each, from the same source. A file
regenerated from a CodeQL that reads comments differently fails here until the tool agrees with it
again, and a case edited without regenerating the file fails here too.

The rest is what CodeQL does not decide: the one spelling lint accepts, the reason it gives for
refusing every other, and that a file which does not parse is refused rather than read with a guess.
"""

from __future__ import annotations

import json
import subprocess
from pathlib import Path
from typing import Any

import pytest

import suppression_vectors
import suppressions

VECTORS = Path(__file__).parent / "SuppressionVectors.json"


def _vectors() -> dict[str, Any]:
    loaded: dict[str, Any] = json.loads(VECTORS.read_text(encoding="utf-8"))
    return loaded


def _text(case: dict[str, Any]) -> str:
    return "\n".join(case["source"]) + "\n"


def _found(text: str, language: str = "csharp") -> list[suppressions.Suppression]:
    return suppressions.suppressions(suppressions.read(text, language), language)


def _problems(text: str, language: str = "csharp") -> list[str]:
    return [problem for found in _found(text, language) for problem in found.problems]


def _method(*lines: str) -> str:
    """C# lines inside a method, so that the code around a comment is code to the grammar."""
    body = "\n".join("        " + line if line else "" for line in lines)
    return f"class C\n{{\n    void M()\n    {{\n{body}\n    }}\n}}\n"


CASES = _vectors()["cases"]


@pytest.mark.parametrize("case", CASES, ids=[case["name"] for case in CASES])
def test_the_tool_suppresses_exactly_what_codeql_suppressed(case: dict[str, Any]) -> None:
    language = case["language"]
    lines = suppressions.coverage(_found(_text(case), language))
    for result in case["results"]:
        mine = suppressions.suppressed(lines, result["rule"], result["line"])
        assert mine == result["suppressed"], f"{result['rule']} at line {result['line']}"


def test_the_vectors_hold_both_languages_and_both_answers() -> None:
    vectors = _vectors()
    answers = [r["suppressed"] for case in vectors["cases"] for r in case["results"]]
    assert vectors["codeql"]
    assert {case["language"] for case in vectors["cases"]} == {"csharp", "python"}
    assert all(case["results"] for case in vectors["cases"])
    assert True in answers and False in answers


def test_the_vectors_are_the_cases_the_generator_writes() -> None:
    written = [(c.name, c.language, c.source) for c in suppression_vectors.cases()]
    recorded = [(c["name"], c["language"], "\n".join(c["source"])) for c in _vectors()["cases"]]
    assert recorded == written


def test_the_generator_reads_a_sarif_log_by_case() -> None:
    def result(uri: str, line: int, rule: str, *kinds: str) -> dict[str, Any]:
        where = {"artifactLocation": {"uri": uri}, "region": {"startLine": line}}
        return {"ruleId": rule, "locations": [{"physicalLocation": where}],
                "suppressions": [{"kind": kind} for kind in kinds]}

    log = {"runs": [{"results": [result("a.cs", 9, "cs/b"), result("a.cs", 4, "cs/a", "inSource"),
                                 result("p.py", 2, "py/x", "external")]}]}
    assert suppression_vectors.results(log) == {
        "a": [{"rule": "cs/a", "line": 4, "suppressed": True},
              {"rule": "cs/b", "line": 9, "suppressed": False}],
        "p": [{"rule": "py/x", "line": 2, "suppressed": False}],
    }


def test_the_generator_refuses_a_case_codeql_found_nothing_in() -> None:
    case = suppression_vectors.Case("quiet", "csharp", "class Q {}")
    with pytest.raises(ValueError, match="quiet"):
        suppression_vectors.render("2.27.3", [case], {})
    found = {"quiet": [{"rule": "cs/x", "line": 1, "suppressed": False}]}
    rendered = json.loads(suppression_vectors.render("2.27.3", [case], found))
    assert rendered == {"codeql": "2.27.3", "cases": [{
        "name": "quiet", "language": "csharp", "source": ["class Q {}"], "results": found["quiet"],
    }]}


@pytest.mark.parametrize(("language", "text"), [
    ("csharp", 'var url = "https://example.com/codeql[cs/x]";\n'),
    ("csharp", 'var path = @"C:\\x\\" + "// still a string";\n'),
    ("csharp", 'var v = @"a verbatim string\n// codeql[cs/x] inside it";\n'),
    ("csharp", 'var raw = """\n    // codeql[cs/x] inside a raw string\n    """;\n'),
    ("csharp", 'var hole = $"{(a ? "x // y" : "z")} // in the string";\n'),
    ("python", 'doc = """\n# codeql[py/x] inside a string\n"""\n'),
    ("python", "f = f\"{x['k']} # not a comment\"\n"),
    ("python", 't = t"{x} # not a comment"\n'),
])
def test_a_marker_inside_a_string_is_no_comment(language: str, text: str) -> None:
    assert suppressions.read(text, language).comments == ()


def test_a_comment_after_a_string_is_still_found_where_it_starts() -> None:
    source = suppressions.read("var quote = '\"'; // after a character\n", "csharp")
    assert [(c.line, c.column, c.delimiter, c.text) for c in source.comments] == [
        (1, 17, "//", " after a character")]


def test_line_endings_and_a_byte_order_mark_change_no_line() -> None:
    text = "int a = 1;\r\n// codeql[cs/x] the reason\r\nint b = 2;\r\n"
    found = _found(text)
    assert [(f.line, f.covered, f.reason, f.problems) for f in found] == [(2, 3, "the reason", ())]


@pytest.mark.parametrize(("language", "text"), [
    ("csharp", _method("var value = 1;", "// codeql[cs/useless-assignment-to-local] the reason",
                       "value = 2;")),
    ("csharp", _method("// codeql[cs/catch-of-all-exceptions] codeql[cs/empty-catch-block] both",
                       "Call();")),
    ("python", "def f() -> None:\n    # codeql[py/unused-local-variable] the reason\n    x = 1\n"),
])
def test_the_one_spelling_is_accepted(language: str, text: str) -> None:
    found = _found(text, language)
    assert len(found) == 1
    assert found[0].problems == ()
    assert all(marker.lines == (found[0].covered,) for marker in found[0].markers)


@pytest.mark.parametrize(("text", "problem"), [
    (_method("// codeql[cs/a, cs/b] a list", "Call();"), "is a list"),
    (_method("// codeql[CS/A] the rule in upper case", "Call();"), "in another case"),
    (_method("// CODEQL[cs/a] the keyword in upper case", "Call();"), "is not how a marker"),
    (_method("// codeql[ cs/a ] spaces inside", "Call();"), "is not how a marker"),
    (_method("// codeql[cs/a] codeql[cs/a] twice", "Call();"), "names `cs/a` twice"),
    (_method("// the reason first, codeql[cs/a]", "Call();"), "text before its markers"),
    (_method("// codeql[cs/a] first; codeql[cs/b] second", "Call();"), "text between its markers"),
    (_method("// codeql[cs/a]", "Call();"), "gives no reason"),
    (_method("/// codeql[cs/a] documentation", "Call();"), "documentation comment"),
    (_method("/* codeql[cs/a] a block */", "Call();"), "is a `/* */` comment"),
    (_method("/* a block of", "   codeql[cs/a] two lines */", "Call();"), "several lines"),
    (_method("Call(); // codeql[cs/a] after code"), "follows code on its line"),
    (_method("// codeql[cs/a] a blank line under it", "", "Call();"), "a blank line under it"),
    (_method("// codeql[cs/a] the upper", "// codeql[cs/b] the lower", "Call();"),
     "a comment under it"),
    ("class C { }\n// codeql[cs/a] at the end\n", "nothing under it"),
    (_method("Call(); // lgtm[cs/a] on its line"), "applies it to its own line:"),
    (_method("// lgtm[cs/a] alone", "Call();"), "its own line and the next"),
    (_method("// lgtm", "Call();"), "every rule on its own line and the next"),
    (_method("// checked by hand; lgtm", "Call();"), "every rule"),
])
def test_every_other_spelling_is_refused_with_the_reason(text: str, problem: str) -> None:
    assert any(problem in found for found in _problems(text)), _problems(text)


@pytest.mark.parametrize("text", ["x = 1  # noqa\n", "x = 1  # NOQA because it is a case\n"])
def test_a_noqa_without_codes_is_refused_since_it_suppresses_every_rule(text: str) -> None:
    found = _found(text, "python")
    assert [marker.rule for f in found for marker in f.markers] == ["*"]
    assert any("`noqa` without codes" in problem for problem in _problems(text, "python"))


@pytest.mark.parametrize(("language", "text"), [
    ("csharp", _method("// nobody would call this lgtm", "Call();")),
    ("csharp", _method("// CodeQL reads this, but no bracket follows", "Call();")),
    ("python", "x = 1  # noqa: F841\n"),
    ("python", "x = 1  # noqa: S105, a password that is a name\n"),
])
def test_what_is_no_suppression_is_left_alone(language: str, text: str) -> None:
    assert _found(text, language) == []


def test_a_rule_is_matched_exactly_and_every_rule_by_a_star() -> None:
    lines = {3: {"cs/a"}, 5: {"*"}}
    assert suppressions.suppressed(lines, "cs/a", 3)
    assert not suppressions.suppressed(lines, "CS/A", 3)
    assert not suppressions.suppressed(lines, "cs/a", 4)
    assert suppressions.suppressed(lines, "anything/at-all", 5)


def test_the_language_follows_the_extension() -> None:
    assert suppressions.language_of("src/A.cs") == "csharp"
    assert suppressions.language_of(Path("tools/x.PY")) == "python"
    assert suppressions.language_of("build.ps1") is None


def _repository(tmp_path: Path, files: dict[str, str]) -> Path:
    for name, text in files.items():
        (tmp_path / name).parent.mkdir(parents=True, exist_ok=True)
        (tmp_path / name).write_text(text, encoding="utf-8", newline="\n")
    subprocess.run(["git", "init", "-q", str(tmp_path)], check=True)  # noqa: S603, S607
    subprocess.run(["git", "-C", str(tmp_path), "add", "--", *files], check=True)  # noqa: S603, S607
    return tmp_path


GOOD = "def f() -> None:\n    # codeql[py/unused-local-variable] the reason\n    x = 1\n"


def test_a_file_holding_a_marker_that_does_not_parse_is_refused(tmp_path: Path) -> None:
    root = _repository(tmp_path, {
        "broken.cs": "class C {\n    // codeql[cs/a] the reason\n    void M( {\n}\n",
        "unread.cs": "class D { void M( {\n",
    })
    refused = suppressions.lint(suppressions.scan(root, suppressions.tracked(root)))
    assert len(refused) == 1
    assert refused[0].startswith("broken.cs: holds a suppression comment and does not parse")


def test_the_lint_reads_only_tracked_files_that_could_hold_a_marker(tmp_path: Path) -> None:
    root = _repository(tmp_path, {"src/plain.cs": "class C { }\n", "doc/notes.md": "lgtm\n"})
    (root / "untracked.py").write_text("x = 1  # noqa\n", encoding="utf-8")
    assert [p.name for p in suppressions.tracked(root)] == ["plain.cs"]
    assert suppressions.scan(root, suppressions.tracked(root)) == []


def test_the_lint_names_every_refused_comment_and_fails(tmp_path: Path,
                                                        capsys: pytest.CaptureFixture[str]) -> None:
    root = _repository(tmp_path, {"good.py": GOOD, "bad.py": "x = 1  # noqa\n"})
    assert suppressions.main(["--root", str(root)]) == 1
    printed = capsys.readouterr().out
    assert "bad.py:1: CodeQL reads a `noqa` without codes" in printed
    assert "good.py" not in printed


def test_the_lint_passes_a_repository_holding_only_the_one_spelling(
        tmp_path: Path, capsys: pytest.CaptureFixture[str]) -> None:
    root = _repository(tmp_path, {"good.py": GOOD})
    assert suppressions.main(["--root", str(root)]) == 0
    assert "1 files, 1 suppression comment(s), none refused." in capsys.readouterr().out
