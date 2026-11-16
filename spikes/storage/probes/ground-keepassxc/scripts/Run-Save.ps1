param([string] $Set = 'fast', [string[]] $Versions, [string] $Source = 'A.kdbx', [string] $Tag = 'save')
. (Join-Path $PSScriptRoot 'Common.ps1')
if (-not $Versions) { $Versions = $script:Releases }
$base = Join-Path $script:Root "work\base-$Set"
$outDir = Join-Path $script:Root "work\$Tag-$Set"
New-Item -ItemType Directory -Force -Path $outDir | Out-Null
foreach ($v in $Versions) {
    $cell = Join-Path $outDir $v
    Reset-Dir $cell
    $db = Join-Path $cell 'S.kdbx'
    Copy-Item (Join-Path $base $Source) $db
    $log = Join-Path $cell 'cli.log'
    $steps = New-Object System.Collections.Generic.List[string]

    $r = Invoke-Cli $v @('add', '-u', 'xc-user', '--url', 'https://example.invalid/xc', $db, 'General/xc-added') $log $cell
    $steps.Add("add exit $($r.Exit)")
    [System.IO.File]::WriteAllLines((Join-Path $cell 'after-add.txt'), [string[]] (Invoke-Dump @($db)))

    $plan = @(
        @('edit', '-t', 'Ordinary1-renamed', $db, 'General/Ordinary1'),
        @('edit', '-u', 'xc-user', $db, 'KPG/cd-both'),
        @('edit', '-u', 'xc-user', $db, 'KPG/prot-A'),
        @('edit', '-u', 'xc-user', $db, 'KPG/att-A'),
        @('edit', '-u', 'xc-user', $db, 'KPG/hist-many'),
        @('edit', '-u', 'xc-user', $db, 'KPG/hist-big'),
        @('rm', $db, 'General/Ordinary2'),
        @('rm', $db, 'Recycle Bin/bin-A')
    )
    if ([version] $v -ge [version] '2.6.6') { $plan += , @('mv', $db, 'General/Ordinary3', 'KPG/Sub') }
    foreach ($a in $plan) {
        $r = Invoke-Cli $v $a $log $cell
        $steps.Add("$($a[0]) $($a[-1]) exit $($r.Exit)")
    }
    [System.IO.File]::WriteAllLines((Join-Path $cell 'after-all.txt'), [string[]] (Invoke-Dump @($db)))
    [System.IO.File]::WriteAllLines((Join-Path $cell 'steps.txt'), [string[]] $steps)
    Write-Host "$v $($steps -join '; ')"
}
