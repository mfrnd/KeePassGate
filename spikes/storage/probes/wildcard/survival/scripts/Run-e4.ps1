# wildcard-survival e4: two copies both wrote (the design's set C). In copy A the head is in the
# recycle bin and Delete Empty Groups has removed the empty store group, with no plugin write since.
# Then kdbxweb merges both ways, KeePass Synchronize as the control. Invented databases, AES-KDF.
$ErrorActionPreference = 'Stop'
$Root = Split-Path -Parent $PSScriptRoot
$S = Split-Path -Parent $Root
$Wc = Join-Path $S 'design\probe\out\Wc.exe'
$Kp = Join-Path $Root 'scripts\kpops.ps1'
$Kw = Join-Path $Root 'scripts\kw.js'
$Mod = Join-Path $S 'design\kdbxweb\node_modules\kdbxweb'
$W = Join-Path $Root 'work'
$base = Join-Path $W 'base'
$env:WC_NAMES = Join-Path $base 'names.txt'
$e4 = Join-Path $W 'e4'
if (Test-Path -LiteralPath $e4) { Remove-Item -LiteralPath $e4 -Recurse -Force }
New-Item -ItemType Directory -Path $e4 | Out-Null
$log = New-Object System.Collections.Generic.List[string]
function Say([string] $t) { $log.Add($t) }
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
Say '## e4: set C, the head of A in the bin and the store group removed by Delete Empty Groups, then kdbxweb'
$ca = Join-Path $base 'C-A.kdbx'; $cb = Join-Path $base 'C-B.kdbx'
Sum 'C-A' $ca
Sum 'C-B' $cb
$bin = Join-Path $e4 'C-A-bin.kdbx'; KP tobin $ca $bin; Sum 'C-A, its snapshot moved into the recycle bin' $bin
$deg = Join-Path $e4 'C-A-bin-deg.kdbx'; KP deg $bin $deg; Sum 'then Delete Empty Groups' $deg
$k1 = Join-Path $e4 'kw-intoA.kdbx'; KW $deg $cb $k1; Sum 'kdbxweb, C-B merged into it' $k1
$k2 = Join-Path $e4 'kw-intoB.kdbx'; KW $cb $deg $k2; Sum 'kdbxweb, it merged into C-B' $k2
$p1 = Join-Path $e4 'kp-intoA.kdbx'; & $Wc kpmerge $deg $cb Synchronize $p1 | Out-Null; Sum 'control: KeePass Synchronize, C-B into it' $p1
$p2 = Join-Path $e4 'kp-intoB.kdbx'; & $Wc kpmerge $cb $deg Synchronize $p2 | Out-Null; Sum 'control: KeePass Synchronize, it into C-B' $p2
$n1 = Join-Path $e4 'kw-intoA-next.kdbx'; & $Wc write $k1 $n1 | Out-Null; Sum 'the kdbxweb result into A, then the plugin next write' $n1
[System.IO.File]::WriteAllLines((Join-Path $Root 'out\e4.txt'), $log)
