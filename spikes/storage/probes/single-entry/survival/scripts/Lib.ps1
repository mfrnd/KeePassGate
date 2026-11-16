# Library for the single-entry probe. Windows PowerShell 5.1, KeePassLib 2.62 loaded from a copy of
# KeePass.exe. No window. Invented content only.
$ErrorActionPreference = 'Stop'
Set-StrictMode -Version 2

$script:T0 = New-Object DateTime 2026, 1, 1, 0, 0, 0, ([DateTimeKind]::Utc)
$script:Password = 'probe-only'
$script:Label = @{}
$script:Window = 64
$script:Rng = New-Object System.Security.Cryptography.RNGCryptoServiceProvider
$script:Sha = [System.Security.Cryptography.SHA256]::Create()

function At([int]$s) { return $script:T0.AddSeconds($s) }
function Hex([byte[]]$b) { return ([BitConverter]::ToString($b)).Replace('-', '').ToLowerInvariant() }
function ShaBytes([string]$t) { return $script:Sha.ComputeHash([Text.Encoding]::UTF8.GetBytes($t)) }
function Uuid16([string]$t) { $b = [byte[]](ShaBytes $t)[0..15]; return New-Object KeePassLib.PwUuid(, $b) }
function Str([string]$v) { return New-Object KeePassLib.Security.ProtectedString($false, $v) }
function L([string]$vid) { if ($script:Label.ContainsKey($vid)) { return $script:Label[$vid] } return ('?' + $vid.Substring(0, 6)) }

$script:GroupUuid = $null
$script:StateUuid = $null
function InitUuids {
    $script:GroupUuid = Uuid16 'KPG/1/group/'
    $script:StateUuid = Uuid16 'KPG/1/state/'
}

function Key {
    $ck = New-Object KeePassLib.Keys.CompositeKey
    $ck.AddUserKey((New-Object KeePassLib.Keys.KcpPassword($script:Password)))
    return $ck
}
function OpenDb([string]$path) {
    $db = New-Object KeePassLib.PwDatabase
    $db.Open([KeePassLib.Serialization.IOConnectionInfo]::FromPath($path), (Key), $null)
    return $db
}
function SaveDb($db) { $db.Save($null) }

# ------------------------------------------------------------------ the document
# A version: Id (random), Gen, Anc (hashtable id -> gen), Data (hashtable key -> @{S=stamp; V=value}), Pad.
function NewVid {
    $b = New-Object byte[] 8
    $script:Rng.GetBytes($b)
    return (Hex $b)
}
function DocText($doc) {
    $sb = New-Object System.Text.StringBuilder
    [void]$sb.Append("KPG-STATE 1`n")
    [void]$sb.Append("id " + $doc.Id + "`n")
    [void]$sb.Append("gen " + $doc.Gen + "`n")
    $anc = @($doc.Anc.Keys | Sort-Object | ForEach-Object { $_ + ':' + $doc.Anc[$_] })
    [void]$sb.Append("anc " + ($anc -join ' ') + "`n")
    foreach ($k in ($doc.Data.Keys | Sort-Object)) {
        [void]$sb.Append("d " + $k + " " + $doc.Data[$k].S + " " + $doc.Data[$k].V + "`n")
    }
    if ($doc.Pad) { [void]$sb.Append("pad " + $doc.Pad + "`n") }
    $body = $sb.ToString()
    return $body + "sum " + (Hex (ShaBytes $body)) + "`n"
}
function ParseDoc([string]$text) {
    if ([string]::IsNullOrEmpty($text)) { return $null }
    $i = $text.LastIndexOf("sum ")
    if ($i -lt 0) { return $null }
    $body = $text.Substring(0, $i)
    $sum = $text.Substring($i + 4).Trim()
    if ((Hex (ShaBytes $body)) -ne $sum) { return $null }
    $doc = [pscustomobject]@{ Id = ''; Gen = 0; Anc = @{}; Data = @{}; Pad = ''; Text = $text }
    foreach ($line in $body.Split("`n")) {
        if ($line.StartsWith('id ')) { $doc.Id = $line.Substring(3) }
        elseif ($line.StartsWith('gen ')) { $doc.Gen = [int]$line.Substring(4) }
        elseif ($line.StartsWith('anc ')) {
            foreach ($p in $line.Substring(4).Split(' ', [StringSplitOptions]::RemoveEmptyEntries)) {
                $q = $p.Split(':'); $doc.Anc[$q[0]] = [int]$q[1]
            }
        }
        elseif ($line.StartsWith('d ')) {
            $q = $line.Substring(2).Split(' ', 3)
            $v = ''; if ($q.Length -gt 2) { $v = $q[2] }
            $doc.Data[$q[0]] = @{ S = $q[1]; V = $v }
        }
        elseif ($line.StartsWith('pad ')) { $doc.Pad = $line.Substring(4) }
    }
    if ($doc.Id -eq '') { return $null }
    return $doc
}
# Makes a version from its parents and a data map whose entries carry S = $null where the value is new.
function NewVersion([object[]]$parents, [hashtable]$data, [string]$label, [string]$pad = '') {
    $vid = NewVid
    $gen = 0; $anc = @{}
    foreach ($p in $parents) {
        if ($p.Gen -gt $gen) { $gen = $p.Gen }
        foreach ($k in $p.Anc.Keys) { $anc[$k] = $p.Anc[$k] }
        $anc[$p.Id] = $p.Gen
    }
    $gen = $gen + 1
    if ($anc.Count -gt $script:Window) {
        $keep = @($anc.Keys | Sort-Object { $anc[$_] } -Descending | Select-Object -First $script:Window)
        $n = @{}; foreach ($k in $keep) { $n[$k] = $anc[$k] }; $anc = $n
    }
    $d = @{}
    foreach ($k in $data.Keys) {
        $s = $data[$k].S; if (-not $s) { $s = $vid }
        $d[$k] = @{ S = $s; V = $data[$k].V }
    }
    $doc = [pscustomobject]@{ Id = $vid; Gen = $gen; Anc = $anc; Data = $d; Pad = $pad; Text = '' }
    $doc.Text = DocText $doc
    $script:Label[$vid] = $label
    return $doc
}
# An ordinary edit: $set is key -> value, $remove a list of keys.
function Edit($doc, [hashtable]$set, [string[]]$remove, [string]$label, [string]$pad = '') {
    $data = @{}
    foreach ($k in $doc.Data.Keys) { $data[$k] = @{ S = $doc.Data[$k].S; V = $doc.Data[$k].V } }
    foreach ($k in $set.Keys) {
        if ($data.ContainsKey($k) -and $data[$k].V -eq $set[$k]) { continue }
        $data[$k] = @{ S = $null; V = $set[$k] }
    }
    foreach ($k in $remove) { if ($k) { $data.Remove($k) } }
    return NewVersion @($doc) $data $label $pad
}
function Meet([string]$a, [string]$b) {
    $wa = $a.Split(' ', [StringSplitOptions]::RemoveEmptyEntries)
    $wb = $b.Split(' ', [StringSplitOptions]::RemoveEmptyEntries)
    return (@($wa | Where-Object { $wb -contains $_ }) -join ' ')
}
# Datum by datum, by the stamps: a value the other lineage saw and changed loses to the change; two
# changes that never saw each other give their meet.
function MergeTwo($d1, $d2, [System.Collections.Generic.List[string]]$notes) {
    $out = @{}
    $keys = @($d1.Data.Keys) + @($d2.Data.Keys) | Sort-Object -Unique
    foreach ($k in $keys) {
        $e1 = $null; $e2 = $null
        if ($d1.Data.ContainsKey($k)) { $e1 = $d1.Data[$k] }
        if ($d2.Data.ContainsKey($k)) { $e2 = $d2.Data[$k] }
        if ($e1 -and $e2) {
            if ($e1.S -eq $e2.S) { $out[$k] = @{ S = $e1.S; V = $e1.V } }
            elseif ($d2.Anc.ContainsKey($e1.S) -and -not $d1.Anc.ContainsKey($e2.S)) { $out[$k] = @{ S = $e2.S; V = $e2.V } }
            elseif ($d1.Anc.ContainsKey($e2.S) -and -not $d2.Anc.ContainsKey($e1.S)) { $out[$k] = @{ S = $e1.S; V = $e1.V } }
            elseif ($e1.V -eq $e2.V) { $out[$k] = @{ S = $e1.S; V = $e1.V } }
            else { $out[$k] = @{ S = $null; V = (Meet $e1.V $e2.V) }; $notes.Add("meet at $k") }
        }
        elseif ($e1) { if (-not $d2.Anc.ContainsKey($e1.S)) { $out[$k] = @{ S = $e1.S; V = $e1.V } } }
        else { if (-not $d1.Anc.ContainsKey($e2.S)) { $out[$k] = @{ S = $e2.S; V = $e2.V } } }
    }
    return $out
}
function DataText($doc) {
    if (-not $doc) { return '(none)' }
    return (@($doc.Data.Keys | Sort-Object | ForEach-Object { $_ + '=' + $doc.Data[$_].V }) -join '; ')
}

# ------------------------------------------------------------------ the entries
function SetTimes($e, [DateTime]$t) {
    $e.CreationTime = $t; $e.LastModificationTime = $t; $e.LastAccessTime = $t; $e.LocationChanged = $t
}
function StateEntry($db) { return $db.RootGroup.FindEntry($script:StateUuid, $true) }
function StoreGroup($db) { return $db.RootGroup.FindGroup($script:GroupUuid, $true) }
function WitnessUuid([string]$vid) { return Uuid16 ('KPG/1/witness/' + $vid) }
function AllEntries($db) { return $db.RootGroup.GetEntries($true) }
function Witnesses($db) {
    $r = @{}
    foreach ($e in (AllEntries $db)) {
        if ($e.Strings.ReadSafe('KPG.Kind') -ne 'witness') { continue }
        $vid = $e.Strings.ReadSafe('KPG.Version')
        # a witness counts only under the identifier its version derives; a copy is not one
        if ($e.Uuid.Equals((WitnessUuid $vid))) { $r[$vid] = $e }
    }
    return $r
}
function AddWitness($db, [string]$vid, [DateTime]$t) {
    $u = WitnessUuid $vid
    if ($db.RootGroup.FindEntry($u, $true)) { return }
    $e = New-Object KeePassLib.PwEntry($false, $false)
    $e.SetUuid($u, $false)
    SetTimes $e $t
    $e.Strings.Set('Title', (Str ('KPG version ' + $vid.Substring(0, 8))))
    $e.Strings.Set('UserName', (Str '')); $e.Strings.Set('Password', (Str '')); $e.Strings.Set('URL', (Str ''))
    $e.Strings.Set('Notes', (Str 'KeePassGate data. Managed on the KPG tab in KeePass.'))
    $e.Strings.Set('KPG.Kind', (Str 'witness'))
    $e.Strings.Set('KPG.Version', (Str $vid))
    (StoreGroup $db).AddEntry($e, $true, $false)
}
function RemoveEntryForGood($db, $e, [DateTime]$t) {
    [void]$e.ParentGroup.Entries.Remove($e)
    $db.DeletedObjects.Add((New-Object KeePassLib.PwDeletedObject($e.Uuid, $t)))
}
# The plugin's write: the document replaces the string, the time moves, no history item is made
# (unless -Backup, the variant that writes as KeePass's entry dialog does), history is let go, the
# version's witness is added and witnesses of versions it descends from are removed.
function WriteDoc($db, $doc, [DateTime]$t, [switch]$Backup) {
    $e = StateEntry $db
    if ($Backup) { $e.CreateBackup($db) } else { $e.History.Clear() }
    $e.Strings.Set('KPG.State', (Str $doc.Text))
    $e.LastModificationTime = $t
    AddWitness $db $doc.Id $t
    $w = Witnesses $db
    foreach ($vid in @($w.Keys)) {
        if ($doc.Anc.ContainsKey($vid)) { RemoveEntryForGood $db $w[$vid] $t }
    }
}
function ReadState($db) {
    $e = StateEntry $db
    $st = [pscustomobject]@{ Entry = $e; Cur = $null; CurTime = $null; Hist = @(); HistRaw = 0; Wit = @() }
    if ($e) {
        $st.Cur = ParseDoc ($e.Strings.ReadSafe('KPG.State'))
        $st.CurTime = $e.LastModificationTime
        $st.HistRaw = [int]$e.History.UCount
        $h = @()
        for ($i = 0; $i -lt $e.History.UCount; $i++) {
            $d = ParseDoc ($e.History.GetAt($i).Strings.ReadSafe('KPG.State'))
            if ($d) { $h += $d }
        }
        $st.Hist = $h
    }
    $st.Wit = @((Witnesses $db).Keys)
    return $st
}
function Describe($st) {
    if (-not $st.Entry) { return 'state entry absent' }
    $c = '(unreadable)'; if ($st.Cur) { $c = L $st.Cur.Id }
    $h = @($st.Hist | ForEach-Object { L $_.Id }) -join ','
    $w = @($st.Wit | ForEach-Object { L $_ } | Sort-Object) -join ','
    return ("current=$c history=[$h] witnesses=[$w]")
}
# The reconciling step. $mem is the document the plugin last served for this database, or $null.
# Returns a status word and the effective document.
function Reconcile($db, $mem, [DateTime]$t, [switch]$NoWrite) {
    $st = ReadState $db
    $cands = @{}
    if ($st.Cur) { $cands[$st.Cur.Id] = $st.Cur }
    foreach ($d in $st.Hist) { $cands[$d.Id] = $d }
    if ($mem) { $cands[$mem.Id] = $mem }
    if ($cands.Count -eq 0) { return [pscustomobject]@{ Status = 'REFUSE no readable version'; Doc = $null } }
    $heads = @()
    foreach ($id in $cands.Keys) {
        $isAnc = $false
        foreach ($o in $cands.Keys) { if ($o -ne $id -and $cands[$o].Anc.ContainsKey($id)) { $isAnc = $true } }
        if (-not $isAnc) { $heads += $cands[$id] }
    }
    $missing = @()
    foreach ($w in $st.Wit) {
        if ($cands.ContainsKey($w)) { continue }
        $known = $false
        foreach ($h in $heads) { if ($h.Anc.ContainsKey($w)) { $known = $true } }
        if (-not $known) { $missing += $w }
    }
    if ($missing.Count -gt 0) {
        $names = @($missing | ForEach-Object { L $_ }) -join ','
        return [pscustomobject]@{ Status = "REFUSE version missing: $names"; Doc = $null; Heads = $heads; Missing = $missing }
    }
    if ($heads.Count -eq 1) {
        $h = $heads[0]
        if ($st.Cur -and $st.Cur.Id -eq $h.Id) { return [pscustomobject]@{ Status = 'ok'; Doc = $h } }
        if (-not $NoWrite) { WriteDoc $db $h $t }
        return [pscustomobject]@{ Status = ('put back ' + (L $h.Id)); Doc = $h }
    }
    $notes = New-Object 'System.Collections.Generic.List[string]'
    $sorted = @($heads | Sort-Object Id)
    $data = $sorted[0].Data
    $acc = $sorted[0]
    for ($i = 1; $i -lt $sorted.Count; $i++) {
        $data = MergeTwo $acc $sorted[$i] $notes
        # an intermediate version so that a third head merges against the union of ancestries
        $acc = NewVersion @($acc, $sorted[$i]) $data 'tmp'
        $data = $acc.Data
    }
    $name = 'M(' + ((@($sorted | ForEach-Object { L $_.Id })) -join '+') + ')'
    $m = NewVersion $sorted $data $name
    if (-not $NoWrite) { WriteDoc $db $m $t }
    $n = ''; if ($notes.Count -gt 0) { $n = ' [' + ($notes -join ', ') + ']' }
    return [pscustomobject]@{ Status = ('merged ' + $name + $n); Doc = $m }
}

# ------------------------------------------------------------------ databases
function NewBase([string]$path, $doc, [int]$histMax = 10) {
    $db = New-Object KeePassLib.PwDatabase
    $db.New([KeePassLib.Serialization.IOConnectionInfo]::FromPath($path), (Key))
    $a = (New-Object KeePassLib.Cryptography.KeyDerivation.Argon2Kdf).GetDefaultParameters()
    $a.SetUInt64('I', [UInt64]2); $a.SetUInt64('M', [UInt64]1048576); $a.SetUInt32('P', [UInt32]1)
    $db.KdfParameters = $a
    $db.HistoryMaxItems = $histMax
    $db.RootGroup.LastModificationTime = (At 0)
    $g = New-Object KeePassLib.PwGroup($false, $false, 'KeePassGate', [KeePassLib.PwIcon]::TerminalEncrypted)
    $g.Uuid = $script:GroupUuid
    $g.CreationTime = (At 0); $g.LastModificationTime = (At 0); $g.LastAccessTime = (At 0); $g.LocationChanged = (At 0)
    $g.EnableSearching = $false; $g.EnableAutoType = $false
    $db.RootGroup.AddGroup($g, $true, $false)
    $e = New-Object KeePassLib.PwEntry($false, $false)
    $e.SetUuid($script:StateUuid, $false)
    SetTimes $e (At 0)
    $e.Strings.Set('Title', (Str 'KeePassGate data'))
    $e.Strings.Set('UserName', (Str '')); $e.Strings.Set('Password', (Str '')); $e.Strings.Set('URL', (Str ''))
    $e.Strings.Set('Notes', (Str 'KeePassGate data. Managed on the KPG tab in KeePass.'))
    $e.Strings.Set('KPG.Kind', (Str 'state'))
    $e.Strings.Set('KPG.State', (Str $doc.Text))
    $g.AddEntry($e, $true, $false)
    AddWitness $db $doc.Id (At 0)
    $u = New-Object KeePassLib.PwGroup($true, $true, 'Users', [KeePassLib.PwIcon]::Folder)
    $db.RootGroup.AddGroup($u, $true, $false)
    $x = New-Object KeePassLib.PwEntry($true, $true)
    $x.Strings.Set('Title', (Str 'invented entry X'))
    $u.AddEntry($x, $true, $false)
    $db.Save($null)
    $db.Close()
}

function Invoke-Cli([string]$exe, [string[]]$Arguments, [string]$WorkDir) {
    $info = New-Object System.Diagnostics.ProcessStartInfo
    $info.FileName = $exe
    $info.Arguments = ($Arguments | ForEach-Object { '"' + $_ + '"' }) -join ' '
    $info.WorkingDirectory = $WorkDir
    $info.UseShellExecute = $false
    $info.CreateNoWindow = $true
    $info.RedirectStandardInput = $true
    $info.RedirectStandardOutput = $true
    $info.RedirectStandardError = $true
    $p = [System.Diagnostics.Process]::Start($info)
    $o = $p.StandardOutput.ReadToEndAsync()
    $e = $p.StandardError.ReadToEndAsync()
    $p.StandardInput.WriteLine($script:Password)
    $p.StandardInput.Close()
    $p.WaitForExit()
    return [pscustomobject]@{ Exit = $p.ExitCode; Text = ($o.Result + $e.Result) }
}
