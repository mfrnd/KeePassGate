<#
.SYNOPSIS
    Builds the design's probe and runs it from the branch, as `RUN.md` describes.

.DESCRIPTION
    Added on the branch: the agent ran these steps by hand and kept no script. What it runs is
    unchanged. `Rc.exe` is called three ways, as the probe's `Main` takes them: `keepass` (the 112
    KeePassLib cells, Create new IDs and Delete Duplicate Entries), `cost` (the cost of the read
    pass), and `xc` once for each KeePassXC release (a plain save and the 112 `keepassxc-cli`
    cells). Each writes the file of the same name that `recorded\` holds, into `out\`. The
    databases go to `work\`, which is emptied first. `signatures.txt` is the publisher's signature
    on each `keepassxc-cli.exe`, in the form the recorded file has.

    Windows PowerShell 5.1, from the branch root:

        powershell.exe -NoProfile -ExecutionPolicy Bypass -File spikes\storage\probes\rules-central\design\run.ps1
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

# Provides and checks the pinned KeePass the project references.
$null = Get-KeePassExe
& dotnet build (Join-Path $here 'probe\Rc.csproj') -c Release -o $bin -v q
if ($LASTEXITCODE -ne 0) { throw "The build failed." }
$rc = Join-Path $bin 'Rc.exe'

function Step([string] $Name, [scriptblock] $Run) {
    $clock = [Diagnostics.Stopwatch]::StartNew()
    & $Run
    if ($LASTEXITCODE -ne 0) { throw "$Name exited $LASTEXITCODE." }
    Write-Host ([string]::Format([Globalization.CultureInfo]::InvariantCulture, '{0}: {1:0.0} s', $Name, $clock.Elapsed.TotalSeconds))
}

Step 'keepass' { & $rc (Join-Path $work 'kp') keepass (Join-Path $out 'keepass.txt') }
Step 'cost' { & $rc (Join-Path $work 'cost') cost (Join-Path $out 'cost.txt') }

$signatures = New-Object System.Collections.Generic.List[string]
foreach ($version in '2.4.2', '2.6.6', '2.7.12', '2.8.0') {
    $cli = Get-KeePassXCCli -Version $version
    Step "xc $version" { & $rc (Join-Path $work 'xc') xc (Join-Path $out "xc-$version.txt") $version $cli }
    $signature = Get-AuthenticodeSignature $cli
    $signatures.Add("keepassxc-cli $version : $($signature.Status) $($signature.SignerCertificate.Subject)")
}
[IO.File]::WriteAllLines((Join-Path $out 'signatures.txt'), $signatures)
