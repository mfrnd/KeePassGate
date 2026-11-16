param([string]$KeePassExe, [string]$OutFile)
# Windows PowerShell 5.1. Loads KeePass 2.62 as a library, builds an in-memory database with
# invented content, and records what KeePass's own duplication and duplicate removal do to a ruled
# group: today's rule in group custom data, and the design's marker entry with KPG.GroupRule.
$ErrorActionPreference = 'Stop'
[void][Reflection.Assembly]::LoadFrom($KeePassExe)
$lines = New-Object System.Collections.Generic.List[string]
function Say([string]$s) { $lines.Add($s) }
function Str([bool]$p, [string]$v) { return New-Object KeePassLib.Security.ProtectedString -ArgumentList $p, $v }

$db = New-Object KeePassLib.PwDatabase
$ioc = [KeePassLib.Serialization.IOConnectionInfo]::FromPath('C:\nonexistent\probe-copies.kdbx')
$key = New-Object KeePassLib.Keys.CompositeKey
$key.AddUserKey((New-Object KeePassLib.Keys.KcpPassword -ArgumentList 'invented-probe-password'))
$db.New($ioc, $key)
$root = $db.RootGroup

# A ruled group G: today's rule as group custom data, the design's marker entry beside it, and one
# ordinary entry inside that the rule narrows.
$g = New-Object KeePassLib.PwGroup -ArgumentList $true, $true, 'G', ([KeePassLib.PwIcon]::Folder)
$root.AddGroup($g, $true)
$g.CustomData.Set('KPG', '{"default":"deny"}')
$m = New-Object KeePassLib.PwEntry -ArgumentList $true, $true
$m.Strings.Set('Title', (Str $false 'KPG rule for this group'))
$ruleText = '{"v":"1","kind":"rule","node":"' + $g.Uuid.ToHexString() + '"}'
$m.Strings.Set('KPG.GroupRule', (Str $false $ruleText))
$g.AddEntry($m, $true)
$s = New-Object KeePassLib.PwEntry -ArgumentList $true, $true
$s.Strings.Set('Title', (Str $false 'invented secret'))
$s.Strings.Set('Password', (Str $true 'not-a-real-password'))
$g.AddEntry($s, $true)

# What MainForm.OnGroupDuplicate does, without the dialog: Duplicate, insert after the original.
# DuplicationForm.ApplyTo by default appends " - Copy" to each entry title; both settings are run.
foreach ($extend in @($true, $false)) {
    $copy = $g.Duplicate()
    # The parent setter is internal to KeePass.exe; AddGroup sets it and appends after G, which
    # keeps the original ahead of the copy in tree order, as MainForm's insert does.
    $root.AddGroup($copy, $true)
    if ($extend) {
        foreach ($pe in $copy.GetEntries($true)) {
            $t = $pe.Strings.ReadSafe('Title') + ' - Copy'
            $pe.Strings.Set('Title', (Str $false $t))
        }
    }
    $mc = $null
    foreach ($pe in $copy.Entries) { if ($pe.Strings.Get('KPG.GroupRule') -ne $null) { $mc = $pe } }
    Say ("extendTitle=" + $extend + ": copy group custom data KPG=" + $copy.CustomData.Get('KPG') +
        "; copy marker title='" + $mc.Strings.ReadSafe('Title') + "'" +
        "; copy marker KPG.GroupRule names original=" + ($mc.Strings.ReadSafe('KPG.GroupRule').Contains($g.Uuid.ToHexString())) +
        "; copy marker lastmod equals original=" + ($mc.LastModificationTime -eq $m.LastModificationTime))

    $n = $db.DeleteDuplicateEntries($null)
    $after = @($db.RootGroup.GetEntries($true) | ForEach-Object { $_.Uuid.ToHexString() })
    $origMarker = $after -contains $m.Uuid.ToHexString()
    $copyMarker = $after -contains $mc.Uuid.ToHexString()
    Say ("  Delete Duplicate Entries deleted " + $n + "; original marker kept=" + $origMarker +
        "; copy marker kept=" + $copyMarker + "; deletion records=" + $db.DeletedObjects.UCount)

    # Undo for the next round.
    [void]$root.Groups.Remove($copy)
    if (-not $origMarker) { $g.AddEntry($m, $true) }
    if (-not ($after -contains $s.Uuid.ToHexString())) { $g.AddEntry($s, $true) }
    $db.DeletedObjects.Clear()
}

# An entry ruled the way the design rules one: a KPG.Rule string naming the entry itself, beside
# today's rule in its custom data.
$x = New-Object KeePassLib.PwEntry -ArgumentList $true, $true
$x.Strings.Set('Title', (Str $false 'X'))
$xRule = '{"v":"1","kind":"rule","node":"' + $x.Uuid.ToHexString() + '"}'
$x.Strings.Set('KPG.Rule', (Str $false $xRule))
$x.CustomData.Set('KPG', '{"default":"deny"}')
$root.AddEntry($x, $true)
$xc = $x.Duplicate()
Say ("entry Duplicate: copy custom data KPG=" + $xc.CustomData.Get('KPG') +
    "; copy KPG.Rule names original=" + ($xc.Strings.ReadSafe('KPG.Rule').Contains($x.Uuid.ToHexString())) +
    "; copy has its own UUID=" + (-not $xc.Uuid.Equals($x.Uuid)))

Say ("KeePassLib " + [KeePassLib.PwDefs]::VersionString)
[IO.File]::WriteAllLines($OutFile, $lines)
