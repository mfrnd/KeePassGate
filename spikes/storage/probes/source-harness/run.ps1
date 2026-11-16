<#
.SYNOPSIS
    Runs every measurement of `source-harness` into `out\`: the calibration against the emulator's
    record, then the three measurements.

.DESCRIPTION
    `fetch.ps1` first provides the sources and tools and builds the harnesses. Then:

      1. the bases of the emulator measurement, made again by its own probe into `work\base\`
      2. calibrate.py, every recorded cell of both programs: out\calibrate-k2a.txt and
         out\calibrate-kpdx.txt; then repeat.py, the KeePassDX merge that did not end on the
         device, 30 times each way: out\repeat-kpdx.txt
      3. the kdbx-places family's phone bases into `work\places\bases\`, then places.py:
         out\places-table.md and out\places-<program>.txt
      4. StoreLab's histories into `work\store\` (-Seed, -Two, -Three), both harnesses over them,
         KeePassLib's Synchronize over them, and StoreLab's analysis: out\store-<program>-summary.txt
         and out\store-<program>-findings.txt
      5. StoreLab's long strings into `work\long\`, through both harnesses, KeePassLib and the
         analysis: out\long-<program>-summary.txt and -findings.txt
      6. formats.py: out\formats.txt

    -Steps takes a comma separated subset of calibrate, places, store, long and formats. Windows
    PowerShell 5.1, from the branch root:

        powershell.exe -NoProfile -ExecutionPolicy Bypass -File spikes\storage\probes\source-harness\run.ps1
#>
param(
    [string] $Steps = 'calibrate,places,store,long,formats',
    [int] $Seed = 20261116,
    [int] $Two = 2000,
    [int] $Three = 1000
)
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
if ($PSVersionTable.PSEdition -ne 'Desktop') { throw 'Windows PowerShell 5.1 only (powershell.exe).' }

$here = $PSScriptRoot
$work = Join-Path $here 'work'
$out = Join-Path $here 'out'
New-Item -ItemType Directory -Force $work, $out | Out-Null
$env:UV_EXCLUDE_NEWER = '2026-11-16T00:00:00Z'
$py = @('run', '--no-project', '--with', 'cryptography', '--with', 'argon2-cffi', '--with', 'lxml', 'python', '-I', '-X', 'utf8')
$probe = Join-Path $here 'dumper\bin\Release\net48\StoreProbe.exe'
$lab = Join-Path $here 'kplib\bin\Release\net48\StoreLab.exe'
$k2a = Join-Path $here 'k2a\bin\Release\net10.0\K2aHarness.exe'
$kpdx = Join-Path $here 'kpdx\bin\harness.cmd'
foreach ($f in $probe, $lab, $k2a, $kpdx) { if (-not (Test-Path $f)) { throw "$f is missing: run fetch.ps1 first" } }

function Invoke-Native([scriptblock] $Command) {
    $saved = $ErrorActionPreference
    $ErrorActionPreference = 'Continue'
    try { & $Command 2>&1 | ForEach-Object { "$_" } }
    finally { $ErrorActionPreference = $saved }
}

function Step([string] $Name, [scriptblock] $Body) {
    $clock = [Diagnostics.Stopwatch]::StartNew()
    & $Body
    Write-Host ([string]::Format([Globalization.CultureInfo]::InvariantCulture, '{0}: {1:0} s', $Name, $clock.Elapsed.TotalSeconds))
}

if ($Steps -match 'calibrate') {
    Step 'calibrate' {
        $base = Join-Path $work 'base'
        New-Item -ItemType Directory -Force $base | Out-Null
        Invoke-Native { & $probe make $base } | Out-Null
        Invoke-Native { & $probe makeow $base } | Out-Null
        Invoke-Native { & $probe makesync (Join-Path $base 'SOLD.kdbx') } | Out-Null
        Invoke-Native { & $probe newsync (Join-Path $base 'SOLD.kdbx') (Join-Path $base 'SNEW-fixed.kdbx') } | Out-Null
        Invoke-Native { uv @py (Join-Path $here 'calibrate.py') k2a kpdx } | Write-Host
        Invoke-Native { uv @py (Join-Path $here 'repeat.py') 30 30 } | Write-Host
    }
}

if ($Steps -match 'places') {
    Step 'places' {
        $places = Join-Path $here '..\kdbx-places'
        $kpl = Join-Path $places 'probe\bin\Release\net48\Kpl.exe'
        if (-not (Test-Path $kpl)) {
            Invoke-Native { dotnet build (Join-Path $places 'probe\Kpl.csproj') -c Release -v quiet -nologo } | Write-Host
        }
        Invoke-Native { uv @py (Join-Path $places 'android\make-bases.py') --kpl $kpl --work (Join-Path $work 'places\bases') } | Write-Host
        Invoke-Native { uv @py (Join-Path $here 'places.py') k2a kpdx } | Out-Null
    }
}

if ($Steps -match 'store') {
    Step 'store' {
        $store = Join-Path $work 'store'
        if (Test-Path $store) { Remove-Item -Recurse -Force $store }
        Invoke-Native { & $lab gen $store $Seed $Two $Three } | Write-Host
        Invoke-Native { uv @py (Join-Path $here 'batch.py') k2a (Join-Path $store 'jobs-k2a.txt') } | Write-Host
        Invoke-Native { uv @py (Join-Path $here 'batch.py') kpdx (Join-Path $store 'jobs-kpdx.txt') 4 } | Write-Host
        Invoke-Native { & $lab ref $store } | Write-Host
        Invoke-Native { & $lab analyze $store kplib k2a kpdx } | Out-Null
        foreach ($p in 'kplib', 'k2a', 'kpdx') {
            foreach ($k in 'summary', 'findings') {
                Copy-Item (Join-Path $store "$p-$k.txt") (Join-Path $out "store-$p-$k.txt") -Force
            }
        }
        Invoke-Native { uv @py (Join-Path $here 'store_table.py') } | Out-Null
    }
}

if ($Steps -match 'long') {
    Step 'long' {
        $long = Join-Path $work 'long'
        if (Test-Path $long) { Remove-Item -Recurse -Force $long }
        Invoke-Native { & $lab long $long } | Write-Host
        Invoke-Native { uv @py (Join-Path $here 'batch.py') k2a (Join-Path $long 'jobs-k2a.txt') } | Write-Host
        Invoke-Native { uv @py (Join-Path $here 'batch.py') kpdx (Join-Path $long 'jobs-kpdx.txt') 1 } | Write-Host
        Invoke-Native { & $lab ref $long } | Write-Host
        Invoke-Native { & $lab analyze $long kplib k2a kpdx } | Out-Null
        foreach ($p in 'kplib', 'k2a', 'kpdx') {
            foreach ($k in 'summary', 'findings') {
                Copy-Item (Join-Path $long "$p-$k.txt") (Join-Path $out "long-$p-$k.txt") -Force
            }
        }
    }
}

if ($Steps -match 'formats') {
    Step 'formats' { Invoke-Native { uv @py (Join-Path $here 'formats.py') k2a kpdx } | Write-Host }
}
