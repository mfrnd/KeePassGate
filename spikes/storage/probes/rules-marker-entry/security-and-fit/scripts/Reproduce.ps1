# The output of this probe that the security and fit critique cites, in one command from the
# branch root:
#
#   pwsh -NoProfile -File spikes\storage\probes\rules-marker-entry\security-and-fit\scripts\Reproduce.ps1
#
# Written for this branch. The probe's author built it and ran it by hand, with the work directory
# as its one argument and its output redirected to collision.txt; this does the same. Nothing here
# measures anything; Rmc.exe does, unchanged but for the reference `RUN.md` lists.
$ErrorActionPreference = 'Stop'
$Root = Split-Path -Parent $PSScriptRoot
$Out = Join-Path $Root 'out'
$Probe = Join-Path $Root 'probe\out\Rmc.exe'

dotnet build -c Release -v q (Join-Path $Root 'probe\Rmc.csproj')
if ($LASTEXITCODE -ne 0) { throw 'The probe did not build.' }
New-Item -ItemType Directory -Force -Path $Out | Out-Null

& $Probe (Join-Path $Root 'work') | Set-Content -LiteralPath (Join-Path $Out 'collision.txt')
if ($LASTEXITCODE -ne 0) { throw 'Rmc.exe failed.' }
Write-Host "outputs in $Out"
