<#
.SYNOPSIS
    Builds the survival critique's probe and runs it, into `out\`.

.DESCRIPTION
    Added on the branch: the agent ran these steps by hand and kept no script. What it runs is
    `probe\Program.cs`, unchanged. `Rls.exe` takes a work directory, an output file, and the
    `keepassxc-cli.exe` of 2.4.2 and of 2.8.0, and runs the chains C1 to C6 into `survival.txt`,
    the file `recorded\` holds under that name. The agent's databases went to `work2\`; they go to
    `work\` here, which the branch ignores and which this empties first, since the probe takes the
    directory as its first argument and writes nothing else there. `signatures.txt` is the
    publisher's signature on each `keepassxc-cli.exe` and on KeePass.exe, in the form the recorded
    file has, with each program named by its path from the branch root.

    Windows PowerShell 5.1, from the branch root:

        powershell.exe -NoProfile -ExecutionPolicy Bypass -File spikes\storage\probes\rules-linked\survival\run.ps1
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

$keepass = Get-KeePassExe
& dotnet build (Join-Path $here 'probe\Rls.csproj') -c Release -o $bin -v q
if ($LASTEXITCODE -ne 0) { throw "The build failed." }
$rls = Join-Path $bin 'Rls.exe'

$cli242 = Get-KeePassXCCli -Version '2.4.2'
$cli280 = Get-KeePassXCCli -Version '2.8.0'

$signatures = New-Object System.Collections.Generic.List[string]
foreach ($program in $cli242, $cli280, $keepass) {
    $signature = Get-AuthenticodeSignature $program
    $name = $program.Substring($root.TrimEnd('\').Length + 1)
    $signatures.Add("$name $($signature.Status) $($signature.SignerCertificate.Subject)")
}
[IO.File]::WriteAllLines((Join-Path $out 'signatures.txt'), $signatures)

$clock = [Diagnostics.Stopwatch]::StartNew()
& $rls $work (Join-Path $out 'survival.txt') $cli242 $cli280
if ($LASTEXITCODE -ne 0) { throw "Rls.exe exited $LASTEXITCODE." }
Write-Host ([string]::Format([Globalization.CultureInfo]::InvariantCulture, 'survival: {0:0.0} s', $clock.Elapsed.TotalSeconds))
