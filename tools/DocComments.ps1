<#
.SYNOPSIS
    Refuses a doc comment that describes more than one thing, or nothing at all.

.DESCRIPTION
    Two `<summary>` elements in one block is what happens when a member moves and its documentation
    does not: the block ends up on whatever is next, that member now has two summaries, and the one
    the text was written for has none. The compiler says nothing, because it simply takes the last
    one it sees, so the wrong description sits on the wrong member until somebody reads both.

    That is not hypothetical here. Nine of them accumulated before anybody noticed, and one had been
    corrected in one copy and left stale in the other, so the tree carried a sentence and its own
    correction in two places at once.

    A block with nothing after it is the same accident with the member deleted rather than moved.

    Deliberately not a style check. It says nothing about whether a member is documented, how the
    prose reads, or which tags are used: only that a block describes exactly one thing and that the
    thing is there. `GenerateDocumentationFile` already catches malformed XML, which is a different
    fault and worth having as well.
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

            # A closing brace is not a member. A block sitting in front of one documents the
            # end of a type, which is the same accident as a block at the end of a file, and it
            # was passing because the brace was taken for what the block described.
            if ($line -eq '}') {
                $problems += "$relative`:$blockStart is a doc comment with no member after it."
                $blockStart = -1
                continue
            }

            if ($summaries -gt 1) {
                $problems += "$relative`:$blockStart has $summaries <summary> elements in one block, so it describes more than one member."
            }

            if ($remarks -gt 1) {
                $problems += "$relative`:$blockStart has $remarks <remarks> elements in one block."
            }

            $blockStart = -1
        }

        if ($blockStart -ge 0) {
            $problems += "$relative`:$blockStart is a doc comment with no member after it."
        }
    }
}

if ($problems.Count -gt 0) {
    foreach ($problem in $problems) { Write-Host "  $problem" -ForegroundColor Red }
    throw "$($problems.Count) doc comment(s) describe more than one member, or none."
}

Write-Host '  every doc comment describes exactly one member' -ForegroundColor DarkGray
