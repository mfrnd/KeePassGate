# Does a foreign document, merged under the fixed state-entry identifier (Synchronize / a merge that
# keeps identifiers), ADD a rule for a node that had none, rather than only narrow one that existed?
# The design (section 3.4) claims "its rules can only narrow". This tests that claim with the
# design's own document, merge and reconcile code (Lib.ps1), over KeePassLib 2.62 Synchronize.
param([string]$KeePassExe, [string]$Work, [string]$OutFile)
. (Join-Path $PSScriptRoot 'Lib.ps1')
[void][Reflection.Assembly]::LoadFrom($KeePassExe)
InitUuids
$out = New-Object 'System.Collections.Generic.List[string]'
function Say([string]$s) { $out.Add($s); Write-Host $s }
Say ("KeePassLib " + [KeePassLib.PwDefs]::VersionString)

# This database: an authority, a client holding profile `agent`, a SEAL on group G (default deny),
# a child node C under G with NO rule of its own (so it inherits G's deny: agent reaches nothing).
$victimData = @{
    'authority.fp1'  = @{ S = $null; V = 'ca-doc' }
    'client.cagent'  = @{ S = $null; V = 'agent' }
    'profile.agent'  = @{ S = $null; V = 'on' }
    'rule.G'         = @{ S = $null; V = 'DENY' }   # a seal: default deny on the group
}
# The foreign file: made by somebody who knows only the public fixed identifiers and the node UUID C
# and the profile name `agent` (both learnable by a prompt-injected client). It carries NO authority.
# Its document adds a rule on C granting agent read+protected+list. Random lineage: it has never seen
# this database's versions, and this database has never seen its stamps.
$script:Label = @{}
$dir = Join-Path $Work 'plant'
if (Test-Path $dir) { Remove-Item $dir -Recurse -Force }
New-Item -ItemType Directory -Path $dir | Out-Null

$victim0 = NewVersion @() $victimData 'V0'
$bp = Join-Path $dir 'victim.kdbx'
NewBase $bp $victim0

# Build the foreign database by hand: same fixed store group and state entry identifiers, a crafted
# document, its own witness. Reuse NewBase then overwrite the state string with the foreign document.
$foreignData = @{ 'rule.C' = @{ S = $null; V = 'read protected list' } }
$foreign0 = NewVersion @() $foreignData 'F0'    # random id, empty ancestry: never saw the victim
$fp = Join-Path $dir 'foreign.kdbx'
NewBase $fp $foreign0

Say ''
Say '== A foreign file carries only a crafted document that adds rule.C; it brings no authority.'
Say ("   victim doc holds: " + (DataText $victim0))
Say ("   foreign doc holds: " + (DataText $foreign0))

foreach ($m in 'Synchronize', 'OverwriteIfNewer', 'KeepExisting') {
    foreach ($foreignLater in $true, $false) {
        $cell = Join-Path $dir ("$m-" + ($(if ($foreignLater) { 'foreignLater' } else { 'victimLater' })))
        New-Item -ItemType Directory -Path $cell | Out-Null
        Copy-Item $bp (Join-Path $cell 'victim.kdbx')
        Copy-Item $fp (Join-Path $cell 'foreign.kdbx')
        # Set the two state entries' times: both already At 0 from NewBase; move them to model order.
        $v = OpenDb (Join-Path $cell 'victim.kdbx')
        (StateEntry $v).LastModificationTime = (At 100)
        SaveDb $v; $v.Close()
        $f = OpenDb (Join-Path $cell 'foreign.kdbx')
        (StateEntry $f).LastModificationTime = (At $(if ($foreignLater) { 200 } else { 50 }))
        SaveDb $f; $f.Close()
        # The operator synchronizes the victim with the foreign file (victim is open).
        $t = OpenDb (Join-Path $cell 'victim.kdbx'); $s = OpenDb (Join-Path $cell 'foreign.kdbx')
        $mem = (ReadState $t).Cur
        $t.MergeIn($s, [KeePassLib.PwMergeMethod]$m)
        $raw = Describe (ReadState $t)
        $r = Reconcile $t $mem (At 5000) -NoWrite
        $planted = ''
        if ($r.Doc -and $r.Doc.Data.ContainsKey('rule.C')) { $planted = 'PLANTED rule.C = ' + $r.Doc.Data['rule.C'].V }
        elseif ($r.Doc) { $planted = 'rule.C absent (not planted)' }
        $sealKept = ''
        if ($r.Doc -and $r.Doc.Data.ContainsKey('rule.G')) { $sealKept = 'rule.G=' + $r.Doc.Data['rule.G'].V }
        Say ''
        Say ("-- $m, foreign " + $(if ($foreignLater) { 'later' } else { 'earlier' }) + " than victim")
        Say ("   after merge: $raw")
        Say ("   step: " + $r.Status)
        Say ("   outcome: $planted ; $sealKept")
        $t.Close(); $s.Close()
    }
}
[IO.File]::WriteAllLines($OutFile, [string[]]$out)
