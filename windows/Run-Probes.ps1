<#
.SYNOPSIS
    Every probe on .NET Framework, one process each, into one file of lines: the baseline that
    the Mono run is read against.

.DESCRIPTION
    Builds nothing: run `dotnet build -c Release` in `probes\MonoProbe` first. Writes the same
    format `wsl/run-probes.sh` writes, so `Compare-Runs.ps1` can put the two side by side. The
    forms probe makes a window handle and never shows the window.

.EXAMPLE
    .\windows\Run-Probes.ps1 -Out results\net48.txt
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory)] [string] $Out,
    [string] $Build = (Join-Path $PSScriptRoot '..\probes\MonoProbe\bin\Release\net48')
)

$ErrorActionPreference = 'Stop'
$probe = Join-Path $Build 'MonoProbe.exe'
$lines = New-Object System.Collections.Generic.List[string]
foreach ($name in (& $probe list)) {
    $lines.Add("### $name")
    $output = & $probe $name 2>&1
    $lines.AddRange([string[]]@($output | ForEach-Object { "$_" }))
    $lines.Add("exit $LASTEXITCODE")
}

$directory = Split-Path -Parent $Out
if ($directory) {
    New-Item -ItemType Directory -Force $directory | Out-Null
}

# LF and no byte order mark, as the Mono run writes, so the two compare line for line.
[IO.File]::WriteAllText((Join-Path (Resolve-Path -LiteralPath .) $Out), (($lines -join "`n") + "`n"))
Write-Host "wrote $Out"
