<#
.SYNOPSIS
    Builds the security critic's probe and runs it, then the collation script, into `out\`.

.DESCRIPTION
    Added on this branch; the probe is `probe\Attack.cs` and the script `collation.ps1`, both
    unchanged. The probe takes a work directory, an output file, and for its scenario S2 a
    KeePassXC release and its `keepassxc-cli.exe`, here 2.8.0 as in the recorded run. It writes
    `attack.txt`, and each `keepassxc-cli` run's console output to `cli.txt` in that cell's work
    directory. Databases are made under `work\`, which the branch ignores and which this clears
    first. The collation script prints to the console; this writes what it prints to
    `out\collation.txt`, as UTF-8 without a byte order mark, which is how the recorded copy is
    stored. Both run in this Windows PowerShell's .NET Framework, which is what the script measures.

    Run from the branch root in Windows PowerShell 5.1:

        powershell.exe -NoProfile -ExecutionPolicy Bypass -File spikes\storage\probes\rules-on-node\security-and-fit\run.ps1
#>
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$root = (Resolve-Path (Join-Path $PSScriptRoot '..\..\..\..\..')).Path
. (Join-Path $root 'tools\KeePass.ps1')
. (Join-Path $root 'tools\KeePassXC.ps1')
$null = Get-KeePassExe

& dotnet build (Join-Path $PSScriptRoot 'probe\Attack.csproj') -c Release -v quiet
if ($LASTEXITCODE -ne 0) { throw "dotnet build exited with $LASTEXITCODE" }
$exe = Join-Path $PSScriptRoot 'probe\bin\Release\net48\Attack.exe'

$out = Join-Path $PSScriptRoot 'out'
$work = Join-Path $PSScriptRoot 'work'
if (Test-Path $work) { Remove-Item -Recurse -Force $work }
New-Item -ItemType Directory -Force $out | Out-Null

$cli = Get-KeePassXCCli -Version '2.8.0'
$clock = [Diagnostics.Stopwatch]::StartNew()
& $exe $work (Join-Path $out 'attack.txt') '2.8.0' $cli
if ($LASTEXITCODE -ne 0) { throw "the probe exited with $LASTEXITCODE" }
'attack: {0:0.0} s' -f $clock.Elapsed.TotalSeconds

$clock = [Diagnostics.Stopwatch]::StartNew()
$lines = [string[]] @(& (Join-Path $PSScriptRoot 'collation.ps1'))
[IO.File]::WriteAllLines((Join-Path $out 'collation.txt'), $lines, (New-Object Text.UTF8Encoding($false)))
'collation: {0:0.0} s' -f $clock.Elapsed.TotalSeconds
