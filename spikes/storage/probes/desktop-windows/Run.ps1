<#
.SYNOPSIS
    Runs one measurement of the desktop-windows family from the branch root, in KeePass's and
    KeePassXC's own windows on this machine's desktop.

.DESCRIPTION
    **It takes the desktop.** Windows appear and are driven with UI automation, the keyboard and
    the mouse for minutes at a time; nothing else may use the desktop meanwhile, and a session
    that locks its screen stops it.

    What it does, in order: provides KeePass 2.62 (`tools\KeePass.ps1`) and KeePassXC 2.8.0 and
    2.7.12 (`tools\KeePassXC.ps1`); builds the probe (`probe\`), the two probe plugins
    (`plugin\DeskLog`, `plugin\DeskWrite`) and the auto-type target (`target\`) into their `out\`
    directories; makes `work\` beside this script, maps it as the drive given (W: by default) with
    `subst`, so that no dialog shows a path that names a user, and copies there KeePass three times
    (`kp`, `kp-log` with DeskLog, `kp-write` with DeskWrite) and each KeePassXC release; makes the
    base databases with the probe; runs the measurement's script; copies every text and picture the
    run left into `out\`, never a database; and removes the drive mapping.

    **Every program the run starts is started under a basic user token** through the script given
    as -Unelevated, which runs a command through `runas /trustlevel:0x20000` and waits for it
    (on the machine of the record, the session's `unelevated.ps1`). Every program it started is
    stopped before it returns, and after any failure.

    Measurements: M1 KeePass over a file changed underneath it; M2 the same in KeePassXC; M3 the
    entry dialog and a plugin string (with DeskWrite); M3v what a person sees and does with that
    string and the store in KeePass, M2v the same in KeePassXC; M4 KeePass's maintenance commands on a store made of entries; M5 what a
    plugin observes around KeePass's synchronize, import and save (with DeskLog).

.EXAMPLE
    powershell.exe -NoProfile -ExecutionPolicy Bypass -File spikes\storage\probes\desktop-windows\Run.ps1 -Measurement M1 -Unelevated <path to unelevated.ps1>
#>
param(
    [Parameter(Mandatory)] [ValidateSet('M1', 'M2', 'M2v', 'M3', 'M3v', 'M4', 'M5')] [string] $Measurement,
    [Parameter(Mandatory)] [string] $Unelevated,
    [string] $Case = 'all',
    [string] $Drive = 'W:'
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
if ($PSVersionTable.PSEdition -ne 'Desktop') { throw 'Run this under Windows PowerShell 5.1, powershell.exe.' }

$family = $PSScriptRoot
$root = (Resolve-Path (Join-Path $family '..\..\..\..')).Path
. (Join-Path $root 'tools\KeePass.ps1')
. (Join-Path $root 'tools\KeePassXC.ps1')
Install-KeePass | Out-Null
$keepass = Split-Path -Parent (Get-KeePassExe)
$xc = @{}
foreach ($v in '2.8.0', '2.7.12') { $xc[$v] = Split-Path -Parent (Get-KeePassXCCli -Version $v) }

foreach ($p in 'probe\Desk.csproj', 'plugin\DeskLog\DeskLog.csproj', 'plugin\DeskWrite\DeskWrite.csproj', 'target\DeskTarget.csproj') {
    $proj = Join-Path $family $p
    $out = Join-Path (Split-Path -Parent $proj) 'out'
    if ($p -like 'plugin\*') { $out = Join-Path $family ('plugin\out\' + [IO.Path]::GetFileNameWithoutExtension($proj)) }
    & dotnet build $proj -c Release -o $out --nologo | Out-Null
    if ($LASTEXITCODE -ne 0) { throw "dotnet build $p failed" }
}

$work = Join-Path $family 'work'
New-Item -ItemType Directory -Force $work | Out-Null
& subst.exe $Drive $work
if ($LASTEXITCODE -ne 0) { throw "subst $Drive failed; is the letter free?" }
try {
    $w = "$Drive\"
    foreach ($n in 'kp', 'kp-log', 'kp-write') {
        $d = Join-Path $w $n
        if (-not (Test-Path $d)) { Copy-Item -Recurse $keepass $d }
    }
    New-Item -ItemType Directory -Force (Join-Path $w 'kp-log\Plugins\DeskLog'), (Join-Path $w 'kp-write\Plugins\DeskWrite') | Out-Null
    Copy-Item (Join-Path $family 'plugin\out\DeskLog\DeskLog.dll') (Join-Path $w 'kp-log\Plugins\DeskLog\') -Force
    Copy-Item (Join-Path $family 'plugin\out\DeskWrite\DeskWrite.dll') (Join-Path $w 'kp-write\Plugins\DeskWrite\') -Force
    foreach ($v in $xc.Keys) { $d = Join-Path $w "kpxc-$v"; if (-not (Test-Path $d)) { Copy-Item -Recurse $xc[$v] $d } }
    $probe = Join-Path $family 'probe\out\Desk.exe'
    $base = Join-Path $w 'base'
    New-Item -ItemType Directory -Force $base | Out-Null
    & $probe m1base $base | Out-Null
    & $probe m3base (Join-Path $base 'M3.kdbx') | Out-Null
    & $probe m3other (Join-Path $base 'M3-other.kdbx') | Out-Null
    & $probe m4base (Join-Path $base 'M4.kdbx') | Out-Null
    Get-ChildItem $base -Filter *.kdbx | ForEach-Object { & $probe dump $_.FullName ($_.FullName + '.dump.txt') | Out-Null }

    & powershell.exe -NoProfile -ExecutionPolicy Bypass -File (Join-Path $family "scripts\$Measurement.ps1") -Work $w -Case $Case -Unelevated $Unelevated
    & (Join-Path $family 'scripts\Collect.ps1') -Work $w -To (Join-Path $family 'out') -LongWork $work -Replace @("$root=<branch>", "$env:USERPROFILE=<profile>")
}
finally {
    Get-Process -Name KeePass, KeePassXC, DeskTarget -ErrorAction SilentlyContinue |
        Where-Object { $_.Path -and $_.Path.StartsWith("$Drive\", [StringComparison]::OrdinalIgnoreCase) } |
        Stop-Process -Force -ErrorAction SilentlyContinue
    & subst.exe $Drive /d
}
