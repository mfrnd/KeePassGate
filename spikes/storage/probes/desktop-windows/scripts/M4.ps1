<#
.SYNOPSIS
    Measurement 4: KeePass 2.62's own maintenance and editing commands on a store made of entries.

.DESCRIPTION
    Each case opens a fresh copy of M4.kdbx (probe `m4base`: the group KeePassGate with write-once
    entries, copies under other identifiers, store entries in the recycle bin, deletion records
    that name no entry and records of real deletions, old and new) in KeePass's window, runs one
    command through the menus or keys a person would use, answers its dialogs as the case names,
    saves with Ctrl+S, and reads the file back. What changed is the diff of the file's dump
    against the base's.
#>
param(
    [string] $Work = 'W:\',
    [string[]] $Case = @('all'),
    [int] $Repeat = 1,
    [string] $Unelevated = (Join-Path $env:TEMP 'unelevated.ps1')
)

$ErrorActionPreference = 'Stop'
# `powershell.exe -File` passes `a,b` as one string rather than a list.
$Case = @($Case | ForEach-Object { $_ -split ',' })
. (Join-Path $PSScriptRoot 'Ui.ps1')
. (Join-Path $PSScriptRoot 'Kp.ps1')
$family = Split-Path -Parent $PSScriptRoot
$probe = Join-Path $family 'probe\out\Desk.exe'
$script:WorkRoot = $Work
$script:UnelevatedScript = $Unelevated
$script:KpExe = Join-Path $Work 'kp\KeePass.exe'
$base = Join-Path $Work 'base'
New-Item -ItemType Directory -Force (Join-Path $Work 'logs'), (Join-Path $Work 'runs\m4') | Out-Null
if (-not (Test-Path (Join-Path $base 'M4.kdbx'))) { & $probe m4base (Join-Path $base 'M4.kdbx') | Out-Null }
$baseDump = Join-Path $base 'M4.dump.txt'
& $probe dump (Join-Path $base 'M4.kdbx') $baseDump | Out-Null

function Probe([string[]] $a) {
    $out = & $probe @a
    if ($LASTEXITCODE -ne 0) { throw "probe $($a -join ' ') failed" }
    $out
}

# The lines of the file's dump that differ from the base's, times of access left out.
function Write-Diff([string] $Dump, [string] $Out) {
    $a = Get-Content $baseDump | Where-Object { $_ -notmatch ' t\.accessed | usage ' -and $_ -notmatch '^# file' }
    $b = Get-Content $Dump | Where-Object { $_ -notmatch ' t\.accessed | usage ' -and $_ -notmatch '^# file' }
    $d = @(Compare-Object $a $b | Sort-Object { $_.InputObject } | ForEach-Object { $(if ($_.SideIndicator -eq '<=') { '- ' } else { '+ ' }) + $_.InputObject })
    [IO.File]::WriteAllLines($Out, [string[]]$d)
    $d
}

function Run-M4([hashtable] $c, [int] $Attempt) {
    $id = "$($c['Id'])-$Attempt"
    $dir = Join-Path $Work "runs\m4\$id"
    if (Test-Path $dir) { Remove-Item -LiteralPath $dir -Recurse -Force }
    New-Item -ItemType Directory -Force $dir | Out-Null
    $script:LogFile = Join-Path $dir 'run.log'
    $script:ShotDir = $dir
    $db = Join-Path $dir 'db.kdbx'
    Copy-Item (Join-Path $base 'M4.kdbx') $db
    Say "==== $id : $($c['What'])"
    Write-KpConfig @{}
    $kp = Start-Kp $db $id
    try {
        $known = Get-Handles $kp.Process.Id
        & $c['Do'] $kp $id
        $seen = @(Kp-Dialogs $kp $known $id $c['Answers'] 5)
        Say "dialogs: $($seen.Count)"
        Kp-Look $kp 'after'
        if (-not $c['NoSave']) {
            $known = Get-Handles $kp.Process.Id
            Send-To $kp.Main '^s' 1000
            [void](Kp-Dialogs $kp $known "$id-save" @() 3)
        }
        Say "file: $(Get-FileState $db)"
        [void](Kp-Exit $kp "$id-exit")
    }
    finally { Stop-Started }
    Probe @('dump', $db, (Join-Path $dir 'db.dump.txt')) | Out-Null
    $d = @(Write-Diff (Join-Path $dir 'db.dump.txt') (Join-Path $dir 'diff-vs-base.txt'))
    Say "diff against the base: $($d.Count) lines"
    $d | Where-Object { $_ -match '^[-+] (D |F groups|M customIcons|M CD |E \S+ parent |G \S+ parent |G \S+ name )' -or $_ -match 'history.count' } | ForEach-Object { Say "  $_" }
    Add-Content (Join-Path $Work 'logs\m4-summary.txt') "$id | $($d.Count) diff lines" -Encoding UTF8
}

# The commands, each a script block over the running KeePass.
$cases = @(
    @{ Id = 'maint-history'; What = 'Database Maintenance: delete history entries older than the days shown'
       Do = { param($kp, $id) Kp-Maintenance $kp $id 'history' }; Answers = @() }
    @{ Id = 'maint-records'; What = 'Database Maintenance: remove deletion records'
       Do = { param($kp, $id) Kp-Maintenance $kp $id 'records' }; Answers = @() }
    @{ Id = 'maint-plugindata'; What = 'Database Maintenance: Plugin data, delete KPG.Profiles'
       Do = { param($kp, $id) Kp-Maintenance $kp $id 'plugindata' }; Answers = @() }
    @{ Id = 'dde'; What = 'Tools, Database Tools, Delete Duplicate Entries'
       Do = { param($kp, $id) Kp-Menu $kp @('Tools', 'Database Tools', 'Delete Duplicate Entries') }; Answers = @('*', 'Yes', '*', 'OK') }
    @{ Id = 'deg'; What = 'Tools, Database Tools, Delete Empty Groups'
       Do = { param($kp, $id) Kp-Menu $kp @('Tools', 'Database Tools', 'Delete Empty Groups') }; Answers = @('*', 'Yes', '*', 'OK') }
    @{ Id = 'ducis'; What = 'Tools, Database Tools, Delete Unused Custom Icons'
       Do = { param($kp, $id) Kp-Menu $kp @('Tools', 'Database Tools', 'Delete Unused Custom Icons') }; Answers = @('*', 'Yes', '*', 'OK') }
    @{ Id = 'group-delete'; What = 'the group KeePassGate selected, Delete key'
       Do = { param($kp, $id) [void](Kp-Group $kp 'KeePassGate'); Send-To $null '{DELETE}' 500 }; Answers = @('*', 'Yes') }
    @{ Id = 'group-shiftdelete'; What = 'the group KeePassGate selected, Shift+Delete'
       Do = { param($kp, $id) [void](Kp-Group $kp 'KeePassGate'); Send-To $null '+{DELETE}' 500 }; Answers = @('*', 'Delete', '*', 'Yes') }
    @{ Id = 'entry-delete'; What = 'the store entry KPG grant 1 selected, Delete key'
       Do = { param($kp, $id) [void](Kp-SelectEntry $kp 'KeePassGate' 'KPG grant 1'); Send-To $null '{DELETE}' 500 }; Answers = @('*', 'Yes') }
    @{ Id = 'entry-shiftdelete'; What = 'the store entry KPG grant 1 selected, Shift+Delete'
       Do = { param($kp, $id) [void](Kp-SelectEntry $kp 'KeePassGate' 'KPG grant 1'); Send-To $null '+{DELETE}' 500 }; Answers = @('*', 'Delete', '*', 'Yes') }
    @{ Id = 'empty-bin'; What = 'the recycle bin selected, Group menu, Empty Recycle Bin'
       Do = { param($kp, $id) [void](Kp-Group $kp 'Recycle Bin'); Kp-Menu $kp @('Group', 'Empty Recycle Bin') }; Answers = @('*', 'Delete', '*', 'Yes') }
    @{ Id = 'group-duplicate'; What = 'the group KeePassGate selected, Group, Duplicate Group'
       Do = { param($kp, $id) [void](Kp-Group $kp 'KeePassGate'); Kp-Menu $kp @('Group', 'Duplicate Group') }; Answers = @('*', 'OK') }
    @{ Id = 'entry-duplicate'; What = 'KPG grant 1 selected, Entry, Duplicate Entry, the options as offered'
       Do = { param($kp, $id) [void](Kp-SelectEntry $kp 'KeePassGate' 'KPG grant 1'); Kp-Menu $kp @('Entry', 'Duplicate Entr') }; Answers = @('*', 'OK') }
    @{ Id = 'group-sort'; What = 'the root selected, Group, Rearrange, Sort Direct Subgroups'
       Do = { param($kp, $id) [void](Kp-Group $kp 'Desk store'); Kp-Menu $kp @('Group', 'Rearrange', 'Sort Direct Subgroups') }; Answers = @() }
    @{ Id = 'group-moveup'; What = 'the group Passwords selected, Group, Rearrange, Move Group Up'
       Do = { param($kp, $id) [void](Kp-Group $kp 'Passwords'); Kp-Menu $kp @('Group', 'Rearrange', 'Move Group One Up') }; Answers = @() }
    @{ Id = 'entry-movedown'; What = 'KPG grant 1 selected, Entry, Rearrange, Move Entry One Down by its access keys (Alt+E, R, D)'
       Do = { param($kp, $id) [void](Kp-SelectEntry $kp 'KeePassGate' 'KPG grant 1'); Send-To $null '%e' 500; Send-To $null 'r' 500; Send-To $null 'd' 800 }; Answers = @() }
    @{ Id = 'entry-move'; What = 'KPG grant 1 dragged onto the group Passwords'
       Do = { param($kp, $id) [void](Kp-SelectEntry $kp 'KeePassGate' 'KPG grant 1'); Kp-DragEntry $kp 'KPG grant 1' 'Passwords' }; Answers = @() }
)

function Kp-Maintenance($kp, [string] $Id, [string] $What) {
    $known = Get-Handles $kp.Process.Id
    Kp-Menu $kp @('Tools', 'Database Tools', 'Database Maintenance')
    $d = Need (Wait-Window $kp.Process.Id 'Database Maintenance*' 10 $known) 'Database Maintenance'
    Start-Sleep -Milliseconds 600
    [void](Read-Window $d "$Id-maint")
    Save-Shot $d "$Id-maint"
    $h = Get-Handle $d
    $raw = { param($t) Get-Kids $h | Where-Object { $_.Class -like '*BUTTON*' -and $_.Text -eq $t } | Select-Object -First 1 }
    switch ($What) {
        # "&Delete" in the group "Entry history", "D&elete" in "Deleted objects information".
        'history' { $b = & $raw '&Delete'; Click-At ([int](($b.L + $b.R) / 2)) ([int](($b.T + $b.B) / 2)); Say 'pressed Delete under Entry history'; Start-Sleep -Seconds 2 }
        'records' { $b = & $raw 'D&elete'; Click-At ([int](($b.L + $b.R) / 2)) ([int](($b.T + $b.B) / 2)); Say 'pressed Delete under Deleted objects information'; Start-Sleep -Seconds 2 }
        'plugindata' {
            $tab = Get-Kids $h | Where-Object { $_.Class -like '*SysTabControl32*' } | Select-Object -First 1
            $page = Get-MsaaItems $tab.H | Where-Object { $_.Name -eq 'Plugin Data' } | Select-Object -First 1
            $loc = [DeskMsaa]::Location([IntPtr]$tab.H, $page.Id).Split(',')
            Click-At ([int]$loc[0] + [int]$loc[2] / 2) ([int]$loc[1] + [int]$loc[3] / 2)
            Start-Sleep -Milliseconds 700
            [void](Read-Window $d "$Id-plugindata")
            Save-Shot $d "$Id-plugindata"
            $list = Get-Kids $h | Where-Object { $_.Class -like '*SysListView32*' } | Select-Object -First 1
            $rows = Get-MsaaItems $list.H | Where-Object { $_.Role -eq '34' }
            $rows | ForEach-Object { Say "plugin data row <<$($_.Name)>>" }
            $row = $rows | Where-Object { $_.Name -eq 'KPG.Profiles' } | Select-Object -First 1
            $loc = [DeskMsaa]::Location([IntPtr]$list.H, $row.Id).Split(',')
            Click-At ([int]$loc[0] + 20) ([int]$loc[1] + [int]$loc[3] / 2)
            Start-Sleep -Milliseconds 400
            $b = & $raw '&Delete'
            Click-At ([int](($b.L + $b.R) / 2)) ([int](($b.T + $b.B) / 2))
            Say 'pressed Delete on the Plugin Data tab'
            Start-Sleep -Seconds 1
        }
    }
    [void](Read-Window $d "$Id-maint-after")
    [void](Click-Kid $h 'Close')
    [void](Wait-Gone $d)
}

foreach ($c in $cases) {
    if ($Case -notcontains 'all' -and $Case -notcontains $c['Id']) { continue }
    for ($r = 1; $r -le $Repeat; $r++) {
        try { Run-M4 $c $r }
        catch {
            Say "RUN FAILED $($c['Id'])-${r}: $($_.Exception.Message) $($_.ScriptStackTrace)"
            Add-Content (Join-Path $Work 'logs\m4-summary.txt') "$($c['Id'])-$r | FAILED: $($_.Exception.Message)" -Encoding UTF8
            Stop-Started
            # A run whose flow failed is kept apart and repeated, never read.
            $failed = Join-Path $Work "runs\m4\$($c['Id'])-$r"
            if (Test-Path $failed) { Move-Item $failed (Join-Path $Work "runs\m4-not-counted\$($c['Id'])-$r-$((Get-Date).ToString('HHmmss'))") -Force }
        }
    }
}
