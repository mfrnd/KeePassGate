<#
.SYNOPSIS
    Runs the desktop-hide-and-lock measurements from the branch root, in KeePass 2.62's own window
    on this machine's desktop.

.DESCRIPTION
    **It takes the desktop.** Windows appear and are driven with UI automation, the keyboard and
    the mouse for minutes at a time; nothing else may use the desktop meanwhile, and a session that
    locks its screen stops it.

    What it does, in order: provides KeePass 2.62 (`tools\KeePass.ps1`); builds the probe
    (`probe\`, the desktop-windows family's Desk.cs with the database of this family added) and the
    probe plugin (`plugin\HideLock`); makes the work directory given (`work\` beside this script by
    default), maps it as the drive given (W: by default) with `subst`, so that no dialog shows a
    path that names a user, and copies KeePass there as `kp-hl` with the plugin in
    `Plugins\HideLock`; runs the cases of `scripts\Cases.ps1` through `scripts\Hl.ps1`; copies every
    text and picture the run left into `out\` with the desktop-windows family's `Collect.ps1`, never
    a database; and removes the drive mapping.

    **Every KeePass the run starts is started under a basic user token** through the script given
    as -Unelevated, which runs a command through `runas /trustlevel:0x20000` and waits for it.
    Every KeePass it started is stopped before it returns, and after any failure.

.EXAMPLE
    powershell.exe -NoProfile -ExecutionPolicy Bypass -File spikes\storage\probes\desktop-hide-and-lock\Run.ps1 -Unelevated <path to unelevated.ps1> -Case a-base
#>
param(
    [Parameter(Mandatory)] [string] $Unelevated,
    [string] $Case = 'all',
    [string] $Drive = 'W:',
    [string] $WorkDir,
    [int] $RunNumber = 1
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
if ($PSVersionTable.PSEdition -ne 'Desktop') { throw 'Run this under Windows PowerShell 5.1, powershell.exe.' }

$family = $PSScriptRoot
$root = (Resolve-Path (Join-Path $family '..\..\..\..')).Path
. (Join-Path $root 'tools\KeePass.ps1')
Install-KeePass | Out-Null
$keepass = Split-Path -Parent (Get-KeePassExe)

& dotnet build (Join-Path $family 'probe\Desk.csproj') -c Release -o (Join-Path $family 'probe\out') --nologo | Out-Null
if ($LASTEXITCODE -ne 0) { throw 'dotnet build probe failed' }
& dotnet build (Join-Path $family 'plugin\HideLock\HideLock.csproj') -c Release -o (Join-Path $family 'plugin\out\HideLock') --nologo | Out-Null
if ($LASTEXITCODE -ne 0) { throw 'dotnet build plugin failed' }

if (-not $WorkDir) { $WorkDir = Join-Path $family 'work' }
New-Item -ItemType Directory -Force $WorkDir | Out-Null
& subst.exe $Drive $WorkDir
if ($LASTEXITCODE -ne 0) { throw "subst $Drive failed; is the letter free?" }
try {
    $w = "$Drive\"
    $kp = Join-Path $w 'kp-hl'
    if (-not (Test-Path $kp)) { Copy-Item -Recurse $keepass $kp }
    New-Item -ItemType Directory -Force (Join-Path $kp 'Plugins\HideLock'), (Join-Path $w 'bin') | Out-Null
    Copy-Item (Join-Path $family 'plugin\out\HideLock\HideLock.dll') (Join-Path $kp 'Plugins\HideLock\') -Force
    Copy-Item (Join-Path $family 'probe\out\*') (Join-Path $w 'bin\') -Force
    & powershell.exe -NoProfile -ExecutionPolicy Bypass -File (Join-Path $family 'scripts\Hl.ps1') -Work $w -Case $Case -Unelevated $Unelevated -Run $RunNumber
    & (Join-Path $root 'spikes\storage\probes\desktop-windows\scripts\Collect.ps1') -Work $w -To (Join-Path $family 'out') -LongWork $WorkDir -Replace @("$root=<branch>", "$env:USERPROFILE=<profile>")
}
finally {
    Get-Process -Name KeePass -ErrorAction SilentlyContinue |
        Where-Object { $_.Path -and $_.Path.StartsWith("$Drive\", [StringComparison]::OrdinalIgnoreCase) } |
        Stop-Process -Force -ErrorAction SilentlyContinue
    & subst.exe $Drive /d
}
