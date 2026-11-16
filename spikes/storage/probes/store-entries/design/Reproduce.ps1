# Added on this branch to run the design's probe from the branch root with one command, in Windows
# PowerShell 5.1. It measures nothing itself: it builds `probe\Sep.csproj` into `probe\out\`, runs
# `scripts\Run.ps1`, which writes `out\merges.txt`, `out\walk.txt` and `out\uuids.txt`, and then
# condenses `out\merges.txt` into `out\summary.txt` with `scripts\summarize.py`, which is the order
# the recorded outputs were made in. Everything it writes is gitignored: `probe\out\`, `probe\obj\`,
# `work\` and `out\`.
#
# `scripts\Run.ps1` runs in PowerShell 7 and not in Windows PowerShell: it sets
# `ProcessStartInfo.StandardInputEncoding`, which .NET Framework does not have, so it cannot have
# been recorded in 5.1. What it measures runs in `Sep.exe` and `keepassxc-cli` either way.
# cspell:ignore nologo
$ErrorActionPreference = 'Stop'
$Here = $PSScriptRoot

& dotnet build (Join-Path $Here 'probe\Sep.csproj') -c Release -o (Join-Path $Here 'probe\out') --nologo -v q
if ($LASTEXITCODE -ne 0) { throw 'dotnet build failed' }

& pwsh.exe -NoProfile -ExecutionPolicy Bypass -File (Join-Path $Here 'scripts\Run.ps1')
if ($LASTEXITCODE -ne 0) { throw 'scripts\Run.ps1 failed' }

$summary = & uv run --no-project python -I -X utf8 (Join-Path $Here 'scripts\summarize.py') (Join-Path $Here 'out\merges.txt')
if ($LASTEXITCODE -ne 0) { throw 'scripts\summarize.py failed' }
[System.IO.File]::WriteAllLines((Join-Path $Here 'out\summary.txt'), [string[]] $summary)
