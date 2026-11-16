# Written on this branch, not by the critic. The critique's section 2.1 cites
# `out\group-records.txt` for which copies hold a deletion record naming the store group, and the
# critic recorded the file and not the command that made it. Its shape is that of
# `scripts\kpops.ps1 records` over eleven of the files `scripts\Run.ps1` and `scripts\Run-e4.ps1`
# leave in `work\`, keeping only the lines that name the store group's fixed identifier, under a
# line naming each file. This runs exactly that, over the same eleven files in the record's order,
# into `out\group-records.txt`. Windows PowerShell 5.1.
$ErrorActionPreference = 'Stop'
$Root = Split-Path -Parent $PSScriptRoot
$Kp = Join-Path $PSScriptRoot 'kpops.ps1'
$W = Join-Path $Root 'work'
$StoreGroup = '4B504747000000000000000000000001'
$files = 'e1\R-A-deg-write.kdbx', 'e1\kw-intoA.kdbx', 'e1\kp-intoA.kdbx', 'e1\kp-intoB.kdbx',
    'e1\xc-2.8.0-intoA.kdbx', 'e1\xc-2.8.0-intoB.kdbx', 'e1\kp-recover.kdbx',
    'e2\U-A-create.kdbx', 'e2\kp-intoA.kdbx', 'e4\C-A-bin-deg.kdbx', 'e4\kw-intoB.kdbx'

$lines = New-Object System.Collections.Generic.List[string]
foreach ($f in $files) {
    $lines.Add("== $f")
    $r = & powershell.exe -NoProfile -ExecutionPolicy Bypass -File $Kp records (Join-Path $W $f) 'none'
    if ($LASTEXITCODE -ne 0) { throw "kpops records failed: $f" }
    foreach ($l in $r) { if ($l -like "*$StoreGroup*") { $lines.Add($l) } }
}
[System.IO.File]::WriteAllLines((Join-Path $Root 'out\group-records.txt'), $lines)
