<#
.SYNOPSIS
    Runs the survival critic's probe from the branch, every part the recorded output came from, as
    `RUN.md` describes.

.DESCRIPTION
    Added on the branch: the agent started the parts by hand and kept no command. What they run is
    unchanged. `scripts\Survival.ps1` is started once per part, with `-Only`, each into the file its
    recorded twin is named after: `w1` (W1 and W1b) into `w1.txt`, then `w2`, `c1`, `k1` and `d1`.
    Every file goes to `out\` and the databases go to `work\`; both are emptied first.

    `npm ci` installs kdbxweb 2.1.1 into `kdbxweb\node_modules\` from the lock file the install the
    critic used was made with. KeePass comes from `tools\KeePass.ps1` and `keepassxc-cli` 2.8.0
    from `tools\KeePassXC.ps1`, which provide and check them; the probe takes all four paths as
    parameters, as it always did.

    Windows PowerShell 5.1, from the branch root:

        powershell.exe -NoProfile -ExecutionPolicy Bypass -File spikes\storage\probes\single-entry\survival\run.ps1
#>
$ErrorActionPreference = 'Stop'

$here = $PSScriptRoot
$out = Join-Path $here 'out'
$work = Join-Path $here 'work'
foreach ($directory in $out, $work) {
    if (Test-Path $directory) { Remove-Item $directory -Recurse -Force }
}
[void](New-Item -ItemType Directory -Force $out, $work)

$clock = [Diagnostics.Stopwatch]::StartNew()
Push-Location (Join-Path $here 'kdbxweb')
try {
    & npm.cmd ci --no-audit --no-fund --silent | Out-Null
    if ($LASTEXITCODE -ne 0) { throw "npm ci exited $LASTEXITCODE." }
}
finally { Pop-Location }
Write-Host ([string]::Format([Globalization.CultureInfo]::InvariantCulture, 'npm ci: {0:0.0} s', $clock.Elapsed.TotalSeconds))

# In child scopes, because the tools set `Set-StrictMode -Version Latest`.
$root = Join-Path $here '..\..\..\..\..'
$keePassExe = & { . (Join-Path $root 'tools\KeePass.ps1'); Get-KeePassExe }
$xcCli = & { . (Join-Path $root 'tools\KeePassXC.ps1'); Get-KeePassXCCli -Version '2.8.0' }
$kwJs = Join-Path $here 'scripts\kw.js'
$kwMod = Join-Path $here 'kdbxweb\node_modules\kdbxweb'

foreach ($part in 'w1', 'w2', 'c1', 'k1', 'd1') {
    $clock = [Diagnostics.Stopwatch]::StartNew()
    & powershell.exe -NoProfile -ExecutionPolicy Bypass -File (Join-Path $here 'scripts\Survival.ps1') `
        -KeePassExe $keePassExe -Work $work -OutFile (Join-Path $out "$part.txt") `
        -XcCli $xcCli -KwJs $kwJs -KwMod $kwMod -Only $part | Out-Null
    if ($LASTEXITCODE -ne 0) { throw "$part exited $LASTEXITCODE." }
    Write-Host ([string]::Format([Globalization.CultureInfo]::InvariantCulture, '{0}: {1:0.0} s', $part, $clock.Elapsed.TotalSeconds))
}
