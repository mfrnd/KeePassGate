<#
.SYNOPSIS
    Fetches both programs' source at their tags, and the stand-in for Keepass2Android's native
    Argon2, into `work\`, and builds every harness of the family.

.DESCRIPTION
    Nothing fetched is committed: `work\` is ignored by the branch. Each clone is checked against
    the commit its tag pointed at when the family was measured, and each download against its
    SHA-256, and a mismatch stops the script.

      work\src\k2a\          Keepass2Android, tag v1.15-r6
      work\src\kpdx-4.5.8\   KeePassDX, tag 4.5.8 (the newest release on 2026-11-16 as well)
      work\tools\libargon2\  libargon2.dll, the reference Argon2 built for Windows x64, taken from
                             the NuGet package Argon2.Bindings 1.21.0 (MIT); it exports the
                             `argon2_hash` the Keepass2Android copy of KeePassLib calls

    Then `kpdx\build.ps1` provides the JVM tools and builds the KeePassDX harness, and `dotnet build`
    builds `dumper\`, `k2a\` and `kplib\`. Windows PowerShell 5.1, from the branch root:

        powershell.exe -NoProfile -ExecutionPolicy Bypass -File spikes\storage\probes\source-harness\fetch.ps1
#>
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$ProgressPreference = 'SilentlyContinue'
[Net.ServicePointManager]::SecurityProtocol = [Net.SecurityProtocolType]::Tls12

$here = $PSScriptRoot
$work = Join-Path $here 'work'
$src = Join-Path $work 'src'
$tools = Join-Path $work 'tools'
New-Item -ItemType Directory -Force $src, $tools | Out-Null

function Invoke-Native([scriptblock] $Command) {
    $saved = $ErrorActionPreference
    $ErrorActionPreference = 'Continue'
    try { & $Command 2>&1 | ForEach-Object { "$_" } }
    finally { $ErrorActionPreference = $saved }
}

function Get-Source([string] $Name, [string] $Url, [string] $Tag, [string] $Commit) {
    $dir = Join-Path $src $Name
    if (-not (Test-Path (Join-Path $dir '.git'))) {
        Invoke-Native { git clone --quiet --depth 1 --branch $Tag $Url $dir } | Write-Host
        if ($LASTEXITCODE -ne 0) { throw "git clone of $Url at $Tag failed" }
    }
    $head = (Invoke-Native { git -C $dir rev-parse HEAD } | Select-Object -Last 1).Trim()
    if ($head -ne $Commit) { throw "$Name is at $head, not at $Commit (tag $Tag)" }
    Write-Host "$Name $Tag $head"
}

function Get-Pinned([string] $Url, [string] $File, [string] $Sha256) {
    if (-not (Test-Path $File)) { Invoke-WebRequest -UseBasicParsing -Uri $Url -OutFile $File }
    $got = (Get-FileHash -Algorithm SHA256 $File).Hash
    if ($got -ne $Sha256) { throw "$File has SHA-256 $got, pinned $Sha256" }
}

Get-Source 'k2a' 'https://github.com/PhilippC/keepass2android' 'v1.15-r6' 'bbcf3787c75bd5b8a9769576191129b5dd9e8bc0'
Get-Source 'kpdx-4.5.8' 'https://github.com/Kunzisoft/KeePassDX' '4.5.8' '77ecfa8654a5bd0d79d512f8fa71c0af8a29b554'

$argonDir = Join-Path $tools 'libargon2'
$argonDll = Join-Path $argonDir 'libargon2.dll'
New-Item -ItemType Directory -Force $argonDir | Out-Null
$pkg = Join-Path $argonDir 'argon2.bindings.1.21.0.zip'
Get-Pinned 'https://www.nuget.org/api/v2/package/Argon2.Bindings/1.21.0' $pkg 'D7691EE528C66018DB913AE86AA4C4E35C94726AAFC7A43B73F3671D8A25B1DA'
if (-not (Test-Path $argonDll)) {
    $x = Join-Path $argonDir 'x'
    Expand-Archive -Path $pkg -DestinationPath $x -Force
    Copy-Item (Join-Path $x 'runtimes\win-x64\native\libargon2.dll') $argonDll
    Remove-Item -Recurse -Force $x
}
$got = (Get-FileHash -Algorithm SHA256 $argonDll).Hash
if ($got -ne '3644BFD3F0CF7D509BBE7074397C75F1E55D69E5313B50CAD2AE8D86C803B647') { throw "libargon2.dll has SHA-256 $got" }

& (Join-Path $here 'kpdx\build.ps1')
if (-not $?) { throw 'kpdx\build.ps1 failed' }

$projects = @('dumper\Dumper.csproj', 'k2a\K2aHarness.csproj', 'kplib\StoreLab.csproj')
# The second KeePassDX record's probe, when a copy of that record is in work\kpdx-record2\ (RUN.md 7.2).
if (Test-Path (Join-Path $work 'kpdx-record2\probe\Store.cs')) { $projects += 'dumper2\Dumper2.csproj' }
foreach ($p in $projects) {
    Invoke-Native { dotnet build (Join-Path $here $p) -c Release -v quiet -nologo } | Write-Host
    if ($LASTEXITCODE -ne 0) { throw "dotnet build $p exited with $LASTEXITCODE" }
}
Write-Host 'source-harness: fetched and built'
