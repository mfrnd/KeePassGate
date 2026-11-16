<#
.SYNOPSIS
    Builds the plugin at the product's commit and runs the inventory's `sizes.ps1` over it, into
    `out\sizes.txt`, and `constants.ps1` into `out\constants.txt`.

.DESCRIPTION
    Added on this branch; `sizes.ps1` is the inventory's probe, unchanged. It takes the directory
    holding the plugin's build and the path of `KeePass.exe`. The recorded run gave it a copy of a
    Release build of the plugin dated 2026-11-02, which nothing can rebuild; this builds the plugin
    at the commit `tools\Product.ps1` names instead, from the product's own project and build
    properties exported into `.product\`, in Release, with its output under `work\`, which the branch
    ignores.

    The branch root is found by walking up to the directory holding `keepass.version`, so that this
    runs unchanged wherever the inventory's directory ends up.

    Run from the branch root in Windows PowerShell 5.1:

        powershell.exe -NoProfile -ExecutionPolicy Bypass -File spikes\storage\probes\ground-inventory\run.ps1
#>
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$root = $PSScriptRoot
while (-not (Test-Path (Join-Path $root 'keepass.version'))) {
    $parent = Split-Path -Parent $root
    if (-not $parent -or $parent -eq $root) { throw "No keepass.version above $PSScriptRoot." }
    $root = $parent
}
. (Join-Path $root 'tools\KeePass.ps1')
. (Join-Path $root 'tools\Product.ps1')
$KeePassExe = Get-KeePassExe

# The plugin's project and every file beside it, and the build properties and analyzer settings the
# project inherits from the product's root. `keepass.version` and `global.json` are not needed: the
# KeePass to compile against is given below, and `global.json` is read from the directory `dotnet`
# runs in rather than from the project's.
$commit = Get-ProductCommit
$listing = & git -C $root ls-tree -r --name-only $commit -- src/KeePassGate
if ($LASTEXITCODE -ne 0) { throw "git ls-tree exited with $LASTEXITCODE" }
$null = Export-ProductFile -Path (@('Directory.Build.props', '.editorconfig') + $listing)

$project = Join-Path (Get-ProductDirectory) 'src\KeePassGate\KeePassGate.csproj'
$plugin = Join-Path $PSScriptRoot 'work\plugin'
if (Test-Path $plugin) { Remove-Item -Recurse -Force $plugin }
& dotnet build $project -c Release -v quiet --nologo "-p:KeePassDevDir=$(Split-Path -Parent $KeePassExe)" `
    "-p:BaseIntermediateOutputPath=$plugin\obj\" -o "$plugin\bin"
if ($LASTEXITCODE -ne 0) { throw "dotnet build exited with $LASTEXITCODE" }

$out = Join-Path $PSScriptRoot 'out'
New-Item -ItemType Directory -Force $out | Out-Null

$clock = [Diagnostics.Stopwatch]::StartNew()
& powershell.exe -NoProfile -ExecutionPolicy Bypass -File (Join-Path $PSScriptRoot 'sizes.ps1') `
    -Bin "$plugin\bin" -KeePass $KeePassExe | Out-File -Encoding ascii (Join-Path $out 'sizes.txt')
if ($LASTEXITCODE -ne 0) { throw "sizes.ps1 exited with $LASTEXITCODE" }
'sizes: {0:0.0} s' -f $clock.Elapsed.TotalSeconds

$clock = [Diagnostics.Stopwatch]::StartNew()
& powershell.exe -NoProfile -ExecutionPolicy Bypass -File (Join-Path $PSScriptRoot 'constants.ps1') `
    -KeePass $KeePassExe | Out-File -Encoding ascii (Join-Path $out 'constants.txt')
if ($LASTEXITCODE -ne 0) { throw "constants.ps1 exited with $LASTEXITCODE" }
'constants: {0:0.0} s' -f $clock.Elapsed.TotalSeconds
