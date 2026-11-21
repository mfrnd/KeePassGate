# Builds a throwaway KeePass for the input probe: a copy of the pinned KeePass, the configuration
# the end to end tier writes, the plugin as built, and a database of invented entries whose
# password is a dummy. Shows no window.
#   new-keepass.ps1 -Worktree <a KeePassGate worktree, built> -Directory <empty or absent directory>
param([Parameter(Mandatory)] [string] $Worktree, [Parameter(Mandatory)] [string] $Directory)
$ErrorActionPreference = 'Stop'
$version = (Get-Content (Join-Path $Worktree 'keepass.version') -Raw).Trim()
$source = Join-Path $Worktree ".keepass\$version"
if (Test-Path $Directory) { Remove-Item $Directory -Recurse -Force }
$keepass = Join-Path $Directory 'keepass'
New-Item -ItemType Directory -Force $keepass | Out-Null
Copy-Item (Join-Path $source '*') $keepass -Recurse
Set-Content (Join-Path $keepass 'KeePass.config.xml') -Encoding UTF8 -Value @'
<?xml version="1.0" encoding="utf-8"?>
<Configuration>
  <Meta>
    <PreferUserConfiguration>false</PreferUserConfiguration>
  </Meta>
  <Application>
    <Start>
      <CheckForUpdate>false</CheckForUpdate>
      <CheckForUpdateConfigured>true</CheckForUpdateConfigured>
    </Start>
  </Application>
  <MainWindow>
    <EscAction>None</EscAction>
  </MainWindow>
  <Integration>
    <LimitToSingleInstance>false</LimitToSingleInstance>
  </Integration>
</Configuration>
'@
$plugins = Join-Path $keepass 'plugins\KeePassGate'
New-Item -ItemType Directory -Force $plugins | Out-Null
Copy-Item (Join-Path $Worktree 'src\KeePassGate\bin\Debug\net48\*.dll') $plugins

[void] [Reflection.Assembly]::LoadFrom((Join-Path $keepass 'KeePass.exe'))
$database = New-Object KeePassLib.PwDatabase
$key = New-Object KeePassLib.Keys.CompositeKey
$key.AddUserKey((New-Object KeePassLib.Keys.KcpPassword('dummy-probe-password')))
$path = Join-Path $Directory 'throwaway.kdbx'
$database.New([KeePassLib.Serialization.IOConnectionInfo]::FromPath($path), $key)
$database.Name = 'throwaway'
$group = New-Object KeePassLib.PwGroup($true, $true, 'Probe group', [KeePassLib.PwIcon]::Folder)
$database.RootGroup.AddGroup($group, $true)
foreach ($title in 'Alpha', 'Beta', 'Gamma') {
    $entry = New-Object KeePassLib.PwEntry($true, $true)
    $entry.Strings.Set('Title', (New-Object KeePassLib.Security.ProtectedString($false, $title)))
    $entry.Strings.Set('UserName', (New-Object KeePassLib.Security.ProtectedString($false, "user-$title")))
    $database.RootGroup.AddEntry($entry, $true)
}
$database.Save($null)
$database.Close()
"made $path"
