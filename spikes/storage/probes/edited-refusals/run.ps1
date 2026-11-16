<#
.SYNOPSIS
    Runs the whole edited-refusals probe: builds it, makes the copies, merges them with KeePassLib
    2.62 and four `keepassxc-cli` releases, and gives every result a verdict under every reading rule.

.DESCRIPTION
    Windows PowerShell 5.1, from the branch root or anywhere. In order: checks that KeePass 2.62 and
    the four KeePassXC releases are provided; builds `probe\Edr.csproj` into `probe\out\`; empties
    `work\` and `out\`; `Edr.exe make` writes every scenario's copies A and B twice, under the fast key
    derivation into `work\fast\` and under the slow one into `work\slow\`; `Edr.exe reads` dumps both
    copies as they are; `Edr.exe kp` merges the fast copies with KeePassLib's five methods, both
    directions; `Edr.exe xc` merges the slow copies with `keepassxc-cli merge -s` of each release,
    both directions, so that the two loads of a merge fall in different seconds; `scripts\condense.py`
    folds the dumps; `scripts\verdicts.py` applies `rules.py`. Each phase's time goes to
    `out\phases.txt`, and the tools' identities to `out\tools.txt`.

    Writes only into gitignored places beside it: `work\`, `out\`, `probe\out\`, `probe\obj\`, and
    through the tools into `.keepassxc\` when a release is not provided yet. Every database is made
    by the run, in `work\`. Nothing it starts shows a window.

.PARAMETER Degree
    How many cells run at once. Each slow load uses two threads.
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

foreach ($dir in 'work', 'out') {
    $path = Join-Path $family $dir
    if (Test-Path -LiteralPath $path) { Remove-Item -LiteralPath $path -Recurse -Force }
}
$work = Join-Path $family 'work'
$out = Join-Path $family 'out'
$raw = Join-Path $out 'raw'
New-Item -ItemType Directory -Path $raw -Force | Out-Null

$phases = New-Object System.Collections.Generic.List[string]
function Step([string] $name, [scriptblock] $body) {
    $started = Get-Date
    & $body
    if ($LASTEXITCODE -ne 0) { throw "$name failed." }
    $line = '{0}: {1:N0} s' -f $name, ((Get-Date) - $started).TotalSeconds
    $phases.Add($line)
    Write-Host $line
}

$fast = Join-Path $work 'fast'
$slow = Join-Path $work 'slow'
$cliArgs = @($programs | ForEach-Object { "$($_.Release)=$($_.Path)" })
$everything = Get-Date
Step 'make, fast derivation' { & $edr make $fast fast $Degree }
Step 'make, slow derivation' { & $edr make $slow slow $Degree }
Step 'reads' { & $edr reads $fast (Join-Path $raw 'reads.jsonl') }
Step 'KeePassLib merges' { & $edr kp $fast (Join-Path $work 'kp') (Join-Path $raw 'kp.jsonl') $Degree }
Step 'keepassxc-cli merges' { & $edr xc $slow (Join-Path $work 'xc') (Join-Path $raw 'xc.jsonl') (Join-Path $out 'xc-times.tsv') $Degree @cliArgs }
Step 'condense' { & uv run --no-project python -I -X utf8 (Join-Path $family 'scripts\condense.py') $raw $out }
Step 'verdicts' { & uv run --no-project python -I -X utf8 (Join-Path $family 'scripts\verdicts.py') (Join-Path $family 'rules.py') $out --only R1,R2,R3,R4,R5 }
$phases.Add(('everything after the build: {0:N0} s' -f ((Get-Date) - $everything).TotalSeconds))
[System.IO.File]::WriteAllLines((Join-Path $out 'phases.txt'), $phases)

$tools = New-Object System.Collections.Generic.List[string]
$tools.Add('KeePass.exe SHA-256 ' + (Get-FileHash -LiteralPath $kpExe -Algorithm SHA256).Hash + ', version ' + (Get-Item -LiteralPath $kpExe).VersionInfo.FileVersion)
foreach ($c in $programs) {
    $s = Get-AuthenticodeSignature -LiteralPath $c.Path
    $tools.Add("keepassxc-cli $($c.Release): $($s.Status), $($s.SignerCertificate.Subject)")
}
[System.IO.File]::WriteAllLines((Join-Path $out 'tools.txt'), $tools)
