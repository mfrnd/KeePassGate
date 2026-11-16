<#
.SYNOPSIS
    Builds the survival critic's probe and runs both of its parts that `critique-survival.md`
    quotes, into `out\`.

.DESCRIPTION
    Added on this branch. The probe is `scripts\Survival.cs` and `scripts\Run-XcSavedStale.ps1`,
    both as they ran apart from the paths of the second, and the design's `Probe.exe`, which the
    script calls to read a database back. Each part writes the file of `out\` whose recorded copy
    is in `recorded\`:

        store      bin\Survival.exe work\store                       store-sequences.txt
        keepassxc  scripts\Run-XcSavedStale.ps1, over work\base      xcsaved.txt

    `work\base` holds `old.kdbx` and `anchored.kdbx` as the design's probe makes them with its slow
    KDF, `Probe.exe make <directory> 200 64 2`: the critic copied the two from the design's own
    run, and this makes them the same way, in `work\make`, and copies them over. The critic then
    shortened every run of 40 or more `A` in its output, the authority document that KeePassXC
    prints when it removes it, to `<n x A>`, and this does the same, with the critic's own
    expression. The `keepassxc` part took about four minutes here, a minute and a half of it the
    slow `make`; the `store` part takes seconds. `-Part` runs one of them only: `-Part store`.

    `Run-XcSavedStale.ps1` runs in PowerShell 7, `pwsh`, which is what ran it: it sets
    `ProcessStartInfo.StandardInputEncoding`, which .NET Framework does not have. Everything else
    runs here, in Windows PowerShell 5.1, from the branch root:

        powershell.exe -NoProfile -ExecutionPolicy Bypass -File spikes\storage\probes\outside-file\survival\run.ps1
#>
param([string[]] $Part = @('store', 'keepassxc'))

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

# Given through `-File`, a list arrives as one string, so `-Part store,keepassxc` is split here.
$Part = @($Part | ForEach-Object { $_ -split '[,\s]+' } | Where-Object { $_ })
foreach ($p in $Part) {
    if ($p -notin 'store', 'keepassxc') { throw "There is no part '$p': store, keepassxc." }
}

$root = (Resolve-Path (Join-Path $PSScriptRoot '..\..\..\..\..')).Path
. (Join-Path $root 'tools\KeePass.ps1')
. (Join-Path $root 'tools\KeePassXC.ps1')

$out = Join-Path $PSScriptRoot 'out'
$work = Join-Path $PSScriptRoot 'work'
New-Item -ItemType Directory -Force $out, $work | Out-Null
$utf8 = New-Object System.Text.UTF8Encoding $false

function Clear-Work([string] $Name) {
    $directory = Join-Path $work $Name
    if (Test-Path $directory) { Remove-Item -Recurse -Force $directory }
    $directory
}

if ($Part -contains 'store') {
    & dotnet build (Join-Path $PSScriptRoot 'scripts\Survival.csproj') -c Release -v quiet
    if ($LASTEXITCODE -ne 0) { throw "dotnet build exited with $LASTEXITCODE" }
    $clock = [Diagnostics.Stopwatch]::StartNew()
    $lines = @(& (Join-Path $PSScriptRoot 'bin\Survival.exe') (Clear-Work 'store'))
    if ($LASTEXITCODE -ne 0) { throw "Survival.exe exited with $LASTEXITCODE" }
    [IO.File]::WriteAllLines((Join-Path $out 'store-sequences.txt'), [string[]] $lines, $utf8)
    'store-sequences.txt: {0:0.0} s' -f $clock.Elapsed.TotalSeconds
}

if ($Part -contains 'keepassxc') {
    $null = Get-KeePassExe
    foreach ($release in '2.4.2', '2.6.6', '2.7.12', '2.8.0') { $null = Get-KeePassXCCli -Version $release }
    & dotnet build (Join-Path $PSScriptRoot '..\design\probe\Probe.csproj') -c Release -v quiet
    if ($LASTEXITCODE -ne 0) { throw "dotnet build exited with $LASTEXITCODE" }
    $probe = Join-Path $PSScriptRoot '..\design\probe\out\Probe.exe'

    $make = Clear-Work 'make'
    & $probe make $make 200 64 2 | Out-Null
    if ($LASTEXITCODE -ne 0) { throw "Probe.exe make exited with $LASTEXITCODE" }
    $base = Clear-Work 'base'
    New-Item -ItemType Directory $base | Out-Null
    Copy-Item -LiteralPath (Join-Path $make 'old.kdbx'), (Join-Path $make 'anchored.kdbx') -Destination $base

    $clock = [Diagnostics.Stopwatch]::StartNew()
    $script = (Join-Path $PSScriptRoot 'scripts\Run-XcSavedStale.ps1') -replace "'", "''"
    $target = (Join-Path $out 'xcsaved.txt') -replace "'", "''"
    & pwsh -NoProfile -ExecutionPolicy Bypass -Command "& '$script' 2>&1 | Tee-Object -FilePath '$target'"
    if ($LASTEXITCODE -ne 0) { throw "Run-XcSavedStale.ps1 exited with $LASTEXITCODE" }
    'xcsaved.txt: {0:0.0} s' -f $clock.Elapsed.TotalSeconds

    # The critic's own step after its run, as it ran it.
    $t = Get-Content -LiteralPath (Join-Path $out 'xcsaved.txt') -Raw -Encoding UTF8
    $t = [regex]::Replace($t, 'A{40,}', { param($m) '<' + $m.Value.Length + ' x A>' })
    [IO.File]::WriteAllText((Join-Path $out 'xcsaved.txt'), $t, $utf8)
}
