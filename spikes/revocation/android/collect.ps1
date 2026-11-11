<#
.SYNOPSIS
    Reads back every database under one subdirectory of the work directory with KeePassLib.

.DESCRIPTION
    Every `.kdbx` under `<Work>\<Dir>` is read by `Kpg.cs` through `kp.ps1`: a block per file into
    `<Work>\out\<Name>-dump.txt` and a line per file into `<Work>\out\<Name>-sum.txt`, which is
    also printed. Both name each file by its full path, so neither is for committing as it comes
    back. Windows PowerShell 5.1 only.

.PARAMETER Work
    The work directory the emulator scripts wrote into, `work\android` under the repository unless
    `KPG_ANDROID_WORK` named another.

.PARAMETER Dir
    The subdirectory to read, `work\A\kpdx-4.5.8` for one program's saves.

.PARAMETER Name
    What the two output files are called.

.PARAMETER KeePass
    The directory holding `KeePass.exe`, `.keepass\2.61.1` once `tools\KeePass.ps1` has provided it.

.EXAMPLE
    powershell.exe -NoProfile -File collect.ps1 -Work work\android -Dir work\A\kpdx-4.5.8 -Name A-kpdx-4.5.8 -KeePass .keepass\2.61.1
#>
param(
    [Parameter(Mandatory)] [string] $Work,
    [Parameter(Mandatory)] [string] $Dir,
    [Parameter(Mandatory)] [string] $Name,
    [Parameter(Mandatory)] [string] $KeePass
)
$me = Split-Path -Parent $MyInvocation.MyCommand.Path
$X = (Resolve-Path $Work).Path
$KeePass = (Resolve-Path $KeePass).Path
$files = @(Get-ChildItem (Join-Path $X $Dir) -Recurse -Filter *.kdbx | Sort-Object FullName | ForEach-Object FullName)
$lst = Join-Path $X "out\$Name-files.lst"
$files | Set-Content $lst -Encoding ASCII
& powershell.exe -NoProfile -File (Join-Path $me 'kp.ps1') -KeePass $KeePass -Source Kpg.cs -Type Kpg dump @files 2>&1 | Set-Content (Join-Path $X "out\$Name-dump.txt") -Encoding UTF8
& powershell.exe -NoProfile -File (Join-Path $me 'kp.ps1') -KeePass $KeePass -Source Kpg.cs -Type Kpg list $lst 2>&1 | Set-Content (Join-Path $X "out\$Name-sum.txt") -Encoding UTF8
Get-Content (Join-Path $X "out\$Name-sum.txt")
