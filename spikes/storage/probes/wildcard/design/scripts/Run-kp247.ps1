# Written on this branch, not by the agent whose record it reproduces. The design's
# `recorded\keepass247.txt` came from a plain save by KeePass 2.47's KeePassLib, through the
# revocation-merge spike's `Save-WithKeePass.ps1` (carried unchanged beside this script), of three
# AES-KDF files the agent kept in a `work-kp247` directory without the commands: `U-A` and `C-B` of
# the AES sets, and a 1 MiB snapshot made by `Wc.exe big` with AES-KDF. This runs what the record
# and those files imply: the AES sets `scripts\Run-kdbxweb.ps1` made in `work\kw\base\`, copied to
# `work\kp247\` under the record's names; `Wc.exe big` with `WC_KDF=aes` and the 1,048,576
# characters `scripts\Run.ps1` asks for; the save, with KeePass 2.47 as `tools\KeePass.ps1` provides
# it; and one `Wc.exe sum` line per file, labeled with the KDBX version read from its header, into
# `out\keepass247.txt`. What the save script printed goes to `out\keepass247-save.txt`, which has no
# recorded twin. Windows PowerShell 5.1, which `Save-WithKeePass.ps1` requires.
$ErrorActionPreference = 'Stop'
$Root = Split-Path -Parent $PSScriptRoot
$Probe = Join-Path $Root 'probe\out\Wc.exe'
$Save = Join-Path $PSScriptRoot 'Save-WithKeePass.ps1'
$Base = Join-Path $Root 'work\kw\base'
$Work = Join-Path $Root 'work\kp247'
$Out = Join-Path $Root 'out'
. (Join-Path $PSScriptRoot '..\..\..\..\..\..\tools\KeePass.ps1')
$KeePass247 = Split-Path -Parent (Get-KeePassExe -Version '2.47')

if (-not (Test-Path -LiteralPath (Join-Path $Base 'names.txt'))) { throw 'Run scripts\Run-kdbxweb.ps1 first: it makes the AES sets in work\kw\base.' }
if (Test-Path -LiteralPath $Work) { Remove-Item -LiteralPath $Work -Recurse -Force }
New-Item -ItemType Directory -Force -Path $Work, $Out | Out-Null

$env:WC_KDF = 'aes'
$files = @(
    (Join-Path $Work 'U-A-aes.kdbx'),
    (Join-Path $Work 'C-B-aes.kdbx'),
    (Join-Path $Work 'big-aes.kdbx')
)
Copy-Item (Join-Path $Base 'U-A.kdbx') $files[0]
Copy-Item (Join-Path $Base 'C-B.kdbx') $files[1]
& $Probe big $files[2] 1048576 | Out-Null
if ($LASTEXITCODE -ne 0) { throw 'Wc.exe big failed' }

$said = 'probe-only' | & powershell.exe -NoProfile -ExecutionPolicy Bypass -File $Save -KeePass $KeePass247 @files
if ($LASTEXITCODE -ne 0) { throw 'Save-WithKeePass.ps1 failed' }
[System.IO.File]::WriteAllLines((Join-Path $Out 'keepass247-save.txt'), [string[]] $said)

$env:WC_NAMES = Join-Path $Base 'names.txt'
$lines = New-Object System.Collections.Generic.List[string]
$lines.Add('## KeePass 2.47 (KeePassLib loaded from KeePass.exe 2.47), a plain save of AES-KDF files holding the store')
foreach ($file in $files) {
    $head = New-Object byte[] 12
    $stream = [System.IO.File]::OpenRead($file)
    try { [void] $stream.Read($head, 0, 12) } finally { $stream.Dispose() }
    $v = [System.BitConverter]::ToUInt32($head, 8)
    $kdbx = '{0}.{1}' -f ($v -shr 16), ($v -band 0xFFFF)
    $line = & $Probe sum "KeePass 2.47 save of $(Split-Path -Leaf $file), kdbx $kdbx" $file
    if ($LASTEXITCODE -ne 0) { throw "Wc.exe sum failed: $file" }
    $lines.Add([string] $line)
}
[System.IO.File]::WriteAllLines((Join-Path $Out 'keepass247.txt'), $lines)
