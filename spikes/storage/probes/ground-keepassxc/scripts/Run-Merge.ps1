param([string] $Set = 'fast', [string[]] $Versions)
. (Join-Path $PSScriptRoot 'Common.ps1')
if (-not $Versions) { $Versions = $script:Releases }
$base = Join-Path $script:Root "work\base-$Set"
if (-not (Test-Path (Join-Path $base 'A.kdbx'))) {
    if ($Set -eq 'extra') { & $script:Probe make2 $base fast | Out-Null } else { & $script:Probe make $base $Set | Out-Null }
}
$outDir = Join-Path $script:Root "work\merge-$Set"
New-Item -ItemType Directory -Force -Path $outDir | Out-Null
foreach ($v in $Versions) {
    foreach ($into in 'A', 'B') {
        $from = if ($into -eq 'A') { 'B' } else { 'A' }
        $cell = Join-Path $outDir "$v-into$into"
        Reset-Dir $cell
        Copy-Item (Join-Path $base 'A.kdbx') (Join-Path $cell 'A.kdbx')
        Copy-Item (Join-Path $base 'B.kdbx') (Join-Path $cell 'B.kdbx')
        $log = Join-Path $cell 'cli.log'
        $help = Invoke-Cli $v @('merge', '--help') $log $cell
        if ($help.Text -match 'dry-run') {
            $dry = Invoke-Cli $v @('merge', '-s', '--dry-run', (Join-Path $cell "$into.kdbx"), (Join-Path $cell "$from.kdbx")) $log $cell
            [System.IO.File]::WriteAllText((Join-Path $cell 'dry.txt'), $dry.Text)
        }
        $m = Invoke-Cli $v @('merge', '-s', (Join-Path $cell "$into.kdbx"), (Join-Path $cell "$from.kdbx")) $log $cell
        [System.IO.File]::WriteAllText((Join-Path $cell 'merge.txt'), $m.Text)
        $d = Invoke-Dump @((Join-Path $cell "$into.kdbx"))
        [System.IO.File]::WriteAllLines((Join-Path $cell 'result.txt'), [string[]] $d)
        Write-Host "$Set $v into $into exit $($m.Exit) took $([int]($m.End - $m.Start).TotalMilliseconds) ms"
    }
}
