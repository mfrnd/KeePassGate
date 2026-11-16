<#
.SYNOPSIS
    The design's counts over the product's tests, asked of the product's commit.

.DESCRIPTION
    Added on the branch. The agent counted with GNU `grep -r` in a checkout of the product at
    `a42e38aa` and kept the console output only, which `recorded\search.txt` holds as the session
    transcript has it. This asks the same patterns of the same commit through `git grep`, so that
    no checkout is needed: `grep -rn PATTERN tests --include=*.cs | wc -l` becomes the number of
    lines `git grep -n` finds in `tests/*.cs`, and `grep -rl ... | wc -l` the number of files
    `git grep -l` finds. GNU `grep` is case sensitive and so is this; `-E` stays `-E`, and the
    basic expressions are written in their extended form (`\(` for a literal parenthesis).

    Windows PowerShell 5.1, from the branch root:

        powershell.exe -NoProfile -ExecutionPolicy Bypass -File spikes\storage\probes\grant-only\design\scripts\search.ps1
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

function Count([string] $Label, [string] $Pattern, [string] $Paths) {
    $n = @(Lines @('-n', '-E', '-e', $Pattern, $commit, '--', $Paths)).Count
    $f = @(Lines @('-l', '-E', '-e', $Pattern, $commit, '--', $Paths)).Count
    "{0}: {1} lines in {2} files" -f $Label, $n, $f
}

$report = New-Object System.Collections.Generic.List[string]
$report.Add("product commit $commit, git grep over tests/*.cs, case sensitive")
$report.Add((Count 'Rules.Sealing(' 'Rules\.Sealing\(' 'tests/*.cs'))
$report.Add((Count 'Rules.Granting(' 'Rules\.Granting\(' 'tests/*.cs'))
$report.Add((Count 'empty masks' '\("[a-z0-9_-]+", ?("" *|"\.{8}")\)' 'tests/*.cs'))
$report.Add((Count 'test methods naming a narrowing' 'public (async Task|void) [A-Za-z_]*(seal|Seal|nearest|Nearest|takes_everything_away|grants_less|narrow|Narrow|empty_rule|Empty_rule|refuses_rather|struck|Struck|closes|stops_the_walk)[A-Za-z_]*\(' 'tests/*.cs'))
$report.Add((Count 'test attributes [Test or [TestCase' '\[(Test|TestCase)' 'tests/*.cs'))
# Added on the branch, with nothing recorded beside it: the same name pattern over the one file the
# design counts "7 of 30" in, which the agent judged by reading the list below.
$report.Add((Count 'of those, in AclResolverTests.cs (added check)' 'public (async Task|void) [A-Za-z_]*(seal|Seal|nearest|Nearest|takes_everything_away|grants_less|narrow|Narrow|empty_rule|Empty_rule|refuses_rather|struck|Struck|closes|stops_the_walk)[A-Za-z_]*\(' 'tests/KeePassGate.Tests/AclResolverTests.cs'))
$methods = @(Lines @('-n', '-e', 'public void ', $commit, '--', 'tests/KeePassGate.Tests/AclResolverTests.cs'))
$report.Add("public void in AclResolverTests.cs: $($methods.Count)")
foreach ($m in $methods) { $report.Add('    ' + ($m -replace '^[^:]+:[^:]+:', '' -replace 'public void ', '').Trim()) }
[IO.File]::WriteAllLines((Join-Path $out 'search.txt'), $report)
$report
