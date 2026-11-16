# Console probe over KeePassLib 2.62 (loaded from KeePass.exe as an assembly, no window).
# Invented content only. Run with Windows PowerShell 5.1:
#   powershell.exe -NoProfile -ExecutionPolicy Bypass -File probe.ps1 <work dir> <output file>
param([string] $Work, [string] $Out)

$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot '..\..\..\..\..\..\tools\KeePass.ps1'); $KeePassExe = Get-KeePassExe
[void] [Reflection.Assembly]::LoadFrom($KeePassExe)

$lines = New-Object System.Collections.Generic.List[string]
function Say([string] $s) { $lines.Add($s); Write-Host $s }

$T0 = [DateTime]::SpecifyKind([DateTime]::Parse('2026-01-01T00:00:00'), [DateTimeKind]::Utc)
function At([int] $s) { return $T0.AddSeconds($s) }

function Key() {
    $ck = New-Object KeePassLib.Keys.CompositeKey
    $ck.AddUserKey((New-Object KeePassLib.Keys.KcpPassword('probe-only')))
    return $ck
}

function NewDb([string] $path) {
    $db = New-Object KeePassLib.PwDatabase
    $db.New([KeePassLib.Serialization.IOConnectionInfo]::FromPath($path), (Key))
    $kdf = New-Object KeePassLib.Cryptography.KeyDerivation.AesKdf
    $p = $kdf.GetDefaultParameters()
    $p.SetUInt64([KeePassLib.Cryptography.KeyDerivation.AesKdf]::ParamRounds, 1)
    $db.KdfParameters = $p
    return $db
}

function OpenDb([string] $path) {
    $db = New-Object KeePassLib.PwDatabase
    $db.Open([KeePassLib.Serialization.IOConnectionInfo]::FromPath($path), (Key), $null)
    return $db
}

function SaveAs($db, [string] $path) {
    $db.SaveAs([KeePassLib.Serialization.IOConnectionInfo]::FromPath($path), $false, $null)
}

function Uuid([string] $hex) {
    return New-Object KeePassLib.PwUuid (,[KeePassLib.Utility.MemUtil]::HexStringToByteArray($hex))
}

function Entry($group, [string] $title, [string] $uuidHex, [int] $mod, [hashtable] $extra) {
    $e = New-Object KeePassLib.PwEntry($true, $true)
    if ($uuidHex) { $e.SetUuid((Uuid $uuidHex), $false) }
    $e.Strings.Set('Title', (New-Object KeePassLib.Security.ProtectedString($false, $title)))
    $pwText = 'pw-' + $title
    $e.Strings.Set('Password', (New-Object KeePassLib.Security.ProtectedString($true, $pwText)))
    if ($extra) {
        foreach ($k in $extra.Keys) {
            $prot = ($k -eq 'KPG.Store')
            $val = [string] $extra[$k]
            $e.Strings.Set($k, (New-Object KeePassLib.Security.ProtectedString($prot, $val)))
        }
    }
    $group.AddEntry($e, $true)
    $e.CreationTime = At 0
    $e.LastModificationTime = At $mod
    $e.LastAccessTime = At $mod
    $e.LocationChanged = At 0
    return $e
}

function Listing($db, [string] $label) {
    Say ("  " + $label + ":")
    foreach ($e in $db.RootGroup.GetEntries($true)) {
        $kpg = $e.Strings.Get('KPG.Store')
        $mark = if ($kpg -ne $null) { ' KPG.Store' } else { '' }
        Say ("    " + $e.Uuid.ToHexString() + " '" + $e.Strings.ReadSafe('Title') + "' in " + $e.ParentGroup.Name + $mark)
    }
    $ids = @()
    foreach ($d in $db.DeletedObjects) { $ids += $d.Uuid.ToHexString() }
    Say ("    deletion records: " + ($ids -join ', '))
}

# ---------------------------------------------------------------------------------------------
# P1: the database imported into itself with Create new IDs, then Delete Duplicate Entries.
# Which of the two identical snapshots does KeePass delete?
Say '## P1 self-import with Create new IDs, then Delete Duplicate Entries'
$snapId = '4B50472D533100000002A1B2C3D4E5F6'   # "KPG-S1", seq 2, six bytes
$storeGroupId = '4B50472D47524F55500000000000000A'
foreach ($order in @('as-made')) {
    $pa = Join-Path $Work 'p1-a.kdbx'
    $a = NewDb $pa
    $general = New-Object KeePassLib.PwGroup($true, $true, 'General', [KeePassLib.PwIcon]::Folder)
    $a.RootGroup.AddGroup($general, $true)
    $store = New-Object KeePassLib.PwGroup($false, $true, 'KeePassGate', [KeePassLib.PwIcon]::TerminalEncrypted)
    $store.Uuid = Uuid $storeGroupId
    $store.EnableSearching = $false
    $store.EnableAutoType = $false
    $a.RootGroup.AddGroup($store, $true)
    [void] (Entry $general 'user-1' $null 5 $null)
    [void] (Entry $general 'user-2' $null 5 $null)
    [void] (Entry $store 'KeePassGate store' $snapId 10 @{ 'KPG.Store' = '{"v":"1","id":"' + $snapId + '","seq":"2"}'; 'KPG.Seal' = 'c2VhbA==' })
    SaveAs $a $pa
    $a.Close()

    $a = OpenDb $pa
    $b = OpenDb $pa
    $a.MergeIn($b, [KeePassLib.PwMergeMethod]::CreateNewUuids, $null)
    $b.Close()
    Listing $a 'after the import'
    $n = $a.DeleteDuplicateEntries($null)
    Say ("  Delete Duplicate Entries removed " + $n)
    Listing $a 'after Delete Duplicate Entries'
    $kept = @($a.RootGroup.GetEntries($true) | Where-Object { $_.Strings.Get('KPG.Store') -ne $null })
    foreach ($e in $kept) {
        $verdict = if ($e.Uuid.ToHexString() -eq $snapId) { 'the original (its own identifier)' } else { 'a copy (another identifier)' }
        Say ("  snapshot left: " + $e.Uuid.ToHexString() + " = " + $verdict)
    }
    $a.Close()
}

# ---------------------------------------------------------------------------------------------
# P2: a deletion record used as a witness, when the other copy holds a node under the witness's
# identifier. KeePass's Synchronize, both directions, the node newer and older than the record.
Say ''
Say '## P2 a witness deletion record against a node carrying its identifier'
$witness = '4B50472D5231C0FFEE00112233445566'   # "KPG-R1" and ten bytes of a digest
foreach ($nodeMod in @(20, 5)) {
    foreach ($dir in @('into-witness-copy', 'into-node-copy')) {
        $pw = Join-Path $Work ("p2-w-" + $nodeMod + "-" + $dir + ".kdbx")
        $pn = Join-Path $Work ("p2-n-" + $nodeMod + "-" + $dir + ".kdbx")

        $w = NewDb $pw
        $w.DeletedObjects.Add((New-Object KeePassLib.PwDeletedObject((Uuid $witness), (At 10))))
        SaveAs $w $pw
        $w.Close()

        $n = NewDb $pn
        $g = New-Object KeePassLib.PwGroup($true, $true, 'General', [KeePassLib.PwIcon]::Folder)
        $n.RootGroup.AddGroup($g, $true)
        [void] (Entry $g 'planted' $witness $nodeMod $null)
        SaveAs $n $pn
        $n.Close()

        if ($dir -eq 'into-witness-copy') { $t = OpenDb $pw; $s = OpenDb $pn } else { $t = OpenDb $pn; $s = OpenDb $pw }
        $t.MergeIn($s, [KeePassLib.PwMergeMethod]::Synchronize, $null)
        $s.Close()
        $still = $false
        foreach ($d in $t.DeletedObjects) { if ($d.Uuid.ToHexString() -eq $witness) { $still = $true } }
        $node = $t.RootGroup.FindEntry((Uuid $witness), $true)
        Say ("  node modified +" + $nodeMod + " s, record at +10 s, " + $dir + ": witness kept=" + $still + ", node present=" + ($node -ne $null))
        $t.Close()
    }
}

[IO.File]::WriteAllLines($Out, $lines)
