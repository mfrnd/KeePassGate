<#
.SYNOPSIS
    Runs the design's probe from the branch, both parts the recorded output came from, as `RUN.md`
    describes.

.DESCRIPTION
    Added on the branch: the agent started the two scripts by hand and kept no command. What they
    run is unchanged. `scripts\Cells.ps1` writes `cells.txt` and `scripts\Rounds.ps1` writes
    `rounds.txt`, each with every part it has, which is how the recorded files were made. Both go to
    `out\`, under the names their recorded twins have in `recorded\`, and the databases go to
    `work\`. Both directories are emptied first.

    KeePass comes from `tools\KeePass.ps1`, which provides and checks the pinned 2.62; the probes
    take its path as a parameter, as they always did. The KeePassXC releases are asked for by the
    probes themselves through `tools\KeePassXC.ps1`, so `-XcRoot` is not passed: the probes no
    longer read it.

    Windows PowerShell 5.1, from the branch root:

        powershell.exe -NoProfile -ExecutionPolicy Bypass -File spikes\storage\probes\single-entry\design\run.ps1
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

function Step([string] $Name, [string] $Script, [string[]] $Arguments) {
    $clock = [Diagnostics.Stopwatch]::StartNew()
    & powershell.exe -NoProfile -ExecutionPolicy Bypass -File (Join-Path $here "scripts\$Script") @Arguments | Out-Null
    if ($LASTEXITCODE -ne 0) { throw "$Name exited $LASTEXITCODE." }
    Write-Host ([string]::Format([Globalization.CultureInfo]::InvariantCulture, '{0}: {1:0.0} s', $Name, $clock.Elapsed.TotalSeconds))
}

Step 'cells' 'Cells.ps1' @('-KeePassExe', $keePassExe, '-Work', $work, '-OutFile', (Join-Path $out 'cells.txt'))
Step 'rounds' 'Rounds.ps1' @('-KeePassExe', $keePassExe, '-Work', $work, '-OutFile', (Join-Path $out 'rounds.txt'))
