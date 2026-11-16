<#
.SYNOPSIS
    Runs the survival critic's probe from the branch, every task the recorded output came from, as
    `RUN.md` describes.

.DESCRIPTION
    Added on the branch: the agent ran these steps by hand and kept no script. What it runs is
    unchanged. `scripts\Chain.ps1` is started once per task, as its own first lines show: `make`
    (the bases C and P), `chain` (the KeePassXC merges, the KeePassXC save and the seven KeePassLib
    Synchronize cells over them), `branch` (two copies editing one rule) and `xctimes` (a 4.1 file
    saved by KeePassXC 2.8.0). Each writes the file of its name into `out\`, the name its recorded
    twin has in `recorded\`, and the databases go to `work\`. Both are emptied first, because the
    probe appends to its output file.

    Windows PowerShell 5.1, from the branch root:

        powershell.exe -NoProfile -ExecutionPolicy Bypass -File spikes\storage\probes\hardened-custom-data\survival\run.ps1
#>
$ErrorActionPreference = 'Stop'

$here = $PSScriptRoot
$out = Join-Path $here 'out'
$work = Join-Path $here 'work'
foreach ($directory in $out, $work) {
    if (Test-Path $directory) { Remove-Item $directory -Recurse -Force }
}
[void](New-Item -ItemType Directory -Force $out, $work)

$probe = Join-Path $here 'scripts\Chain.ps1'

foreach ($task in 'make', 'chain', 'branch', 'xctimes') {
    $clock = [Diagnostics.Stopwatch]::StartNew()
    & powershell.exe -NoProfile -ExecutionPolicy Bypass -File $probe -Task $task -Work $work -Out (Join-Path $out "$task.txt")
    if ($LASTEXITCODE -ne 0) { throw "$task exited $LASTEXITCODE." }
    Write-Host ([string]::Format([Globalization.CultureInfo]::InvariantCulture, '{0}: {1:0.0} s', $task, $clock.Elapsed.TotalSeconds))
}
