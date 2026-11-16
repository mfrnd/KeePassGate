# Critique probe for option single-entry (security and fit). Windows PowerShell 5.1, KeePassLib 2.62
# from a verified copy of KeePass.exe. No window, invented content, small databases.
# Part W: a copy that wrote nothing, merged after the other copy made more versions than `seen` holds.
# Part D: KeePass's default import of a copy of the same database, then Delete Duplicate Entries.
param([string]$KeePassExe, [string]$Work, [string]$OutFile)
. (Join-Path $PSScriptRoot 'Lib.ps1')
[void][Reflection.Assembly]::LoadFrom($KeePassExe)
InitUuids
$out = New-Object 'System.Collections.Generic.List[string]'
function Say([string]$s) { $out.Add($s); Write-Host $s }
Say ("KeePassLib " + [KeePassLib.PwDefs]::VersionString + ", PowerShell " + $PSVersionTable.PSVersion + ", window " + $script:Window)

# ---------------------------------------------------------------- Part W
$baseData = @{
    'rule.X' = @{ S = $null; V = 'read update' }
    'rule.Y' = @{ S = $null; V = 'read' }
    'profile.dev' = @{ S = $null; V = 'on' }
    'profile.old' = @{ S = $null; V = 'on' }
    'client.c0' = @{ S = $null; V = 'dev' }
}
foreach ($n in 10, 70) {
    foreach ($m in 'Synchronize', 'OverwriteIfNewer') {
        foreach ($into in 'A', 'B') {
            $script:Label = @{}
            $dir = Join-Path $Work ("w-$n-$m-$into")
            if (Test-Path $dir) { Remove-Item $dir -Recurse -Force }
            New-Item -ItemType Directory -Path $dir | Out-Null
            $d0 = NewVersion @() $baseData 'D0'
            $bp = Join-Path $dir 'base.kdbx'
            NewBase $bp $d0
            Copy-Item $bp (Join-Path $dir 'A.kdbx'); Copy-Item $bp (Join-Path $dir 'B.kdbx')
            $a = OpenDb (Join-Path $dir 'A.kdbx')
            # A: the first version takes profile old off the list and narrows X; then n-1 small writes
            $d = Edit $d0 @{ 'rule.X' = 'read' } @('profile.old') 'A1'
            WriteDoc $a $d (At 10)
            for ($i = 2; $i -le $n; $i++) {
                $d = Edit $d @{ ("client.a$i") = 'dev' } @() "A$i"
                WriteDoc $a $d (At (10 * $i))
            }
            $aCur = $d
            SaveDb $a; $a.Close()
            # B wrote nothing: a phone that only read the file
            $from = 'B'; if ($into -eq 'B') { $from = 'A' }
            $t = OpenDb (Join-Path $dir "$into.kdbx"); $s = OpenDb (Join-Path $dir "$from.kdbx")
            $mem = $null; if ($into -eq 'A') { $mem = $aCur }
            $t.MergeIn($s, [KeePassLib.PwMergeMethod]$m)
            $raw = Describe (ReadState $t)
            $r0 = Reconcile $t $null (At 5000) -NoWrite
            $r1 = Reconcile $t $mem (At 5000) -NoWrite
            $want = DataText $aCur
            $c0 = 'data as A wrote it'; if ($r0.Doc) { if ((DataText $r0.Doc) -ne $want) { $c0 = 'DATA: ' + (DataText $r0.Doc) } } else { $c0 = '' }
            $c1 = 'data as A wrote it'; if ($r1.Doc) { if ((DataText $r1.Doc) -ne $want) { $c1 = 'DATA: ' + (DataText $r1.Doc) } } else { $c1 = '' }
            $inAnc = $aCur.Anc.ContainsKey($d0.Id)
            Say ''
            Say ("== W n=$n versions in A after D0, $m into $into; D0 in A's seen: $inAnc")
            Say ("   after merge: $raw")
            Say ("   no memory : " + $r0.Status + "; " + $c0)
            if ($into -eq 'A') { Say ("   memory    : " + $r1.Status + "; " + $c1) }
            $t.Close(); $s.Close()
        }
    }
}

# ---------------------------------------------------------------- Part D
$script:Label = @{}
$dir = Join-Path $Work 'd'
if (Test-Path $dir) { Remove-Item $dir -Recurse -Force }
New-Item -ItemType Directory -Path $dir | Out-Null
$d0 = NewVersion @() $baseData 'D0'
$bp = Join-Path $dir 'base.kdbx'
NewBase $bp $d0
Copy-Item $bp (Join-Path $dir 'backup.kdbx')
$db = OpenDb $bp
$x = $null
foreach ($e in (AllEntries $db)) { if ($e.Strings.ReadSafe('Title') -eq 'invented entry X') { $x = $e } }
$xUuid = $x.Uuid
$usersUuid = $x.ParentGroup.Uuid
Say ''
Say ('== D before: X ' + $xUuid.ToHexString() + ' in group ' + $x.ParentGroup.Name + ' ' + $usersUuid.ToHexString())
$src = OpenDb (Join-Path $dir 'backup.kdbx')
$db.MergeIn($src, [KeePassLib.PwMergeMethod]::CreateNewUuids)
function Census($db, [string]$when) {
    $states = @(); $xs = @()
    foreach ($e in (AllEntries $db)) {
        if ($e.Strings.ReadSafe('KPG.Kind') -eq 'state') { $states += $e }
        if ($e.Strings.ReadSafe('Title') -eq 'invented entry X') { $xs += $e }
    }
    $fixed = [bool]($db.RootGroup.FindEntry($script:StateUuid, $true))
    Say ("   $when" + ": state entries " + $states.Count + ", one under the fixed identifier: $fixed; tombstones " + $db.DeletedObjects.UCount)
    foreach ($e in $xs) {
        $orig = $e.Uuid.Equals($xUuid)
        $pgOrig = $e.ParentGroup.Uuid.Equals($usersUuid)
        Say ("     X copy " + $e.Uuid.ToHexString().Substring(0, 8) + " original identifier: $orig; in the original group: $pgOrig; group " + $e.ParentGroup.Name + " under " + $e.ParentGroup.ParentGroup.Name)
    }
}
Census $db 'after import (Create new IDs)'
$r = Reconcile $db $null (At 5000) -NoWrite
Say ("     step: " + $r.Status)
$k = $db.DeleteDuplicateEntries($null)
Census $db ("after Delete Duplicate Entries ($k deleted)")
$r = Reconcile $db $null (At 5000) -NoWrite
Say ("     step: " + $r.Status)
SaveDb $db; $db.Close(); $src.Close()
$back = OpenDb $bp
Census $back 'saved and reopened'
$back.Close()
[IO.File]::WriteAllLines($OutFile, [string[]]$out)
