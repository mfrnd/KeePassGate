<#
.SYNOPSIS
    Refuses a doc comment that describes more than one member, or that has no summary.

.DESCRIPTION
    Two `<summary>` elements in one block is what happens when a member moves and its documentation
    does not: the block ends up on whatever is next, that member now has two summaries, and the one
    the text was written for has none. The compiler says nothing, because it simply takes the last
    one it sees, so the wrong description sits on the wrong member until somebody reads both.

    That is not hypothetical here. Nine of them accumulated before anybody noticed, and one had been
    corrected in one copy and left stale in the other, so the tree carried a sentence and its own
    correction in two places at once.

    A block with no `<summary>` documents nothing at the place it is read. The summary is what a
    reader of the member's tooltip gets, so a block holding only a `<param>`, a `<remarks>` or a
    `<returns>` leaves that reader with the signature and nothing more, however much it explains to
    somebody reading the source. CodeQL's `cs/xmldoc/missing-summary` asks for one on public types,
    methods and constructors; this asks it of every block, because a private member's tooltip is
    read by the same people.

    Deliberately not a style check. It asks nothing about whether a member is documented or how the
    prose reads: only that a block describes exactly one member and has a summary to describe it
    with. Two faults are left to the compiler, which refuses both once `GenerateDocumentationFile`
    is on and warnings are errors: malformed XML, and a block with no member after it, which is
    CS1587 in front of a closing brace, at the end of a file and inside a method alike.
#>

[CmdletBinding()]
param(
    [string[]] $Path = @('src', 'tests')
)

$ErrorActionPreference = 'Stop'

$root = Split-Path -Parent $PSScriptRoot
$problems = @()

foreach ($where in $Path) {
    $full = Join-Path $root $where
    if (-not (Test-Path $full)) { continue }

    foreach ($file in Get-ChildItem -Path $full -Filter '*.cs' -Recurse -File) {
        $relative = $file.FullName.Substring($root.Length + 1)

        # Somebody else's source, and generated files nobody writes by hand. Matched on the
        # path relative to the root, because a checkout that itself sits under a directory of
        # one of these names would otherwise skip every file and report success.
        if ($relative -match '(^|\\)(bin|obj|vendor)(\\|$)') { continue }

        # Forced to an array: `Get-Content` returns a bare string for a one line file and
        # nothing at all for an empty one, and under the `Set-StrictMode -Version Latest` this
        # inherits, `.Count` and an index into either of those both throw.
        $lines = @(Get-Content -LiteralPath $file.FullName)

        $blockStart = -1
        $summaries = 0
        $remarks = 0

        for ($i = 0; $i -lt $lines.Count; $i++) {
            $line = $lines[$i].Trim()

            if ($line.StartsWith('///')) {
                if ($blockStart -lt 0) {
                    $blockStart = $i + 1
                    $summaries = 0
                    $remarks = 0
                }

                $summaries += ([regex]::Matches($line, '<summary>')).Count
                $remarks += ([regex]::Matches($line, '<remarks>')).Count
                continue
            }

            if ($blockStart -lt 0) { continue }

            # An attribute sits between a block and what it documents, and so does a blank line
            # inside a block somebody spaced out. Neither ends it.
            if ($line.StartsWith('[') -or $line -eq '') { continue }

            # Whatever comes next is taken for the member, a closing brace included: a block with
            # no member after it is CS1587, which the compiler refuses wherever the block sits.
            # What is asked here is what the compiler does not ask. A block with no summary
            # documents nothing where it is read, since the summary is what a reader of the
            # member's tooltip gets.
            if ($summaries -eq 0) {
                $problems += "$relative`:$blockStart is a doc comment with no <summary>, so it documents nothing where it is read."
            }

            if ($summaries -gt 1) {
                $problems += "$relative`:$blockStart has $summaries <summary> elements in one block, so it describes more than one member."
            }

            if ($remarks -gt 1) {
                $problems += "$relative`:$blockStart has $remarks <remarks> elements in one block."
            }

            $blockStart = -1
        }
    }
}

if ($problems.Count -gt 0) {
    foreach ($problem in $problems) { Write-Host "  $problem" -ForegroundColor Red }
    throw "$($problems.Count) doc comment(s) describe more than one member, or have no <summary>."
}

Write-Host '  every doc comment has a summary and describes exactly one member' -ForegroundColor DarkGray
