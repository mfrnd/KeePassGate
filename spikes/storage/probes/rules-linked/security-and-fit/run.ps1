<#
.SYNOPSIS
    Builds the security critique's probe and runs both of its parts, into `out\`.

.DESCRIPTION
    Added on the branch: the agent ran these steps by hand and kept no script. What it runs is
    `probe\Model.cs` and `probe\Mine.cs`, unchanged. `Rlsf.exe` is called twice, as its `Main`
    takes it: with a work directory, an output file and pairs of a KeePassXC release and its
    `keepassxc-cli.exe` it runs plant-confirm, midsave, paste, template, otherdb and delgroup into
    `probe.txt`; with a work directory, an output file and the word `dedup` it runs dedup into
    `dedup.txt`. The releases are passed as the recorded run passed them, 2.8.0 first and 2.4.2
    second, which is the order `probe.txt` prints them in. The databases go to `work\`, which the
    branch ignores and which this empties first. `tools\KeePassXC.ps1` checks the publisher's
    signature on each `keepassxc-cli.exe` before handing it over.

    Windows PowerShell 5.1, from the branch root:

        powershell.exe -NoProfile -ExecutionPolicy Bypass -File spikes\storage\probes\rules-linked\security-and-fit\run.ps1
#>
$ErrorActionPreference = 'Stop'

$root = (Resolve-Path (Join-Path $PSScriptRoot '..\..\..\..\..')).Path
. (Join-Path $root 'tools\KeePass.ps1')
. (Join-Path $root 'tools\KeePassXC.ps1')

$here = $PSScriptRoot
$out = Join-Path $here 'out'
$work = Join-Path $here 'work'
$bin = Join-Path $here 'bin'
if (Test-Path $work) { Remove-Item $work -Recurse -Force }
[void](New-Item -ItemType Directory -Force $out, $work)

$null = Get-KeePassExe
& dotnet build (Join-Path $here 'probe\Rlsf.csproj') -c Release -o $bin -v q
if ($LASTEXITCODE -ne 0) { throw "The build failed." }
$rlsf = Join-Path $bin 'Rlsf.exe'

$cli280 = Get-KeePassXCCli -Version '2.8.0'
$cli242 = Get-KeePassXCCli -Version '2.4.2'

function Step([string] $Name, [scriptblock] $Run) {
    $clock = [Diagnostics.Stopwatch]::StartNew()
    & $Run
    if ($LASTEXITCODE -ne 0) { throw "$Name exited $LASTEXITCODE." }
    Write-Host ([string]::Format([Globalization.CultureInfo]::InvariantCulture, '{0}: {1:0.0} s', $Name, $clock.Elapsed.TotalSeconds))
}

Step 'probe' { & $rlsf $work (Join-Path $out 'probe.txt') '2.8.0' $cli280 '2.4.2' $cli242 }
Step 'dedup' { & $rlsf $work (Join-Path $out 'dedup.txt') dedup }
