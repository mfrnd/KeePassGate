param([string]$Set)
$S = Split-Path -Parent $PSScriptRoot
$W = Join-Path $S "work\$Set"
$O = Join-Path $S "work\out-kdbxweb-$Set"
New-Item -ItemType Directory -Force $O | Out-Null
$M = Join-Path $S 'kdbxweb\node_modules\kdbxweb'
$kw = Join-Path $S 'scripts\kw.js'
node $kw $M resave (Join-Path $W 'A.kdbx') (Join-Path $O 'A-resaved.kdbx') | Out-Null
$pairs = @(@('A','B'), @('B','A'), @('A','Btie'), @('Btie','A'), @('A','Bcd'), @('Bcd','A'))
$outs = @((Join-Path $O 'A-resaved.kdbx'))
foreach ($p in $pairs) {
  $out = Join-Path $O ($p[0] + '+' + $p[1] + '.kdbx')
  node $kw $M merge (Join-Path $W ($p[0] + '.kdbx')) (Join-Path $W ($p[1] + '.kdbx')) $out | Out-Null
  $outs += $out
}
powershell.exe -NoProfile -ExecutionPolicy Bypass -File (Join-Path $S 'scripts\kp.ps1') dump @outs
