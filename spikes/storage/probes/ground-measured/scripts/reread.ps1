# Re-reads kept result databases with KeePassLib and prints, per file, what the recorded outputs
# never printed: every string of every entry (names, and values that are not protected), each
# history item's time, rule and strings, and the database's history and maintenance settings.
# Windows PowerShell 5.1 only. Reads copies; writes nothing but standard output.
#   powershell.exe -NoProfile -File reread.ps1 -KeePass <dir with KeePass.exe> -List <file of paths> -Password <pw> [-Root <prefix to cut>]
param(
    [Parameter(Mandatory)] [string] $KeePass,
    [Parameter(Mandatory)] [string] $List,
    [Parameter(Mandatory)] [string] $Password,
    [string] $Root = ''
)
$ErrorActionPreference = 'Stop'
if ($PSVersionTable.PSEdition -ne 'Desktop') { throw 'Windows PowerShell 5.1 only' }
$asm = [Reflection.Assembly]::LoadFrom((Join-Path $KeePass 'KeePass.exe'))
[Console]::Out.WriteLine('KeePassLib ' + [KeePassLib.PwDefs]::VersionString)

$ruleEC = '{"reader":["read","list"]}'
$ruleEP = '{"reader":[]}'
$ruleGC = '{"reader":["read","list"],"writer":["create","read","update","list"]}'
$ruleGP = '{"reader":["list"]}'

function Rule([object] $cd, [string] $c, [string] $p) {
    if (-not $cd.Exists('KPG')) { return '-' }
    $v = $cd.Get('KPG')
    if ($v -eq $c) { return 'C' }
    if ($v -eq $p) { return 'P' }
    return '?'
}

function Iso([DateTime] $t) { return $t.ToUniversalTime().ToString('yyyy-MM-ddTHH:mm:ssZ', [Globalization.CultureInfo]::InvariantCulture) }

function Strings([object] $e) {
    $parts = @()
    foreach ($kv in $e.Strings) {
        if ($kv.Value.IsProtected) { $parts += ($kv.Key + '=<protected ' + $kv.Value.Length + '>') }
        else { $parts += ($kv.Key + '=' + $kv.Value.ReadString()) }
    }
    return ($parts -join '; ')
}

function OtherData([object] $cd) {
    $parts = @()
    foreach ($kv in $cd) { if ($kv.Key -ne 'KPG') { $parts += $kv.Key } }
    if ($parts.Count -eq 0) { return '' }
    return ' other customdata [' + ($parts -join ',') + ']'
}

$paths = Get-Content -LiteralPath $List | Where-Object { $_.Trim().Length -gt 0 }
foreach ($path in $paths) {
    $name = $path
    if ($Root -and $path.StartsWith($Root)) { $name = $path.Substring($Root.Length) }
    $bytes = New-Object byte[] 12
    $fs = [IO.File]::OpenRead($path); [void] $fs.Read($bytes, 0, 12); $fs.Close()
    $ver = [BitConverter]::ToUInt32($bytes, 8)
    $key = New-Object KeePassLib.Keys.CompositeKey
    $key.AddUserKey((New-Object KeePassLib.Keys.KcpPassword -ArgumentList $Password))
    $db = New-Object KeePassLib.PwDatabase
    try {
        $db.Open([KeePassLib.Serialization.IOConnectionInfo]::FromPath($path), $key, $null)
    } catch {
        [Console]::Out.WriteLine("$name | OPEN FAILED: " + $_.Exception.Message)
        continue
    }
    try {
        [Console]::Out.WriteLine(('{0} | kdbx {1}.{2} | HistoryMaxItems {3} HistoryMaxSize {4} MaintenanceHistoryDays {5} | SettingsChanged {6} | RecycleBin {7} | deleted {8}' -f
            $name, ($ver -shr 16), ($ver -band 0xFFFF), $db.HistoryMaxItems, $db.HistoryMaxSize, $db.MaintenanceHistoryDays,
            (Iso $db.SettingsChanged), $db.RecycleBinEnabled, $db.DeletedObjects.UCount))
        $groups = @($db.RootGroup) + @($db.RootGroup.GetGroups($true))
        foreach ($g in $groups) {
            $r = '-'
            if ($g.Name -eq 'G') { $r = Rule $g.CustomData $ruleGC $ruleGP }
            [Console]::Out.WriteLine(('  group {0} {1} mtime {2} rule {3} entries {4}{5}' -f $g.Name, $g.Uuid.ToHexString().Substring(0, 8),
                (Iso $g.LastModificationTime), $r, $g.Entries.UCount, (OtherData $g.CustomData)))
        }
        foreach ($e in $db.RootGroup.GetEntries($true)) {
            $title = $e.Strings.ReadSafe('Title')
            $r = '-'
            if ($title -eq 'E1') { $r = Rule $e.CustomData $ruleEC $ruleEP }
            [Console]::Out.WriteLine(('  entry {0} {1} in {2} mtime {3} rule {4} tags [{5}] binaries {6} strings {{{7}}}{8}' -f $title,
                $e.Uuid.ToHexString().Substring(0, 8), $e.ParentGroup.Name, (Iso $e.LastModificationTime), $r, ($e.Tags -join ','),
                $e.Binaries.UCount, (Strings $e), (OtherData $e.CustomData)))
            $i = 0
            foreach ($h in $e.History) {
                $i++
                $hr = '-'
                if ($title -eq 'E1') { $hr = Rule $h.CustomData $ruleEC $ruleEP }
                [Console]::Out.WriteLine(('    history {0}: mtime {1} rule {2} strings {{{3}}}{4}' -f $i, (Iso $h.LastModificationTime), $hr,
                    (Strings $h), (OtherData $h.CustomData)))
            }
        }
    } finally { $db.Close() }
}
