# wildcard-survival: three doubts about option-wildcard.md, measured from a console.
#   e1  Delete Empty Groups while the snapshot sits in the recycle bin, the plugin's next write,
#       then kdbxweb (KeeWeb) merges both ways; KeePass Synchronize and KeePassXC as controls.
#   e2  the store group deleted for good, the documented recovery (a new store made under the same
#       fixed group identifier), then kdbxweb merges both ways; KeePass Synchronize as control.
#   e3  a copy that took the newest snapshot through a merge that carries no deletion records,
#       then a hand deletion of that snapshot that does leave a record.
# Invented databases only, AES-KDF at one round so that kdbxweb can open them. No window.
$ErrorActionPreference = 'Stop'
$Root = Split-Path -Parent $PSScriptRoot
$S = Split-Path -Parent $Root
$Wc = Join-Path $S 'design\probe\out\Wc.exe'
$Kp = Join-Path $Root 'scripts\kpops.ps1'
$Kw = Join-Path $Root 'scripts\kw.js'
$Mod = Join-Path $S 'design\kdbxweb\node_modules\kdbxweb'
. (Join-Path $PSScriptRoot '..\..\..\..\..\..\tools\KeePassXC.ps1')
$Cli = [ordered]@{
    '2.4.2' = (Get-KeePassXCCli -Version '2.4.2')
    '2.8.0' = (Get-KeePassXCCli -Version '2.8.0')
}
$W = Join-Path $Root 'work'
$O = Join-Path $Root 'out'
if (Test-Path -LiteralPath $W) { Remove-Item -LiteralPath $W -Recurse -Force }
New-Item -ItemType Directory -Path $W | Out-Null
New-Item -ItemType Directory -Force -Path $O | Out-Null
$log = New-Object System.Collections.Generic.List[string]
function Say([string] $t) { $log.Add($t); Write-Host $t }
function Sum([string] $label, [string] $file) { Say ([string] (& $Wc sum $label $file)) }
function KP([string] $op, [string] $in, [string] $out) {
    $r = & powershell.exe -NoProfile -ExecutionPolicy Bypass -File $Kp $op $in $out
    if ($LASTEXITCODE -ne 0) { throw "kpops $op failed" }
    foreach ($l in $r) { Say ('    ' + $l) }
}
function KW([string] $local, [string] $remote, [string] $out) {
    $r = & node $Kw $Mod merge $local $remote $out 2>&1
    if ($LASTEXITCODE -ne 0) { throw "kdbxweb failed: $r" }
}
function XC([string] $v, [string] $target, [string] $source) {
    $info = New-Object System.Diagnostics.ProcessStartInfo
    $info.FileName = $Cli[$v]
    $info.Arguments = '"merge" "-s" "' + $target + '" "' + $source + '"'
    $info.WorkingDirectory = $W
    $info.UseShellExecute = $false
    $info.CreateNoWindow = $true
    $info.RedirectStandardInput = $true
    $info.RedirectStandardOutput = $true
    $info.RedirectStandardError = $true
    $info.StandardInputEncoding = New-Object System.Text.UTF8Encoding $false
    $p = [System.Diagnostics.Process]::Start($info)
    $o = $p.StandardOutput.ReadToEndAsync(); $e = $p.StandardError.ReadToEndAsync()
    foreach ($line in ('probe-only', 'probe-only')) { $p.StandardInput.WriteLine($line) }
    $p.StandardInput.Close(); $p.WaitForExit()
    return $p.ExitCode
}

Say "## signatures"
foreach ($v in $Cli.Keys) { $sig = Get-AuthenticodeSignature -LiteralPath $Cli[$v]; Say "keepassxc-cli $v : $($sig.Status) $($sig.SignerCertificate.Subject)" }
Say ("kdbxweb " + (Get-Content -Raw -LiteralPath (Join-Path $Mod 'package.json') | ConvertFrom-Json).version)

$env:WC_KDF = 'aes'
$base = Join-Path $W 'base'
& $Wc make $base | Out-Null
$env:WC_NAMES = Join-Path $base 'names.txt'
Say ''
Say '## inputs'
foreach ($f in 'U-A', 'U-B', 'R-A', 'R-B') { Sum $f (Join-Path $base "$f.kdbx") }

# ---- e1 ---------------------------------------------------------------------------------------
Say ''
Say '## e1: Delete Empty Groups while the snapshot is in the bin, the next write, kdbxweb'
$e1 = Join-Path $W 'e1'; New-Item -ItemType Directory -Path $e1 | Out-Null
$ra = Join-Path $base 'R-A.kdbx'; $rb = Join-Path $base 'R-B.kdbx'
Say 'R-A before (records and groups):'; KP records $ra ''
$deg = Join-Path $e1 'R-A-deg.kdbx'
Say 'KeePass Delete Empty Groups on R-A:'; KP deg $ra $deg
Sum 'R-A after Delete Empty Groups' $deg
$w1 = Join-Path $e1 'R-A-deg-write.kdbx'
& $Wc write $deg $w1 | Out-Null
Sum 'then the plugin next write' $w1
Say 'its records and groups:'; KP records $w1 ''
$kwA = Join-Path $e1 'kw-intoA.kdbx'; KW $w1 $rb $kwA; Sum 'kdbxweb, stale R-B merged into it' $kwA
$kwB = Join-Path $e1 'kw-intoB.kdbx'; KW $rb $w1 $kwB; Sum 'kdbxweb, it merged into stale R-B' $kwB
Say 'records and groups of the first:'; KP records $kwA ''
$kpA = Join-Path $e1 'kp-intoA.kdbx'; & $Wc kpmerge $w1 $rb Synchronize $kpA | Out-Null; Sum 'control: KeePass Synchronize, R-B into it' $kpA
$kpB = Join-Path $e1 'kp-intoB.kdbx'; & $Wc kpmerge $rb $w1 Synchronize $kpB | Out-Null; Sum 'control: KeePass Synchronize, it into R-B' $kpB
foreach ($v in $Cli.Keys) {
    $t = Join-Path $e1 "xc-$v-intoA.kdbx"; Copy-Item $w1 $t; $x = XC $v $t $rb; Sum "control: KeePassXC $v, R-B into it (exit $x)" $t
    $t = Join-Path $e1 "xc-$v-intoB.kdbx"; Copy-Item $rb $t; $x = XC $v $t $w1; Sum "control: KeePassXC $v, it into R-B (exit $x)" $t
}
$again = Join-Path $e1 'kw-again.kdbx'; KW $kpB $kwA $again
Sum 'kdbxweb, the KeePass-repaired copy merged with the first kdbxweb result' $again
$rec = Join-Path $e1 'kp-recover.kdbx'; & $Wc kpmerge $kwB $w1 Synchronize $rec | Out-Null
Sum 'KeePass Synchronize of the kdbxweb result with the copy that still holds the store' $rec

# ---- e2 ---------------------------------------------------------------------------------------
Say ''
Say '## e2: the store group deleted for good, a new store under the same group identifier, kdbxweb'
$e2 = Join-Path $W 'e2'; New-Item -ItemType Directory -Path $e2 | Out-Null
$ua = Join-Path $base 'U-A.kdbx'; $ub = Join-Path $base 'U-B.kdbx'
$dg = Join-Path $e2 'U-A-delgroup.kdbx'; KP delgroup $ua $dg; Sum 'U-A, store group deleted for good' $dg
$cr = Join-Path $e2 'U-A-create.kdbx'; & $Wc write $dg $cr | Out-Null
Sum 'then a new store (the probe write over gone: new key, empty state, same group identifier)' $cr
Say 'its records and groups:'; KP records $cr ''
$k1 = Join-Path $e2 'kw-intoA.kdbx'; KW $cr $ub $k1; Sum 'kdbxweb, stale U-B merged into it' $k1
$k2 = Join-Path $e2 'kw-intoB.kdbx'; KW $ub $cr $k2; Sum 'kdbxweb, it merged into stale U-B' $k2
$p1 = Join-Path $e2 'kp-intoA.kdbx'; & $Wc kpmerge $cr $ub Synchronize $p1 | Out-Null; Sum 'control: KeePass Synchronize, U-B into it' $p1
$p2 = Join-Path $e2 'kp-intoB.kdbx'; & $Wc kpmerge $ub $cr Synchronize $p2 | Out-Null; Sum 'control: KeePass Synchronize, it into U-B' $p2

# ---- e3 ---------------------------------------------------------------------------------------
Say ''
Say '## e3: the newest snapshot arrived without its witness, then a hand deletion with a record'
$e3 = Join-Path $W 'e3'; New-Item -ItemType Directory -Path $e3 | Out-Null
foreach ($v in $Cli.Keys) {
    $y = Join-Path $e3 "y-xc-$v.kdbx"; Copy-Item $ub $y; $x = XC $v $y $ua
    Sum "U-A merged into stale U-B by KeePassXC $v (exit $x)" $y
    $yd = Join-Path $e3 "y-xc-$v-del.kdbx"; KP delnewest $y $yd
    Sum "then the newest snapshot deleted for good, with a record" $yd
}
$y = Join-Path $e3 'y-kp-oin.kdbx'; & $Wc kpmerge $ub $ua OverwriteIfNewer $y | Out-Null
Sum 'U-A merged into stale U-B by KeePass OverwriteIfNewer' $y
$yd = Join-Path $e3 'y-kp-oin-del.kdbx'; KP delnewest $y $yd
Sum 'then the newest snapshot deleted for good, with a record' $yd

[System.IO.File]::WriteAllLines((Join-Path $O 'run.txt'), $log)
Write-Host "done: $($log.Count) lines"
