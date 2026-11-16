# Every output of this probe that the survival critique cites, in one command from the branch root:
#
#   pwsh -NoProfile -File spikes\storage\probes\rules-marker-entry\survival\scripts\Reproduce.ps1
#
# Written for this branch. The probe's author ran these steps one at a time, and only Run.ps1 was a
# script: the build and the three runs of Rms.exe that wrote s1-s3.txt, s5-s9.txt and s8.txt were
# typed by hand, and are written down here in the order the timestamps of their outputs give. The
# first of them has to come before Run.ps1, which reads the databases s1 leaves in work\s1. Nothing
# here measures anything; Run.ps1 and Rms.exe do, unchanged but for the paths `RUN.md` lists.
#
# PowerShell 7 rather than Windows PowerShell 5.1, because Run.ps1 sets
# `ProcessStartInfo.StandardInputEncoding`, which .NET Framework does not have.
$ErrorActionPreference = 'Stop'
$Root = Split-Path -Parent $PSScriptRoot
$Out = Join-Path $Root 'out'
$Work = Join-Path $Root 'work'
$Probe = Join-Path $Root 'probe\out\Rms.exe'

dotnet build -c Release -v q (Join-Path $Root 'probe\Rms.csproj')
if ($LASTEXITCODE -ne 0) { throw 'The probe did not build.' }
New-Item -ItemType Directory -Force -Path $Out | Out-Null

function Invoke-Scenarios([string[]] $Names, [string] $To) {
    $lines = foreach ($name in $Names) {
        & $Probe $name (Join-Path $Work $name)
        if ($LASTEXITCODE -ne 0) { throw "Rms.exe $name failed." }
    }
    $lines | Set-Content -LiteralPath (Join-Path $Out $To)
}

Invoke-Scenarios 's1', 's2', 's3' 's1-s3.txt'
Invoke-Scenarios 's5', 's7', 's8', 's9' 's5-s9.txt'
Invoke-Scenarios 's8' 's8.txt'

# s4 and s6, and the KeePassXC saves of the s1 files; writes run.txt.
& (Join-Path $PSScriptRoot 'Run.ps1') | Out-Null
Write-Host "outputs in $Out"
