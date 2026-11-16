# Added on this branch. Prints the history defaults and the maintenance period of a new database,
# which `ground-inventory.md` quotes in sections 0, 4.2 and 8 from two lines appended to `sizes.txt`
# "by reflection". The command that wrote them was not kept, so this writes the same line again
# from KeePass 2.62's KeePassLib. `sizes.ps1` printed the two constants as empty because they are
# `internal` in `KeePassLib/PwDatabase.cs` (lines 47 and 48 of the 2.62 source), which PowerShell's
# `[Type]::Name` does not reach, so they are read here as non-public static fields.
# Windows PowerShell 5.1. Shows no window, writes no file.
param([string]$KeePass)

$ErrorActionPreference = 'Stop'
$kp = [System.Reflection.Assembly]::LoadFrom($KeePass)
$type = $kp.GetType('KeePassLib.PwDatabase', $true)
$flags = [System.Reflection.BindingFlags]'NonPublic, Public, Static'
$items = $type.GetField('DefaultHistoryMaxItems', $flags).GetValue($null)
$size = $type.GetField('DefaultHistoryMaxSize', $flags).GetValue($null)
$db = New-Object KeePassLib.PwDatabase

"PwDatabase.DefaultHistoryMaxItems = $items, DefaultHistoryMaxSize = $size, new PwDatabase MaintenanceHistoryDays = $($db.MaintenanceHistoryDays) (KeePass $($kp.GetName().Version.ToString(2)))"
"new PwDatabase HistoryMaxItems = $($db.HistoryMaxItems), HistoryMaxSize = $($db.HistoryMaxSize)"
