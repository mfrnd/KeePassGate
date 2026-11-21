# Runs `InputProbe.exe` against a throwaway KeePass made by `new-keepass.ps1`, on a private desktop
# through the launcher or on the visible desktop under a basic user token, with the visible desktop
# sampled once a second meanwhile. A KeePass is used for one run only: the probe saves into it.
#
#   run-input-probe.ps1 -Where private|visible -KeePass <directory from new-keepass.ps1> -Out <dir>
#
# On the visible desktop KeePass and its dialogs come to the front for about two and a half minutes,
# most of it the wait in step M4; nothing is typed or clicked there, every input is a message.
param(
    [Parameter(Mandatory)] [ValidateSet('private', 'visible')] [string] $Where,
    [Parameter(Mandatory)] [string] $KeePass,
    [Parameter(Mandatory)] [string] $Out)
$here = Split-Path -Parent $PSCommandPath
$root = Split-Path -Parent $here
if (Test-Path $Out) { Remove-Item $Out -Recurse -Force }
New-Item -ItemType Directory -Force $Out | Out-Null
$command = "& '$here\InputProbe.exe' '$KeePass\keepass\KeePass.exe' '$KeePass\throwaway.kdbx' 'dummy-probe-password' '$Out'"
$stop = "$Out\watch.stop"
$watch = Start-Process powershell.exe -WindowStyle Hidden -PassThru -ArgumentList @(
    '-NoProfile', '-ExecutionPolicy', 'Bypass', '-File', "`"$root\runs\watch-visible.ps1`"", '-Out', "`"$Out\visible.csv`"", '-Stop', "`"$stop`"", '-Minutes', '15', '-Every')
if ($Where -eq 'private') {
    & "$root\launcher\private-desktop.ps1" -Dir $here -Log "$Out\run.log" -Command $command -Name 'input-probe' -SampleSeconds 5
}
else {
    & "$root\launcher\basic-user.ps1" -Dir $here -Log "$Out\run.log" -Command $command
}
New-Item $stop -ItemType File | Out-Null
$watch.WaitForExit(15000) | Out-Null
