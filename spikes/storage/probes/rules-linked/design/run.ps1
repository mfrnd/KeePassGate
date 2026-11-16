<#
.SYNOPSIS
    Builds the design's probe and runs every part of it the design quotes, into `out\`.

.DESCRIPTION
    Added on the branch: the agent ran these steps by hand and kept no script. What it runs is
    `probe\Program.cs`, unchanged. `Rl.exe` takes a work directory, a mode and an output file, and
    for `xc` a KeePassXC release and its `keepassxc-cli.exe`. It is called as `keepass` once (the
    96 KeePassLib cells, the 17 acts by hand, KeePass's own copying, the foreign file and the lost
    version) and as `xc` once for each of 2.4.2, 2.6.6, 2.7.12 and 2.8.0 (a plain save, an edit,
    the 96 `keepassxc-cli` cells and the lost version). Each writes the file of the same name that
    `recorded\` holds. The databases go to `work\kp` and `work\xc`, as the agent's did, and `work\`
    is emptied first. `signatures.txt` is the publisher's signature on each `keepassxc-cli.exe`, in
    the form the recorded file has.

    Windows PowerShell 5.1, from the branch root:

        powershell.exe -NoProfile -ExecutionPolicy Bypass -File spikes\storage\probes\rules-linked\design\run.ps1
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
& dotnet build (Join-Path $here 'probe\Rl.csproj') -c Release -o $bin -v q
if ($LASTEXITCODE -ne 0) { throw "The build failed." }
$rl = Join-Path $bin 'Rl.exe'

function Step([string] $Name, [scriptblock] $Run) {
    $clock = [Diagnostics.Stopwatch]::StartNew()
    & $Run
    if ($LASTEXITCODE -ne 0) { throw "$Name exited $LASTEXITCODE." }
    Write-Host ([string]::Format([Globalization.CultureInfo]::InvariantCulture, '{0}: {1:0.0} s', $Name, $clock.Elapsed.TotalSeconds))
}

Step 'keepass' { & $rl (Join-Path $work 'kp') keepass (Join-Path $out 'keepass.txt') }

$signatures = New-Object System.Collections.Generic.List[string]
foreach ($version in '2.4.2', '2.6.6', '2.7.12', '2.8.0') {
    $cli = Get-KeePassXCCli -Version $version
    Step "xc $version" { & $rl (Join-Path $work 'xc') xc (Join-Path $out "xc-$version.txt") $version $cli }
    $signature = Get-AuthenticodeSignature $cli
    $signatures.Add("$version $($signature.Status) $($signature.SignerCertificate.Subject)")
}
[IO.File]::WriteAllLines((Join-Path $out 'signatures.txt'), $signatures)
