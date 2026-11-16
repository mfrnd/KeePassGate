<#
.SYNOPSIS
    Builds the survival critique's probe and runs it from the branch, as `RUN.md` describes.

.DESCRIPTION
    Added on the branch: the agent ran these steps by hand from PowerShell and kept no script. What
    it runs is unchanged: `Gos.exe` with the work directory, the output file, and a release and
    the path of its `keepassxc-cli` for each of 2.4.2, 2.6.6, 2.7.12 and 2.8.0, in that order, as
    the agent passed them. It writes `out\survival.txt`, whose recorded copy is
    `recorded\survival.txt`. The databases go to `work\`, which is emptied first, as the agent
    emptied it.

    Windows PowerShell 5.1, from the branch root:

        powershell.exe -NoProfile -ExecutionPolicy Bypass -File spikes\storage\probes\grant-only\survival\run.ps1
#>
$ErrorActionPreference = 'Stop'

$root = (Resolve-Path (Join-Path $PSScriptRoot '..\..\..\..\..')).Path
. (Join-Path $root 'tools\KeePass.ps1')
. (Join-Path $root 'tools\KeePassXC.ps1')

$here = $PSScriptRoot
$out = Join-Path $here 'out'
$work = Join-Path $here 'work'
$bin = Join-Path $here 'bin'
if (Test-Path $work) { Remove-Item $work -Recurse -Force }
[void](New-Item -ItemType Directory -Force $out, $work)

# Provides and checks the pinned KeePass the project references.
$null = Get-KeePassExe
& dotnet build (Join-Path $here 'probe\Gos.csproj') -c Release -o $bin -v q -nologo
if ($LASTEXITCODE -ne 0) { throw "The build failed." }

$arguments = @($work, (Join-Path $out 'survival.txt'))
foreach ($version in '2.4.2', '2.6.6', '2.7.12', '2.8.0') {
    $arguments += $version
    $arguments += Get-KeePassXCCli -Version $version
}

$clock = [Diagnostics.Stopwatch]::StartNew()
& (Join-Path $bin 'Gos.exe') @arguments
if ($LASTEXITCODE -ne 0) { throw "Gos.exe exited $LASTEXITCODE." }
Write-Host ([string]::Format([Globalization.CultureInfo]::InvariantCulture, 'survival: {0:0.0} s', $clock.Elapsed.TotalSeconds))
