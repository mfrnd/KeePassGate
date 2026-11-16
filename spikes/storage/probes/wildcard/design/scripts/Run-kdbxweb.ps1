# Written on this branch, not by the agent whose record it reproduces. The design's
# `recorded\kdbxweb.txt` came from `scripts\kw.js` and `Wc.exe sum` over the seven sets made with
# AES-KDF, so that kdbxweb can open them, in a `work-kw` directory the agent kept with its databases
# but without the commands. This runs what the record and those files imply: `Wc.exe make` with
# `WC_KDF=aes` into `work\kw\base\`; a kdbxweb resave of `U-A`; a kdbxweb merge of sets U, C, H, D
# and K in both directions, "into A" being A loaded and B merged into it, into `work\kw\out\`, in
# the order of the files' times; and one `Wc.exe sum` line per result, into `out\kdbxweb.txt`.
# Windows PowerShell 5.1.
$ErrorActionPreference = 'Stop'
$Root = Split-Path -Parent $PSScriptRoot
$Probe = Join-Path $Root 'probe\out\Wc.exe'
$Kw = Join-Path $PSScriptRoot 'kw.js'
$Mod = Join-Path $Root 'kdbxweb\node_modules\kdbxweb'
$Work = Join-Path $Root 'work\kw'
$Out = Join-Path $Root 'out'
$base = Join-Path $Work 'base'
$results = Join-Path $Work 'out'

if (Test-Path -LiteralPath $Work) { Remove-Item -LiteralPath $Work -Recurse -Force }
New-Item -ItemType Directory -Force -Path $results, $Out | Out-Null
$env:WC_KDF = 'aes'
& $Probe make $base | Out-Null
if ($LASTEXITCODE -ne 0) { throw 'Wc.exe make failed' }
$env:WC_NAMES = Join-Path $base 'names.txt'

$version = (Get-Content -Raw -LiteralPath (Join-Path $Mod 'package.json') | ConvertFrom-Json).version
$lines = New-Object System.Collections.Generic.List[string]
$lines.Add("## kdbxweb $version (KeeWeb's library), AES-KDF sets")
function Kw([string[]] $Arguments) {
    & node $Kw $Mod @Arguments | Out-Null
    if ($LASTEXITCODE -ne 0) { throw "kw.js $($Arguments[0]) failed" }
}
function Sum([string] $Label, [string] $File) {
    $line = & $Probe sum $Label $File
    if ($LASTEXITCODE -ne 0) { throw "Wc.exe sum failed: $File" }
    $lines.Add([string] $line)
}

$resaved = Join-Path $results 'U-A-resaved.kdbx'
Kw @('resave', (Join-Path $base 'U-A.kdbx'), $resaved)
Sum 'kdbxweb resave of U-A' $resaved
foreach ($set in 'U', 'C', 'H', 'D', 'K') {
    foreach ($into in 'A', 'B') {
        $from = if ($into -eq 'A') { 'B' } else { 'A' }
        $result = Join-Path $results "$set-into$into.kdbx"
        Kw @('merge', (Join-Path $base "$set-$into.kdbx"), (Join-Path $base "$set-$from.kdbx"), $result)
        Sum "kdbxweb $set into $into" $result
    }
}
[System.IO.File]::WriteAllLines((Join-Path $Out 'kdbxweb.txt'), $lines)
