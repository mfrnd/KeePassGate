<#
.SYNOPSIS
    Runs the history extension: makes every case, merges it with KeePassLib 2.62 and four
    `keepassxc-cli` releases, and judges every result under every rule of `rules.py`.

.DESCRIPTION
    Windows PowerShell 5.1, from the branch root or anywhere. In order: checks that KeePass 2.62 and
    the four KeePassXC releases are provided; builds `probe\Edr.csproj`; empties `work\history\` and
    `out\history\`; `Edr.exe history` writes every case's copies A and B under the fast key
    derivation; `scripts\pk_edit.py` makes part 3's pykeepass edits in their copies B; `Edr.exe
    reads`, `kp` and `xc` dump both copies and every merge, `xc` over the same fast copies, since no
    record of this store lives where the two loads' seconds decide anything; `scripts\condense.py`
    folds the dumps; `scripts\history.py` judges them, and judges part 0's record again under every
    rule into `out\history\part0\`.

    Writes only into gitignored places: `work\history\`, `out\history\`, `probe\out\`,
    `probe\obj\`. Nothing it starts shows a window.

.PARAMETER Degree
    How many cells run at once.
#>
param([int] $Degree = 6)

$ErrorActionPreference = 'Stop'
$family = $PSScriptRoot
$root = (Resolve-Path (Join-Path $family '..\..\..\..')).Path

$kpExe = & {
    . (Join-Path $root 'tools\KeePass.ps1')
    Get-KeePassExe
}
$releases = '2.4.2', '2.6.6', '2.7.12', '2.8.0'
$programs = & {
    . (Join-Path $root 'tools\KeePassXC.ps1')
    foreach ($r in $releases) { [pscustomobject]@{ Release = $r; Path = (Get-KeePassXCCli -Version $r) } }
}

& dotnet build (Join-Path $family 'probe\Edr.csproj') -c Release -o (Join-Path $family 'probe\out') --nologo -v q
if ($LASTEXITCODE -ne 0) { throw 'The probe did not build.' }
$edr = Join-Path $family 'probe\out\Edr.exe'

$work = Join-Path $family 'work\history'
$out = Join-Path $family 'out\history'
foreach ($path in $work, $out) {
    if (Test-Path -LiteralPath $path) { Remove-Item -LiteralPath $path -Recurse -Force }
}
$raw = Join-Path $out 'raw'
New-Item -ItemType Directory -Path $raw -Force | Out-Null
$copies = Join-Path $work 'copies'
$env:UV_EXCLUDE_NEWER = '2026-11-16T00:00:00Z'

$phases = New-Object System.Collections.Generic.List[string]
function Step([string] $name, [scriptblock] $body) {
    $started = Get-Date
    & $body
    if ($LASTEXITCODE -ne 0) { throw "$name failed." }
    $line = '{0}: {1} s' -f $name, [math]::Round(((Get-Date) - $started).TotalSeconds)
    $phases.Add($line)
    Write-Host $line
}

$cliArgs = @($programs | ForEach-Object { "$($_.Release)=$($_.Path)" })
$py = @('run', '--no-project', 'python', '-I', '-X', 'utf8')
Step 'make' { & $edr history $copies $Degree }
Step 'pykeepass edits' { & uv run --no-project --with pykeepass python -I -X utf8 (Join-Path $family 'scripts\pk_edit.py') $copies | Set-Content -LiteralPath (Join-Path $out 'pykeepass.txt') -Encoding ascii }
Step 'reads' { & $edr reads $copies (Join-Path $raw 'reads.jsonl') }
Step 'KeePassLib merges' { & $edr kp $copies (Join-Path $work 'kp') (Join-Path $raw 'kp.jsonl') $Degree }
Step 'keepassxc-cli merges' { & $edr xc $copies (Join-Path $work 'xc') (Join-Path $raw 'xc.jsonl') (Join-Path $out 'xc-times.tsv') $Degree @cliArgs }
Step 'condense' { & uv @py (Join-Path $family 'scripts\condense.py') $raw $out }
Step 'judge' { & uv @py (Join-Path $family 'scripts\history.py') (Join-Path $family 'rules.py') $out (Join-Path $family 'recorded') (Join-Path $out 'part0') }
[System.IO.File]::WriteAllLines((Join-Path $out 'phases.txt'), $phases)
