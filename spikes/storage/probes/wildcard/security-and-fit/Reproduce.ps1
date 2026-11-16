# Added on this branch to run the security and fit critique's probes from the branch root with one
# command, in Windows PowerShell 5.1. It measures nothing itself. In order: `scripts\probe.ps1`,
# P1 and P2, into `out\probe.txt`, with its databases in `work\`, where the critic used `work2\`,
# which `.gitignore` does not cover; then `scripts\count-code.ps1`, the counts written on this
# branch for the critique's sections 5.1 and 5.2, into `out\code-counts.txt`. Everything it writes
# is gitignored: `work\` and `out\`, and the product's files `tools\Product.ps1` exports into
# `.product\`.
$ErrorActionPreference = 'Stop'
$Here = $PSScriptRoot
$Work = Join-Path $Here 'work'
$Out = Join-Path $Here 'out'
if (Test-Path -LiteralPath $Work) { Remove-Item -LiteralPath $Work -Recurse -Force }
New-Item -ItemType Directory -Force -Path $Work, $Out | Out-Null

& powershell.exe -NoProfile -ExecutionPolicy Bypass -File (Join-Path $Here 'scripts\probe.ps1') $Work (Join-Path $Out 'probe.txt')
if ($LASTEXITCODE -ne 0) { throw 'scripts\probe.ps1 failed' }

& powershell.exe -NoProfile -ExecutionPolicy Bypass -File (Join-Path $Here 'scripts\count-code.ps1') -OutFile (Join-Path $Out 'code-counts.txt')
if ($LASTEXITCODE -ne 0) { throw 'scripts\count-code.ps1 failed' }
