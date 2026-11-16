<#
.SYNOPSIS
    Builds the design's probe and runs every part of it that the design quotes, into `out\`.

.DESCRIPTION
    Added on this branch; the probe is `probe\Program.cs`, unchanged. It takes a work directory, a
    mode and an output file, and for `xc` a KeePassXC release and its `keepassxc-cli.exe`. The four
    runs here are the ones whose output the design quotes: `keepass` writes `keepass.txt`, `cost`
    writes `cost.txt`, and `xc` writes `xc-<release>.txt` for each of 2.4.2, 2.6.6, 2.7.12 and
    2.8.0. Databases are made under `work\`, which the branch ignores and which this clears first.

    Run from the branch root in Windows PowerShell 5.1:

        powershell.exe -NoProfile -ExecutionPolicy Bypass -File spikes\storage\probes\rules-on-node\design\run.ps1
#>
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$root = (Resolve-Path (Join-Path $PSScriptRoot '..\..\..\..\..')).Path
. (Join-Path $root 'tools\KeePass.ps1')
. (Join-Path $root 'tools\KeePassXC.ps1')
$null = Get-KeePassExe

& dotnet build (Join-Path $PSScriptRoot 'probe\Rules.csproj') -c Release -v quiet
if ($LASTEXITCODE -ne 0) { throw "dotnet build exited with $LASTEXITCODE" }
$exe = Join-Path $PSScriptRoot 'probe\bin\Release\net48\Rules.exe'

$out = Join-Path $PSScriptRoot 'out'
$work = Join-Path $PSScriptRoot 'work'
if (Test-Path $work) { Remove-Item -Recurse -Force $work }
New-Item -ItemType Directory -Force $out | Out-Null

function Invoke-Probe([string] $Name, [string[]] $Arguments) {
    $clock = [Diagnostics.Stopwatch]::StartNew()
    & $exe @Arguments
    if ($LASTEXITCODE -ne 0) { throw "$Name exited with $LASTEXITCODE" }
    '{0}: {1:0.0} s' -f $Name, $clock.Elapsed.TotalSeconds
}

Invoke-Probe 'keepass' @((Join-Path $work 'kp'), 'keepass', (Join-Path $out 'keepass.txt'))
Invoke-Probe 'cost' @((Join-Path $work 'cost'), 'cost', (Join-Path $out 'cost.txt'))
foreach ($release in '2.4.2', '2.6.6', '2.7.12', '2.8.0') {
    $cli = Get-KeePassXCCli -Version $release
    Invoke-Probe "xc $release" @((Join-Path $work 'xc'), 'xc', (Join-Path $out "xc-$release.txt"), $release, $cli)
}
