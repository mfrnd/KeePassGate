# Added on this branch to run the survival critique's probes from the branch root with one command,
# in Windows PowerShell 5.1. It measures nothing itself. In order:
#
#   1. `dotnet build` of the design's `probe\Wc.csproj` into `..\design\probe\out\`, since the
#      critic ran the design's `Wc.exe` unchanged;
#   2. `npm ci` of kdbxweb 2.1.1 into `..\design\kdbxweb\node_modules\`, the one install both papers
#      use;
#   3. `scripts\Run.ps1`, sequences e1 to e3, which writes `out\run.txt` and leaves `work\`;
#   4. `scripts\Run-e4.ps1`, sequence e4 over the sets step 3 made, into `out\e4.txt`;
#   5. `scripts\group-records.ps1`, written on this branch for the record the critic made without
#      keeping the command, into `out\group-records.txt`.
#
# `scripts\Run.ps1` runs in PowerShell 7 and not in Windows PowerShell: it sets
# `ProcessStartInfo.StandardInputEncoding`, which .NET Framework does not have, so it cannot have
# been recorded in 5.1. It starts `scripts\kpops.ps1` in Windows PowerShell itself, which is where
# KeePassLib runs. Everything this writes is gitignored: `work\` and `out\` here, the design's
# `probe\out\`, `probe\obj\` and `kdbxweb\node_modules\`, and through the tools `.keepassxc\` when a
# release is absent.
# cspell:ignore nologo loglevel
$ErrorActionPreference = 'Stop'
$Here = $PSScriptRoot
$Design = Join-Path (Split-Path -Parent $Here) 'design'

& dotnet build (Join-Path $Design 'probe\Wc.csproj') -c Release -o (Join-Path $Design 'probe\out') --nologo -v q
if ($LASTEXITCODE -ne 0) { throw 'dotnet build failed' }

& npm ci --prefix (Join-Path $Design 'kdbxweb') --prefer-offline --no-audit --no-fund --loglevel error
if ($LASTEXITCODE -ne 0) { throw 'npm ci failed' }

& pwsh.exe -NoProfile -ExecutionPolicy Bypass -File (Join-Path $Here 'scripts\Run.ps1')
if ($LASTEXITCODE -ne 0) { throw 'scripts\Run.ps1 failed' }

& powershell.exe -NoProfile -ExecutionPolicy Bypass -File (Join-Path $Here 'scripts\Run-e4.ps1')
if ($LASTEXITCODE -ne 0) { throw 'scripts\Run-e4.ps1 failed' }

& powershell.exe -NoProfile -ExecutionPolicy Bypass -File (Join-Path $Here 'scripts\group-records.ps1')
if ($LASTEXITCODE -ne 0) { throw 'scripts\group-records.ps1 failed' }
