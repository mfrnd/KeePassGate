<#
.SYNOPSIS
    The security critique's counts over the product's tests, asked of the product's commit.

.DESCRIPTION
    Added on the branch. The agent counted with `Select-String` in a checkout of the product at
    `a42e38aa` and kept the console output only, which `recorded\search.txt` holds as the session
    transcript has it. This asks the same patterns of the same commit through `git grep`, so that
    no checkout is needed. `Select-String` matches without case and counts lines, so this passes
    `-i` and counts the lines `git grep -n` finds; its patterns are .NET expressions, so they go
    to `git grep -P`; `-SimpleMatch` becomes `-F`. The files are the ones the agent named:
    `*.cs` and `*.py` under `tests`, `*.cs` alone, `*.py` under `tests/pykeepassgate`.

    Windows PowerShell 5.1, from the branch root:

        powershell.exe -NoProfile -ExecutionPolicy Bypass -File spikes\storage\probes\grant-only\security-and-fit\scripts\search.ps1
#>
$ErrorActionPreference = 'Stop'

$root = (Resolve-Path (Join-Path $PSScriptRoot '..\..\..\..\..\..')).Path
. (Join-Path $root 'tools\Product.ps1')
$commit = Get-ProductCommit
$out = Join-Path (Split-Path -Parent $PSScriptRoot) 'out'
[void](New-Item -ItemType Directory -Force $out)

# One argument as the Windows command line parses it back: a quote inside is escaped, and so is
# every backslash before a quote. `Invoke-ProductGit` quotes without escaping, which a pattern
# holding a quote does not survive.
function Quote([string] $Text) {
    '"' + ($Text -replace '(\\*)"', '$1$1\"' -replace '(\\+)$', '$1$1') + '"'
}

# `git grep` over the branch's repository, which holds the product's commit; its output as lines.
function Lines([string[]] $Arguments) {
    $info = New-Object System.Diagnostics.ProcessStartInfo
    $info.FileName = 'git'
    $info.Arguments = (@('-C', $root, 'grep') + $Arguments | ForEach-Object { Quote $_ }) -join ' '
    $info.UseShellExecute = $false
    $info.CreateNoWindow = $true
    $info.RedirectStandardOutput = $true
    $info.RedirectStandardError = $true
    $info.StandardOutputEncoding = New-Object System.Text.UTF8Encoding($false)
    $process = [System.Diagnostics.Process]::Start($info)
    try {
        $errors = $process.StandardError.ReadToEndAsync()
        $text = $process.StandardOutput.ReadToEnd()
        $process.WaitForExit()
        if ($process.ExitCode -gt 1) { throw "git grep failed: $($errors.Result)" }
    }
    finally { $process.Dispose() }
    @($text -split "`n" | Where-Object { $_ -ne '' })
}

function Count([string] $Label, [string] $Kind, [string] $Pattern, [string[]] $Paths) {
    $n = @(Lines (@('-n', '-i', $Kind, '-e', $Pattern, $commit, '--') + $Paths)).Count
    $f = @(Lines (@('-l', '-i', $Kind, '-e', $Pattern, $commit, '--') + $Paths)).Count
    "{0}: {1} lines in {2} files" -f $Label, $n, $f
}

$report = New-Object System.Collections.Generic.List[string]
$report.Add("product commit $commit, git grep without case, lines counted")
$report.Add((Count 'Rules.Sealing( in tests *.cs, *.py' '-P' 'Rules\.Sealing\(' @('tests/*.cs', 'tests/*.py')))
$report.Add((Count 'Rules.Granting( in tests *.cs, *.py' '-P' 'Rules\.Granting\(' @('tests/*.cs', 'tests/*.py')))
$report.Add((Count 'AclDefault.Deny in tests *.cs' '-F' 'AclDefault.Deny' @('tests/*.cs')))
foreach ($l in (Lines @('-n', '-i', '-F', '-e', 'AclDefault.Deny', $commit, '--', 'tests/*.cs'))) {
    $report.Add('    ' + (($l -split ':')[1..2] -join ':'))
}
$report.Add((Count 'Access as one mask in tests/pykeepassgate *.py' '-P' '\.allows\(|Access\.of|"grants"' @('tests/pykeepassgate/*.py')))
$report.Add('InheritedMask in tests and src/KeePassGate *.cs, by file:')
$hits = Lines @('-c', '-i', '-F', '-e', 'InheritedMask', $commit, '--', 'tests/*.cs', 'src/KeePassGate/*.cs')
foreach ($h in $hits) { $report.Add('    ' + (($h -split ':')[1..2] -join ' ')) }
[IO.File]::WriteAllLines((Join-Path $out 'search.txt'), $report)
$report
