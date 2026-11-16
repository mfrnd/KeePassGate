<#
.SYNOPSIS
    Runs the security critic's two probes from the branch, as `RUN.md` describes.

.DESCRIPTION
    Added on the branch: the agent started the two scripts by hand and kept no command. What they
    run is unchanged. `scripts\Probe.ps1` writes `probe.txt` and `scripts\Plant.ps1` writes
    `plant.txt`, in the order the recorded files were written. Both go to `out\`, under the names
    their recorded twins have in `recorded\`, and the databases go to `work\`. Both directories are
    emptied first.

    KeePass comes from `tools\KeePass.ps1`, which provides and checks the pinned 2.62; the probes
    take its path as a parameter, as they always did.

    Windows PowerShell 5.1, from the branch root:

        powershell.exe -NoProfile -ExecutionPolicy Bypass -File spikes\storage\probes\single-entry\security-and-fit\run.ps1
#>
$ErrorActionPreference = 'Stop'

$here = $PSScriptRoot
$out = Join-Path $here 'out'
$work = Join-Path $here 'work'
foreach ($directory in $out, $work) {
    if (Test-Path $directory) { Remove-Item $directory -Recurse -Force }
}
[void](New-Item -ItemType Directory -Force $out, $work)

# In a child scope, because the tool sets `Set-StrictMode -Version Latest`.
$keePassExe = & { . (Join-Path $here '..\..\..\..\..\tools\KeePass.ps1'); Get-KeePassExe }

foreach ($probe in 'Probe', 'Plant') {
    $clock = [Diagnostics.Stopwatch]::StartNew()
    & powershell.exe -NoProfile -ExecutionPolicy Bypass -File (Join-Path $here "scripts\$probe.ps1") `
        -KeePassExe $keePassExe -Work $work -OutFile (Join-Path $out ($probe.ToLowerInvariant() + '.txt')) | Out-Null
    if ($LASTEXITCODE -ne 0) { throw "$probe exited $LASTEXITCODE." }
    Write-Host ([string]::Format([Globalization.CultureInfo]::InvariantCulture, '{0}: {1:0.0} s', $probe, $clock.Elapsed.TotalSeconds))
}
