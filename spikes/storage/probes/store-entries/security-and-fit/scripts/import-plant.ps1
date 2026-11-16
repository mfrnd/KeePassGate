# Probe for the critique of option store-entries, key store-entries-security-and-fit.
# Question: does KeePass 2.62's own Bitwarden JSON importer, run headless, put a custom field
# named by the imported file (here KPG.Kind and KPG.Data) into the open database verbatim,
# the way File then Import does it (a temporary database merged with CreateNewUuids)?
# Invented content only; no file database is written; no window.
param(
    [Parameter(Mandatory = $true)][string]$KeePassExe,
    [Parameter(Mandatory = $true)][string]$WorkDir,
    [Parameter(Mandatory = $true)][string]$OutFile
)
$ErrorActionPreference = 'Stop'
$kp = [System.Reflection.Assembly]::LoadFrom($KeePassExe)
$lines = New-Object System.Collections.Generic.List[string]
$lines.Add("KeePass assembly: " + $kp.GetName().Version + "  runtime " + [Environment]::Version)

$digest = ('3f2a' * 16)   # invented 64 hex digits standing for an authority digest
$json = @"
{
  "encrypted": false,
  "folders": [ { "id": "f1", "name": "Shopping" } ],
  "items": [
    {
      "id": "i1", "folderId": "f1", "type": 1, "name": "Example shop",
      "login": { "username": "someone", "password": "invented-password" },
      "fields": [
        { "name": "KPG.Kind", "value": "retired", "type": 0 },
        { "name": "KPG.Data", "value": "{\"v\":\"1\",\"authority\":\"$digest\"}", "type": 0 }
      ]
    }
  ]
}
"@
$jsonPath = [System.IO.Path]::Combine($WorkDir, 'invented-bitwarden.json')
[System.IO.File]::WriteAllText($jsonPath, $json, (New-Object System.Text.UTF8Encoding($false)))

function New-MemoryDatabase([string]$name) {
    $db = New-Object KeePassLib.PwDatabase
    $ck = New-Object KeePassLib.Keys.CompositeKey
    $ck.AddUserKey((New-Object KeePassLib.Keys.KcpPassword('invented-throwaway')))
    $db.New((New-Object KeePassLib.Serialization.IOConnectionInfo), $ck)
    $db.Name = $name
    return $db
}

$target = New-MemoryDatabase 'target'
$temp = New-MemoryDatabase 'imported'

$fmtType = $kp.GetType('KeePass.DataExchange.Formats.BitwardenJson112', $true)
$fmt = [System.Activator]::CreateInstance($fmtType, $true)
$lines.Add("format: " + $fmt.FormatName + "  SupportsUuids=" + $fmt.SupportsUuids)

$fs = [System.IO.File]::OpenRead($jsonPath)
try { $fmt.Import($temp, $fs, $null) } finally { $fs.Dispose() }

# File then Import: a format without identifiers is merged with CreateNewUuids (ImportUtil.cs:191).
$target.MergeIn($temp, [KeePassLib.PwMergeMethod]::CreateNewUuids)

foreach ($pe in $target.RootGroup.GetEntries($true)) {
    $lines.Add("entry " + $pe.Uuid.ToHexString() + " in group '" + $pe.ParentGroup.Name + "'")
    foreach ($kvp in $pe.Strings) {
        $lines.Add("  string [" + $kvp.Key + "] protected=" + $kvp.Value.IsProtected + " value=" + $kvp.Value.ReadString())
    }
}
foreach ($pg in $target.RootGroup.GetGroups($true)) {
    $lines.Add("group '" + $pg.Name + "' searching=" + $pg.EnableSearching + " autotype=" + $pg.EnableAutoType)
}
[System.IO.File]::WriteAllLines($OutFile, $lines)
