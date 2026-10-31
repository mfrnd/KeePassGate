"""Writes `tests/tools/SuppressionVectors.json`: suppression comments, and what CodeQL made of each.

`suppressions.py` decides what a comment suppresses the way CodeQL decides it, and these vectors
are what holds it to that. Every case below is analyzed by a real CodeQL, and every finding in it
is written down with whether CodeQL suppressed it; `tests/tools/test_suppressions.py` then asks the
tool the same question of the same source and expects the same answer, finding by finding. Without
them the tool would be held only to what somebody believed CodeQL does.

**Regenerated from CodeQL, never from the tool**, and again whenever the CodeQL the workflow pins
moves, since a new version may read a comment differently. That is the bundle
`github/codeql-action` releases as `codeql-bundle-v<version>`, the one its pinned commit links,
whose `codeql` executable is the one argument this needs. A bundle extracted under a long path may
fail to read its own query packs on Windows, where paths over 260 characters need `subst` or a short
folder; and CodeQL's Python extractor asks for the `py` launcher unless `--python` names an
interpreter, which one installed by `uv` does not bring.

**Every case is source no pull request should hold**, a finding with a comment that may or may not
suppress it, which is why the cases live here as text rather than as files: as `.cs` and `.py`
files the repository's own analysis would read them, and lint would refuse most of them.
"""

from __future__ import annotations

import argparse
import json
import os
import shutil
import subprocess
import sys
import tempfile
from dataclasses import dataclass
from pathlib import Path
from typing import Any

VECTORS = Path(__file__).resolve().parent.parent / "tests" / "tools" / "SuppressionVectors.json"
EXTENSIONS = {"csharp": ".cs", "python": ".py"}

CSHARP = """using System;

public static class CLASS
{
    private static bool Left() { return Environment.TickCount > 0; }

    private static bool Right() { return Environment.ProcessorCount > 0; }

    public static int Run()
    {
BODY
        return 0;
    }
}"""

PYTHON = '''"""One case of the suppression vectors."""


def work() -> None:
    print("work")


def run() -> None:
BODY'''

#: Written where a case's line has to start at the margin rather than inside the method.
MARGIN = "|"


@dataclass(frozen=True)
class Case:
    """One file to analyze: its name, its language, and its text."""

    name: str
    language: str
    source: str


def _indent(lines: tuple[str, ...], by: str) -> str:
    return "\n".join(
        line[len(MARGIN) :] if line.startswith(MARGIN) else (by + line if line else "")
        for line in lines
    )


def _csharp(name: str, *body: str) -> Case:
    title = "".join(word.capitalize() for word in name.split("-"))
    source = CSHARP.replace("CLASS", title).replace("BODY", _indent(body, " " * 8))
    return Case(name, "csharp", source)


def _python(name: str, *body: str) -> Case:
    return Case(name, "python", PYTHON.replace("BODY", _indent(body, " " * 4)))


# A useless assignment, the finding most C# cases are about: the first value is read, the second
# never is. And the same in Python, a variable nothing reads.
USELESS = ("var value = Left() ? 1 : 2;", "Console.WriteLine(value);")
ASSIGN = "value = 5;"
UNUSED = "value = 1"
RULE = "cs/useless-assignment-to-local"
PYTHON_RULE = "py/unused-local-variable"


def _catch(*comment: str) -> tuple[str, ...]:
    """An empty catch of everything, which two rules report on the same line."""
    return ("try", "{", "    Console.WriteLine(Left());", "}", *comment,
            "catch (Exception)", "{", "}")


def _except(*comment: str) -> tuple[str, ...]:
    """The same in Python: an empty handler of everything, two rules on the one line."""
    return ("try:", "    work()", *comment, "except BaseException:", "    pass")


def cases() -> list[Case]:
    """Every case, in the order the vectors list them."""
    both = "codeql[cs/catch-of-all-exceptions] codeql[cs/empty-catch-block]"
    python_both = "codeql[py/catch-base-exception] codeql[py/empty-except]"
    return [
        _csharp("codeql-above", *USELESS, f"// codeql[{RULE}] the reason", ASSIGN),
        _csharp("two-markers", *_catch(f"// {both} one reason for both")),
        _csharp("reason-after-each", *_catch(
            "// codeql[cs/catch-of-all-exceptions] first; codeql[cs/empty-catch-block] second")),
        _csharp("list", *_catch(
            "// codeql[cs/catch-of-all-exceptions, cs/empty-catch-block] a list")),
        _csharp("stacked", *_catch("// codeql[cs/catch-of-all-exceptions] the upper one",
                                   "// codeql[cs/empty-catch-block] the lower one")),
        _csharp("blank-between", *USELESS, f"// codeql[{RULE}] a blank line under it", "", ASSIGN),
        _csharp("trailing-before", USELESS[0],
                f"Console.WriteLine(value); // codeql[{RULE}] after code on the line before",
                ASSIGN),
        _csharp("trailing-same-line", *USELESS, f"value = 5; // codeql[{RULE}] on the line itself"),
        _csharp("lgtm-same-line", *USELESS, f"value = 5; // lgtm[{RULE}] on the line itself"),
        _csharp("lgtm-alone-above", *USELESS, f"// lgtm[{RULE}] alone on the line above", ASSIGN),
        _csharp("lgtm-bare-above", *USELESS, "// lgtm", ASSIGN),
        _csharp("lgtm-after-semicolon", *USELESS, "// checked by hand; lgtm", ASSIGN),
        _csharp("lgtm-in-prose", *USELESS, "// nobody would call this lgtm", ASSIGN),
        _csharp("documentation-comment", *USELESS, f"/// codeql[{RULE}] documentation", ASSIGN),
        _csharp("block-one-line", *USELESS, f"/* codeql[{RULE}] a block on one line */", ASSIGN),
        _csharp("block-lines", *USELESS, "/* a block comment of three lines,",
                f"   codeql[{RULE}] on the middle one", "*/", ASSIGN),
        _csharp("verbatim-string", *USELESS,
                'var text = @"a verbatim string, whose second line is no comment',
                f'|// codeql[{RULE}] inside the string";', ASSIGN, "Console.WriteLine(text);"),
        _csharp("raw-string", *USELESS, 'var text = """',
                '    a raw string, with "quotes" and // slashes in it', '    """;',
                f"// codeql[{RULE}] after the raw string", ASSIGN, "Console.WriteLine(text);"),
        _csharp("interpolation", *USELESS,
                'var text = $"{(Left() ? "a // b" : "c")} and // more of the string";',
                f"// codeql[{RULE}] after an interpolated string", ASSIGN,
                "Console.WriteLine(text);"),
        _csharp("character-quote", *USELESS,
                "var quote = '\"'; // a quote in a character, then a comment",
                f"// codeql[{RULE}] after a character holding a quote", ASSIGN,
                "Console.WriteLine(quote);"),
        _csharp("active-branch", *USELESS, "|#if !NEVER",
                f"// codeql[{RULE}] in a branch the compiler keeps", ASSIGN, "|#endif"),
        _csharp("upper-case", *USELESS, f"// codeql[{RULE.upper()}] the rule in upper case",
                ASSIGN),
        _csharp("upper-case-keyword", *USELESS, f"// CODEQL[{RULE}] the keyword in upper case",
                ASSIGN),
        _csharp("spaces-inside", *USELESS, f"// codeql[ {RULE} ] with spaces inside", ASSIGN),
        _csharp("space-before-bracket", *USELESS, f"// codeql [{RULE}] with a space before",
                ASSIGN),
        _csharp("wrong-rule", *USELESS, "// codeql[cs/non-short-circuit] another rule's name",
                ASSIGN),
        _csharp("text-before", *USELESS, f"// the reason first, then codeql[{RULE}]", ASSIGN),
        _csharp("another-rule", "var flag = Left();",
                "// codeql[cs/non-short-circuit] both sides have to run",
                "var both = flag & Right();", "Console.WriteLine(both);"),
        _python("py-codeql-above", f"# codeql[{PYTHON_RULE}] the reason", UNUSED),
        _python("py-two-markers", *_except(f"# {python_both} one reason for both")),
        _python("py-list", *_except("# codeql[py/catch-base-exception, py/empty-except] a list")),
        _python("py-stacked", *_except("# codeql[py/catch-base-exception] the upper one",
                                       "# codeql[py/empty-except] the lower one")),
        _python("py-blank-between", f"# codeql[{PYTHON_RULE}] a blank line under it", "", UNUSED),
        _python("py-trailing-same-line", f"{UNUSED}  # codeql[{PYTHON_RULE}] on the line itself"),
        _python("py-lgtm-same-line", f"{UNUSED}  # lgtm[{PYTHON_RULE}] on the line itself"),
        _python("py-lgtm-alone-above", f"# lgtm[{PYTHON_RULE}] alone on the line above", UNUSED),
        _python("py-lgtm-bare-above", "# lgtm", UNUSED),
        _python("py-lgtm-after-semicolon", "# checked by hand; lgtm", UNUSED),
        _python("py-noqa-bare", f"{UNUSED}  # noqa"),
        _python("py-noqa-words", f"{UNUSED}  # noqa because it is a case"),
        _python("py-noqa-codes", f"{UNUSED}  # noqa: F841"),
        _python("py-noqa-alone-above", "# noqa", UNUSED),
        _python("py-triple-quoted", 'text = """a string, whose second line is no comment',
                f'|# codeql[{PYTHON_RULE}] inside the string"""', UNUSED, "print(text)"),
        _python("py-hash-in-strings", "text = f\"{'#'} and # more of the string\" + '# and here'",
                f"# codeql[{PYTHON_RULE}] after strings holding a hash", UNUSED, "print(text)"),
        _python("py-upper-case", f"# codeql[{PYTHON_RULE.upper()}] the rule in upper case",
                UNUSED),
        _python("py-upper-case-keyword", f"# CODEQL[{PYTHON_RULE}] the keyword in upper case",
                UNUSED),
        _python("py-security", "import ssl", "",
                "# codeql[py/insecure-protocol] a security finding, suppressed as any other",
                "context = ssl.SSLContext(ssl.PROTOCOL_TLSv1)", "print(context)"),
    ]


def results(sarif: dict[str, Any]) -> dict[str, list[dict[str, Any]]]:
    """Every finding in a SARIF log, by the case it is in, with whether CodeQL suppressed it."""
    found: dict[str, list[dict[str, Any]]] = {}
    for run in sarif["runs"]:
        for result in run["results"]:
            where = result["locations"][0]["physicalLocation"]
            name = Path(where["artifactLocation"]["uri"]).stem
            kinds = {s.get("kind") for s in result.get("suppressions", [])}
            found.setdefault(name, []).append({
                "rule": result["ruleId"],
                "line": where["region"]["startLine"],
                "suppressed": "inSource" in kinds,
            })
    for listed in found.values():
        listed.sort(key=lambda r: (r["line"], r["rule"]))
    return found


def render(version: str, every: list[Case], found: dict[str, list[dict[str, Any]]]) -> str:
    """The vectors file, refusing a case CodeQL found nothing in, since it would test nothing."""
    empty = [case.name for case in every if not found.get(case.name)]
    if empty:
        raise ValueError(f"CodeQL found nothing in {', '.join(empty)}, which tests nothing.")
    document = {
        "codeql": version,
        "cases": [
            {
                "name": case.name,
                "language": case.language,
                "source": case.source.split("\n"),
                "results": found[case.name],
            }
            for case in every
        ],
    }
    return json.dumps(document, indent=2, ensure_ascii=False) + "\n"


def _run(command: list[str | Path], environment: dict[str, str]) -> str:  # pragma: no cover
    ran = subprocess.run(command, capture_output=True, text=True, check=False, env=environment)  # noqa: S603
    if ran.returncode != 0:
        raise RuntimeError(f"{' '.join(map(str, command[:3]))} failed:\n{ran.stdout}\n{ran.stderr}")
    return ran.stdout


def analyze(codeql: Path, python: Path | None, work: Path,
            every: list[Case]) -> tuple[str, dict[str, list[dict[str, Any]]]]:  # pragma: no cover
    """Each language's cases analyzed by CodeQL, as the workflow analyzes the repository's code."""
    shutil.rmtree(work, ignore_errors=True)
    environment = dict(os.environ)
    if python is not None:
        environment["PATH"] = str(python.parent) + os.pathsep + environment["PATH"]
    version = _run([codeql, "version", "--format=terse"], environment).strip()
    found: dict[str, list[dict[str, Any]]] = {}
    for language, extension in EXTENSIONS.items():
        folder = work / "source" / language
        folder.mkdir(parents=True)
        for case in every:
            if case.language == language:
                text = case.source + "\n"
                (folder / (case.name + extension)).write_text(text, encoding="utf-8", newline="\n")
        database, sarif = work / f"db-{language}", work / f"{language}.sarif"
        extractor = []
        if language == "python" and python is not None:
            extractor = ["-O", "python.python_executable_name=python"]
        _run([codeql, "database", "create", database, f"--language={language}", "--build-mode=none",
              f"--source-root={folder}", "--overwrite", *extractor], environment)
        queries = f"codeql/{language}-queries"
        _run([codeql, "database", "analyze", database,
              f"{queries}:codeql-suites/{language}-security-and-quality.qls",
              f"{queries}:AlertSuppression.ql", "--format=sarif-latest", f"--output={sarif}"],
             environment)
        found.update(results(json.loads(sarif.read_text(encoding="utf-8"))))
    return version, found


def main(argv: list[str] | None = None) -> int:  # pragma: no cover
    """Analyze every case with the CodeQL named, and write what it found over the vectors."""
    parser = argparse.ArgumentParser(description="Regenerate the suppression vectors from CodeQL.")
    parser.add_argument("--codeql", type=Path, required=True, help="the bundle's codeql executable")
    parser.add_argument("--python", type=Path, help="an interpreter for CodeQL's Python extractor")
    parser.add_argument("--work", type=Path, help="where the cases are analyzed, emptied first",
                        default=Path(tempfile.gettempdir()) / "suppression-vectors")
    arguments = parser.parse_args(argv)
    every = cases()
    version, found = analyze(arguments.codeql, arguments.python, arguments.work, every)
    VECTORS.write_text(render(version, every, found), encoding="utf-8", newline="\n")
    print(f"  {len(every)} cases written to {VECTORS}, as CodeQL {version} analyzed them.")
    return 0


if __name__ == "__main__":  # pragma: no cover
    sys.exit(main())
