<#
.SYNOPSIS
    Builds the design's probe and runs every part of it that `design.md` quotes, into `out\`.

.DESCRIPTION
    Added on this branch. The probe is `probe\Kp.cs` and `probe\Store.cs`, one program, unchanged,
    with the three scripts that ran beside it: `probe\Run-Xc.ps1`, `probe\Run-Xc2.ps1` and
    `probe\Replace.ps1`. This runs them in the order and with the arguments the recorded run used,
    each into the file of `out\` whose recorded copy is in `recorded\`:

        store      Probe.exe scenarios work\scenarios            scenarios.txt
                   Probe.exe timing work\timing                  timing.txt
        keepass    Probe.exe make work\kp 200 64 2               make.txt
                   Probe.exe make work\kpfast 2 1 1              make-fast.txt
                   Probe.exe kpmerge work\kpfast                 kpmerge.txt
        keepassxc  probe\Run-Xc.ps1, over work\kp                xc.txt
                   probe\Run-Xc2.ps1, over work\kp               xc2.txt
        replace    probe\Replace.ps1                             replace.txt

    The `keepassxc` part needs the `keepass` part's `work\kp` and took about thirteen minutes here,
    the slow `make` about a minute and a half, and the others seconds. `-Part` runs some of them
    only, written with no space: `-Part store,keepass`.

    The three scripts run in PowerShell 7, `pwsh`, which is what ran them: the two KeePassXC
    scripts set `ProcessStartInfo.StandardInputEncoding`, which .NET Framework does not have, so
    they stop at once in Windows PowerShell 5.1. Everything else runs here, in Windows PowerShell
    5.1, from the branch root:

        powershell.exe -NoProfile -ExecutionPolicy Bypass -File spikes\storage\probes\outside-file\design\run.ps1
#>
param([string[]] $Part = @('store', 'keepass', 'keepassxc', 'replace'))

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

# Given through `-File`, a list arrives as one string, so `-Part store,keepass` is split here.
$Part = @($Part | ForEach-Object { $_ -split '[,\s]+' } | Where-Object { $_ })
foreach ($p in $Part) {
    if ($p -notin 'store', 'keepass', 'keepassxc', 'replace') { throw "There is no part '$p': store, keepass, keepassxc, replace." }
}

$root = (Resolve-Path (Join-Path $PSScriptRoot '..\..\..\..\..')).Path
. (Join-Path $root 'tools\KeePass.ps1')
. (Join-Path $root 'tools\KeePassXC.ps1')
$null = Get-KeePassExe

& dotnet build (Join-Path $PSScriptRoot 'probe\Probe.csproj') -c Release -v quiet
if ($LASTEXITCODE -ne 0) { throw "dotnet build exited with $LASTEXITCODE" }
$exe = Join-Path $PSScriptRoot 'probe\out\Probe.exe'

$out = Join-Path $PSScriptRoot 'out'
$work = Join-Path $PSScriptRoot 'work'
New-Item -ItemType Directory -Force $out, $work | Out-Null
$utf8 = New-Object System.Text.UTF8Encoding $false

function Invoke-Probe([string] $File, [string[]] $Arguments) {
    $clock = [Diagnostics.Stopwatch]::StartNew()
    $lines = @(& $exe @Arguments)
    if ($LASTEXITCODE -ne 0) { throw "Probe.exe $($Arguments[0]) exited with $LASTEXITCODE" }
    [IO.File]::WriteAllLines((Join-Path $out $File), [string[]] $lines, $utf8)
    '{0}: {1:0.0} s' -f $File, $clock.Elapsed.TotalSeconds
}

function Invoke-Pwsh([string] $Script, [string] $File) {
    $clock = [Diagnostics.Stopwatch]::StartNew()
    $path = (Join-Path $PSScriptRoot "probe\$Script") -replace "'", "''"
    if ($File) {
        $target = (Join-Path $out $File) -replace "'", "''"
        & pwsh -NoProfile -ExecutionPolicy Bypass -Command "& '$path' 2>&1 | Tee-Object -FilePath '$target'"
    }
    else {
        & pwsh -NoProfile -ExecutionPolicy Bypass -File $path
    }
    if ($LASTEXITCODE -ne 0) { throw "$Script exited with $LASTEXITCODE" }
    '{0}: {1:0.0} s' -f $Script, $clock.Elapsed.TotalSeconds
}

function Clear-Work([string] $Name) {
    $directory = Join-Path $work $Name
    if (Test-Path $directory) { Remove-Item -Recurse -Force $directory }
    $directory
}

if ($Part -contains 'store') {
    Invoke-Probe 'scenarios.txt' @('scenarios', (Clear-Work 'scenarios'))
    Invoke-Probe 'timing.txt' @('timing', (Clear-Work 'timing'))
}

if ($Part -contains 'keepass') {
    Invoke-Probe 'make.txt' @('make', (Clear-Work 'kp'), '200', '64', '2')
    Invoke-Probe 'make-fast.txt' @('make', (Clear-Work 'kpfast'), '2', '1', '1')
    Invoke-Probe 'kpmerge.txt' @('kpmerge', (Join-Path $work 'kpfast'))
}

if ($Part -contains 'keepassxc') {
    if (-not (Test-Path (Join-Path $work 'kp\anchored-notimes.kdbx'))) { throw "work\kp is not there: run the keepass part first." }
    foreach ($release in '2.4.2', '2.6.6', '2.7.12', '2.8.0') { $null = Get-KeePassXCCli -Version $release }
    Invoke-Pwsh 'Run-Xc.ps1' 'xc.txt'
    Invoke-Pwsh 'Run-Xc2.ps1' 'xc2.txt'
}

if ($Part -contains 'replace') {
    Invoke-Pwsh 'Replace.ps1' $null
}
