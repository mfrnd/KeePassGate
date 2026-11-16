<#
.SYNOPSIS
    Runs every measurement of `kdbx-places` into `out\`, and the summary of it.

.DESCRIPTION
    Builds `probe\Kpl.csproj` against the pinned KeePass 2.62, provides KeePass 2.47 and the four
    `keepassxc-cli` releases through `tools\`, installs kdbxweb 2.1.1 into `kdbxweb\node_modules`
    from the lock file when it is absent, and hands all of them to `scripts\run.py`, which makes
    the databases in `work\` (emptied first) and writes what it measured into `out\`. Then
    `scripts\summarize.py` turns `out\` into `out\tables.md`, and `android\make-bases.py` writes the
    files for the phone measurement into `work\android\`.

    -Steps takes a comma separated subset of bases, saves, merges, key_format and cap; every step but
    bases reads what bases made. -CapPlaces names the places `cap` measures.

    Windows PowerShell 5.1, from the branch root:

        powershell.exe -NoProfile -ExecutionPolicy Bypass -File spikes\storage\probes\kdbx-places\run.ps1
#>
param(
    [string] $Steps = 'bases,saves,merges,key_format,cap',
    [string] $CapPlaces = 'H.pcd,M.name,M.desc,M.user,M.cd,M.icon.name,M.icon.data,G.notes,G.at-sequence,G.cd,E.override-url,E.cd,E.tags,E.at-window'
)
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
if ($PSVersionTable.PSEdition -ne 'Desktop') { throw 'Windows PowerShell 5.1 only (powershell.exe).' }

$here = $PSScriptRoot
$root = (Resolve-Path (Join-Path $here '..\..\..\..')).Path
. (Join-Path $root 'tools\KeePass.ps1')
. (Join-Path $root 'tools\KeePassXC.ps1')
$null = Get-KeePassExe
$kp247 = Split-Path -Parent (Get-KeePassExe -Version '2.47')
$xc = foreach ($v in '2.4.2', '2.6.6', '2.7.12', '2.8.0') { '--xc'; "$v=$(Get-KeePassXCCli -Version $v)" }

function Invoke-Native([scriptblock] $Command) {
    $saved = $ErrorActionPreference
    $ErrorActionPreference = 'Continue'
    try { & $Command 2>&1 | ForEach-Object { "$_" } }
    finally { $ErrorActionPreference = $saved }
}

$clock = [Diagnostics.Stopwatch]::StartNew()
Invoke-Native { dotnet build (Join-Path $here 'probe\Kpl.csproj') -c Release -v quiet -nologo } | Write-Host
if ($LASTEXITCODE -ne 0) { throw "dotnet build exited with $LASTEXITCODE" }
$kpl = Join-Path $here 'probe\bin\Release\net48\Kpl.exe'

$kw = Join-Path $here 'kdbxweb\node_modules\kdbxweb'
$installed = Join-Path $kw 'package.json'
if (-not (Test-Path $installed) -or ((Get-Content -Raw $installed | ConvertFrom-Json).version -ne '2.1.1')) {
    Push-Location (Join-Path $here 'kdbxweb')
    try { Invoke-Native { npm ci --ignore-scripts --no-audit --no-fund } | Write-Host }
    finally { Pop-Location }
}

$work = Join-Path $here 'work'
$out = Join-Path $here 'out'
if ($Steps -match 'bases') {
    foreach ($d in $work, $out) { if (Test-Path $d) { Remove-Item $d -Recurse -Force } }
}
New-Item -ItemType Directory -Force $work, $out | Out-Null

$env:UV_EXCLUDE_NEWER = '2026-11-16T00:00:00Z'
$py = @('run', '--no-project', '--with', 'cryptography', '--with', 'argon2-cffi', '--with', 'lxml', 'python', '-I', '-X', 'utf8')
Invoke-Native { uv @py (Join-Path $here 'scripts\run.py') --work $work --out $out --kpl $kpl --kp247 $kp247 --kw $kw @xc --steps $Steps --cap-places $CapPlaces } | Write-Host
if ($LASTEXITCODE -ne 0) { throw "run.py exited with $LASTEXITCODE" }
Invoke-Native { uv @py (Join-Path $here 'scripts\summarize.py') $out } | Write-Host
if ($LASTEXITCODE -ne 0) { throw "summarize.py exited with $LASTEXITCODE" }
Invoke-Native { uv @py (Join-Path $here 'android\make-bases.py') --kpl $kpl --work (Join-Path $work 'android') } | Write-Host
if ($LASTEXITCODE -ne 0) { throw "make-bases.py exited with $LASTEXITCODE" }
Write-Host ([string]::Format([Globalization.CultureInfo]::InvariantCulture, 'kdbx-places: {0:0} s', $clock.Elapsed.TotalSeconds))
