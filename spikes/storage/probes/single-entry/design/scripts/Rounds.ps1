# Part 2 of the single-entry probe: twelve rounds of two copies both writing, large documents,
# history limits, Create new IDs, the state entry deleted, and what a read costs.
param([string]$KeePassExe, [string]$Work, [string]$OutFile, [string]$XcRoot, [string]$Only = '')
. (Join-Path $PSScriptRoot 'Lib.ps1')
[void][Reflection.Assembly]::LoadFrom($KeePassExe)
InitUuids
$out = New-Object 'System.Collections.Generic.List[string]'
function Say([string]$s) { $out.Add($s); Write-Host $s }
Say ("KeePassLib " + [KeePassLib.PwDefs]::VersionString + ", PowerShell " + $PSVersionTable.PSVersion)
# Changed on the branch: the two releases come from `tools\KeePassXC.ps1`, which provides and
# checks each one, in place of the paths into the keepassxc ground report's downloads under
# `$XcRoot`, which is no longer read. Dot-sourced in a child scope, because the tool sets
# `Set-StrictMode -Version Latest`, which would otherwise apply to this probe as well.
$xc = & {
    . (Join-Path $PSScriptRoot '..\..\..\..\..\..\tools\KeePassXC.ps1')
    @{
        '2.4.2' = Get-KeePassXCCli -Version '2.4.2'
        '2.8.0' = Get-KeePassXCCli -Version '2.8.0'
    }
}
function FreshDir([string]$name) {
    $dir = Join-Path $Work $name
    if (Test-Path $dir) { Remove-Item $dir -Recurse -Force }
    New-Item -ItemType Directory -Path $dir | Out-Null
    return $dir
}
# The operator's "continue without it": a version that names the missing versions as seen.
function Accept($db, $r, [DateTime]$t) {
    $notes = New-Object 'System.Collections.Generic.List[string]'
    $heads = @($r.Heads | Sort-Object Id)
    $acc = $heads[0]
    for ($i = 1; $i -lt $heads.Count; $i++) { $acc = NewVersion @($acc, $heads[$i]) (MergeTwo $acc $heads[$i] $notes) 'tmp' }
    $m = NewVersion $heads $acc.Data 'accepted'
    foreach ($id in $r.Missing) { $m.Anc[$id] = 0 }
    $m.Text = DocText $m
    WriteDoc $db $m $t
    return $m
}
$script:Refusals = 0
function LoadAndWrite([string]$path, [string]$key, [DateTime]$tLoad, [DateTime]$tWrite) {
    $db = OpenDb $path
    $r = Reconcile $db $null $tLoad
    $doc = $r.Doc
    if (-not $doc) { $script:Refusals++; $doc = Accept $db $r $tLoad }
    $set = @{}; $set[$key] = 'dev'
    WriteDoc $db (Edit $doc $set @() $key) $tWrite
    SaveDb $db; $db.Close()
}

if ($Only -eq '' -or $Only -eq 'rounds') {
    Say ''
    Say '== Twelve rounds: in each, copy A adds client.a<k>, copy B adds client.b<k>, then one merge whose result both copies take'
    $variants = @(
        @{ Name = 'KeePass Synchronize into A, plugin loaded (memory of the open copy), B 5 s after A'; Via = 'kp-mem'; Gap = 5; Hist = 10 },
        @{ Name = 'KeePass Synchronize into A, plugin loaded, B 5 s before A'; Via = 'kp-mem'; Gap = -5; Hist = 10 },
        @{ Name = 'KeePass Synchronize into A, plugin loaded, same second'; Via = 'kp-mem'; Gap = 0; Hist = 10 },
        @{ Name = 'KeePass Synchronize into A, plugin loaded, HistoryMaxItems=0, B 5 s after A'; Via = 'kp-mem'; Gap = 5; Hist = 0 },
        @{ Name = 'KeePass Synchronize into A, plugin loaded, HistoryMaxItems=0, B 5 s before A'; Via = 'kp-mem'; Gap = -5; Hist = 0 },
        @{ Name = 'KeePassLib Synchronize into B with no plugin at the merge (the shape of a merge on a phone), B 5 s after A'; Via = 'kp-nomem'; Gap = 5; Hist = 10 },
        @{ Name = 'KeePassLib Synchronize into B with no plugin at the merge, same second'; Via = 'kp-nomem'; Gap = 0; Hist = 10 },
        @{ Name = 'keepassxc-cli 2.8.0 merge into B, B 5 s after A'; Via = 'xc'; Gap = 5; Hist = 10 },
        @{ Name = 'keepassxc-cli 2.8.0 merge into B, B 5 s before A'; Via = 'xc'; Gap = -5; Hist = 10 },
        @{ Name = 'keepassxc-cli 2.8.0 merge into B, same second'; Via = 'xc'; Gap = 0; Hist = 10 }
    )
    $vn = 0
    foreach ($v in $variants) {
        $vn++
        $dir = FreshDir "rounds\v$vn"
        $script:Label = @{}; $script:Refusals = 0
        $d0 = NewVersion @() @{ 'client.c0' = @{ S = $null; V = 'dev' } } 'D0'
        $pa = Join-Path $dir 'A.kdbx'; $pb = Join-Path $dir 'B.kdbx'
        NewBase $pa $d0 $v.Hist
        Copy-Item $pa $pb
        $maxHist = 0
        for ($k = 1; $k -le 12; $k++) {
            $tA = 1000 * $k; $tB = $tA + $v.Gap
            LoadAndWrite $pa "client.a$k" (At ($tA - 100)) (At $tA)
            LoadAndWrite $pb "client.b$k" (At ($tA - 100)) (At $tB)
            if ($v.Via -eq 'kp-mem') {
                $t = OpenDb $pa; $s = OpenDb $pb
                $mem = (ReadState $t).Cur
                $t.MergeIn($s, [KeePassLib.PwMergeMethod]::Synchronize)
                $r = Reconcile $t $mem (At ($tA + 100))
                if (-not $r.Doc) { $script:Refusals++; [void](Accept $t $r (At ($tA + 100))) }
                SaveDb $t; $t.Close(); $s.Close()
                Copy-Item $pa $pb -Force
            }
            elseif ($v.Via -eq 'kp-nomem') {
                $t = OpenDb $pb; $s = OpenDb $pa
                $t.MergeIn($s, [KeePassLib.PwMergeMethod]::Synchronize)
                SaveDb $t; $t.Close(); $s.Close()
                Copy-Item $pb $pa -Force
            }
            else {
                $r = Invoke-Cli $xc['2.8.0'] @('merge', '-s', $pb, $pa) $dir
                if ($r.Exit -ne 0) { Say ("   keepassxc-cli exit " + $r.Exit + ": " + $r.Text) }
                Copy-Item $pb $pa -Force
            }
            $chk = OpenDb $pa; $h = (ReadState $chk).HistRaw; if ($h -gt $maxHist) { $maxHist = $h }; $chk.Close()
        }
        $db = OpenDb $pa
        $r = Reconcile $db $null (At 99000)
        $doc = $r.Doc
        if (-not $doc) { $script:Refusals++; $doc = Accept $db $r (At 99000) }
        $have = @($doc.Data.Keys | Where-Object { $_ -match '^client\.[ab]\d+$' }).Count
        $lost = @(1..12 | ForEach-Object { "client.a$_"; "client.b$_" } | Where-Object { -not $doc.Data.ContainsKey($_) }) -join ','
        $wit = @((Witnesses $db).Keys).Count
        Say ("   {0}" -f $v.Name)
        Say ("        data present at the end: {0} of 24; refusals on the way: {1}; lost: [{2}]; witnesses in the file: {3}; most history items seen on the state entry: {4}; generation {5}" -f $have, $script:Refusals, $lost, $wit, $maxHist, $doc.Gen)
        $db.Close()
    }
}

if ($Only -eq '' -or $Only -eq 'big') {
    Say ''
    Say '== Large documents: A writes (+10) and B writes (+20), each padding the document; then one merge'
    foreach ($size in 100000, 1000000, 7000000) {
        $dir = FreshDir "big\$size"
        $script:Label = @{}
        $d0 = NewVersion @() @{ 'client.c0' = @{ S = $null; V = 'dev' } } 'D0'
        $pa = Join-Path $dir 'A.kdbx'; $pb = Join-Path $dir 'B.kdbx'
        NewBase $pa $d0; Copy-Item $pa $pb
        $pad = New-Object string ('x', $size)
        $a = OpenDb $pa; WriteDoc $a (Edit $d0 @{ 'client.ca' = 'dev' } @() 'A1' $pad) (At 10); SaveDb $a; $a.Close()
        $b = OpenDb $pb; WriteDoc $b (Edit $d0 @{ 'client.cb' = 'dev' } @() 'B1' $pad) (At 20); SaveDb $b; $b.Close()
        Say ("   document of {0} characters; file A {1} bytes" -f $size, (Get-Item $pa).Length)
        $cell = Join-Path $dir 'kp'; New-Item -ItemType Directory $cell | Out-Null
        Copy-Item $pa $cell; Copy-Item $pb $cell
        $t = OpenDb (Join-Path $cell 'A.kdbx'); $s = OpenDb (Join-Path $cell 'B.kdbx')
        $sw = [Diagnostics.Stopwatch]::StartNew()
        $t.MergeIn($s, [KeePassLib.PwMergeMethod]::Synchronize)
        $ms = $sw.ElapsedMilliseconds
        SaveDb $t; $t.Close()
        $back = OpenDb (Join-Path $cell 'A.kdbx')
        $r = Reconcile $back $null (At 200) -NoWrite
        Say ("        KeePass Synchronize into A ({0} ms), saved, reopened: {1}; {2}; file {3} bytes" -f $ms, (Describe (ReadState $back)), $r.Status, (Get-Item (Join-Path $cell 'A.kdbx')).Length)
        $back.Close()
        if ($size -le 1000000) {
            foreach ($ver in '2.4.2', '2.8.0') {
                foreach ($into in 'A', 'B') {
                    $from = 'B'; if ($into -eq 'B') { $from = 'A' }
                    $cell = Join-Path $dir "xc-$ver-$into"; New-Item -ItemType Directory $cell | Out-Null
                    Copy-Item $pa $cell; Copy-Item $pb $cell
                    $sw = [Diagnostics.Stopwatch]::StartNew()
                    $c = Invoke-Cli $xc[$ver] @('merge', '-s', (Join-Path $cell "$into.kdbx"), (Join-Path $cell "$from.kdbx")) $cell
                    $ms = $sw.ElapsedMilliseconds
                    $back = OpenDb (Join-Path $cell "$into.kdbx")
                    $r = Reconcile $back $null (At 200) -NoWrite
                    Say ("        keepassxc-cli {0} into {1} (exit {2}, {3} ms): {4}; {5}; file {6} bytes" -f $ver, $into, $c.Exit, $ms, (Describe (ReadState $back)), $r.Status, (Get-Item (Join-Path $cell "$into.kdbx")).Length)
                    $back.Close()
                }
            }
        }
    }
}

if ($Only -eq '' -or $Only -eq 'newids') {
    Say ''
    Say '== Create new IDs: copy B (B1, +20) imported into copy A (A1, +10) with CreateNewUuids'
    $dir = FreshDir 'newids'
    $script:Label = @{}
    $d0 = NewVersion @() @{ 'client.c0' = @{ S = $null; V = 'dev' } } 'D0'
    $pa = Join-Path $dir 'A.kdbx'; $pb = Join-Path $dir 'B.kdbx'
    NewBase $pa $d0; Copy-Item $pa $pb
    $a = OpenDb $pa; WriteDoc $a (Edit $d0 @{ 'client.ca' = 'dev' } @() 'A1') (At 10); SaveDb $a; $a.Close()
    $b = OpenDb $pb; WriteDoc $b (Edit $d0 @{ 'client.cb' = 'dev' } @() 'B1') (At 20); SaveDb $b; $b.Close()
    $t = OpenDb $pa; $s = OpenDb $pb
    $t.MergeIn($s, [KeePassLib.PwMergeMethod]::CreateNewUuids)
    $states = 0; $wcopies = 0; $groups = 0
    foreach ($e in (AllEntries $t)) {
        $k = $e.Strings.ReadSafe('KPG.Kind')
        if ($k -eq 'state') { $states++ }
        if ($k -eq 'witness' -and -not $e.Uuid.Equals((WitnessUuid $e.Strings.ReadSafe('KPG.Version')))) { $wcopies++ }
    }
    foreach ($g in $t.RootGroup.GetGroups($true)) { if ($g.Name -eq 'KeePassGate') { $groups++ } }
    $r = Reconcile $t $null (At 200) -NoWrite
    Say ("   entries of kind state: {0} (one under the fixed identifier); groups named KeePassGate: {1}; witness copies under other identifiers: {2}" -f $states, $groups, $wcopies)
    Say ("   under the fixed identifiers: {0}; {1}" -f (Describe (ReadState $t)), $r.Status)
    $t.Close(); $s.Close()
}

if ($Only -eq '' -or $Only -eq 'delete') {
    Say ''
    Say '== The state entry deleted'
    $cases = [ordered]@{
        'A deletes it for good at +20 (deletion record); B unchanged' = @{ B = 0; Bin = $false }
        'A deletes it for good at +20; B wrote at +10' = @{ B = 10; Bin = $false }
        'A deletes it for good at +20; B wrote at +30' = @{ B = 30; Bin = $false }
        'A moves it to the recycle bin at +20 (time unchanged); B wrote at +30' = @{ B = 30; Bin = $true }
    }
    $cn = 0
    foreach ($name in $cases.Keys) {
        $cn++
        $c = $cases[$name]
        $dir = FreshDir "delete\c$cn"
        $script:Label = @{}
        $d0 = NewVersion @() @{ 'client.c0' = @{ S = $null; V = 'dev' } } 'D0'
        $pa = Join-Path $dir 'A.kdbx'; $pb = Join-Path $dir 'B.kdbx'
        NewBase $pa $d0; Copy-Item $pa $pb
        $a = OpenDb $pa
        $e = StateEntry $a
        if ($c.Bin) {
            $bin = New-Object KeePassLib.PwGroup($true, $true, 'Recycle Bin', [KeePassLib.PwIcon]::TrashBin)
            $a.RootGroup.AddGroup($bin, $true); $a.RecycleBinUuid = $bin.Uuid
            [void]$e.ParentGroup.Entries.Remove($e)
            $bin.AddEntry($e, $true, $true); $e.LocationChanged = (At 20)
        }
        else { RemoveEntryForGood $a $e (At 20) }
        SaveDb $a; $a.Close()
        if ($c.B -gt 0) { $b = OpenDb $pb; WriteDoc $b (Edit $d0 @{ 'client.cb' = 'dev' } @() 'B1') (At $c.B); SaveDb $b; $b.Close() }
        Say "   $name"
        foreach ($m in 'Synchronize', 'OverwriteIfNewer') {
            foreach ($into in 'A', 'B') {
                $from = 'B'; if ($into -eq 'B') { $from = 'A' }
                $cell = Join-Path $dir "kp-$m-$into"; New-Item -ItemType Directory $cell | Out-Null
                Copy-Item $pa $cell; Copy-Item $pb $cell
                $t = OpenDb (Join-Path $cell "$into.kdbx"); $s = OpenDb (Join-Path $cell "$from.kdbx")
                $t.MergeIn($s, [KeePassLib.PwMergeMethod]$m)
                $st = ReadState $t
                $where = 'absent'; if ($st.Entry) { $where = 'in group ' + $st.Entry.ParentGroup.Name }
                Say ("        KeePass {0,-16} into {1}: {2}; {3}; deletion records {4}" -f $m, $into, $where, (Describe $st), $t.DeletedObjects.UCount)
                $t.Close(); $s.Close()
            }
        }
        foreach ($into in 'A', 'B') {
            $from = 'B'; if ($into -eq 'B') { $from = 'A' }
            $cell = Join-Path $dir "xc-$into"; New-Item -ItemType Directory $cell | Out-Null
            Copy-Item $pa $cell; Copy-Item $pb $cell
            $r = Invoke-Cli $xc['2.8.0'] @('merge', '-s', (Join-Path $cell "$into.kdbx"), (Join-Path $cell "$from.kdbx")) $cell
            $t = OpenDb (Join-Path $cell "$into.kdbx")
            $st = ReadState $t
            $where = 'absent'; if ($st.Entry) { $where = 'in group ' + $st.Entry.ParentGroup.Name }
            Say ("        keepassxc-cli 2.8.0     into {0}: {1}; {2}; deletion records {3}" -f $into, $where, (Describe $st), $t.DeletedObjects.UCount)
            $t.Close()
        }
    }
    # Recovery: after a deletion for good, a synchronize with a backup, and the plugin putting it back.
    $dir = FreshDir 'delete\recover'
    $script:Label = @{}
    $d0 = NewVersion @() @{ 'client.c0' = @{ S = $null; V = 'dev' } } 'D0'
    $pa = Join-Path $dir 'A.kdbx'; $pb = Join-Path $dir 'backup.kdbx'
    NewBase $pa $d0; Copy-Item $pa $pb
    $a = OpenDb $pa; $mem = (ReadState $a).Cur
    RemoveEntryForGood $a (StateEntry $a) (At 20)
    $s = OpenDb $pb
    $a.MergeIn($s, [KeePassLib.PwMergeMethod]::Synchronize)
    Say ("   Recovery. Deleted for good at +20, then Synchronize with a backup that still holds it: " + (Describe (ReadState $a)) + "; deletion records " + $a.DeletedObjects.UCount)
    # the plugin puts the document it remembers back under the fixed identifier, at +40
    $e = New-Object KeePassLib.PwEntry($false, $false)
    $e.SetUuid($script:StateUuid, $false); SetTimes $e (At 40)
    $e.Strings.Set('Title', (Str 'KeePassGate data')); $e.Strings.Set('KPG.Kind', (Str 'state')); $e.Strings.Set('KPG.State', (Str $mem.Text))
    (StoreGroup $a).AddEntry($e, $true, $false)
    SaveDb $a; $a.Close(); $s.Close()
    $a = OpenDb $pa; $s = OpenDb $pb
    $a.MergeIn($s, [KeePassLib.PwMergeMethod]::Synchronize)
    Say ("   Put back by the plugin at +40 with the deletion record still in the file, saved, reopened, Synchronize with the backup again: " + (Describe (ReadState $a)) + "; deletion records " + $a.DeletedObjects.UCount)
    $s.MergeIn((OpenDb $pa), [KeePassLib.PwMergeMethod]::Synchronize)
    Say ("   The other direction (the backup open, the repaired file arriving): " + (Describe (ReadState $s)) + "; deletion records " + $s.DeletedObjects.UCount)
    $a.Close(); $s.Close()
}

if ($Only -eq '' -or $Only -eq 'cost') {
    Say ''
    Say '== What a read costs (PowerShell around .NET calls, so upper bounds)'
    $dir = FreshDir 'cost'
    $script:Label = @{}
    $d0 = NewVersion @() @{ 'client.c0' = @{ S = $null; V = 'dev' } } 'D0'
    $pa = Join-Path $dir 'A.kdbx'
    NewBase $pa $d0
    $a = OpenDb $pa
    $users = $a.RootGroup.Groups.GetAt(1)
    for ($g = 0; $g -lt 100; $g++) {
        $pg = New-Object KeePassLib.PwGroup($true, $true, "G$g", [KeePassLib.PwIcon]::Folder)
        $users.AddGroup($pg, $true)
        for ($i = 0; $i -lt 100; $i++) { $pe = New-Object KeePassLib.PwEntry($true, $true); $pg.AddEntry($pe, $true) }
    }
    $text = (New-Object string ('y', 50000))
    (StateEntry $a).Strings.Set('KPG.State', (Str $text))
    (StateEntry $a).Strings.Set('KPG.Secret', (New-Object KeePassLib.Security.ProtectedString($true, (New-Object string ('k', 3000)))))
    $sw = [Diagnostics.Stopwatch]::StartNew()
    for ($i = 0; $i -lt 200; $i++) { $e = $a.RootGroup.FindEntry($script:StateUuid, $true) }
    Say ("   FindEntry by identifier, state entry in the first group, 10,001 other entries behind it: {0:N3} ms a call" -f ($sw.Elapsed.TotalMilliseconds / 200))
    $last = $a.RootGroup.GetEntries($true).GetAt(10000).Uuid
    $sw.Restart()
    for ($i = 0; $i -lt 200; $i++) { $x = $a.RootGroup.FindEntry($last, $true) }
    Say ("   FindEntry by identifier, the last of 10,002 entries: {0:N3} ms a call" -f ($sw.Elapsed.TotalMilliseconds / 200))
    $e = StateEntry $a
    $p1 = $e.Strings.Get('KPG.State'); $p2 = $e.Strings.Get('KPG.State')
    Say ("   the string object handed back twice is the same object: " + [object]::ReferenceEquals($p1, $p2))
    $sw.Restart()
    for ($i = 0; $i -lt 200; $i++) { $t = $e.Strings.Get('KPG.State').ReadString() }
    Say ("   ReadString of an unprotected string of 50,000 characters: {0:N3} ms a call" -f ($sw.Elapsed.TotalMilliseconds / 200))
    $sw.Restart()
    for ($i = 0; $i -lt 200; $i++) { $t = $e.Strings.Get('KPG.Secret').ReadString() }
    Say ("   ReadString of a protected string of 3,000 characters: {0:N3} ms a call" -f ($sw.Elapsed.TotalMilliseconds / 200))
    $bytes = [Text.Encoding]::UTF8.GetBytes($text)
    $sw.Restart()
    for ($i = 0; $i -lt 200; $i++) { $h = $script:Sha.ComputeHash($bytes) }
    Say ("   SHA-256 over 50,000 bytes: {0:N3} ms a call" -f ($sw.Elapsed.TotalMilliseconds / 200))
    $a.Close()
}
[IO.File]::WriteAllLines($OutFile, [string[]]$out)
