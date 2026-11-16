# Survival chains for the store-entries option. Console only, invented databases only.
#   A: KeePass revokes c1; keepassxc-cli merges that copy into a stale copy; the revocation entry is
#      then deleted by hand, by pykeepass (no deletion record) or by keepassxc-cli rm twice (one).
#   B: pykeepass deletes the whole store group in the copy that revoked; KeePassLib merges a stale copy.
#   C: the store group deleted for good in one copy (X1), made again under the same UUID in another
#      (P1, P2); kdbxweb merges the two; KeePassLib synchronizes the result with a stale copy (Q).
$ErrorActionPreference = 'Stop'
$Root = Split-Path -Parent $PSScriptRoot
$Scripts = Join-Path $Root 'scripts'
$Base = Join-Path $Root 'work\base'
$Work = Join-Path $Root 'work\chains'
$Out = Join-Path $Root 'out'
$Surv = Join-Path $Scripts 'surv.ps1'
. (Join-Path $PSScriptRoot '..\..\..\..\..\..\tools\KeePassXC.ps1')
$Cli = [ordered]@{
    '2.4.2' = (Get-KeePassXCCli -Version '2.4.2')
    '2.8.0' = (Get-KeePassXCCli -Version '2.8.0')
}
$KdbxWeb = Join-Path $Root 'kdbxweb\node_modules\kdbxweb'
$Password = 'test'
$Methods = 'Synchronize', 'OverwriteIfNewer', 'OverwriteExisting', 'KeepExisting', 'CreateNewUuids'

function Invoke-Cli([string] $Version, [string[]] $Arguments, [string] $WorkDir) {
    $info = New-Object System.Diagnostics.ProcessStartInfo
    $info.FileName = $Cli[$Version]
    $info.Arguments = ($Arguments | ForEach-Object { '"' + $_ + '"' }) -join ' '
    $info.WorkingDirectory = $WorkDir
    $info.UseShellExecute = $false
    $info.CreateNoWindow = $true
    $info.RedirectStandardInput = $true
    $info.RedirectStandardOutput = $true
    $info.RedirectStandardError = $true
    $info.StandardInputEncoding = New-Object System.Text.UTF8Encoding $false
    $p = [System.Diagnostics.Process]::Start($info)
    $o = $p.StandardOutput.ReadToEndAsync()
    $e = $p.StandardError.ReadToEndAsync()
    foreach ($line in ($Password, $Password)) { $p.StandardInput.WriteLine($line) }
    $p.StandardInput.Close()
    $p.WaitForExit()
    [pscustomobject]@{ Exit = $p.ExitCode; Text = ($o.Result + $e.Result) }
}
function Dump([string[]] $Files) { [string[]] (powershell.exe -NoProfile -ExecutionPolicy Bypass -File $Surv dump @Files) }
function KpMerge($t, $s, $m, $o) { powershell.exe -NoProfile -ExecutionPolicy Bypass -File $Surv merge $t $s $m $o | Out-Null; if ($LASTEXITCODE) { throw "merge $t $s $m" } }
function Pk([string[]] $a) { [string] (uv run --no-project --with pykeepass python -I -X utf8 (Join-Path $Scripts 'pk_delete.py') @a 2>&1) }

if (Test-Path -LiteralPath $Work) { Remove-Item -LiteralPath $Work -Recurse -Force }
New-Item -ItemType Directory -Path $Work | Out-Null
$r = New-Object System.Collections.Generic.List[string]
foreach ($v in $Cli.Keys) { $s = Get-AuthenticodeSignature -LiteralPath $Cli[$v]; $r.Add("keepassxc-cli $v : $($s.Status) $($s.SignerCertificate.Subject)") }

# ---- chain A ----------------------------------------------------------------------------
foreach ($v in $Cli.Keys) {
    $d = Join-Path $Work "A-$v"; New-Item -ItemType Directory -Path $d | Out-Null
    Copy-Item (Join-Path $Base 'S.kdbx') (Join-Path $d 'S1.kdbx')
    Copy-Item (Join-Path $Base 'P.kdbx') (Join-Path $d 'P.kdbx')
    $m = Invoke-Cli $v @('merge', '-s', (Join-Path $d 'S1.kdbx'), (Join-Path $d 'P.kdbx')) $d
    $r.Add(''); $r.Add("## A $v step 1: keepassxc-cli merge, the revoking copy P into the stale copy S, exit $($m.Exit)")
    $r.AddRange([string[]](Dump @((Join-Path $d 'S1.kdbx'))))
    $r.Add(''); $r.Add("## A $v step 2a: pykeepass deletes 'KPG revoked client c1' in that result")
    $r.Add((Pk @('entry', (Join-Path $d 'S1.kdbx'), (Join-Path $d 'S2-pk.kdbx'), 'KPG revoked client c1')))
    $r.AddRange([string[]](Dump @((Join-Path $d 'S2-pk.kdbx'))))
    Copy-Item (Join-Path $d 'S1.kdbx') (Join-Path $d 'S3-xc.kdbx')
    $m1 = Invoke-Cli $v @('rm', (Join-Path $d 'S3-xc.kdbx'), 'KeePassGate/KPG revoked client c1') $d
    $m2 = Invoke-Cli $v @('rm', (Join-Path $d 'S3-xc.kdbx'), 'Recycle Bin/KPG revoked client c1') $d
    $r.Add(''); $r.Add("## A $v step 2b: keepassxc-cli rm, then rm again from the bin, in the result of step 1, exits $($m1.Exit) $($m2.Exit)")
    $r.AddRange([string[]](Dump @((Join-Path $d 'S3-xc.kdbx'))))
    foreach ($meth in $Methods) {
        KpMerge (Join-Path $d 'S2-pk.kdbx') (Join-Path $d 'P.kdbx') $meth (Join-Path $d "S2-pk+P-$meth.kdbx")
        KpMerge (Join-Path $d 'P.kdbx') (Join-Path $d 'S2-pk.kdbx') $meth (Join-Path $d "P+S2-pk-$meth.kdbx")
    }
    $r.Add(''); $r.Add("## A $v step 3: KeePassLib merges of the step 2a result with P, each method, both directions")
    $r.AddRange([string[]](Dump @($Methods | ForEach-Object { (Join-Path $d "S2-pk+P-$_.kdbx"); (Join-Path $d "P+S2-pk-$_.kdbx") })))
}

# ---- chain B ----------------------------------------------------------------------------
$d = Join-Path $Work 'B'; New-Item -ItemType Directory -Path $d | Out-Null
$r.Add(''); $r.Add('## B step 1: pykeepass deletes the group KeePassGate in the revoking copy P')
$r.Add((Pk @('group', (Join-Path $Base 'P.kdbx'), (Join-Path $d 'P3.kdbx'), 'KeePassGate')))
$r.AddRange([string[]](Dump @((Join-Path $d 'P3.kdbx'))))
foreach ($meth in $Methods) {
    KpMerge (Join-Path $d 'P3.kdbx') (Join-Path $Base 'S.kdbx') $meth (Join-Path $d "P3+S-$meth.kdbx")
    KpMerge (Join-Path $Base 'S.kdbx') (Join-Path $d 'P3.kdbx') $meth (Join-Path $d "S+P3-$meth.kdbx")
}
$r.Add(''); $r.Add('## B step 2: KeePassLib merges of that copy with the stale copy S, each method, both directions')
$r.AddRange([string[]](Dump @($Methods | ForEach-Object { (Join-Path $d "P3+S-$_.kdbx"); (Join-Path $d "S+P3-$_.kdbx") })))

# ---- chain C ----------------------------------------------------------------------------
$d = Join-Path $Work 'C'; New-Item -ItemType Directory -Path $d | Out-Null
$r.Add(''); $r.Add('## C step 1: kdbxweb merges, P2 (store made again, c5 revoked) and X1 (the old deletion record of the group)')
$r.Add([string] (node (Join-Path $Scripts 'kw_merge.js') $KdbxWeb (Join-Path $Base 'P2.kdbx') (Join-Path $Base 'X1.kdbx') (Join-Path $d 'K1.kdbx') 2>&1))
$r.Add([string] (node (Join-Path $Scripts 'kw_merge.js') $KdbxWeb (Join-Path $Base 'X1.kdbx') (Join-Path $Base 'P2.kdbx') (Join-Path $d 'K2.kdbx') 2>&1))
$r.Add([string] (node (Join-Path $Scripts 'kw_merge.js') $KdbxWeb (Join-Path $d 'K1.kdbx') (Join-Path $Base 'P2.kdbx') (Join-Path $d 'K3.kdbx') 2>&1))
$r.Add([string] (node (Join-Path $Scripts 'kw_merge.js') $KdbxWeb (Join-Path $Base 'Q.kdbx') (Join-Path $Base 'X1.kdbx') (Join-Path $d 'K4.kdbx') 2>&1))
$r.AddRange([string[]](Dump @((Join-Path $d 'K1.kdbx'), (Join-Path $d 'K2.kdbx'), (Join-Path $d 'K3.kdbx'), (Join-Path $d 'K4.kdbx'))))
KpMerge (Join-Path $d 'K1.kdbx') (Join-Path $Base 'Q.kdbx') Synchronize (Join-Path $d 'K1+Q-Sync.kdbx')
KpMerge (Join-Path $Base 'Q.kdbx') (Join-Path $d 'K1.kdbx') Synchronize (Join-Path $d 'Q+K1-Sync.kdbx')
KpMerge (Join-Path $Base 'P2.kdbx') (Join-Path $Base 'X1.kdbx') Synchronize (Join-Path $d 'P2+X1-Sync.kdbx')
KpMerge (Join-Path $d 'K4.kdbx') (Join-Path $Base 'P2.kdbx') Synchronize (Join-Path $d 'K4+P2-Sync.kdbx')
$r.Add(''); $r.Add('## C step 2: KeePassLib Synchronize of those results with the stale copy Q and with P2')
$r.AddRange([string[]](Dump @((Join-Path $d 'K1+Q-Sync.kdbx'), (Join-Path $d 'Q+K1-Sync.kdbx'), (Join-Path $d 'P2+X1-Sync.kdbx'), (Join-Path $d 'K4+P2-Sync.kdbx'))))

# C step 3: the revocation of c5 reaches the stale copy Q only as an entry (keepassxc-cli merge of P2
# into Q), then kdbxweb merges that copy with X1, then KeePassLib synchronizes the result with Q.
Copy-Item (Join-Path $Base 'Q.kdbx') (Join-Path $d 'Q5.kdbx')
Copy-Item (Join-Path $Base 'P2.kdbx') (Join-Path $d 'P2.kdbx')
$m = Invoke-Cli '2.8.0' @('merge', '-s', (Join-Path $d 'Q5.kdbx'), (Join-Path $d 'P2.kdbx')) $d
$r.Add(''); $r.Add("## C step 3a: keepassxc-cli 2.8.0 merge of P2 into the stale copy Q, exit $($m.Exit)")
$r.AddRange([string[]](Dump @((Join-Path $d 'Q5.kdbx'))))
$r.Add(''); $r.Add('## C step 3b: kdbxweb merges that result with X1')
$r.Add([string] (node (Join-Path $Scripts 'kw_merge.js') $KdbxWeb (Join-Path $d 'Q5.kdbx') (Join-Path $Base 'X1.kdbx') (Join-Path $d 'K5.kdbx') 2>&1))
$r.AddRange([string[]](Dump @((Join-Path $d 'K5.kdbx'))))
foreach ($meth in $Methods) {
    KpMerge (Join-Path $d 'K5.kdbx') (Join-Path $Base 'Q.kdbx') $meth (Join-Path $d "K5+Q-$meth.kdbx")
    KpMerge (Join-Path $Base 'Q.kdbx') (Join-Path $d 'K5.kdbx') $meth (Join-Path $d "Q+K5-$meth.kdbx")
}
$r.Add(''); $r.Add('## C step 3c: KeePassLib merges of the kdbxweb result with the stale copy Q, each method, both directions')
$r.AddRange([string[]](Dump @($Methods | ForEach-Object { (Join-Path $d "K5+Q-$_.kdbx"); (Join-Path $d "Q+K5-$_.kdbx") })))

[System.IO.File]::WriteAllLines((Join-Path $Out 'chains.txt'), $r)
Write-Host "done: $($r.Count) lines"
