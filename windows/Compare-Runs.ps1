<#
.SYNOPSIS
    Two files of probe lines side by side, as a Markdown table of every measurement, marking the
    ones whose values differ.

.DESCRIPTION
    A value that holds a time, a random digest or a byte count that changes from run to run is
    still listed, and differs on every pair of runs; reading which differences matter is the
    point of `RESULTS.md`, not of this.

.EXAMPLE
    .\windows\Compare-Runs.ps1 -Left results\net48.txt -Right results\mono.txt -LeftName '.NET Framework 4.8' -RightName 'Mono'
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory)] [string] $Left,
    [Parameter(Mandatory)] [string] $Right,
    [string] $LeftName = 'left',
    [string] $RightName = 'right'
)

$ErrorActionPreference = 'Stop'

function Read-Lines([string] $Path) {
    $values = [ordered]@{}
    foreach ($line in [IO.File]::ReadAllLines((Resolve-Path -LiteralPath $Path))) {
        $at = $line.IndexOf(' = ')
        if ($at -gt 0) {
            $values[$line.Substring(0, $at)] = $line.Substring($at + 3)
        }
    }

    $values
}

function Cell([string] $Text) {
    if ($null -eq $Text) {
        return '(absent)'
    }

    '`' + $Text.Replace('|', '/').Replace('`', "'") + '`'
}

$a = Read-Lines $Left
$b = Read-Lines $Right
$keys = New-Object System.Collections.Generic.List[string]
$keys.AddRange([string[]]@($a.Keys))
foreach ($key in $b.Keys) {
    if (-not $keys.Contains($key)) {
        $keys.Add($key)
    }
}

"| measurement | $LeftName | $RightName | |"
'| --- | --- | --- | --- |'
foreach ($key in $keys) {
    $differs = if ($a[$key] -ne $b[$key]) { 'differs' } else { '' }
    "| $key | $(Cell $a[$key]) | $(Cell $b[$key]) | $differs |"
}
