# Written on this branch, not by the design's agent: checks of what `design.md` states about the
# files a run leaves in `work\` and that no recorded output shows. It changes nothing and measures
# nothing the probe measured. It prints, into `out\checks.txt`, which has no recorded twin:
#
#   - the KDBX version in the header of every database under `work\`, counted by the program that
#     wrote it, which the directory names (design section 11), and for a file above 4.0 the
#     previous parent group of each snapshot entry, which is the 4.1 field a move writes;
#   - for set X, how many groups each merged file holds under the store group's fixed identifier
#     and how many are named `KeePassGate` (design sections 2 and 3.1, "one group after a merge");
#   - the size of `work\base\base.kdbx` (design section 3.2, "7.1 KB");
#   - the pykeepass release `uv` resolves (design sections 8.1 and 16, "pykeepass 4.2.0").
#
# Windows PowerShell 5.1, because it loads KeePassLib.
$ErrorActionPreference = 'Stop'
$Root = Split-Path -Parent $PSScriptRoot
$Work = Join-Path $Root 'work'
$Out = Join-Path $Root 'out'
. (Join-Path $PSScriptRoot '..\..\..\..\..\..\tools\KeePass.ps1')
[void] [Reflection.Assembly]::LoadFrom((Get-KeePassExe))

$lines = New-Object System.Collections.Generic.List[string]

function Get-KdbxVersion([string] $Path) {
    $head = New-Object byte[] 12
    $stream = [System.IO.File]::OpenRead($Path)
    try { [void] $stream.Read($head, 0, 12) } finally { $stream.Dispose() }
    $v = [System.BitConverter]::ToUInt32($head, 8)
    '{0}.{1}' -f ($v -shr 16), ($v -band 0xFFFF)
}

function Get-Writer([string] $Relative) {
    $top = ($Relative -split '\\')[0]
    $leaf = Split-Path -Leaf $Relative
    if ($top -eq 'base' -or $top -eq 'chain' -or $top -eq 'self') { return 'the probe (KeePassLib 2.62)' }
    if ($top -like 'kp-*') { return 'the probe (KeePassLib 2.62)' }
    if ($top -like 'xc-hand-*' -and $leaf -eq 'stale.kdbx') { return 'the probe (a copy of an input)' }
    if ($top -like 'xc-save-*' -or $top -like 'xc-hand-*') { return "keepassxc-cli $($top -replace '^xc-(save|hand)-', '')" }
    if ($top -like 'xc-*') {
        if ($leaf -eq 'source.kdbx') { return 'the probe (a copy of an input)' }
        return "keepassxc-cli $($top -replace '^xc-[A-Z]-into[AB]-', '')"
    }
    if ($top -eq 'big') { return 'the probe (KeePassLib 2.62)' }
    if ($top -eq 'pk') { return 'pykeepass' }
    if ($top -eq 'kw') { if ($Relative -like 'kw\out\*') { return 'kdbxweb' } else { return 'the probe (KeePassLib 2.62)' } }
    if ($top -eq 'kp247') { return 'KeePass 2.47' }
    return "unknown ($top)"
}

$lines.Add('## KDBX versions of every database under work, by the program that wrote it')
$counts = @{}
foreach ($file in Get-ChildItem -LiteralPath $Work -Recurse -Filter '*.kdbx') {
    $relative = $file.FullName.Substring($Work.Length + 1)
    $key = (Get-Writer $relative) + ': kdbx ' + (Get-KdbxVersion $file.FullName)
    if ($counts.ContainsKey($key)) { $counts[$key]++ } else { $counts[$key] = 1 }
}
foreach ($key in ($counts.Keys | Sort-Object)) { $lines.Add("$key, $($counts[$key]) files") }

# A file above 4.0 holds a 4.1 field. The one a move writes is an entry's previous parent group.
$lines.Add('')
$lines.Add('## files above KDBX 4.0, and the previous parent group of each entry holding KPG.Store')
foreach ($file in Get-ChildItem -LiteralPath $Work -Recurse -Filter '*.kdbx') {
    $version = Get-KdbxVersion $file.FullName
    if ($version -eq '4.0' -or $version -like '3.*') { continue }
    $key = New-Object KeePassLib.Keys.CompositeKey
    $key.AddUserKey((New-Object KeePassLib.Keys.KcpPassword 'probe-only'))
    $db = New-Object KeePassLib.PwDatabase
    $db.Open([KeePassLib.Serialization.IOConnectionInfo]::FromPath($file.FullName), $key, $null)
    try {
        foreach ($e in $db.RootGroup.GetEntries($true)) {
            if ($e.Strings.Get('KPG.Store') -eq $null) { continue }
            $lines.Add("$($file.FullName.Substring($Work.Length + 1)): kdbx $version, previous parent group $($e.PreviousParentGroup.ToHexString())")
        }
    }
    finally { $db.Close() }
}

$lines.Add('')
$lines.Add('## set X: groups in each merged file')
$storeGroup = New-Object KeePassLib.PwUuid (,[byte[]](0x4B, 0x50, 0x47, 0x47, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 1))
$cells = @(Get-ChildItem -LiteralPath $Work -Directory | Where-Object { $_.Name -like 'kp-X-*' -or $_.Name -like 'xc-X-*' } | Sort-Object Name)
foreach ($cell in $cells) {
    $file = if ($cell.Name -like 'kp-*') { Join-Path $cell.FullName 'result.kdbx' } else { Join-Path $cell.FullName 'target.kdbx' }
    $key = New-Object KeePassLib.Keys.CompositeKey
    $key.AddUserKey((New-Object KeePassLib.Keys.KcpPassword 'probe-only'))
    $db = New-Object KeePassLib.PwDatabase
    $db.Open([KeePassLib.Serialization.IOConnectionInfo]::FromPath($file), $key, $null)
    try {
        $byId = 0; $byName = 0
        foreach ($g in $db.RootGroup.GetGroups($true)) {
            if ($g.Uuid.Equals($storeGroup)) { $byId++ }
            if ($g.Name -eq 'KeePassGate') { $byName++ }
        }
        $lines.Add("$($cell.Name): $byId under the fixed identifier, $byName named KeePassGate")
    }
    finally { $db.Close() }
}

$lines.Add('')
$lines.Add('## sizes')
$base = Get-Item -LiteralPath (Join-Path $Work 'base\base.kdbx')
$lines.Add("work\base\base.kdbx: $($base.Length) bytes")

$lines.Add('')
$lines.Add('## pykeepass')
# What uv prints on its standard error while it resolves is not wanted here, and Windows PowerShell
# turns a native program's standard error into errors, which 'Stop' would make fatal.
$ErrorActionPreference = 'Continue'
$pk = @(& uv run --no-project --with pykeepass python -I -X utf8 -c "import importlib.metadata as m; print('pykeepass', m.version('pykeepass'))" 2>&1 | ForEach-Object { "$_" })
$ErrorActionPreference = 'Stop'
if ($LASTEXITCODE -ne 0) { throw 'uv run failed' }
$lines.Add(@($pk | Where-Object { $_ -like 'pykeepass *' })[-1])

[System.IO.File]::WriteAllLines((Join-Path $Out 'checks.txt'), $lines)
