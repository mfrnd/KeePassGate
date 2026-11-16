# Added on this branch to run the security and fit critique's probes from the branch root with one
# command, in Windows PowerShell 5.1. It measures nothing itself. In order: `scripts\import-plant.ps1`
# with the pinned KeePass, into `out\import-plant.txt`; `scripts\pk_delete.py` through pykeepass,
# into `out\pk-delete.txt`; and `scripts\count-affected-tests.ps1`, the count written on this branch
# over the critic's recorded list, into `out\affected-tests-count.txt`. The critic's work
# directories `work-import` and `work-pykeepass` become `work\import` and `work\pykeepass`, which
# `.gitignore` covers. Everything it writes is gitignored: `work\` and `out\`, and the product's
# files that `tools\Product.ps1` exports into `.product\`.
$ErrorActionPreference = 'Stop'
$Here = $PSScriptRoot
. (Join-Path $Here '..\..\..\..\..\tools\KeePass.ps1')
$Out = Join-Path $Here 'out'
$WorkImport = Join-Path $Here 'work\import'
$WorkPk = Join-Path $Here 'work\pykeepass'
foreach ($d in $WorkImport, $WorkPk) { if (Test-Path -LiteralPath $d) { Remove-Item -LiteralPath $d -Recurse -Force } }
foreach ($d in $Out, $WorkImport, $WorkPk) { New-Item -ItemType Directory -Force -Path $d | Out-Null }

& powershell.exe -NoProfile -ExecutionPolicy Bypass -File (Join-Path $Here 'scripts\import-plant.ps1') -KeePassExe (Get-KeePassExe) -WorkDir $WorkImport -OutFile (Join-Path $Out 'import-plant.txt')
if ($LASTEXITCODE -ne 0) { throw 'scripts\import-plant.ps1 failed' }

& uv run --no-project --with pykeepass python -I -X utf8 (Join-Path $Here 'scripts\pk_delete.py') $WorkPk (Join-Path $Out 'pk-delete.txt')
if ($LASTEXITCODE -ne 0) { throw 'scripts\pk_delete.py failed' }

& powershell.exe -NoProfile -ExecutionPolicy Bypass -File (Join-Path $Here 'scripts\count-affected-tests.ps1') -List (Join-Path $Here 'recorded\affected-tests.txt') -OutFile (Join-Path $Out 'affected-tests-count.txt')
if ($LASTEXITCODE -ne 0) { throw 'scripts\count-affected-tests.ps1 failed' }
