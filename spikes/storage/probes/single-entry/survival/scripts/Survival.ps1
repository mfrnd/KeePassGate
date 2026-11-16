# Critique probe for option single-entry, lens survival. Windows PowerShell 5.1 over KeePassLib 2.62
# (a copy of KeePass.exe), keepassxc-cli 2.8.0 and kdbxweb 2.1.1. No window. Invented content only.
# Lib.ps1 is the design's own probe library, copied unchanged; this script adds what it does not model.
param([string]$KeePassExe, [string]$Work, [string]$OutFile, [string]$XcCli, [string]$KwJs, [string]$KwMod, [string]$Only = '')
. (Join-Path $PSScriptRoot 'Lib.ps1')
[void][Reflection.Assembly]::LoadFrom($KeePassExe)
InitUuids
$script:Window = 256   # the design's figure (section 2.2); the design's own probe used 64
$out = New-Object 'System.Collections.Generic.List[string]'
function Say([string]$s) { $out.Add($s); Write-Host $s }
Say ("KeePassLib " + [KeePassLib.PwDefs]::VersionString + ", PowerShell " + $PSVersionTable.PSVersion + ", seen window " + $script:Window)

function FreshDir([string]$name) {
    $dir = Join-Path $Work $name
    if (Test-Path $dir) { Remove-Item $dir -Recurse -Force }
    New-Item -ItemType Directory -Path $dir | Out-Null
    return $dir
}
function Show($doc) {
    if (-not $doc) { return '(no document served)' }
    return (@($doc.Data.Keys | Where-Object { $_ -ne 'n' } | Sort-Object | ForEach-Object { $_ + '=' + $doc.Data[$_].V }) -join '; ')
}

# ---------------------------------------------------------------- readings of the bounded `seen`
function Knows($d, [string]$s) { return ($s -eq $d.Id) -or $d.Anc.ContainsKey($s) }
function GenOf([string]$s, $d1, $d2) {
    if ($s -eq $d1.Id) { return $d1.Gen }; if ($s -eq $d2.Id) { return $d2.Gen }
    if ($d1.Anc.ContainsKey($s)) { return $d1.Anc[$s] }; if ($d2.Anc.ContainsKey($s)) { return $d2.Anc[$s] }
    return -1
}
# The oldest generation a version's `seen` still holds, or -1 while nothing has been cut from it.
function Floor($d) {
    if ($d.Anc.Count -lt $script:Window) { return -1 }
    return [int](($d.Anc.Values | Measure-Object -Minimum).Minimum)
}
# Design section 3.4, last row: "a stamp older than what seen still holds".
function OldFor($d, [string]$s, $d1, $d2) {
    if (Knows $d $s) { return $false }
    $g = GenOf $s $d1 $d2
    if ($g -lt 0) { return $true }
    return ($g -lt (Floor $d))
}
function NarrowOf([string]$k, $e) {
    if ($k -like 'rule.*') { return 'REFUSED' }
    if ($k -like 'refused.*') { return $e.V }
    return $null
}
# The design's merge with its last row, read the most favorable way.
function MergeNarrow($d1, $d2, [System.Collections.Generic.List[string]]$notes) {
    $o = @{}
    $keys = @($d1.Data.Keys) + @($d2.Data.Keys) | Sort-Object -Unique
    foreach ($k in $keys) {
        $e1 = $null; $e2 = $null
        if ($d1.Data.ContainsKey($k)) { $e1 = $d1.Data[$k] }
        if ($d2.Data.ContainsKey($k)) { $e2 = $d2.Data[$k] }
        if ($e1 -and $e2) {
            if ($e1.S -eq $e2.S) { $o[$k] = @{ S = $e1.S; V = $e1.V } }
            elseif ((Knows $d2 $e1.S) -and -not (Knows $d1 $e2.S)) { $o[$k] = @{ S = $e2.S; V = $e2.V } }
            elseif ((Knows $d1 $e2.S) -and -not (Knows $d2 $e1.S)) { $o[$k] = @{ S = $e1.S; V = $e1.V } }
            elseif ((OldFor $d2 $e1.S $d1 $d2) -or (OldFor $d1 $e2.S $d1 $d2)) {
                $v = NarrowOf $k $e1; $notes.Add("old stamp at $k"); if ($null -ne $v) { $o[$k] = @{ S = $null; V = $v } }
            }
            elseif ($e1.V -eq $e2.V) { $o[$k] = @{ S = $e1.S; V = $e1.V } }
            else { $o[$k] = @{ S = $null; V = (Meet $e1.V $e2.V) }; $notes.Add("meet at $k") }
        }
        else {
            $e = $e1; $mine = $d1; $other = $d2
            if ($e2) { $e = $e2; $mine = $d2; $other = $d1 }
            if (Knows $other $e.S) { continue }
            if (OldFor $other $e.S $d1 $d2) { $v = NarrowOf $k $e; $notes.Add("old stamp at $k"); if ($null -ne $v) { $o[$k] = @{ S = $null; V = $v } }; continue }
            $o[$k] = @{ S = $e.S; V = $e.V }
        }
    }
    return $o
}
# The step of design section 3.3, with a choice of merge and of how heads are judged:
#   Heads 'anc' = "the candidates no other candidate has seen", as written;
#   Heads 'gen' = a candidate below another's window is taken as that one's ancestor.
function Step($db, $mem, [string]$Merge = 'probe', [string]$HeadMode = 'anc') {
    $st = ReadState $db
    $cands = @{}
    if ($st.Cur) { $cands[$st.Cur.Id] = $st.Cur }
    foreach ($d in $st.Hist) { $cands[$d.Id] = $d }
    if ($mem) { $cands[$mem.Id] = $mem }
    if ($cands.Count -eq 0) { return [pscustomobject]@{ Status = 'REFUSE no readable version'; Doc = $null; Heads = @(); Missing = @() } }
    $heads = @()
    foreach ($id in $cands.Keys) {
        $c = $cands[$id]; $isAnc = $false
        foreach ($o in $cands.Keys) {
            if ($o -eq $id) { continue }
            $x = $cands[$o]
            if ($x.Anc.ContainsKey($id)) { $isAnc = $true }
            if ($HeadMode -eq 'gen' -and $c.Gen -lt (Floor $x) -and $x.Gen -gt $c.Gen) { $isAnc = $true }
        }
        if (-not $isAnc) { $heads += $c }
    }
    $missing = @()
    foreach ($w in $st.Wit) {
        if ($cands.ContainsKey($w)) { continue }
        $known = $false
        foreach ($h in $heads) { if ($h.Anc.ContainsKey($w) -or $h.Data.ContainsKey('ack.' + $w)) { $known = $true } }
        if (-not $known) { $missing += $w }
    }
    if ($missing.Count -gt 0) {
        $names = @($missing | ForEach-Object { L $_ }) -join ','
        return [pscustomobject]@{ Status = "REFUSE version missing: $names"; Doc = $null; Heads = $heads; Missing = $missing }
    }
    if ($heads.Count -eq 1) {
        $h = $heads[0]
        if ($st.Cur -and $st.Cur.Id -eq $h.Id) { return [pscustomobject]@{ Status = ('ok, serves ' + (L $h.Id)); Doc = $h; Heads = $heads; Missing = @() } }
        return [pscustomobject]@{ Status = ('put back ' + (L $h.Id)); Doc = $h; Heads = $heads; Missing = @() }
    }
    $notes = New-Object 'System.Collections.Generic.List[string]'
    $sorted = @($heads | Sort-Object Id)
    $acc = $sorted[0]
    for ($i = 1; $i -lt $sorted.Count; $i++) {
        if ($Merge -eq 'narrow') { $data = MergeNarrow $acc $sorted[$i] $notes } else { $data = MergeTwo $acc $sorted[$i] $notes }
        $acc = NewVersion @($acc, $sorted[$i]) $data 'tmp'
    }
    $name = 'M(' + ((@($sorted | ForEach-Object { L $_.Id })) -join '+') + ')'
    $m = NewVersion $sorted $acc.Data $name
    $n = ''; if ($notes.Count -gt 0) { $n = ' [' + (($notes | Sort-Object -Unique) -join ', ') + ']' }
    return [pscustomobject]@{ Status = ('merged ' + $name + $n); Doc = $m; Heads = $heads; Missing = @() }
}
# "Continue with what is here", as the design's probe implements it: the missing versions named as seen.
function Accept($db, $r, [DateTime]$t, [switch]$Ack) {
    $notes = New-Object 'System.Collections.Generic.List[string]'
    $heads = @($r.Heads | Sort-Object Id)
    $acc = $heads[0]
    for ($i = 1; $i -lt $heads.Count; $i++) { $acc = NewVersion @($acc, $heads[$i]) (MergeTwo $acc $heads[$i] $notes) 'tmp' }
    $data = @{}; foreach ($k in $acc.Data.Keys) { $data[$k] = $acc.Data[$k] }
    if ($Ack) { foreach ($id in $r.Missing) { $data['ack.' + $id] = @{ S = $null; V = '1' } } }
    $m = NewVersion $heads $data 'accepted'
    if (-not $Ack) { foreach ($id in $r.Missing) { $m.Anc[$id] = 0 }; $m.Text = DocText $m }
    WriteDoc $db $m $t
    return $m
}
function NewBaseAes([string]$path, $doc) {
    NewBase $path $doc
    $db = OpenDb $path
    $p = (New-Object KeePassLib.Cryptography.KeyDerivation.AesKdf).GetDefaultParameters()
    $p.SetUInt64('R', [UInt64]1)
    $db.KdfParameters = $p
    SaveDb $db; $db.Close()
}
function Sync([string]$into, [string]$from, $method = [KeePassLib.PwMergeMethod]::Synchronize) {
    $t = OpenDb $into; $s = OpenDb $from
    $t.MergeIn($s, $method); SaveDb $t; $t.Close(); $s.Close()
}

# ================================================================ W1: a copy older than the window
if ($Only -eq '' -or $Only -eq 'w1') {
    Say ''
    Say '== W1: copy B stays at D0 while copy A writes N versions; then B is synchronized into A (KeePass, plugin loaded)'
    Say '   D0: profiles P, Q; rule N1 "list"; rule N2 "read list"; rule N3 "list"; client c0; c9 refused forever'
    Say '   A: v1 takes P off the list; v2 widens N1 to "read list"; v3 removes N3''s rule; v4 lifts c9; v5..vN change a counter'
    foreach ($n in @(20, 300)) {
        $dir = FreshDir "w1\n$n"
        $script:Label = @{}
        $d0 = NewVersion @() @{
            'profile.P' = @{ S = $null; V = 'on' }; 'profile.Q' = @{ S = $null; V = 'on' }
            'rule.N1' = @{ S = $null; V = 'list' }; 'rule.N2' = @{ S = $null; V = 'read list' }; 'rule.N3' = @{ S = $null; V = 'list' }
            'client.c0' = @{ S = $null; V = 'rec' }; 'refused.c9' = @{ S = $null; V = 'forever' } } 'D0'
        $pa = Join-Path $dir 'A.kdbx'; $pb = Join-Path $dir 'B.kdbx'
        NewBase $pa $d0; Copy-Item $pa $pb
        $db = OpenDb $pa
        $v = Edit $d0 @{} @('profile.P') 'v1'; WriteDoc $db $v (At 10)
        $v = Edit $v @{ 'rule.N1' = 'read list' } @() 'v2'; WriteDoc $db $v (At 20)
        $v = Edit $v @{} @('rule.N3') 'v3'; WriteDoc $db $v (At 30)
        $v = Edit $v @{} @('refused.c9') 'v4'; WriteDoc $db $v (At 40)
        for ($i = 5; $i -le $n; $i++) { $v = Edit $v @{ 'n' = "$i" } @() "v$i"; WriteDoc $db $v (At (10 * $i)) }
        SaveDb $db; $db.Close()
        $vn = $v
        Say ("   N = $n; A serves: " + (Show $vn) + "; seen holds " + $vn.Anc.Count + " versions, oldest generation " + (Floor $vn))
        foreach ($mode in @(@('probe', 'anc'), @('narrow', 'anc'), @('probe', 'gen'))) {
            $pm = Join-Path $dir ('A-' + $mode[0] + '-' + $mode[1] + '.kdbx')
            Copy-Item $pa $pm
            $t = OpenDb $pm; $s = OpenDb $pb
            $mem = (ReadState $t).Cur
            $t.MergeIn($s, [KeePassLib.PwMergeMethod]::Synchronize)
            $st = ReadState $t
            $r = Step $t $mem $mode[0] $mode[1]
            Say ("     merge " + $mode[0] + ", heads " + $mode[1] + ": after the merge " + (Describe $st) + " -> " + $r.Status)
            Say ("        the gate serves: " + (Show $r.Doc))
            $t.Close(); $s.Close()
        }
    }
    Say ''
    Say '== W1b: copy B forks at D0 and writes B1 (narrows N2 to "list", pairs cB); A writes N versions; B synchronized into A'
    foreach ($n in @(20, 300)) {
        $dir = FreshDir "w1b\n$n"
        $script:Label = @{}
        $d0 = NewVersion @() @{ 'rule.N2' = @{ S = $null; V = 'read list' }; 'client.c0' = @{ S = $null; V = 'rec' } } 'D0'
        $pa = Join-Path $dir 'A.kdbx'; $pb = Join-Path $dir 'B.kdbx'
        NewBase $pa $d0; Copy-Item $pa $pb
        $b = OpenDb $pb; $b1 = Edit $d0 @{ 'rule.N2' = 'list'; 'client.cB' = 'rec' } @() 'B1'; WriteDoc $b $b1 (At 5); SaveDb $b; $b.Close()
        $db = OpenDb $pa; $v = $d0
        for ($i = 1; $i -le $n; $i++) { $v = Edit $v @{ 'n' = "$i" } @() "v$i"; WriteDoc $db $v (At (10 * $i)) }
        SaveDb $db; $db.Close()
        foreach ($mode in @(@('probe', 'anc'), @('narrow', 'anc'), @('probe', 'gen'))) {
            $pm = Join-Path $dir ('A-' + $mode[0] + '-' + $mode[1] + '.kdbx')
            Copy-Item $pa $pm
            $t = OpenDb $pm; $s = OpenDb $pb
            $mem = (ReadState $t).Cur
            $t.MergeIn($s, [KeePassLib.PwMergeMethod]::Synchronize)
            $st = ReadState $t
            $r = Step $t $mem $mode[0] $mode[1]
            Say ("   N = $n, merge " + $mode[0] + ", heads " + $mode[1] + ": " + (Describe $st) + " -> " + $r.Status)
            Say ("        the gate serves: " + (Show $r.Doc))
            $t.Close(); $s.Close()
        }
    }
}

# ================================================================ W2: witnesses KeePassXC keeps
if ($Only -eq '' -or $Only -eq 'w2') {
    Say ''
    Say '== W2: the desktop (KeePassLib, the plugin''s writes) writes 300 versions; every 25 the laptop merges the'
    Say '   desktop''s file into its own with keepassxc-cli 2.8.0 merge -s; then KeePass with the plugin opens the laptop''s file'
    $dir = FreshDir 'w2'
    $script:Label = @{}
    $d0 = NewVersion @() @{ 'client.c0' = @{ S = $null; V = 'rec' } } 'D0'
    $pa = Join-Path $dir 'desktop.kdbx'; $pl = Join-Path $dir 'laptop.kdbx'
    NewBase $pa $d0; Copy-Item $pa $pl
    $v = $d0; $db = $null; $merges = 0
    for ($i = 1; $i -le 300; $i++) {
        if (-not $db) { $db = OpenDb $pa }
        $v = Edit $v @{ 'n' = "$i" } @() "v$i"; WriteDoc $db $v (At (10 * $i))
        if ($i % 25 -eq 0) {
            SaveDb $db; $db.Close(); $db = $null
            $r = Invoke-Cli $XcCli @('merge', '-s', $pl, $pa) $dir
            if ($r.Exit -ne 0) { Say ("   keepassxc-cli exit " + $r.Exit + ": " + $r.Text) } else { $merges++ }
        }
    }
    Say ("   keepassxc-cli merges that exited 0: $merges")
    foreach ($p in @($pa, $pl)) {
        $x = OpenDb $p
        $st = ReadState $x
        $r = Step $x $null 'probe' 'anc'
        Say ("   " + (Split-Path $p -Leaf) + ": current " + (L $st.Cur.Id) + ", history items after KeePassLib's load " + $st.HistRaw + ", witnesses " + $st.Wit.Count + ", deletion records " + $x.DeletedObjects.UCount + " -> " + $r.Status)
        if ($r.Missing) {
            $m = Accept $x $r (At 99999)
            $r2 = Step $x $null 'probe' 'anc'
            Say ("        after Continue: witnesses " + (ReadState $x).Wit.Count + ", the step then: " + $r2.Status)
        }
        $x.Close()
    }
}

# ================================================================ C1: Continue, then the lost version turns up
if ($Only -eq '' -or $Only -eq 'c1') {
    Say ''
    Say '== C1: A writes A1 (narrows N to "list", pairs cA) and B writes B1 (pairs cB) in the same second; B merged into A'
    Say '   by a KeePassLib Synchronize with no plugin at it; the plugin then refuses; the operator presses Continue; later'
    Say '   a copy of A taken before that merge (the laptop''s own file, a backup) is synchronized in'
    foreach ($ack in @($false, $true)) {
        $dir = FreshDir ("c1\ack-" + $ack)
        $script:Label = @{}
        $d0 = NewVersion @() @{ 'rule.N' = @{ S = $null; V = 'read list' }; 'client.c0' = @{ S = $null; V = 'rec' } } 'D0'
        $pa = Join-Path $dir 'A.kdbx'; $pb = Join-Path $dir 'B.kdbx'; $pk = Join-Path $dir 'A-before.kdbx'
        NewBase $pa $d0; Copy-Item $pa $pb
        $a = OpenDb $pa; $a1 = Edit $d0 @{ 'rule.N' = 'list'; 'client.cA' = 'rec' } @() 'A1'; WriteDoc $a $a1 (At 100); SaveDb $a; $a.Close()
        $b = OpenDb $pb; $b1 = Edit $d0 @{ 'client.cB' = 'rec' } @() 'B1'; WriteDoc $b $b1 (At 100); SaveDb $b; $b.Close()
        Copy-Item $pa $pk
        Sync $pa $pb
        $a = OpenDb $pa
        $r = Step $a $null
        Say ("   Continue " + $(if ($ack) { 'records the missing version as acknowledged (the change proposed here)' } else { 'names the missing version as seen (as designed)' }))
        Say ("     after the merge: " + (Describe (ReadState $a)) + " -> " + $r.Status)
        if ($r.Missing) { $m = Accept $a $r (At 200) -Ack:$ack; Say ("     after Continue the gate serves: " + (Show $m)) }
        SaveDb $a; $a.Close()
        Sync $pa $pk
        $a = OpenDb $pa
        $r2 = Step $a $null
        Say ("     after A-before is synchronized in: " + (Describe (ReadState $a)) + " -> " + $r2.Status)
        Say ("        the gate serves: " + (Show $r2.Doc))
        $a.Close()
    }
}

# ================================================================ K1: the fixed identifier and a deletion record, through kdbxweb
if ($Only -eq '' -or $Only -eq 'k1') {
    Say ''
    Say '== K1: the state entry deleted for good in A (a deletion record); B takes that file; A then puts the entry back'
    Say '   under the fixed identifier with a later time (design 6.4 way 3) and drops the record; kdbxweb merges A and B'
    $dir = FreshDir 'k1'
    $script:Label = @{}
    $d0 = NewVersion @() @{ 'client.c0' = @{ S = $null; V = 'rec' } } 'D0'
    $pa = Join-Path $dir 'A.kdbx'; $pb = Join-Path $dir 'B.kdbx'
    NewBaseAes $pa $d0
    $a = OpenDb $pa; $v1 = Edit $d0 @{ 'rule.N' = 'list' } @() 'v1'; WriteDoc $a $v1 (At 10)
    RemoveEntryForGood $a (StateEntry $a) (At 50); SaveDb $a; $a.Close()
    Copy-Item $pa $pb
    $a = OpenDb $pa
    $e = New-Object KeePassLib.PwEntry($false, $false)
    $e.SetUuid($script:StateUuid, $false)
    SetTimes $e (At 100)
    $e.Strings.Set('Title', (Str 'KeePassGate data'))
    $e.Strings.Set('UserName', (Str '')); $e.Strings.Set('Password', (Str '')); $e.Strings.Set('URL', (Str ''))
    $e.Strings.Set('Notes', (Str 'KeePassGate data. Managed on the KPG tab in KeePass.'))
    $e.Strings.Set('KPG.Kind', (Str 'state'))
    $e.Strings.Set('KPG.State', (Str $v1.Text))
    (StoreGroup $a).AddEntry($e, $true, $false)
    $keep = @(); foreach ($o in $a.DeletedObjects) { if (-not $o.Uuid.Equals($script:StateUuid)) { $keep += $o } }
    $a.DeletedObjects.Clear(); foreach ($o in $keep) { $a.DeletedObjects.Add($o) }
    SaveDb $a; $a.Close()
    $x = OpenDb $pa; Say ("   A after the restore: " + (Describe (ReadState $x)) + ", deletion records " + $x.DeletedObjects.UCount + " -> " + (Step $x $null).Status); $x.Close()
    $x = OpenDb $pb; Say ("   B: " + (Describe (ReadState $x)) + ", deletion records " + $x.DeletedObjects.UCount); $x.Close()
    foreach ($pair in @(@($pa, $pb, 'kdbxweb, B merged into A'), @($pb, $pa, 'kdbxweb, A merged into B'))) {
        $o = Join-Path $dir ('kw-' + [Guid]::NewGuid().ToString('N').Substring(0, 6) + '.kdbx')
        $res = & node $KwJs $KwMod merge $pair[0] $pair[1] $o 2>&1
        if ($LASTEXITCODE -ne 0) { Say ("   kdbxweb failed: " + $res); continue }
        $x = OpenDb $o; Say ("   " + $pair[2] + ": " + (Describe (ReadState $x)) + " -> " + (Step $x $null).Status); $x.Close()
    }
    foreach ($pair in @(@($pa, $pb, 'KeePass Synchronize, B into A'), @($pb, $pa, 'KeePass Synchronize, A into B'))) {
        $o = Join-Path $dir ('kp-' + [Guid]::NewGuid().ToString('N').Substring(0, 6) + '.kdbx')
        Copy-Item $pair[0] $o; Sync $o $pair[1]
        $x = OpenDb $o; Say ("   " + $pair[2] + ": " + (Describe (ReadState $x)) + " -> " + (Step $x $null).Status); $x.Close()
    }
}

# ================================================================ D1: Create new IDs, then Delete Duplicate Entries
if ($Only -eq '' -or $Only -eq 'd1') {
    Say ''
    Say '== D1: B is a copy of A at the same version (the phone''s copy); File, Import into A with Create new IDs; then'
    Say '   Tools, Database Tools, Delete Duplicate Entries (PwDatabase.DeleteDuplicateEntries)'
    $dir = FreshDir 'd1'
    $script:Label = @{}
    $d0 = NewVersion @() @{ 'client.c0' = @{ S = $null; V = 'rec' }; 'rule.N' = @{ S = $null; V = 'list' } } 'D0'
    $pa = Join-Path $dir 'A.kdbx'; $pb = Join-Path $dir 'B.kdbx'
    NewBase $pa $d0
    $a = OpenDb $pa; $v1 = Edit $d0 @{ 'rule.N' = 'read list' } @() 'v1'; WriteDoc $a $v1 (At 10); SaveDb $a; $a.Close()
    Copy-Item $pa $pb
    $a = OpenDb $pa; $b = OpenDb $pb
    $a.MergeIn($b, [KeePassLib.PwMergeMethod]::CreateNewUuids)
    $kinds = @((AllEntries $a) | ForEach-Object { $_.Strings.ReadSafe('KPG.Kind') } | Where-Object { $_ }) -join ','
    Say ("   after the import: KPG entries by kind [" + $kinds + "]; " + (Describe (ReadState $a)) + " -> " + (Step $a $null).Status)
    $n = $a.DeleteDuplicateEntries($null)
    $kinds = @((AllEntries $a) | ForEach-Object { $_.Strings.ReadSafe('KPG.Kind') } | Where-Object { $_ }) -join ','
    $fixed = [bool](StateEntry $a)
    Say ("   Delete Duplicate Entries removed $n; KPG entries left by kind [" + $kinds + "]; state entry under the fixed identifier present: $fixed")
    $del = @($a.DeletedObjects | Where-Object { $_.Uuid.Equals($script:StateUuid) }).Count
    Say ("   a deletion record names the fixed identifier: " + ($del -gt 0) + " -> " + (Step $a $null).Status)
    SaveDb $a; $a.Close(); $b.Close()
}

[IO.File]::WriteAllLines($OutFile, $out)
