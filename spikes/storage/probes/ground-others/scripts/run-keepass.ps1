param([string]$Set)
$S = Split-Path -Parent $PSScriptRoot
$W = Join-Path $S "work\$Set"
$O = Join-Path $S "work\out-keepass-$Set"
New-Item -ItemType Directory -Force $O | Out-Null
$kp = Join-Path $S 'scripts\kp.ps1'
$pairs = @(@('A','B'), @('B','A'), @('A','Btie'), @('Btie','A'), @('A','Bcd'), @('Bcd','A'))
$outs = @()
foreach ($p in $pairs) {
  $out = Join-Path $O ($p[0] + '+' + $p[1] + '.kdbx')
  powershell.exe -NoProfile -ExecutionPolicy Bypass -File $kp merge (Join-Path $W ($p[0] + '.kdbx')) (Join-Path $W ($p[1] + '.kdbx')) $out 'Synchronize'
  $outs += $out
}
powershell.exe -NoProfile -ExecutionPolicy Bypass -File $kp dump @outs
