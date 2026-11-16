<#
.SYNOPSIS
    Builds the security critique's probe and runs it from the branch, as `RUN.md` describes.

.DESCRIPTION
    Added on the branch: the agent ran these steps by hand from PowerShell and kept no script. What
    it runs is unchanged: `Probe.exe` with the work directory, the output file and the path of
    `keepassxc-cli` 2.8.0, as the agent passed them. It writes `out\probe.txt`, whose recorded copy
    is `recorded\probe.txt`. The agent's recorded run made its databases in `work2\`, after an
    earlier run in `work\`; this makes them in `work\`, which is emptied first and which
    `.gitignore` excludes. Nothing the probe prints names the directory.

    Windows PowerShell 5.1, from the branch root:

        powershell.exe -NoProfile -ExecutionPolicy Bypass -File spikes\storage\probes\grant-only\security-and-fit\run.ps1
#>
$ErrorActionPreference = 'Stop'

$root = (Resolve-Path (Join-Path $PSScriptRoot '..\..\..\..\..')).Path
. (Join-Path $root 'tools\KeePass.ps1')
. (Join-Path $root 'tools\KeePassXC.ps1')
. (Join-Path $root 'tools\Product.ps1')

$here = $PSScriptRoot
$out = Join-Path $here 'out'
$work = Join-Path $here 'work'
$bin = Join-Path $here 'bin'
if (Test-Path $work) { Remove-Item $work -Recurse -Force }
[void](New-Item -ItemType Directory -Force $out, $work)

# The eight files of the product the agent copied, from the product's commit.
$null = Export-ProductFile -Path @(
    'src/KeePassGate/Acl/AclDocument.cs', 'src/KeePassGate/Acl/AclDocuments.cs',
    'src/KeePassGate/Acl/AclGrant.cs', 'src/KeePassGate/Acl/AclInheritance.cs',
    'src/KeePassGate/Acl/AclResolver.cs', 'src/KeePassGate/Acl/AclVerb.cs',
    'src/KeePassGate/Acl/VerbMask.cs', 'src/KeePassGate/Transport/JsonBody.cs')

# Provides and checks the pinned KeePass the project references.
$null = Get-KeePassExe
& dotnet build (Join-Path $here 'probe\Probe.csproj') -c Release -o $bin -v q -nologo
if ($LASTEXITCODE -ne 0) { throw "The build failed." }

$clock = [Diagnostics.Stopwatch]::StartNew()
& (Join-Path $bin 'Probe.exe') $work (Join-Path $out 'probe.txt') (Get-KeePassXCCli -Version 2.8.0)
if ($LASTEXITCODE -ne 0) { throw "Probe.exe exited $LASTEXITCODE." }
Write-Host ([string]::Format([Globalization.CultureInfo]::InvariantCulture, 'probe: {0:0.0} s', $clock.Elapsed.TotalSeconds))
