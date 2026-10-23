<#
.SYNOPSIS
    Asks every question of the probe against a throwaway KeePass and writes one file per question.

.DESCRIPTION
    Copies the pinned KeePass from `.keepass\<version>\` into a work directory, beside a
    `KeePass.config.xml` that turns off the update check and the single instance rule, starts it with
    no database, and hands its main window to each question in a process of its own. The questions
    that end with a domain that will not unload take about fifteen seconds each, so the whole run
    takes several minutes. KeePass is closed at the end and the work directory can be deleted.

    The tree with a menu opened walks the whole desktop, which shows whatever else is open on it,
    so it is not asked here.

.EXAMPLE
    dotnet build spikes\unload -c Release -o spikes\unload\out
    .\spikes\unload\run.ps1 -Out spikes\unload\output -Work work
#>
param(
    [Parameter(Mandatory)] [string] $Out,
    [Parameter(Mandatory)] [string] $Work,
    [int] $Runs = 3
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$root = Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
$probe = Join-Path $PSScriptRoot 'out\Probe.exe'
if (-not (Test-Path $probe)) {
    throw "No probe at $probe. Build it first: dotnet build spikes\unload -c Release -o spikes\unload\out"
}

$version = (Get-Content (Join-Path $root 'keepass.version') -Raw).Trim()
$pinned = Join-Path $root ".keepass\$version"
if (-not (Test-Path (Join-Path $pinned 'KeePass.exe'))) {
    throw "No KeePass $version at $pinned. Provide it first: . .\tools\KeePass.ps1; Install-KeePass"
}

New-Item -ItemType Directory -Force -Path $Out | Out-Null
$keePass = Join-Path $Work 'keepass'
if (Test-Path $keePass) {
    Remove-Item -Recurse -Force $keePass
}
New-Item -ItemType Directory -Force -Path $keePass | Out-Null
Copy-Item -Recurse -Path (Join-Path $pinned '*') -Destination $keePass

@'
<?xml version="1.0" encoding="utf-8"?>
<Configuration>
  <Application>
    <Start>
      <CheckForUpdate>false</CheckForUpdate>
      <CheckForUpdateConfigured>true</CheckForUpdateConfigured>
    </Start>
  </Application>
  <Integration>
    <LimitToSingleInstance>false</LimitToSingleInstance>
  </Integration>
</Configuration>
'@ | Set-Content -Path (Join-Path $keePass 'KeePass.config.xml') -Encoding UTF8

$process = Start-Process -FilePath (Join-Path $keePass 'KeePass.exe') -PassThru
try {
    $deadline = (Get-Date).AddSeconds(30)
    while ($process.MainWindowHandle -eq [IntPtr]::Zero) {
        if ((Get-Date) -gt $deadline) {
            throw 'KeePass showed no main window within thirty seconds.'
        }
        Start-Sleep -Milliseconds 250
        $process.Refresh()
    }
    Start-Sleep -Seconds 2
    $handle = $process.MainWindowHandle.ToInt64().ToString([Globalization.CultureInfo]::InvariantCulture)

    function Ask([string] $File, [string[]] $Arguments) {
        $lines = & $probe @Arguments 2>&1 | ForEach-Object { "$_" }
        $lines | Add-Content -Path (Join-Path $Out $File) -Encoding UTF8
        $lines | Write-Host
    }

    foreach ($file in 'menu.txt', 'minimal.txt', 'tree-default.txt', 'tree-empty.txt', 'stop.txt', 'members.txt', 'items.txt', 'il.txt') {
        $path = Join-Path $Out $file
        if (Test-Path $path) {
            Remove-Item $path
        }
    }

    foreach ($scenario in 'find', 'pattern', 'expand', 'expand-read', 'expand-read-remove') {
        Ask 'menu.txt' @('menu', $scenario, $handle)
    }

    foreach ($run in 1..$Runs) {
        foreach ($scenario in 'nothing', 'handle', 'name', 'id', 'findall') {
            Ask 'minimal.txt' @('minimal', $scenario, $handle)
            Ask 'minimal.txt' @('minimal', "empty-$scenario", $handle)
        }
    }

    Ask 'tree-default.txt' @('tree', 'default', $handle)
    Ask 'tree-empty.txt' @('tree', 'empty', $handle)

    foreach ($run in 1..$Runs) {
        foreach ($scenario in 'findall', 'stop', 'stop-again') {
            Ask 'stop.txt' @('stop', $scenario, $handle)
        }
    }

    Ask 'members.txt' @('members')
    Ask 'items.txt' @('items')
    Ask 'il.txt' @('il')
}
finally {
    if (-not $process.HasExited) {
        $process.Kill()
        $process.WaitForExit()
    }
}
