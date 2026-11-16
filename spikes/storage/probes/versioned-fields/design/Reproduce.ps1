# Added on this branch to run the design's probe from the branch root with one command, in Windows
# PowerShell 5.1. It measures nothing itself. In the order the recorded outputs were made in, it
# builds `probe\Vf.csproj` into `probe\out\`; runs `scripts\Run.ps1`, which writes `out\merges.txt`,
# `out\dumps.txt` and `out\walk.txt`; condenses `out\merges.txt` into `out\summary.txt` with
# `scripts\summarize.py`; and then runs the three commands the record holds the output of and the
# agent left no script for, reconstructed from that output and from the directories its run left:
# `Vf.exe marks` into `out\marks.txt`; `Vf.exe make` into `work\recheck\`, then `Vf.exe newids` of
# `R-base` into `R-A2` there and `Vf.exe dump` of the result, under the heading the record carries,
# into `out\newids-detail.txt`; and `Vf.exe sentinel` into `work\sentinel\` and `out\sentinel.txt`.
# Everything it writes is gitignored: `probe\out\`, `probe\obj\`, `work\` and `out\`.
#
# `scripts\Run.ps1` runs in PowerShell 7 and not in Windows PowerShell: it sets
# `ProcessStartInfo.StandardInputEncoding`, which .NET Framework does not have, so it cannot have
# been recorded in 5.1. What it measures runs in `Vf.exe` and `keepassxc-cli` either way.
# cspell:ignore nologo
$ErrorActionPreference = 'Stop'
$Here = $PSScriptRoot
$Probe = Join-Path $Here 'probe\out\Vf.exe'
$Out = Join-Path $Here 'out'
$Work = Join-Path $Here 'work'

& dotnet build (Join-Path $Here 'probe\Vf.csproj') -c Release -o (Join-Path $Here 'probe\out') --nologo -v q
if ($LASTEXITCODE -ne 0) { throw 'dotnet build failed' }

& pwsh.exe -NoProfile -ExecutionPolicy Bypass -File (Join-Path $Here 'scripts\Run.ps1')
if ($LASTEXITCODE -ne 0) { throw 'scripts\Run.ps1 failed' }

$summary = & uv run --no-project python -I -X utf8 (Join-Path $Here 'scripts\summarize.py') (Join-Path $Out 'merges.txt')
if ($LASTEXITCODE -ne 0) { throw 'scripts\summarize.py failed' }
[System.IO.File]::WriteAllLines((Join-Path $Out 'summary.txt'), [string[]] $summary)

function Invoke-Probe([string[]] $Arguments) {
    $lines = & $Probe @Arguments
    if ($LASTEXITCODE -ne 0) { throw "Vf.exe $($Arguments -join ' ') failed" }
    return , ([string[]] @($lines))
}

[System.IO.File]::WriteAllLines((Join-Path $Out 'marks.txt'), (Invoke-Probe @('marks')))

$recheck = Join-Path $Work 'recheck'
if (Test-Path -LiteralPath $recheck) { Remove-Item -LiteralPath $recheck -Recurse -Force }
Invoke-Probe @('make', $recheck) | Out-Null
$imported = Join-Path $recheck 'newids.kdbx'
$detail = New-Object System.Collections.Generic.List[string]
$detail.AddRange((Invoke-Probe @('newids', (Join-Path $recheck 'R-A2.kdbx'), (Join-Path $recheck 'R-base.kdbx'), $imported)))
$detail.Add('')
$detail.Add('## the file afterwards')
$detail.AddRange((Invoke-Probe @('dump', $imported)))
[System.IO.File]::WriteAllLines((Join-Path $Out 'newids-detail.txt'), $detail)

$sentinel = Join-Path $Work 'sentinel'
if (Test-Path -LiteralPath $sentinel) { Remove-Item -LiteralPath $sentinel -Recurse -Force }
[System.IO.File]::WriteAllLines((Join-Path $Out 'sentinel.txt'), (Invoke-Probe @('sentinel', $sentinel)))
