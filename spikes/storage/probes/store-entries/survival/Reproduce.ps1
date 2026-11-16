# Added on this branch to run the survival critique's probe from the branch root with one command,
# in Windows PowerShell 5.1. It measures nothing itself. In order: kdbxweb 2.1.1 as
# `kdbxweb\package-lock.json` pins it, into `kdbxweb\node_modules\`; the seven base databases,
# through `scripts\surv.ps1 make`, into `work\base\`; their dump into `out\bases.txt`, which the
# critic made by hand with `scripts\surv.ps1 dump` and recorded without the command; and then
# `scripts\Run.ps1`, which writes `out\chains.txt`. Everything it writes is gitignored: `bin\`,
# `work\`, `out\` and `kdbxweb\node_modules\`.
#
# `scripts\Run.ps1` runs in PowerShell 7 and not in Windows PowerShell: it sets
# `ProcessStartInfo.StandardInputEncoding`, which .NET Framework does not have, so it cannot have
# been recorded in 5.1. It starts `scripts\surv.ps1` in Windows PowerShell itself, which is where
# KeePassLib runs, since that script compiles `Surv.cs` with the .NET Framework's CodeDom.
# cspell:ignore loglevel
$ErrorActionPreference = 'Stop'
$Here = $PSScriptRoot
$Surv = Join-Path $Here 'scripts\surv.ps1'
$Base = Join-Path $Here 'work\base'
$Out = Join-Path $Here 'out'

& npm ci --prefix (Join-Path $Here 'kdbxweb') --prefer-offline --no-audit --no-fund --loglevel error
if ($LASTEXITCODE -ne 0) { throw 'npm ci failed' }

if (Test-Path -LiteralPath $Base) { Remove-Item -LiteralPath $Base -Recurse -Force }
New-Item -ItemType Directory -Force -Path $Out | Out-Null
& powershell.exe -NoProfile -ExecutionPolicy Bypass -File $Surv make $Base
if ($LASTEXITCODE -ne 0) { throw 'surv.ps1 make failed' }

$files = 'D0', 'P', 'S', 'X1', 'P1', 'Q', 'P2' | ForEach-Object { Join-Path $Base "$_.kdbx" }
$bases = & powershell.exe -NoProfile -ExecutionPolicy Bypass -File $Surv dump @files
if ($LASTEXITCODE -ne 0) { throw 'surv.ps1 dump failed' }
[System.IO.File]::WriteAllLines((Join-Path $Out 'bases.txt'), [string[]] $bases)

& pwsh.exe -NoProfile -ExecutionPolicy Bypass -File (Join-Path $Here 'scripts\Run.ps1')
if ($LASTEXITCODE -ne 0) { throw 'scripts\Run.ps1 failed' }
