<#
.SYNOPSIS
    Checks that no dash is being used as punctuation.

.DESCRIPTION
    `CONVENTIONS.md` forbids a dash as punctuation anywhere: not an em-dash, not an en-dash, and
    not the ASCII `--` standing in for either. The rule covers prose, code comments, documents and
    anything a user reads, and it says what to do instead, which is to rewrite the sentence.

    A rule nobody checks is obeyed for as long as somebody remembers it, and the first person who
    does not know it is the one who writes the file everybody copies from. That is why this runs
    from the first commit that can run anything, rather than after a count shows what the rule
    has already cost.

    **What is checked is the shape a dash takes when it is punctuation**, which is a `--` standing
    on its own with space on both sides of it. A `--` with a word against it is something else and
    is left alone: `--exclude-standard` is a flag and `at--` is a decrement. The Unicode dashes are
    refused wherever they appear, because none of them has another job in a file here.

    **Two things are exempt, and both are syntax rather than prose.** A `--` on a line that runs
    `git`, `npx`, `dotnet` or `uv`, starting with the command or calling it with `&`, is an
    argument separator, which is how `Anchors.ps1` asks git about a single document. A line
    that only names one of them is prose like any other. And the same token inside a fenced
    `mermaid` block is part of a graph edge, where an arrow carries its label between the two
    halves of its own shaft.

    **`- ` as punctuation is not checked**, though the rule covers it. It cannot be told apart from
    a bullet, a YAML entry, a command-line flag and arithmetic by anything this could reasonably
    do, and a check that cries wolf on `Count - 1` would be turned off within a week. It is left to
    review, which is where a rule that cannot be mechanized belongs.

.PARAMETER Root
    The repository root. Everything is reported relative to it.
#>

[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)] [string] $Root
)

$ErrorActionPreference = 'Stop'

# Every text file the repository has, as git answers it, which is what `RepositoryFiles.ps1` says.
$files = @(& (Join-Path $PSScriptRoot 'RepositoryFiles.ps1') -Root $Root)

# Hyphen, non-breaking hyphen, figure dash, en, em, horizontal bar, and the minus sign. Named
# rather than given as a range so that adding one is a decision somebody wrote down.
$unicode = @(
    [char] 0x2010, [char] 0x2011, [char] 0x2012, [char] 0x2013,
    [char] 0x2014, [char] 0x2015, [char] 0x2212
)

$problems = @()

foreach ($file in $files) {
    $where = $file.FullName.Substring($Root.Length).TrimStart([IO.Path]::DirectorySeparatorChar)

    # Read as UTF-8, which every file here is. Windows PowerShell reads a file without a byte order
    # mark in the ANSI code page otherwise, where a Unicode dash arrives as three other characters
    # and the check passes whatever it is shown; CI runs this under Windows PowerShell.
    $lines = Get-Content -Path $file.FullName -Encoding UTF8
    $mermaid = $false
    $number = 0

    foreach ($line in $lines) {
        $number++

        # A fence closes on any fence, so a block opened as mermaid ends where the next one is.
        if ($file.Extension -eq '.md' -and $line.TrimStart().StartsWith('```')) {
            $mermaid = $line -match '^\s*```\s*mermaid\b'
            continue
        }

        foreach ($dash in $unicode) {
            if ($line.Contains($dash)) {
                $point = '{0:X4}' -f [int] $dash
                $problems += "${where}:${number}: U+$point is a dash. Rewrite the sentence."
                break
            }
        }

        if ($mermaid) {
            continue
        }

        # Space on both sides, where the start and the end of the line count as space. That is
        # what separates a dash doing the work of a comma from a flag or a decrement.
        if ($line -notmatch '(^|\s)--(\s|$)') {
            continue
        }

        # A line that runs one of these, which is one that starts with the command or calls it with
        # `&`. A sentence that only names one of them is prose, and a `--` in it is punctuation.
        if ($line -match '(^\s*|&\s*)(git|npx|dotnet|uv)\s') {
            continue
        }

        $problems += "${where}:${number}: ``--`` is being used as punctuation. Rewrite the sentence."
    }
}

if ($problems.Count -gt 0) {
    foreach ($problem in $problems) {
        Write-Host "  $problem" -ForegroundColor Red
    }

    throw "$($problems.Count) dash(es) used as punctuation. CONVENTIONS.md says to rewrite the sentence."
}

Write-Host "  $($files.Count) files, no dash used as punctuation."
