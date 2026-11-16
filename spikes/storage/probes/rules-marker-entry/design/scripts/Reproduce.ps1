# Every output of this probe that the design paper cites, in one command from the branch root:
#
#   pwsh -NoProfile -File spikes\storage\probes\rules-marker-entry\design\scripts\Reproduce.ps1
#
# Written for this branch. The probe's author ran these steps one at a time, and only Run.ps1 was a
# script: the build, the three summaries and the dumps that `RUN.md` compares were typed by hand,
# and are written down here in the order the timestamps of their outputs give. Nothing here
# measures anything; Run.ps1 and Rme.exe do, unchanged but for the paths `RUN.md` lists.
#
# PowerShell 7 rather than Windows PowerShell 5.1, because Run.ps1 sets
# `ProcessStartInfo.StandardInputEncoding`, which .NET Framework does not have.
$ErrorActionPreference = 'Stop'
$Root = Split-Path -Parent $PSScriptRoot
$Out = Join-Path $Root 'out'
$Work = Join-Path $Root 'work'
$Probe = Join-Path $Root 'probe\out\Rme.exe'

dotnet build -c Release -v q (Join-Path $Root 'probe\Rme.csproj')
if ($LASTEXITCODE -ne 0) { throw 'The probe did not build.' }

# Makes the databases in work\, merges and evaluates them, and writes merges.txt, cost.txt and
# dump-A.txt into out\.
& (Join-Path $PSScriptRoot 'Run.ps1')

function Invoke-Summary([string] $Script, [string] $From, [string] $To) {
    uv run --no-project python -I -X utf8 (Join-Path $PSScriptRoot $Script) (Join-Path $Out $From) > (Join-Path $Out $To)
    if ($LASTEXITCODE -ne 0) { throw "$Script failed." }
}

Invoke-Summary 'summarize.py' 'merges.txt' 'summary.txt'
Invoke-Summary 'compact.py' 'summary.txt' 'compact.txt'
Invoke-Summary 'check.py' 'merges.txt' 'check.txt'

# Every KPG entry of copy A as KeePassLib reads it, and the same after each KeePassXC release saved
# the file, each without the line naming the file, so that equal files mean equal entries.
& $Probe dump (Join-Path $Work 'base\A.kdbx') | Select-Object -Skip 1 |
    Set-Content -LiteralPath (Join-Path $Out 'dump-A-body.txt')
foreach ($v in '2.4.2', '2.6.6', '2.7.12', '2.8.0') {
    & $Probe dump (Join-Path $Work "xc-save-$v\saved.kdbx") | Select-Object -Skip 1 |
        Set-Content -LiteralPath (Join-Path $Out "dump-xcsave-$v.txt")
}
Write-Host "outputs in $Out"
