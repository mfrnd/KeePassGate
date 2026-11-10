<#
.SYNOPSIS
    Opens each file with the KeePassLib inside one KeePass, adds an entry at the root and saves it
    in place.

.DESCRIPTION
    What KeePass's own File then Save reaches, without its window: `PwDatabase.Open`, an entry
    added through `PwGroup.AddEntry` so that the save is not a save of nothing, and
    `PwDatabase.Save`. Loading `KeePass.exe` as an assembly runs none of its user interface.

    It runs in a process of its own, started by `Measure-KeePassXC.ps1`, because one process holds
    one KeePass by name and the reading half is the probe's 2.61.1. Every argument after `-KeePass`
    is a file. A line per file names the KeePassLib that opened it and how many keys it found in
    the database's custom data before saving.

    **The password arrives on standard input**, which `powershell.exe -File` reads before the
    script starts and hands it as pipeline input, leaving nothing for `[Console]::In`. That is also
    why this is not an advanced script: an advanced script refuses pipeline input that no parameter
    takes.

.EXAMPLE
    'probe-only' | powershell.exe -NoProfile -File Save-WithKeePass.ps1 -KeePass .keepass\2.47 work\save\P.kdbx
#>
param([string] $KeePass)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

if ($PSVersionTable.PSEdition -ne 'Desktop') {
    throw 'KeePass is a .NET Framework assembly: run this under Windows PowerShell 5.1, powershell.exe.'
}

$password = @($input)
if (-not $KeePass -or $args.Count -eq 0 -or $password.Count -eq 0) {
    throw 'Usage: <password> | Save-WithKeePass.ps1 -KeePass <directory holding KeePass.exe> <file>...'
}

[void] [Reflection.Assembly]::LoadFrom((Join-Path $KeePass 'KeePass.exe'))

foreach ($path in $args) {
    $key = New-Object KeePassLib.Keys.CompositeKey
    $key.AddUserKey((New-Object KeePassLib.Keys.KcpPassword -ArgumentList $password[0]))

    $database = New-Object KeePassLib.PwDatabase
    $database.Open([KeePassLib.Serialization.IOConnectionInfo]::FromPath($path), $key, $null)
    try {
        $count = $database.CustomData.Count
        $entry = New-Object KeePassLib.PwEntry -ArgumentList $true, $true
        $title = New-Object KeePassLib.Security.ProtectedString -ArgumentList $false, 'kp-added'
        $entry.Strings.Set([KeePassLib.PwDefs]::TitleField, $title)
        $database.RootGroup.AddEntry($entry, $true)
        $database.Save($null)
    }
    finally {
        $database.Close()
    }

    "opened by KeePassLib $([KeePassLib.PwDefs]::VersionString) with $count keys in the database's custom data, saved"
}
