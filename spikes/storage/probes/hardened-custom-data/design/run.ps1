<#
.SYNOPSIS
    Runs the design's probe from the branch, every task the recorded output came from, as
    `RUN.md` describes.

.DESCRIPTION
    Added on the branch: the agent ran these steps by hand and kept no script. What it runs is
    unchanged. `scripts\Probe.ps1` is started once per task, as its own first lines show, in the
    order the recorded files were written: `make` (the four sets of databases, dumped into
    `make2.txt`), `keepass` (the six KeePassLib Synchronize cells), `xc` for each release with the
    slow KDF and for 2.4.2 and 2.8.0 with the fast one, and `xcsave` for 2.4.2 and 2.8.0. Then
    `scripts\Members.ps1`, which prints to the console, into `members.txt`. Every file goes to
    `out\`, under the name its recorded twin has in `recorded\`, and the databases go to `work\`.
    Both are emptied first, because the probe appends to its output file.

    Windows PowerShell 5.1, from the branch root:

        powershell.exe -NoProfile -ExecutionPolicy Bypass -File spikes\storage\probes\hardened-custom-data\design\run.ps1
#>
$ErrorActionPreference = 'Stop'

$here = $PSScriptRoot
$out = Join-Path $here 'out'
$work = Join-Path $here 'work'
foreach ($directory in $out, $work) {
    if (Test-Path $directory) { Remove-Item $directory -Recurse -Force }
}
[void](New-Item -ItemType Directory -Force $out, $work)

$probe = Join-Path $here 'scripts\Probe.ps1'

function Step([string] $Name, [string[]] $Arguments) {
    $clock = [Diagnostics.Stopwatch]::StartNew()
    & powershell.exe -NoProfile -ExecutionPolicy Bypass -File $probe @Arguments
    if ($LASTEXITCODE -ne 0) { throw "$Name exited $LASTEXITCODE." }
    Write-Host ([string]::Format([Globalization.CultureInfo]::InvariantCulture, '{0}: {1:0.0} s', $Name, $clock.Elapsed.TotalSeconds))
}

Step 'make' @('-Task', 'make', '-Work', $work, '-Out', (Join-Path $out 'make2.txt'))
Step 'keepass' @('-Task', 'keepass', '-Work', $work, '-Out', (Join-Path $out 'keepass.txt'))
foreach ($release in '2.4.2', '2.6.6', '2.7.12', '2.8.0') {
    Step "xc $release slow" @('-Task', 'xc', '-Work', $work, '-Out', (Join-Path $out "xc-$release-slow.txt"), '-Release', $release, '-Kdf', 'slow')
}
foreach ($release in '2.4.2', '2.8.0') {
    Step "xc $release fast" @('-Task', 'xc', '-Work', $work, '-Out', (Join-Path $out "xc-$release-fast.txt"), '-Release', $release, '-Kdf', 'fast')
}
foreach ($release in '2.4.2', '2.8.0') {
    Step "xcsave $release" @('-Task', 'xcsave', '-Work', $work, '-Out', (Join-Path $out "xcsave-$release.txt"), '-Release', $release)
}

$clock = [Diagnostics.Stopwatch]::StartNew()
& powershell.exe -NoProfile -ExecutionPolicy Bypass -File (Join-Path $here 'scripts\Members.ps1') |
    Out-File -LiteralPath (Join-Path $out 'members.txt') -Encoding utf8
if ($LASTEXITCODE -ne 0) { throw "Members exited $LASTEXITCODE." }
Write-Host ([string]::Format([Globalization.CultureInfo]::InvariantCulture, 'members: {0:0.0} s', $clock.Elapsed.TotalSeconds))
