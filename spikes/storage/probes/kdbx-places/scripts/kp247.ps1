<#
.SYNOPSIS
    KeePass 2.47's KeePassLib, loaded from its KeePass.exe, opens a file and saves it to another.

.DESCRIPTION
    `-Edit` adds an unrelated entry at the root before saving. A process of its own, because one
    process holds one KeePass by name. Windows PowerShell 5.1:

        powershell.exe -NoProfile -File kp247.ps1 -KeePass .keepass\2.47 -In a.kdbx -Out b.kdbx [-Edit]
#>
param([string] $KeePass, [string] $In, [string] $Out, [switch] $Edit)
$ErrorActionPreference = 'Stop'
try {
    [void] [Reflection.Assembly]::LoadFrom((Join-Path $KeePass 'KeePass.exe'))
    Copy-Item -LiteralPath $In -Destination $Out -Force
    $key = New-Object KeePassLib.Keys.CompositeKey
    $key.AddUserKey((New-Object KeePassLib.Keys.KcpPassword -ArgumentList 'probe-only'))
    $db = New-Object KeePassLib.PwDatabase
    $db.Open([KeePassLib.Serialization.IOConnectionInfo]::FromPath($Out), $key, $null)
    if ($Edit) {
        $e = New-Object KeePassLib.PwEntry -ArgumentList $true, $true
        $e.Strings.Set([KeePassLib.PwDefs]::TitleField, (New-Object KeePassLib.Security.ProtectedString -ArgumentList $false, 'kp247-added'))
        $db.RootGroup.AddEntry($e, $true)
    }
    $db.Save($null)
    $db.Close()
    "saved by KeePassLib $([KeePassLib.PwDefs]::VersionString)"
}
catch {
    $x = $_.Exception
    while ($x.InnerException) { $x = $x.InnerException }
    "ERROR $($x.GetType().Name): $($x.Message)"
    exit 1
}
