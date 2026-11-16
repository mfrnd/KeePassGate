<#
.SYNOPSIS
    Builds the rollback trial of two access rule models and runs it from the branch, as `RUN.md`
    describes.

.DESCRIPTION
    Exports the eight files of the product the probe compiles, from the product's commit, builds
    `probe\Rollback.csproj` into `bin\`, and runs `Rollback.exe` once with the arguments the
    recorded run used:

        out\rollback.txt  1000 histories, trees of 30 nodes, 20 edits each, seed 20261116,
                          and the small world's histories of 4 edits

    The output holds no time and no path, so a fresh run is compared with `recorded\rollback.txt`
    line for line; the result of that comparison and the run's duration are printed to the
    console. Nothing is written but `bin\`, `obj\` and `out\`, and no database is made.

    Windows PowerShell 5.1, from the branch root:

        powershell.exe -NoProfile -ExecutionPolicy Bypass -File spikes\storage\probes\rollback-rule-models\run.ps1
#>
param()
$ErrorActionPreference = 'Stop'

$root = (Resolve-Path (Join-Path $PSScriptRoot '..\..\..\..')).Path
. (Join-Path $root 'tools\Product.ps1')

$here = $PSScriptRoot
$out = Join-Path $here 'out'
$bin = Join-Path $here 'bin'
[void](New-Item -ItemType Directory -Force $out)

# The eight files of the product the grant-only design's probe compiles, from the product's commit.
$null = Export-ProductFile -Path @(
    'src/KeePassGate/Acl/AclDocument.cs', 'src/KeePassGate/Acl/AclDocuments.cs',
    'src/KeePassGate/Acl/AclGrant.cs', 'src/KeePassGate/Acl/AclInheritance.cs',
    'src/KeePassGate/Acl/AclResolver.cs', 'src/KeePassGate/Acl/AclVerb.cs',
    'src/KeePassGate/Acl/VerbMask.cs', 'src/KeePassGate/Transport/JsonBody.cs')

& dotnet build (Join-Path $here 'probe\Rollback.csproj') -c Release -o $bin -v q -nologo
if ($LASTEXITCODE -ne 0) { throw "The build failed." }

$file = Join-Path $out 'rollback.txt'
$clock = [Diagnostics.Stopwatch]::StartNew()
& (Join-Path $bin 'Rollback.exe') $file 1000 30 20 20261116 4
if ($LASTEXITCODE -ne 0) { throw "Rollback.exe exited $LASTEXITCODE." }
Write-Host ([string]::Format([Globalization.CultureInfo]::InvariantCulture, 'trial: {0:0.0} s', $clock.Elapsed.TotalSeconds))

$record = Join-Path $here 'recorded\rollback.txt'
if (Test-Path $record) {
    $a = [IO.File]::ReadAllLines($record)
    $b = [IO.File]::ReadAllLines($file)
    $differ = 0
    for ($i = 0; $i -lt [Math]::Max($a.Length, $b.Length); $i++) {
        if ($i -ge $a.Length -or $i -ge $b.Length -or $a[$i] -cne $b[$i]) { $differ++ }
    }
    Write-Host "out\rollback.txt against recorded\rollback.txt: $($b.Length) and $($a.Length) lines, $differ differ"
}
