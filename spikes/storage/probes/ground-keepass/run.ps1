<#
.SYNOPSIS
    Builds the ground report's probe and runs its thirteen experiments, X1 to X13, into `out\`.

.DESCRIPTION
    Added on this branch; the probe is `probe\Program.cs`, unchanged. It takes an empty work
    directory, the name of one experiment and an output file, which is how the recorded runs were
    made: one work directory for each experiment, `work-x1` to `work-x13` in the directory the
    report was written in, and here `work\x1` to `work\x13`, which the branch ignores and which this
    clears first. Each experiment writes `out\<name>.txt`, the name its recorded copy has under
    `recorded\`.

    -Experiment takes a comma separated list, `x2,x5` for instance, and runs only those.

    Run from the branch root in Windows PowerShell 5.1:

        powershell.exe -NoProfile -ExecutionPolicy Bypass -File spikes\storage\probes\ground-keepass\run.ps1
#>
param([string] $Experiment = 'x1,x2,x3,x4,x5,x6,x7,x8,x9,x10,x11,x12,x13')

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$root = (Resolve-Path (Join-Path $PSScriptRoot '..\..\..\..')).Path
. (Join-Path $root 'tools\KeePass.ps1')
$null = Get-KeePassExe

& dotnet build (Join-Path $PSScriptRoot 'probe\Probe.csproj') -c Release -v quiet
if ($LASTEXITCODE -ne 0) { throw "dotnet build exited with $LASTEXITCODE" }
$exe = Join-Path $PSScriptRoot 'probe\bin\Release\net48\Probe.exe'

$out = Join-Path $PSScriptRoot 'out'
$work = Join-Path $PSScriptRoot 'work'
if (Test-Path $work) { Remove-Item -Recurse -Force $work }
New-Item -ItemType Directory -Force $out | Out-Null

foreach ($name in $Experiment -split ',') {
    $name = $name.Trim()
    $clock = [Diagnostics.Stopwatch]::StartNew()
    & $exe (Join-Path $work $name) $name (Join-Path $out "$name.txt")
    if ($LASTEXITCODE -ne 0) { throw "$name exited with $LASTEXITCODE" }
    '{0}: {1:0.0} s' -f $name, $clock.Elapsed.TotalSeconds
}
