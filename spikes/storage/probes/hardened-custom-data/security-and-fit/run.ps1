<#
.SYNOPSIS
    Runs the security critic's probe from the branch, every task the recorded output came from, as
    `RUN.md` describes.

.DESCRIPTION
    Added on the branch: the agent ran these steps by hand and kept no script. What it runs is
    unchanged. `scripts\Attack.ps1` is started once per task, in the order the scenarios stand in
    the recorded `attack.txt`: `fork` (S1), `import` (S3), `profiles` (S4), `prune` (S2), `xc41`
    (S5) and `xc41save` (S5b), every one appending to `out\attack.txt`. The databases go to
    `work\`. Both are emptied first, because the probe appends to its output file.

    Windows PowerShell 5.1, from the branch root:

        powershell.exe -NoProfile -ExecutionPolicy Bypass -File spikes\storage\probes\hardened-custom-data\security-and-fit\run.ps1
#>
$ErrorActionPreference = 'Stop'

$here = $PSScriptRoot
$out = Join-Path $here 'out'
$work = Join-Path $here 'work'
foreach ($directory in $out, $work) {
    if (Test-Path $directory) { Remove-Item $directory -Recurse -Force }
}
[void](New-Item -ItemType Directory -Force $out, $work)

$probe = Join-Path $here 'scripts\Attack.ps1'
$file = Join-Path $out 'attack.txt'

foreach ($task in 'fork', 'import', 'profiles', 'prune', 'xc41', 'xc41save') {
    $clock = [Diagnostics.Stopwatch]::StartNew()
    & powershell.exe -NoProfile -ExecutionPolicy Bypass -File $probe -Task $task -Work $work -Out $file
    if ($LASTEXITCODE -ne 0) { throw "$task exited $LASTEXITCODE." }
    Write-Host ([string]::Format([Globalization.CultureInfo]::InvariantCulture, '{0}: {1:0.0} s', $task, $clock.Elapsed.TotalSeconds))
}
