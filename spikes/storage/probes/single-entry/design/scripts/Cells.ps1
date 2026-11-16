# Part 1 of the single-entry probe: one merge, every method and direction, KeePassLib 2.62 and
# keepassxc-cli. What the merge leaves of the two versions, and what the reconciling step makes of it.
param([string]$KeePassExe, [string]$Work, [string]$OutFile, [string]$XcRoot)
. (Join-Path $PSScriptRoot 'Lib.ps1')
[void][Reflection.Assembly]::LoadFrom($KeePassExe)
InitUuids
$out = New-Object 'System.Collections.Generic.List[string]'
function Say([string]$s) { $out.Add($s); Write-Host $s }
Say ("KeePassLib " + [KeePassLib.PwDefs]::VersionString + ", PowerShell " + $PSVersionTable.PSVersion + ", CLR " + [Environment]::Version)
Say ("group uuid " + $script:GroupUuid.ToHexString() + ", state entry uuid " + $script:StateUuid.ToHexString())

# Changed on the branch: the four releases come from `tools\KeePassXC.ps1`, which provides and
# checks each one, in place of the paths into the keepassxc ground report's downloads under
# `$XcRoot`, which is no longer read. Dot-sourced in a child scope, because the tool sets
# `Set-StrictMode -Version Latest`, which would otherwise apply to this probe as well.
$xc = & {
    . (Join-Path $PSScriptRoot '..\..\..\..\..\..\tools\KeePassXC.ps1')
    @{
        '2.4.2'  = Get-KeePassXCCli -Version '2.4.2'
        '2.6.6'  = Get-KeePassXCCli -Version '2.6.6'
        '2.7.12' = Get-KeePassXCCli -Version '2.7.12'
        '2.8.0'  = Get-KeePassXCCli -Version '2.8.0'
    }
}

# The base document and the two copies' changes.
$baseData = @{
    'rule.X' = @{ S = $null; V = 'read update' }
    'rule.Y' = @{ S = $null; V = 'read' }
    'rule.Z' = @{ S = $null; V = 'read update' }
    'profile.dev' = @{ S = $null; V = 'on' }
    'client.c0' = @{ S = $null; V = 'dev' }
}
$expected = 'client.c0=dev; client.ca=dev; client.cb=ops; profile.ops=on; rule.X=read; rule.Y=read update; rule.Z=list'
function ChangeA($d, [string]$label) {
    # A narrows X, changes Z, pairs a client, takes profile dev off the list
    return Edit $d @{ 'rule.X' = 'read'; 'rule.Z' = 'read list'; 'client.ca' = 'dev' } @('profile.dev') $label
}
function ChangeB($d, [string]$label) {
    # B widens Y, changes Z another way, pairs a client, adds profile ops
    return Edit $d @{ 'rule.Y' = 'read update'; 'rule.Z' = 'update list'; 'client.cb' = 'ops'; 'profile.ops' = 'on' } @() $label
}

# scenario name -> script that prepares A.kdbx and B.kdbx in a directory and returns the expected text
$scenarios = [ordered]@{}
$scenarios['S1 both wrote, A later (A +20, B +10)'] = { param($a, $b, $d0)
    WriteDoc $a (ChangeA $d0 'A1') (At 20); WriteDoc $b (ChangeB $d0 'B1') (At 10); $expected }
$scenarios['S2 both wrote, B later (A +10, B +20)'] = { param($a, $b, $d0)
    WriteDoc $a (ChangeA $d0 'A1') (At 10); WriteDoc $b (ChangeB $d0 'B1') (At 20); $expected }
$scenarios['S3 both wrote in the same second (+10, +10)'] = { param($a, $b, $d0)
    WriteDoc $a (ChangeA $d0 'A1') (At 10); WriteDoc $b (ChangeB $d0 'B1') (At 10); $expected }
$scenarios['S4 A wrote (+10); B holds the old document but its entry was edited by hand later (+30, title, with a history item as programs make one)'] = { param($a, $b, $d0)
    $a1 = ChangeA $d0 'A1'; WriteDoc $a $a1 (At 10)
    $e = StateEntry $b; $e.CreateBackup($b); $e.Strings.Set('Title', (Str 'KeePassGate data (edited)')); $e.LastModificationTime = (At 30)
    DataText $a1 }
$scenarios['S5 A wrote (+10); B unchanged'] = { param($a, $b, $d0)
    $a1 = ChangeA $d0 'A1'; WriteDoc $a $a1 (At 10); DataText $a1 }
$scenarios['S6 A wrote (+10); in B the document string was damaged by hand later (+30, with a history item)'] = { param($a, $b, $d0)
    $a1 = ChangeA $d0 'A1'; WriteDoc $a $a1 (At 10)
    $e = StateEntry $b; $e.CreateBackup($b); $e.Strings.Set('KPG.State', (Str ($d0.Text.Replace('read', 'read update delete')))); $e.LastModificationTime = (At 30)
    DataText $a1 }
$scenarios['S7 A wrote 12 times without history items (+10..+120); B wrote once (+15)'] = { param($a, $b, $d0)
    $d = $d0
    for ($i = 1; $i -le 12; $i++) {
        if ($i -eq 1) { $d = ChangeA $d 'A1' } else { $d = Edit $d @{ ("client.a$i") = 'dev' } @() "A$i" }
        WriteDoc $a $d (At (10 * $i))
    }
    WriteDoc $b (ChangeB $d0 'B1') (At 15)
    $x = $expected; for ($i = 2; $i -le 12; $i++) { $x = $x.Replace('client.c0=dev;', "client.a$i=dev; client.c0=dev;") }
    # sort the expectation the way DataText sorts
    (@($x.Split(';') | ForEach-Object { $_.Trim() } | Sort-Object) -join '; ') }
$scenarios['S8 as S7, but A wrote with a history item each time, as the entry dialog does'] = { param($a, $b, $d0)
    $d = $d0
    for ($i = 1; $i -le 12; $i++) {
        if ($i -eq 1) { $d = ChangeA $d 'A1' } else { $d = Edit $d @{ ("client.a$i") = 'dev' } @() "A$i" }
        WriteDoc $a $d (At (10 * $i)) -Backup
    }
    WriteDoc $b (ChangeB $d0 'B1') (At 15)
    $x = $expected; for ($i = 2; $i -le 12; $i++) { $x = $x.Replace('client.c0=dev;', "client.a$i=dev; client.c0=dev;") }
    (@($x.Split(';') | ForEach-Object { $_.Trim() } | Sort-Object) -join '; ') }

$methods = @('Synchronize', 'OverwriteIfNewer', 'OverwriteExisting', 'KeepExisting')
$n = 0
foreach ($name in $scenarios.Keys) {
    $n++
    $dir = Join-Path $Work ("cells\s$n")
    if (Test-Path $dir) { Remove-Item $dir -Recurse -Force }
    New-Item -ItemType Directory -Path $dir | Out-Null
    $script:Label = @{}
    $d0 = NewVersion @() $baseData 'D0'
    $basePath = Join-Path $dir 'base.kdbx'
    NewBase $basePath $d0
    Copy-Item $basePath (Join-Path $dir 'A.kdbx'); Copy-Item $basePath (Join-Path $dir 'B.kdbx')
    $a = OpenDb (Join-Path $dir 'A.kdbx'); $b = OpenDb (Join-Path $dir 'B.kdbx')
    $want = @(& $scenarios[$name] $a $b $d0)[-1]
    SaveDb $a; SaveDb $b; $a.Close(); $b.Close()
    Say ''
    Say "== $name"
    Say ("   A: " + (Describe (ReadState (OpenDb (Join-Path $dir 'A.kdbx')))))
    Say ("   B: " + (Describe (ReadState (OpenDb (Join-Path $dir 'B.kdbx')))))
    foreach ($m in $methods) {
        foreach ($into in 'A', 'B') {
            $from = 'B'; if ($into -eq 'B') { $from = 'A' }
            $cell = Join-Path $dir "kp-$m-into$into"
            New-Item -ItemType Directory -Path $cell | Out-Null
            Copy-Item (Join-Path $dir 'A.kdbx') $cell; Copy-Item (Join-Path $dir 'B.kdbx') $cell
            $t = OpenDb (Join-Path $cell "$into.kdbx"); $s = OpenDb (Join-Path $cell "$from.kdbx")
            $mem = (ReadState $t).Cur
            $t.MergeIn($s, [KeePassLib.PwMergeMethod]$m)
            $raw = Describe (ReadState $t)
            # without the plugin's memory of the open copy (the merge ran where no plugin was)
            $r0 = Reconcile $t $null (At 200) -NoWrite
            # with it (the merge ran in KeePass with the plugin loaded)
            $r1 = Reconcile $t $mem (At 200)
            SaveDb $t; $t.Close(); $s.Close()
            $back = OpenDb (Join-Path $cell "$into.kdbx")
            $r2 = Reconcile $back $null (At 300) -NoWrite
            $ok0 = ''; if ($r0.Doc) { if ((DataText $r0.Doc) -eq $want) { $ok0 = ' data complete' } else { $ok0 = ' DATA: ' + (DataText $r0.Doc) } }
            $ok1 = ''; if ($r1.Doc) { if ((DataText $r1.Doc) -eq $want) { $ok1 = ' data complete' } else { $ok1 = ' DATA: ' + (DataText $r1.Doc) } }
            Say ("   KeePass {0,-17} into {1}: {2}" -f $m, $into, $raw)
            Say ("        no memory: {0}{1}" -f $r0.Status, $ok0)
            Say ("        memory   : {0}{1}; after save and reopen: {2}, {3}" -f $r1.Status, $ok1, (Describe (ReadState $back)), $r2.Status)
            $back.Close()
        }
    }
    foreach ($v in @('2.4.2', '2.6.6', '2.7.12', '2.8.0')) {
        foreach ($into in 'A', 'B') {
            $from = 'B'; if ($into -eq 'B') { $from = 'A' }
            $cell = Join-Path $dir "xc-$v-into$into"
            New-Item -ItemType Directory -Path $cell | Out-Null
            Copy-Item (Join-Path $dir 'A.kdbx') $cell; Copy-Item (Join-Path $dir 'B.kdbx') $cell
            $r = Invoke-Cli $xc[$v] @('merge', '-s', (Join-Path $cell "$into.kdbx"), (Join-Path $cell "$from.kdbx")) $cell
            [IO.File]::WriteAllText((Join-Path $cell 'merge.txt'), $r.Text)
            $t = OpenDb (Join-Path $cell "$into.kdbx")
            $raw = Describe (ReadState $t)
            $r0 = Reconcile $t $null (At 200) -NoWrite
            $ok0 = ''; if ($r0.Doc) { if ((DataText $r0.Doc) -eq $want) { $ok0 = ' data complete' } else { $ok0 = ' DATA: ' + (DataText $r0.Doc) } }
            Say ("   KeePassXC {0,-6} into {1} (exit {2}): {3}" -f $v, $into, $r.Exit, $raw)
            Say ("        at the next load in KeePass: {0}{1}" -f $r0.Status, $ok0)
            $t.Close()
        }
    }
}
[IO.File]::WriteAllLines($OutFile, [string[]]$out)
