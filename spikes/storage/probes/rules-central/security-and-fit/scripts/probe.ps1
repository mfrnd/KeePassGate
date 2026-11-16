# Probe for the rules-central critique. Windows PowerShell 5.1, KeePassLib 2.62 loaded from a
# copy of KeePass.exe, in memory only, invented content, no window.
param([string]$KeePassExe, [string]$OutFile)
$ErrorActionPreference = 'Stop'
# Added to run from the branch: when no argument names them, KeePassLib comes from the pinned
# KeePass the branch provides and checks, and the output goes to `out\probe.txt` beside this
# directory. The tool is dot-sourced in a child scope so that its strict mode stays out of the
# probe's own scope.
if (-not $KeePassExe) { $KeePassExe = & { . (Join-Path $PSScriptRoot '..\..\..\..\..\..\tools\KeePass.ps1'); Get-KeePassExe } }
if (-not $OutFile) { $OutFile = Join-Path $PSScriptRoot '..\out\probe.txt' }
[void](New-Item -ItemType Directory -Force (Split-Path -Parent $OutFile))
[void][Reflection.Assembly]::LoadFrom($KeePassExe)
$lines = New-Object System.Collections.Generic.List[string]
function Say([string]$s) { $lines.Add($s) }

function NewDb([string]$pw) {
    $db = New-Object KeePassLib.PwDatabase
    $ck = New-Object KeePassLib.Keys.CompositeKey
    $ck.AddUserKey((New-Object KeePassLib.Keys.KcpPassword($pw)))
    $db.New((New-Object KeePassLib.Serialization.IOConnectionInfo), $ck)
    return $db
}
function Str([string]$v) { return New-Object KeePassLib.Security.ProtectedString($false, $v) }
function Hex([byte[]]$b) { return ([BitConverter]::ToString($b)).Replace('-', '') }
function Sha([string]$t) {
    $s = [Security.Cryptography.SHA256]::Create()
    return $s.ComputeHash([Text.Encoding]::UTF8.GetBytes($t))
}
function Uuid([byte[]]$b16) { return New-Object KeePassLib.PwUuid(,$b16) }

Say ("KeePassLib " + [KeePassLib.PwDefs]::VersionString)

# ---------------------------------------------------------------- part 1: duplicates today
# Today a rule lives in node custom data under key KPG. What do KeePass's duplicate paths
# (PwGroup.Duplicate, used by Duplicate Group and Ctrl-drag copy; PwEntry.Duplicate, used by
# Duplicate Entry and templates) do with it?
$db = NewDb 'probe-only'
$root = $db.RootGroup
$root.CustomData.Set('KPG', '{"default":"inherit","agent":["read","list"]}')
$sealed = New-Object KeePassLib.PwGroup($true, $true, 'Sealed', [KeePassLib.PwIcon]::Folder)
$root.AddGroup($sealed, $true)
$sealed.CustomData.Set('KPG', '{"default":"deny"}')
$x = New-Object KeePassLib.PwEntry($true, $true)
$x.Strings.Set('Title', (Str 'invented secret'))
$x.Strings.Set('Password', (New-Object KeePassLib.Security.ProtectedString($true, 'invented-password')))
$sealed.AddEntry($x, $true)
$y = New-Object KeePassLib.PwEntry($true, $true)
$y.Strings.Set('Title', (Str 'narrowed entry'))
$y.CustomData.Set('KPG', '{"default":"inherit","agent":[]}')
$root.AddEntry($y, $true)

$gdup = $sealed.Duplicate()
$xdup = $gdup.Entries.GetAt(0)
Say ("P1 group duplicate: new uuid=" + (-not $gdup.Uuid.Equals($sealed.Uuid)) +
     "; rule on copy=" + $gdup.CustomData.Get('KPG') +
     "; entry inside new uuid=" + (-not $xdup.Uuid.Equals($x.Uuid)) +
     "; entry password kept=" + ($xdup.Strings.ReadSafe('Password') -eq 'invented-password'))
$ydup = $y.Duplicate()
Say ("P1 entry duplicate: new uuid=" + (-not $ydup.Uuid.Equals($y.Uuid)) +
     "; rule on copy=" + $ydup.CustomData.Get('KPG'))

# ---------------------------------------------------------------- part 2: a foreign file plants a rule
# The victim database V: root grants agent read+list, group S sealed by a rule entry, group C under
# S with no rule, entry X in C. Rules are rule entries in the store group, as the design has them.
$storeUuid = [byte[]](Sha 'KPG/1/group/')[0..15]
$mark = [byte[]](Sha 'KPG/1/rule/')[0..3]
Say ("P2 store group uuid " + (Hex $storeUuid) + ", mark " + (Hex $mark))

function RuleEntry([KeePassLib.PwUuid]$node, [string]$after, [string]$data) {
    $nodeHex = $node.ToHexString()
    $digest = Sha ("KPG/1/rule/" + $nodeHex + "/" + $after + "/" + $data)
    $u = New-Object byte[] 16
    [Array]::Copy($mark, 0, $u, 0, 4)
    [Array]::Copy($node.UuidBytes, 0, $u, 4, 6)
    [Array]::Copy($digest, 0, $u, 10, 6)
    $e = New-Object KeePassLib.PwEntry($false, $true)
    $e.SetUuid((Uuid $u), $false)
    $e.Strings.Set('Title', (Str ('KPG rule ' + $nodeHex.Substring(0, 8))))
    $e.Strings.Set('UserName', (Str ''))
    $e.Strings.Set('Password', (Str ''))
    $e.Strings.Set('URL', (Str ''))
    $e.Strings.Set('Notes', (Str 'KeePassGate data. Managed on the KPG tab in KeePass.'))
    $e.Strings.Set('KPG.Kind', (Str 'rule'))
    $e.Strings.Set('KPG.Node', (Str $nodeHex))
    $e.Strings.Set('KPG.After', (Str $after))
    $e.Strings.Set('KPG.Data', (Str $data))
    return $e
}

$v = NewDb 'victim-password'
$vr = $v.RootGroup
$store = New-Object KeePassLib.PwGroup($false, $true, 'KeePassGate', [KeePassLib.PwIcon]::TerminalEncrypted)
$store.Uuid = (Uuid $storeUuid)
$store.EnableSearching = $false
$store.EnableAutoType = $false
$vr.AddGroup($store, $true)
$s = New-Object KeePassLib.PwGroup($true, $true, 'S', [KeePassLib.PwIcon]::Folder)
$vr.AddGroup($s, $true)
$c = New-Object KeePassLib.PwGroup($true, $true, 'C', [KeePassLib.PwIcon]::Folder)
$s.AddGroup($c, $true)
$xe = New-Object KeePassLib.PwEntry($true, $true)
$xe.Strings.Set('Title', (Str 'X'))
$c.AddEntry($xe, $true)
$store.AddEntry((RuleEntry $vr.Uuid 'none' '{"default":"inherit","agent":["list","read"]}'), $true)
$store.AddEntry((RuleEntry $s.Uuid 'none' '{"default":"deny"}'), $true)
$t0 = [DateTime]::SpecifyKind([DateTime]'2026-01-01T00:00:00', 'Utc')
foreach ($g in @($vr, $s, $c)) { $g.LastModificationTime = $t0; $g.LocationChanged = $t0 }
$xe.LastModificationTime = $t0
Say ("P2 victim C uuid " + $c.Uuid.ToHexString() + ", rules in store " + $store.Entries.UCount)

# The foreign database F: another master key, nothing in common with V but the fixed store group
# UUID (public) and C's UUID (which an agent can learn: API identifiers are UUIDs).
function Foreign() {
    $f = NewDb 'attacker-password'
    $fs = New-Object KeePassLib.PwGroup($false, $true, 'Shared passwords', [KeePassLib.PwIcon]::Folder)
    $fs.Uuid = (Uuid $storeUuid)
    $f.RootGroup.AddGroup($fs, $true)
    $fs.AddEntry((RuleEntry $c.Uuid 'none' '{"default":"inherit","agent":["list","protected","read"]}'), $true)
    $other = New-Object KeePassLib.PwEntry($true, $true)
    $other.Strings.Set('Title', (Str 'Team wifi'))
    $f.RootGroup.AddEntry($other, $true)
    return $f
}

foreach ($mm in @('Synchronize', 'KeepExisting', 'CreateNewUuids')) {
    $vv = NewDb 'victim-password'
    # fresh copy of V for each method: clone the tree
    $vv.RootGroup = $v.RootGroup.CloneDeep()
    $vv.RootGroup.Uuid = $v.RootGroup.Uuid
    $f = Foreign
    $vv.MergeIn($f, [KeePassLib.PwMergeMethod]$mm)
    $vstore = $vv.RootGroup.FindGroup((Uuid $storeUuid), $true)
    $vc = $vv.RootGroup.FindGroup($c.Uuid, $true)
    $planted = @()
    $copies = @()
    foreach ($e in $vv.RootGroup.GetEntries($true)) {
        $k = $e.Strings.ReadSafe('KPG.Kind')
        if ($k -ne 'rule') { continue }
        $b = $e.Uuid.UuidBytes
        $marked = ($b[0] -eq $mark[0] -and $b[1] -eq $mark[1] -and $b[2] -eq $mark[2] -and $b[3] -eq $mark[3])
        if ($e.Strings.ReadSafe('KPG.Node') -eq $c.Uuid.ToHexString()) {
            if ($marked) { $planted += ("marked, in '" + $e.ParentGroup.Name + "'" + $(if ($e.ParentGroup.Uuid.Equals((Uuid $storeUuid))) { ' (the store group)' } else { '' })) }
            else { $copies += ("unmarked copy, in '" + $e.ParentGroup.Name + "'") }
        }
    }
    Say ("P2 " + $mm + ": rule entries naming C: " + ($planted + $copies -join '; ') +
         " | store group name now '" + $vstore.Name + "'" +
         ", searching " + $(if ($vstore.EnableSearching -eq $null) { 'inherit' } else { $vstore.EnableSearching }) +
         ", auto-type " + $(if ($vstore.EnableAutoType -eq $null) { 'inherit' } else { $vstore.EnableAutoType }) +
         " | C modified " + $vc.LastModificationTime.ToString('s') + " (was " + $t0.ToString('s') + ")" +
         " | X modified " + $vv.RootGroup.FindEntry($xe.Uuid, $true).LastModificationTime.ToString('s') +
         ", X history " + $vv.RootGroup.FindEntry($xe.Uuid, $true).History.UCount)
}

[IO.File]::WriteAllLines($OutFile, $lines)
