<#
.SYNOPSIS
    Builds the survival critique's probe and runs it from the branch, as `RUN.md` describes.

.DESCRIPTION
    Added on the branch: the agent ran these steps by hand and kept no script. What it runs is
    unchanged. `Rcs.exe` takes the work directory, the output file, `keepassxc-cli` 2.8.0 and
    2.4.2, `uv`, and `scripts\pk_del.py`, in that order. It writes `out\chains.txt`, the file
    `recorded\` holds, and makes its databases under `work\c1` to `work\c5`, which is emptied first.

    The probe asks `uv` for `pykeepass` without a version. `UV_EXCLUDE_NEWER` is set to the day after
    the recorded run, so that a later run resolves what that run could have resolved (it printed
    pykeepass 4.2.0) rather than whatever has been released since. That is the command's setting,
    not the probe's: the probe is unchanged.

    Windows PowerShell 5.1, from the branch root:

        powershell.exe -NoProfile -ExecutionPolicy Bypass -File spikes\storage\probes\rules-central\survival\run.ps1
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
& dotnet build (Join-Path $here 'scripts\Rcs.csproj') -c Release -o $bin -v q
if ($LASTEXITCODE -ne 0) { throw "The build failed." }

$xc280 = Get-KeePassXCCli -Version 2.8.0
$xc242 = Get-KeePassXCCli -Version 2.4.2
$uv = (Get-Command uv -CommandType Application | Select-Object -First 1).Source
$env:UV_EXCLUDE_NEWER = '2026-11-16T00:00:00Z'

$clock = [Diagnostics.Stopwatch]::StartNew()
& (Join-Path $bin 'Rcs.exe') $work (Join-Path $out 'chains.txt') $xc280 $xc242 $uv (Join-Path $here 'scripts\pk_del.py')
if ($LASTEXITCODE -ne 0) { throw "Rcs.exe exited $LASTEXITCODE." }
Write-Host ([string]::Format([Globalization.CultureInfo]::InvariantCulture, 'chains: {0:0.0} s', $clock.Elapsed.TotalSeconds))
