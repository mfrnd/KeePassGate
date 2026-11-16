<#
.SYNOPSIS
    Runs the survival critique's probes of option `crdt` from the branch, into `out\` beside this
    script, with the databases in `work\`.

.DESCRIPTION
    Added on the branch: the agent typed these commands and kept none of them. It provides and
    checks KeePass 2.62 and the four KeePassXC releases the probes start, through the branch's
    tools, and then runs each probe as the critique names it, unchanged but for its paths:

      facts    probe\facts.py                       out\facts.txt
      replay   probe\bridge.py replay               out\bridge-replays.txt
      random   probe\bridge.py random 2000 2        out\bridge-random-2.txt
      random1  probe\bridge.py random 40 1          out\bridge-random-1.txt
      ext      model\model_ext.py 8000              out\ext-results.txt
      order    probe\order.py, with pykeepass       out\order.txt
      all      every one of them, in that order

    Windows PowerShell 5.1, which the probes' KeePassLib host needs; Python through `uv`. Nothing
    shows a window.

.EXAMPLE
    powershell.exe -NoProfile -ExecutionPolicy Bypass -File spikes\storage\probes\crdt\survival\run.ps1 all
#>
param([Parameter(Mandatory)] [ValidateSet('facts', 'replay', 'random', 'random1', 'ext', 'order', 'all')] [string] $Probe)

$ErrorActionPreference = 'Stop'
$here = $PSScriptRoot
. (Join-Path $here '..\..\..\..\..\tools\KeePass.ps1')
. (Join-Path $here '..\..\..\..\..\tools\KeePassXC.ps1')

# Provided before Python starts, so that nothing a tool prints reaches the host's handshake.
[void](Get-KeePassExe)
foreach ($version in '2.4.2', '2.6.6', '2.7.12', '2.8.0') {
    [void](Get-KeePassXCCli -Version $version)
}

$out = Join-Path $here 'out'
if (-not (Test-Path $out)) { [void](New-Item -ItemType Directory -Path $out) }

function Invoke-Probe([string] $Name, [string[]] $With, [string] $Script, [string[]] $Arguments) {
    $python = @('run', '--no-project') + $With + @('python', '-I', '-X', 'utf8', (Join-Path $here $Script)) + $Arguments
    $clock = [System.Diagnostics.Stopwatch]::StartNew()
    & uv @python
    if ($LASTEXITCODE -ne 0) { throw "$Name exited with $LASTEXITCODE" }
    Write-Host ("== {0}: {1:0} s" -f $Name, $clock.Elapsed.TotalSeconds)
}

$steps = if ($Probe -eq 'all') { 'facts', 'replay', 'random', 'random1', 'ext', 'order' } else { @($Probe) }
foreach ($step in $steps) {
    switch ($step) {
        'facts'   { Invoke-Probe 'facts' @() 'probe\facts.py' @() }
        'replay'  { Invoke-Probe 'replay' @() 'probe\bridge.py' @('replay') }
        'random'  { Invoke-Probe 'random 2000 2' @() 'probe\bridge.py' @('random', '2000', '2') }
        'random1' { Invoke-Probe 'random 40 1' @() 'probe\bridge.py' @('random', '40', '1') }
        'ext'     { Invoke-Probe 'ext' @() 'model\model_ext.py' @('8000', (Join-Path $out 'ext-results.txt')) }
        'order'   { Invoke-Probe 'order' @('--with', 'pykeepass') 'probe\order.py' @() }
    }
}
