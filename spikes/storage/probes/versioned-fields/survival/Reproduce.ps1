# Added on this branch to run the survival critique's probe from the branch root with one
# command, in Windows PowerShell 5.1. It measures nothing itself. In the order the recorded
# outputs were made in, it builds `probe\Vfs.csproj` into `probe\out\`; runs `scripts\Run.ps1`,
# which writes `out\h5.txt`, `out\newids.txt`, `out\gone.txt` and `out\chain.txt`; and then makes
# `out\refined.txt`, which the critic recorded without a script. That last step is reconstructed
# from the record: for each of the twelve `work\h5-*` cells and the two
# `work\gone\xc<release>-B-into-A-pressed` cells, in the record's order, a heading
# `## <cell>, refined condition` and the lines of `Vfs.exe fold3` over the cell's result that name
# the design's history rule or the rule on entry X.
# Everything it writes is gitignored: `probe\out\`, `probe\obj\`, `work\` and `out\`.
#
# `scripts\Run.ps1` runs in PowerShell 7 and not in Windows PowerShell: it sets
# `ProcessStartInfo.StandardInputEncoding`, which .NET Framework does not have, so it cannot have
# been recorded in 5.1. What it measures runs in `Vfs.exe` and `keepassxc-cli` either way.
# cspell:ignore nologo
$ErrorActionPreference = 'Stop'
$Here = $PSScriptRoot
$Probe = Join-Path $Here 'probe\out\Vfs.exe'
$Work = Join-Path $Here 'work'

& dotnet build (Join-Path $Here 'probe\Vfs.csproj') -c Release -o (Join-Path $Here 'probe\out') --nologo -v q
if ($LASTEXITCODE -ne 0) { throw 'dotnet build failed' }

& pwsh.exe -NoProfile -ExecutionPolicy Bypass -File (Join-Path $Here 'scripts\Run.ps1')
if ($LASTEXITCODE -ne 0) { throw 'scripts\Run.ps1 failed' }

$cells = [string[]] @(Get-ChildItem -LiteralPath $Work -Directory -Filter 'h5-*' | ForEach-Object { $_.Name })
[System.Array]::Sort($cells, [System.StringComparer]::Ordinal)
$cells += @('gone\xc2.4.2-B-into-A-pressed', 'gone\xc2.8.0-B-into-A-pressed')
$refined = New-Object System.Collections.Generic.List[string]
foreach ($cell in $cells) {
    $dir = Join-Path $Work $cell
    $file = Join-Path $dir 'result.kdbx'
    if (-not (Test-Path -LiteralPath $file)) { $file = Join-Path $dir 'target.kdbx' }
    $lines = & $Probe fold3 $file
    if ($LASTEXITCODE -ne 0) { throw "Vfs.exe fold3 failed: $cell" }
    $refined.Add('## ' + $cell.Replace('\', '/') + ', refined condition')
    foreach ($line in $lines) { if ($line -match "history rule|rule on entry X") { $refined.Add($line) } }
}
[System.IO.File]::WriteAllLines((Join-Path $Here 'out\refined.txt'), $refined)
