<#
.SYNOPSIS
    Measurement 3, the dialog half: what KeePass 2.62's entry dialog does to a plugin string
    KPG.Ref on a user's entry, in the real window, with the writing plugin DeskWrite in KeePass.

.DESCRIPTION
    Each case opens a fresh copy of M3.kdbx in the KeePass copy `kp-write`, opens a user's entry in
    its edit dialog, and then, by the case: (a) KPG.Ref is on the entry before the dialog opens
    (U1 "Mail account"); (b) the plugin adds KPG.Ref to the live entry while the dialog is open (U3
    "Shop"); (c) the plugin changes KPG.Ref on the live entry while the dialog is open (U1). The
    plugin's write is saved at once, as the product's single writer saves every write, and the
    file is read back right after it. The dialog is then closed by OK with nothing touched, by OK
    after the user name was edited, or by Cancel; the plugin reports what the entry holds in
    memory, the window's title shows whether KeePass counts the database as changed, Ctrl+S
    saves, and the file is read back.
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
$script:KpExe = Join-Path $Work 'kp-write\KeePass.exe'
$pluginDir = Join-Path $Work 'kp-write\Plugins\DeskWrite'
$base = Join-Path $Work 'base'
New-Item -ItemType Directory -Force (Join-Path $Work 'logs'), (Join-Path $Work 'runs\m3') | Out-Null
if (-not (Test-Path (Join-Path $base 'M3.kdbx'))) { & $probe m3base (Join-Path $base 'M3.kdbx') | Out-Null }

function Probe([string[]] $a) {
    $out = & $probe @a
    if ($LASTEXITCODE -ne 0) { throw "probe $($a -join ' ') failed" }
    $out
}

# Hands the plugin one command and waits for its answer.
function Plugin([string] $Command) {
    $done = Join-Path $pluginDir 'DeskWrite.done'
    Remove-Item $done -ErrorAction SilentlyContinue
    [IO.File]::WriteAllText((Join-Path $pluginDir 'DeskWrite.cmd'), $Command)
    $deadline = (Get-Date).AddSeconds(20)
    while (-not (Test-Path $done) -and (Get-Date) -lt $deadline) { Start-Sleep -Milliseconds 200 }
    if (-not (Test-Path $done)) { throw "the plugin did not answer: $Command" }
    $r = Get-Content $done -Raw
    Say "plugin: $r"
    $r
}

function Entry-Lines([string] $Dump, [string] $Alias) {
    Get-Content $Dump | Where-Object { $_ -like "E $Alias *" -and $_ -notmatch ' t\.accessed | usage ' }
}

function Run-M3([hashtable] $c, [int] $Attempt) {
    $id = "$($c['Id'])-$Attempt"
    $dir = Join-Path $Work "runs\m3\$id"
    if (Test-Path $dir) { Remove-Item -LiteralPath $dir -Recurse -Force }
    New-Item -ItemType Directory -Force $dir | Out-Null
    $script:LogFile = Join-Path $dir 'run.log'
    $script:ShotDir = $dir
    $db = Join-Path $dir 'db.kdbx'
    Copy-Item (Join-Path $base 'M3.kdbx') $db
    Probe @('dump', $db, (Join-Path $dir 'base.dump.txt')) | Out-Null
    Say "==== $id : $($c['What'])"
    Write-KpConfig @{}
    $kp = Start-Kp $db $id
    $title = $c['Entry']
    $alias = @{ 'Mail account' = 'U1'; 'Shop' = 'U3'; 'Bank' = 'U2' }[$title]
    try {
        $dlg = Kp-OpenEntry $kp 'Passwords' $title
        Start-Sleep -Milliseconds 500
        Save-Shot $dlg "$id-dialog"
        if ($c['Live']) {
            Plugin $c['Live'] | Out-Null
            Copy-Item $db (Join-Path $dir 'after-plugin.kdbx')
            Probe @('dump', (Join-Path $dir 'after-plugin.kdbx'), (Join-Path $dir 'after-plugin.dump.txt')) | Out-Null
            Remove-Item (Join-Path $dir 'after-plugin.kdbx')
            Say "the file after the plugin's write, KPG.Ref: $((Entry-Lines (Join-Path $dir 'after-plugin.dump.txt') $alias | Where-Object { $_ -like "E $alias S KPG.Ref*" }) -join ' | ')"
            Say "window title while the dialog is open: <<$($kp.Main.Current.Name)>>"
        }
        $h = Get-Handle $dlg
        switch ($c['Close']) {
            'ok' { Kp-Ok $dlg }
            'edit' {
                $u = Need (Find-BoxBy $h 'User name:') 'user name box'
                Type-Hwnd $u.H 'edited-in-dialog'
                Kp-Ok $dlg
            }
            'cancel' { [void](Click-Kid $h 'Cancel'); [void](Wait-Gone $dlg) }
        }
        Start-Sleep -Milliseconds 800
        $known = Get-Handles $kp.Process.Id
        [void](Kp-Dialogs $kp $known "$id-close" @() 2)
        Plugin "dump|$title|$(Join-Path $dir 'memory.txt')" | Out-Null
        Get-Content (Join-Path $dir 'memory.txt') | ForEach-Object { Say "memory: $_" }
        Say "window title after the dialog closed: <<$($kp.Main.Current.Name)>>"
        Send-To $kp.Main '^s' 1200
        [void](Kp-Dialogs $kp $known "$id-save" @() 2)
        Say "window title after Ctrl+S: <<$($kp.Main.Current.Name)>>"
        [void](Kp-Exit $kp "$id-exit")
    }
    finally { Stop-Started }
    Probe @('dump', $db, (Join-Path $dir 'db.dump.txt')) | Out-Null
    $before = Entry-Lines (Join-Path $dir 'base.dump.txt') $alias
    $after = Entry-Lines (Join-Path $dir 'db.dump.txt') $alias
    $d = @(Compare-Object $before $after | ForEach-Object { $(if ($_.SideIndicator -eq '<=') { '- ' } else { '+ ' }) + $_.InputObject } | Sort-Object { $_.Substring(2) })
    [IO.File]::WriteAllLines((Join-Path $dir 'diff-entry.txt'), [string[]]$d)
    $d | ForEach-Object { Say "  $_" }
    $ref = ($after | Where-Object { $_ -like "E $alias S KPG.Ref*" }) -join ' | '
    $hist = ($after | Where-Object { $_ -like "E $alias history.count*" }) -join ''
    $mod = ($after | Where-Object { $_ -like "E $alias t.modified*" }) -join ''
    Add-Content (Join-Path $Work 'logs\m3-summary.txt') "$id | file: $(if ($ref) { $ref } else { 'no KPG.Ref' }) | $hist | $mod" -Encoding UTF8
}

$cases = @(
    @{ Id = 'a-ok'; What = 'KPG.Ref on U1 before; OK, nothing touched'; Entry = 'Mail account'; Close = 'ok' }
    @{ Id = 'a-edit'; What = 'KPG.Ref on U1 before; the user name edited, OK'; Entry = 'Mail account'; Close = 'edit' }
    @{ Id = 'a-cancel'; What = 'KPG.Ref on U1 before; Cancel'; Entry = 'Mail account'; Close = 'cancel' }
    @{ Id = 'b-ok'; What = 'KPG.Ref added to U3 while open, saved; OK, nothing touched'; Entry = 'Shop'; Close = 'ok'; Live = 'setsave|Shop|KPG.Ref|zqxref-u3-live' }
    @{ Id = 'b-edit'; What = 'KPG.Ref added to U3 while open, saved; user name edited, OK'; Entry = 'Shop'; Close = 'edit'; Live = 'setsave|Shop|KPG.Ref|zqxref-u3-live' }
    @{ Id = 'b-cancel'; What = 'KPG.Ref added to U3 while open, saved; Cancel'; Entry = 'Shop'; Close = 'cancel'; Live = 'setsave|Shop|KPG.Ref|zqxref-u3-live' }
    @{ Id = 'b-ok-unsaved'; What = 'KPG.Ref added to U3 while open, not saved; OK, nothing touched'; Entry = 'Shop'; Close = 'ok'; Live = 'set|Shop|KPG.Ref|zqxref-u3-live' }
    @{ Id = 'c-ok'; What = 'KPG.Ref on U1 changed while open, saved; OK, nothing touched'; Entry = 'Mail account'; Close = 'ok'; Live = 'setsave|Mail account|KPG.Ref|zqxref-u1-changed' }
    @{ Id = 'c-edit'; What = 'KPG.Ref on U1 changed while open, saved; user name edited, OK'; Entry = 'Mail account'; Close = 'edit'; Live = 'setsave|Mail account|KPG.Ref|zqxref-u1-changed' }
    @{ Id = 'c-cancel'; What = 'KPG.Ref on U1 changed while open, saved; Cancel'; Entry = 'Mail account'; Close = 'cancel'; Live = 'setsave|Mail account|KPG.Ref|zqxref-u1-changed' }
)

foreach ($c in $cases) {
    if ($Case -notcontains 'all' -and $Case -notcontains $c['Id']) { continue }
    for ($r = 1; $r -le $Repeat; $r++) {
        try { Run-M3 $c $r }
        catch {
            Say "RUN FAILED $($c['Id'])-${r}: $($_.Exception.Message) $($_.ScriptStackTrace)"
            Add-Content (Join-Path $Work 'logs\m3-summary.txt') "$($c['Id'])-$r | FAILED: $($_.Exception.Message)" -Encoding UTF8
            Stop-Started
            # A run whose flow failed is kept apart and repeated, never read.
            $failed = Join-Path $Work "runs\m3\$($c['Id'])-$r"
            if (Test-Path $failed) { Move-Item $failed (Join-Path $Work "runs\m3-not-counted\$($c['Id'])-$r-$((Get-Date).ToString('HHmmss'))") -Force }
        }
    }
}
