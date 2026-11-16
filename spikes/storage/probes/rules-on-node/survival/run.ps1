<#
.SYNOPSIS
    Builds the survival critic's probe and runs it, into `out\survival.txt`.

.DESCRIPTION
    Added on this branch; the probe is `probe\Program.cs`, unchanged. It takes a work directory and
    an output file, and runs its cases C1 to C7 over KeePassLib alone. Databases are made under
    `work\`, which the branch ignores and which this clears first.

    Run from the branch root in Windows PowerShell 5.1:

        powershell.exe -NoProfile -ExecutionPolicy Bypass -File spikes\storage\probes\rules-on-node\survival\run.ps1
#>
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$root = (Resolve-Path (Join-Path $PSScriptRoot '..\..\..\..\..')).Path
. (Join-Path $root 'tools\KeePass.ps1')
$null = Get-KeePassExe

& dotnet build (Join-Path $PSScriptRoot 'probe\Survival.csproj') -c Release -v quiet
if ($LASTEXITCODE -ne 0) { throw "dotnet build exited with $LASTEXITCODE" }
$exe = Join-Path $PSScriptRoot 'probe\bin\Release\net48\Survival.exe'

$out = Join-Path $PSScriptRoot 'out'
$work = Join-Path $PSScriptRoot 'work'
if (Test-Path $work) { Remove-Item -Recurse -Force $work }
New-Item -ItemType Directory -Force $out | Out-Null

$clock = [Diagnostics.Stopwatch]::StartNew()
& $exe $work (Join-Path $out 'survival.txt')
if ($LASTEXITCODE -ne 0) { throw "the probe exited with $LASTEXITCODE" }
'survival: {0:0.0} s' -f $clock.Elapsed.TotalSeconds
