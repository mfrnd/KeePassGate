<#
.SYNOPSIS
    Checks that no `TODO(step N)` marker has outlived the step that was supposed to remove it.

.DESCRIPTION
    A commit may name something that does not exist yet, but only a design document may do it
    silently: describing what is to be built is what a design document is for. Everywhere else the
    reference carries a marker naming the step that will make it true, so a reader who meets the
    claim also meets the reason it is not true yet.

    A marker is a promise with an owner, and this is what collects on it. The build order in
    `DESIGN.md` §132 says which steps are done, so a marker naming a step that is already marked
    **Done** is a promise that was kept without the note being removed, or one that was quietly
    forgotten. Either way the reader is being told to expect something that has already arrived.

    The form is checked as well as the step, because a bare `TODO` names nobody and expires never.

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

# Which steps the build order says are finished. A document without one leaves every step open,
# which is the right way round: a marker cannot outlive a step nobody has claimed to have done.
$done = @{}
$last = 0
$design = Join-Path $Root 'DESIGN.md'
if (Test-Path $design) {
    $order = (Get-Content -Path $design -Raw) -split '## Build order', 2
    if ($order.Count -eq 2) {
        $entries = [regex]::Matches($order[1], '(?ms)^(\d+)\. \*\*(.*?)(?=^\d+\. \*\*|\z)')
        foreach ($entry in $entries) {
            $listed = [int] $entry.Groups[1].Value
            if ($listed -gt $last) {
                $last = $listed
            }

            if ($entry.Groups[2].Value -match '\*\*Done') {
                $done[$listed] = $true
            }
        }
    }
}

# Whether the build order has any step left in it. Once it has none, there is no next step for a
# marker to be waiting on.
$finished = ($last -gt 0 -and $done.Count -eq $last)

$problems = @()
foreach ($file in $files) {
    # A tool that looks for a token holds the token, so this one is not read by itself. Everywhere
    # else the convention is written about in backticks, and a mention inside them is documentation
    # rather than a promise: `TODO(step N)` in a sentence explains the form and owes nobody
    # anything, where the same text in running code is a note somebody left behind.
    if ($file.FullName.EndsWith([IO.Path]::Combine('tools', 'Markers.ps1'))) {
        continue
    }

    $number = 0
    foreach ($raw in Get-Content -Path $file.FullName) {
        $number++
        $line = [regex]::Replace($raw, '`[^`]*`', '')
        # Case sensitive, because the marker is spelled in capitals and prose is not: this
        # script's own step is called "todo markers".
        if ($line -cnotmatch 'TODO') {
            continue
        }

        $where = "$($file.FullName.Substring($Root.Length).TrimStart([IO.Path]::DirectorySeparatorChar)):$number"

        $marker = [regex]::Match($line, 'TODO\(step (\d+)\)')
        if (-not $marker.Success) {
            $problems += "${where}: a marker has to say which step resolves it, as ``TODO(step N)``"
            continue
        }

        # The question is whether the step this names has already been done. A marker naming a
        # step the build order has not listed yet is a promise about work nobody has planned in
        # detail, which is allowed, because the build order grows as the stack does and an early
        # commit cannot be expected to know how many steps there will turn out to be.
        #
        # **Until every step it lists is done.** A finished order does not grow, so a marker naming
        # a step beyond its last is a promise nobody has undertaken to keep, the note that names
        # nobody and expires never, in the form meant for one that does.
        $step = [int] $marker.Groups[1].Value
        if ($done.ContainsKey($step)) {
            $problems += "${where}: names step $step, which the build order marks done"
        }
        elseif ($finished -and $step -gt $last) {
            $problems += "${where}: names step $step, and the build order ends at step $last with every step done"
        }
    }
}

if ($problems.Count -gt 0) {
    foreach ($problem in $problems) {
        Write-Host "  $problem" -ForegroundColor Red
    }

    throw "$($problems.Count) todo marker(s) name no step, or a step the build order has finished with."
}

Write-Host "  $($files.Count) files, no marker outliving its step."
