# Added on this branch to run the design's probe from the branch root with one command, in Windows
# PowerShell 5.1. It measures nothing itself. In order:
#
#   1. `dotnet build` of `probe\Wc.csproj` into `probe\out\`, which the record's `probe\out\` and
#      `probe\obj\Release` imply and which nothing recorded;
#   2. `npm ci` of kdbxweb 2.1.1 as `kdbxweb\package-lock.json` pins it, into
#      `kdbxweb\node_modules\`;
#   3. `scripts\Run.ps1`, which writes `out\summary.txt` and `out\merges.txt`;
#   4. `scripts\Run-kdbxweb.ps1` and `scripts\Run-kp247.ps1`, written on this branch for the two
#      records the agent made without keeping the commands, `out\kdbxweb.txt` and
#      `out\keepass247.txt`;
#   5. `Wc.exe bench` and `Wc.exe lattice`, into `out\bench.txt` and `out\lattice.txt`;
#   6. `scripts\checks.ps1`, written on this branch, into `out\checks.txt`.
#
# `scripts\Run.ps1` runs in PowerShell 7 and not in Windows PowerShell: it sets
# `ProcessStartInfo.StandardInputEncoding`, which .NET Framework does not have, so it cannot have
# been recorded in 5.1. What it measures runs in `Wc.exe`, `keepassxc-cli` and pykeepass either way.
# Everything this writes is gitignored: `probe\out\`, `probe\obj\`, `kdbxweb\node_modules\`,
# `work\` and `out\`, and through the tools `.keepass\2.47\` and `.keepassxc\` when absent.
# cspell:ignore nologo loglevel
$ErrorActionPreference = 'Stop'
$Here = $PSScriptRoot
$Wc = Join-Path $Here 'probe\out\Wc.exe'
$Out = Join-Path $Here 'out'

& dotnet build (Join-Path $Here 'probe\Wc.csproj') -c Release -o (Join-Path $Here 'probe\out') --nologo -v q
if ($LASTEXITCODE -ne 0) { throw 'dotnet build failed' }

& npm ci --prefix (Join-Path $Here 'kdbxweb') --prefer-offline --no-audit --no-fund --loglevel error
if ($LASTEXITCODE -ne 0) { throw 'npm ci failed' }

& pwsh.exe -NoProfile -ExecutionPolicy Bypass -File (Join-Path $Here 'scripts\Run.ps1')
if ($LASTEXITCODE -ne 0) { throw 'scripts\Run.ps1 failed' }

& powershell.exe -NoProfile -ExecutionPolicy Bypass -File (Join-Path $Here 'scripts\Run-kdbxweb.ps1')
if ($LASTEXITCODE -ne 0) { throw 'scripts\Run-kdbxweb.ps1 failed' }

& powershell.exe -NoProfile -ExecutionPolicy Bypass -File (Join-Path $Here 'scripts\Run-kp247.ps1')
if ($LASTEXITCODE -ne 0) { throw 'scripts\Run-kp247.ps1 failed' }

$bench = & $Wc bench
if ($LASTEXITCODE -ne 0) { throw 'Wc.exe bench failed' }
[System.IO.File]::WriteAllLines((Join-Path $Out 'bench.txt'), [string[]] $bench)
$lattice = & $Wc lattice
if ($LASTEXITCODE -ne 0) { throw 'Wc.exe lattice failed' }
[System.IO.File]::WriteAllLines((Join-Path $Out 'lattice.txt'), [string[]] $lattice)

& powershell.exe -NoProfile -ExecutionPolicy Bypass -File (Join-Path $Here 'scripts\checks.ps1')
if ($LASTEXITCODE -ne 0) { throw 'scripts\checks.ps1 failed' }
