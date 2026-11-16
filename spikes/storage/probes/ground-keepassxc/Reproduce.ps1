<#
.SYNOPSIS
    Runs every probe of the KeePassXC ground report again from this branch and compares the result
    with the record.

.DESCRIPTION
    Written on this branch; it measures nothing itself. In order: builds `scripts\probe\Xcp.csproj`
    into `scripts\probe\out\`, where `scripts\Common.ps1` looks for `Xcp.exe`; starts
    `scripts\Run-Merge.ps1` for the sets `fast`, `slow` and `extra`, `scripts\Run-Save.ps1`, and
    `replay\Replay.ps1`, each under PowerShell 7 (see `RUN.md`); copies every text file the run left
    in `work\` into `out\` at the same relative path; then writes `out\compare.txt` with
    `checks\compare.py`, and `out\claims.txt` and `out\claims-recorded.txt` with `checks\claims.py`
    over `out\` and over `recorded\`.

    Writes only into gitignored places beside it: `work\`, `out\`, `scripts\probe\out\`,
    `scripts\probe\obj\`, and through the tools into `.keepassxc\` when a release is not provided yet.
    Every database is made by the run, in `work\`.

.PARAMETER SkipSlow
    Leaves out the set with the slow key derivation, which takes most of the run's time.
#>
param([switch] $SkipSlow)

$ErrorActionPreference = 'Stop'
$family = $PSScriptRoot
$root = (Resolve-Path (Join-Path $family '..\..\..\..')).Path

& {
    . (Join-Path $root 'tools\KeePass.ps1')
    $null = Get-KeePassExe
}

& dotnet build (Join-Path $family 'scripts\probe\Xcp.csproj') -c Release -o (Join-Path $family 'scripts\probe\out') --nologo -v q
if ($LASTEXITCODE -ne 0) { throw 'The probe did not build.' }

foreach ($dir in 'work', 'out') {
    $path = Join-Path $family $dir
    if (Test-Path -LiteralPath $path) { Remove-Item -LiteralPath $path -Recurse -Force }
}

$pwsh = (Get-Command pwsh -ErrorAction Stop).Source
$runs = @(
    @('scripts\Run-Merge.ps1', '-Set', 'fast'),
    @('scripts\Run-Merge.ps1', '-Set', 'slow'),
    @('scripts\Run-Merge.ps1', '-Set', 'extra'),
    @('scripts\Run-Save.ps1'),
    @('replay\Replay.ps1')
)
foreach ($run in $runs) {
    if ($SkipSlow -and $run -contains 'slow') { continue }
    $started = Get-Date
    $script = Join-Path $family $run[0]
    & $pwsh -NoProfile -ExecutionPolicy Bypass -File $script @($run | Select-Object -Skip 1)
    if ($LASTEXITCODE -ne 0) { throw "$($run -join ' ') failed." }
    Write-Host ('{0} took {1:N0} s' -f ($run -join ' '), ((Get-Date) - $started).TotalSeconds)
}

$work = Join-Path $family 'work'
$out = Join-Path $family 'out'
Get-ChildItem -LiteralPath $work -Recurse -File |
    Where-Object { $_.Extension -in '.txt', '.log', '.csv', '.html', '.xml', '.err' -and $_.FullName -notmatch '\\work\\tmp\\' } |
    ForEach-Object {
        $to = Join-Path $out $_.FullName.Substring($work.Length + 1)
        New-Item -ItemType Directory -Force -Path (Split-Path -Parent $to) | Out-Null
        Copy-Item -LiteralPath $_.FullName -Destination $to
    }

$python = @('run', '--no-project', 'python', '-I', '-X', 'utf8')
$compare = & uv @python (Join-Path $family 'checks\compare.py') $family
[System.IO.File]::WriteAllLines((Join-Path $out 'compare.txt'), [string[]] $compare)
$claims = & uv @python (Join-Path $family 'checks\claims.py') $out
[System.IO.File]::WriteAllLines((Join-Path $out 'claims.txt'), [string[]] $claims)
$claims = & uv @python (Join-Path $family 'checks\claims.py') (Join-Path $family 'recorded')
[System.IO.File]::WriteAllLines((Join-Path $out 'claims-recorded.txt'), [string[]] $claims)
Write-Host "Wrote $out\compare.txt, claims.txt and claims-recorded.txt."
