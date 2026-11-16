# KeePassLib 2.62 operations a person or KeePass itself performs, for the wildcard-survival critique.
# Windows PowerShell 5.1 only. Invented databases only. No window.
#   deg       <in> <out>  KeePass's Tools > Database Tools > Delete Empty Groups (PwDatabase.DeleteEmptyGroups)
#   delgroup  <in> <out>  delete the store group for good, as KeePass's permanent delete does (records for it and its contents)
#   delnewest <in> <out>  delete the snapshot with the highest sequence number for good, with a deletion record
#   records   <in>        print every deletion record and every group
param([string] $Op, [string] $In, [string] $Out)
$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot '..\..\..\..\..\..\tools\KeePass.ps1'); $KeePassExe = Get-KeePassExe
[void][System.Reflection.Assembly]::LoadFrom($KeePassExe)

function Open-Db([string] $path) {
    $db = New-Object KeePassLib.PwDatabase
    $k = New-Object KeePassLib.Keys.CompositeKey
    $k.AddUserKey((New-Object KeePassLib.Keys.KcpPassword 'probe-only'))
    $db.Open([KeePassLib.Serialization.IOConnectionInfo]::FromPath($path), $k, $null)
    return $db
}
function Save-Db($db, [string] $path) {
    $db.SaveAs([KeePassLib.Serialization.IOConnectionInfo]::FromPath($path), $false, $null)
    $db.Close()
}
$storeGroup = New-Object KeePassLib.PwUuid (,[byte[]](0x4B,0x50,0x47,0x47,0,0,0,0,0,0,0,0,0,0,0,1))

switch ($Op) {
    'deg' {
        $db = Open-Db $In
        $n = $db.DeleteEmptyGroups()
        "Delete Empty Groups removed $n group(s)"
        Save-Db $db $Out
    }
    'delgroup' {
        $db = Open-Db $In
        $g = $db.RootGroup.FindGroup($storeGroup, $true)
        if ($g -eq $null) { throw 'no store group' }
        $now = [DateTime]::UtcNow
        foreach ($e in $g.GetEntries($true)) { $db.DeletedObjects.Add((New-Object KeePassLib.PwDeletedObject($e.Uuid, $now))) }
        foreach ($sg in $g.GetGroups($true)) { $db.DeletedObjects.Add((New-Object KeePassLib.PwDeletedObject($sg.Uuid, $now))) }
        [void]$g.ParentGroup.Groups.Remove($g)
        $db.DeletedObjects.Add((New-Object KeePassLib.PwDeletedObject($g.Uuid, $now)))
        "store group deleted for good with its entries"
        Save-Db $db $Out
    }
    'delnewest' {
        $db = Open-Db $In
        $best = $null; $bestSeq = -1
        foreach ($e in $db.RootGroup.GetEntries($true)) {
            if ($e.Strings.Get('KPG.Store') -eq $null) { continue }
            $b = $e.Uuid.UuidBytes
            $seq = ([int]$b[4] -shl 24) -bor ([int]$b[5] -shl 16) -bor ([int]$b[6] -shl 8) -bor [int]$b[7]
            if ($seq -gt $bestSeq) { $best = $e; $bestSeq = $seq }
        }
        if ($best -eq $null) { throw 'no snapshot' }
        [void]$best.ParentGroup.Entries.Remove($best)
        $db.DeletedObjects.Add((New-Object KeePassLib.PwDeletedObject($best.Uuid, [DateTime]::UtcNow)))
        "deleted snapshot seq $bestSeq ($($best.Uuid.ToHexString())) for good, with a record"
        Save-Db $db $Out
    }
    'tobin' {
        # Move every snapshot into the recycle bin, making the bin if the database names none,
        # as a delete in KeePass, KeePassXC or an Android program does when the bin is on.
        $db = Open-Db $In
        $now = [DateTime]::UtcNow
        $bin = $null
        if (-not $db.RecycleBinUuid.Equals([KeePassLib.PwUuid]::Zero)) { $bin = $db.RootGroup.FindGroup($db.RecycleBinUuid, $true) }
        if ($bin -eq $null) {
            $bin = New-Object KeePassLib.PwGroup($true, $true, 'Recycle Bin', [KeePassLib.PwIcon]::TrashBin)
            $bin.EnableSearching = $false; $bin.EnableAutoType = $false
            $db.RootGroup.AddGroup($bin, $true)
            $db.RecycleBinUuid = $bin.Uuid; $db.RecycleBinChanged = $now
        }
        foreach ($e in @($db.RootGroup.GetEntries($true))) {
            if ($e.Strings.Get('KPG.Store') -eq $null -or $e.ParentGroup -eq $bin) { continue }
            [void]$e.ParentGroup.Entries.Remove($e)
            $bin.AddEntry($e, $true, $true)
            "moved snapshot $($e.Uuid.ToHexString()) into the recycle bin"
        }
        Save-Db $db $Out
    }
    'records' {
        $db = Open-Db $In
        foreach ($d in $db.DeletedObjects) { 'record ' + $d.Uuid.ToHexString() + ' ' + $d.DeletionTime.ToUniversalTime().ToString('s') + 'Z' }
        foreach ($g in $db.RootGroup.GetGroups($true)) { 'group  ' + $g.Uuid.ToHexString() + ' ' + $g.Name + ' mod ' + $g.LastModificationTime.ToUniversalTime().ToString('s') + 'Z entries ' + $g.Entries.UCount }
        $db.Close()
    }
    default { throw "unknown op $Op" }
}
