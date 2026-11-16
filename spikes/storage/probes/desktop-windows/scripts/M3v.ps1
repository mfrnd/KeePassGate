<#
.SYNOPSIS
    Measurement 3, the person's half: what KeePass 2.62's window shows and does with a plugin
    string KPG.Ref on a user's entry and with the store group, without any plugin.

.DESCRIPTION
    On a fresh copy of M3.kdbx in the KeePass copy `kp` each case reads or does one thing a person
    can do: the entry list and the entry view, quick search for each token, the Advanced tab
    (shown, a string edited, a string deleted), Duplicate Entry with and without its options, Move
    to Group, the clipboard copy into a second database window, a drag into that window, the print
    dialog, every export format, and auto-type into a target window (the selected entry, and the
    global hot key with the target's title naming a store entry and a user's entry). What a file
    holds afterwards is read back with the probe; what a window shows is read through UI
    automation, the accessibility interface and Win32 messages, and pictured.
#>
param(
    [string] $Work = 'W:\',
    [string[]] $Case = @('all'),
    # The export formats to run, by the start of their names; the two Windows Favorites formats
    # write into the user profile's Favorites folder, so they run only when named here.
    [string[]] $ExportOnly = @('KeePass', 'Customizable', 'Transform', 'Mozilla'),
    [string] $Unelevated = (Join-Path $env:TEMP 'unelevated.ps1')
)

$ErrorActionPreference = 'Stop'
$Case = @($Case | ForEach-Object { $_ -split ',' })
$ExportOnly = @($ExportOnly | ForEach-Object { $_ -split ',' })
. (Join-Path $PSScriptRoot 'Ui.ps1')
. (Join-Path $PSScriptRoot 'Kp.ps1')
$family = Split-Path -Parent $PSScriptRoot
$probe = Join-Path $family 'probe\out\Desk.exe'
$target = Join-Path $family 'target\out\DeskTarget.exe'
$script:WorkRoot = $Work
$script:UnelevatedScript = $Unelevated
$script:KpExe = Join-Path $Work 'kp\KeePass.exe'
$base = Join-Path $Work 'base'
New-Item -ItemType Directory -Force (Join-Path $Work 'logs'), (Join-Path $Work 'runs\m3v'), (Join-Path $Work 'runs\m3v-not-counted') | Out-Null
$tokens = @('zqxref-u1', 'zqxref-u2-protected', 'zqxextra-u1', 'zqxstore-grant', 'zqxstore-secret', 'zqxstore-adm', 'zqxcd-u1', 'zqxattach-u1')

function Probe([string[]] $a) {
    $out = & $probe @a
    if ($LASTEXITCODE -ne 0) { throw "probe $($a -join ' ') failed" }
    $out
}

function Summary([string] $Line) { Add-Content (Join-Path $Work 'logs\m3v-summary.txt') $Line -Encoding UTF8 }

function Entry-View($kp) {
    $rich = Get-Kids (Get-Handle $kp.Main) | Where-Object { $_.Class -like '*RICHEDIT50W*' } | Sort-Object T -Descending | Select-Object -First 1
    [DeskNative]::Text([IntPtr]$rich.H).Replace("`r", ' ').Replace("`n", ' / ')
}

# The tab page of an entry dialog, by a click on its tab.
function Kp-Tab($dlg, [string] $Name) {
    $h = Get-Handle $dlg
    $tab = Get-Kids $h | Where-Object { $_.Class -like '*SysTabControl32*' } | Select-Object -First 1
    $page = Get-MsaaItems $tab.H | Where-Object { $_.Name -eq $Name } | Select-Object -First 1
    $loc = [DeskMsaa]::Location([IntPtr]$tab.H, $page.Id).Split(',')
    Click-At ([int]$loc[0] + [int]$loc[2] / 2) ([int]$loc[1] + [int]$loc[3] / 2)
    Start-Sleep -Milliseconds 800
}

# The string fields list of the Advanced tab: the upper of the two list views.
function Kp-StringList($dlg) {
    Get-Kids (Get-Handle $dlg) | Where-Object { $_.Class -like '*SysListView32*' } | Sort-Object T | Select-Object -First 1
}

function Click-Row([int64] $List, $Row) {
    $loc = [DeskMsaa]::Location([IntPtr]$List, $Row.Id).Split(',')
    Click-At ([int]$loc[0] + 20) ([int]$loc[1] + [int]$loc[3] / 2)
    Start-Sleep -Milliseconds 400
}

function Kp-Save($kp, [string] $Label) {
    $known = Get-Handles $kp.Process.Id
    Send-To $kp.Main '^s' 1200
    [void](Kp-Dialogs $kp $known "$Label-save" @() 2)
}

function Dump-Titles([string] $Dump, [string] $Like) {
    $ids = Get-Content $Dump | Where-Object { $_ -match '^E (\S+) S Title p=\d "(.*)"$' -and $Matches[2] -like $Like } | ForEach-Object { ($_ -split ' ')[1] }
    foreach ($id in $ids) {
        $lines = Get-Content $Dump | Where-Object { $_ -like "E $id *" }
        $title = ($lines | Where-Object { $_ -like "E $id S Title*" }) -replace '^.*p=\d ', ''
        $ref = ($lines | Where-Object { $_ -like "E $id S KPG.Ref*" }) -replace '^.*p=', 'p='
        $hist = ($lines | Where-Object { $_ -like "E $id history.count*" }) -replace '^.* ', ''
        $parent = ($lines | Where-Object { $_ -like "E $id parent *" }) -replace '^.* parent ', ''
        $user = ($lines | Where-Object { $_ -like "E $id S UserName*" }) -replace '^.*p=\d ', ''
        "$id title=$title KPG.Ref=$(if ($ref) { $ref } else { 'none' }) user=$user history=$hist parent=$parent"
    }
}

function Start-Target([string] $Title, [string] $Out) {
    Remove-Item $Out -ErrorAction SilentlyContinue
    # The title from a file: one with spaces does not survive the command line handed on.
    $tf = $Out + '.title'
    [IO.File]::WriteAllText($tf, $Title)
    $p = Start-Unelevated -Exe (Copy-TargetExe) -Arguments @(('@' + $tf), $Out) -Tag ('target-' + ($Title -replace '\W', ''))
    $w = Wait-Window $p.Id $Title 15
    if (-not $w) { throw "no target window $Title" }
    [pscustomobject]@{ Process = $p; Window = $w }
}

function Copy-TargetExe {
    $dest = Join-Path $Work 'target\DeskTarget.exe'
    if (-not (Test-Path $dest)) { New-Item -ItemType Directory -Force (Split-Path $dest) | Out-Null; Copy-Item $target $dest }
    $dest
}

# An exported file KeePass still held open when the run read it, read again once KeePass is stopped:
# the HTML export's file stays open, with no sharing, for as long as KeePass runs.
$script:Deferred = @()
function Read-Deferred {
    foreach ($d in $script:Deferred) {
        $text = [IO.File]::ReadAllText($d.File)
        $hits = @($tokens + @('KPG.Ref') | Where-Object { $text.Contains($_) })
        Summary "export | $($d.Format) | $(Split-Path -Leaf $d.File) $((Get-Item $d.File).Length) bytes, read after KeePass stopped | contains: $($hits -join ', ')"
    }
    $script:Deferred = @()
}

function Run-Case([string] $Id, [scriptblock] $Body) {
    if ($Case -notcontains 'all' -and $Case -notcontains $Id) { return }
    $dir = Join-Path $Work "runs\m3v\$Id"
    if (Test-Path $dir) { Remove-Item -LiteralPath $dir -Recurse -Force }
    New-Item -ItemType Directory -Force $dir | Out-Null
    $script:LogFile = Join-Path $dir 'run.log'
    $script:ShotDir = $dir
    $db = Join-Path $dir 'db.kdbx'
    Copy-Item (Join-Path $base 'M3.kdbx') $db
    Say "==== $Id"
    Write-KpConfig @{}
    try {
        $kp = Start-Kp $db $Id
        & $Body $kp $dir $db
        if (-not $kp.Process.HasExited) { [void](Kp-Exit $kp "$Id-exit") }
        Stop-Started
        Probe @('dump', $db, (Join-Path $dir 'db.dump.txt')) | Out-Null
        Read-Deferred
    }
    catch {
        Say "RUN FAILED ${Id}: $($_.Exception.Message) $($_.ScriptStackTrace)"
        Summary "$Id | FAILED: $($_.Exception.Message)"
        Stop-Started
        Move-Item $dir (Join-Path $Work "runs\m3v-not-counted\$Id-$((Get-Date).ToString('HHmmss'))") -Force
    }
}

Run-Case 'view' {
    param($kp, $dir, $db)
    foreach ($e in @(@('Passwords', 'Mail account'), @('Passwords', 'Bank'), @('Passwords', 'Shop'), @('KeePassGate', 'KPG grant 1'), @('KeePassGate', 'KPG admission 1'))) {
        [void](Kp-SelectEntry $kp $e[0] $e[1])
        Start-Sleep -Milliseconds 500
        $v = Entry-View $kp
        Say "entry view of $($e[1]): <<$v>>"
        Summary "view | $($e[1]) | $v"
        Save-Shot $kp.Main ("view-" + ($e[1] -replace '\W', ''))
    }
    $hdr = Get-Kids (Get-Handle (Kp-List $kp)) -Hidden | Where-Object { $_.Class -like '*SysHeader32*' } | Select-Object -First 1
    Summary "view | entry list columns | $((Get-MsaaItems $hdr.H | ForEach-Object Name) -join ', ')"
}

Run-Case 'search' {
    param($kp, $dir, $db)
    $box = Get-Kids (Get-Handle $kp.Main) | Where-Object { $_.Class -eq 'Edit' } | Select-Object -First 1
    foreach ($t in ($tokens + @('KPG grant', 'Mail account', 'KPG.Ref'))) {
        Type-Hwnd $box.H $t
        [System.Windows.Forms.SendKeys]::SendWait('{ENTER}')
        Start-Sleep -Milliseconds 1200
        $found = @(Kp-Entries $kp | ForEach-Object { $_.Name }) -join ', '
        Say "quick search <<$t>>: [$found]"
        Summary "search | $t | [$found]"
        if ($t -in 'zqxref-u1', 'zqxstore-grant') { Save-Shot $kp.Main ("search-" + $t) }
    }
}

Run-Case 'advanced' {
    param($kp, $dir, $db)
    foreach ($title in 'Mail account', 'Bank') {
        $dlg = Kp-OpenEntry $kp 'Passwords' $title
        Kp-Tab $dlg 'Advanced'
        $list = Kp-StringList $dlg
        $rows = @(Get-MsaaItems $list.H | Where-Object { $_.Role -eq '34' })
        foreach ($r in $rows) { Say "Advanced, $title, string <<$($r.Name)>> <<$($r.Description)>>"; Summary "advanced | $title | $($r.Name) | $($r.Description)" }
        $ref = $rows | Where-Object { $_.Name -eq 'KPG.Ref' } | Select-Object -First 1
        Click-Row $list.H $ref
        $btns = Get-Kids (Get-Handle $dlg) | Where-Object { $_.Class -like '*BUTTON*' -and $_.Text -in '&Add', '&Edit', '&Delete', '&More' }
        Summary "advanced | $title | KPG.Ref selected, buttons: $(($btns | ForEach-Object { "$($_.Text.Replace('&',''))=$(if ($_.Enabled) { 'enabled' } else { 'disabled' })" }) -join ', ')"
        Save-Shot $dlg ("advanced-" + ($title -replace '\W', ''))
        [void](Click-Kid (Get-Handle $dlg) 'Cancel')
        [void](Wait-Gone $dlg)
    }
}

Run-Case 'advanced-edit' {
    param($kp, $dir, $db)
    $dlg = Kp-OpenEntry $kp 'Passwords' 'Mail account'
    Kp-Tab $dlg 'Advanced'
    $list = Kp-StringList $dlg
    Click-Row $list.H (Get-MsaaItems $list.H | Where-Object { $_.Name -eq 'KPG.Ref' } | Select-Object -First 1)
    $known = Get-Handles $kp.Process.Id
    $b = Get-Kids (Get-Handle $dlg) | Where-Object { $_.Text -eq '&Edit' } | Select-Object -First 1
    Click-At ([int](($b.L + $b.R) / 2)) ([int](($b.T + $b.B) / 2))
    $s = Need (Wait-Window $kp.Process.Id 'Edit Entry String*' 8 $known) 'Edit Entry String'
    Start-Sleep -Milliseconds 500
    [void](Read-Window $s 'edit-string')
    Save-Shot $s 'edit-string'
    $v = Get-Kids (Get-Handle $s) | Where-Object { $_.Class -like '*RICHEDIT*' } | Select-Object -First 1
    Type-Hwnd $v.H 'zqxref-u1-edited-by-hand'
    [void](Click-Kid (Get-Handle $s) 'OK')
    [void](Wait-Gone $s)
    Kp-Ok $dlg
    Kp-Save $kp 'advanced-edit'
    Copy-Item $db (Join-Path $dir 'after.kdbx')
    Probe @('dump', (Join-Path $dir 'after.kdbx'), (Join-Path $dir 'after.dump.txt')) | Out-Null
    Remove-Item (Join-Path $dir 'after.kdbx')
    Summary "advanced-edit | $((Dump-Titles (Join-Path $dir 'after.dump.txt') 'Mail account*') -join ' ; ')"
}

Run-Case 'advanced-delete' {
    param($kp, $dir, $db)
    $dlg = Kp-OpenEntry $kp 'Passwords' 'Mail account'
    Kp-Tab $dlg 'Advanced'
    $list = Kp-StringList $dlg
    Click-Row $list.H (Get-MsaaItems $list.H | Where-Object { $_.Name -eq 'KPG.Ref' } | Select-Object -First 1)
    $known = Get-Handles $kp.Process.Id
    $b = Get-Kids (Get-Handle $dlg) | Where-Object { $_.Text -eq '&Delete' } | Select-Object -First 1
    Click-At ([int](($b.L + $b.R) / 2)) ([int](($b.T + $b.B) / 2))
    Start-Sleep -Milliseconds 800
    [void](Kp-Dialogs $kp $known 'delete-string' @('*', 'Yes') 2)
    Say "strings left: $((Get-MsaaItems $list.H | Where-Object { $_.Role -eq '34' } | ForEach-Object Name) -join ', ')"
    Kp-Ok $dlg
    Kp-Save $kp 'advanced-delete'
    Copy-Item $db (Join-Path $dir 'after.kdbx')
    Probe @('dump', (Join-Path $dir 'after.kdbx'), (Join-Path $dir 'after.dump.txt')) | Out-Null
    Remove-Item (Join-Path $dir 'after.kdbx')
    Summary "advanced-delete | $((Dump-Titles (Join-Path $dir 'after.dump.txt') 'Mail account*') -join ' ; ')"
}

foreach ($variant in 'default', 'all') {
    Run-Case "duplicate-$variant" {
        param($kp, $dir, $db)
        [void](Kp-SelectEntry $kp 'Passwords' 'Mail account')
        $known = Get-Handles $kp.Process.Id
        Kp-Menu $kp @('Entry', 'Duplicate Entr')
        $d = Need (Wait-Window $kp.Process.Id 'Duplication Options*' 8 $known) 'Duplication Options'
        Start-Sleep -Milliseconds 400
        $checks = Get-Checks (Get-Handle $d)
        Summary "duplicate-$variant | offered: $(($checks | ForEach-Object { "$($_.Text)=$($_.Checked)" }) -join ', ')"
        Save-Shot $d "duplicate-$variant"
        if ($variant -eq 'all') {
            foreach ($cb in ($checks | Where-Object { $_.Checked -ne 1 })) { Focus-Hwnd $cb.H }
            Summary "duplicate-$variant | pressed: $((Get-Checks (Get-Handle $d) | ForEach-Object { "$($_.Text)=$($_.Checked)" }) -join ', ')"
        }
        [void](Click-Kid (Get-Handle $d) 'OK')
        [void](Wait-Gone $d)
        Kp-Save $kp "duplicate-$variant"
        Copy-Item $db (Join-Path $dir 'after.kdbx')
        Probe @('dump', (Join-Path $dir 'after.kdbx'), (Join-Path $dir 'after.dump.txt')) | Out-Null
        Remove-Item (Join-Path $dir 'after.kdbx')
        Summary "duplicate-$variant | $((Dump-Titles (Join-Path $dir 'after.dump.txt') 'Mail account*') -join ' ; ')"
    }
}

Run-Case 'move-to-group' {
    param($kp, $dir, $db)
    # The entry dragged onto another group of the same window, which is how KeePass moves it.
    [void](Kp-SelectEntry $kp 'Passwords' 'Mail account')
    $list = Get-Handle (Kp-List $kp)
    $row = Kp-Entries $kp | Where-Object { $_.Name -eq 'Mail account' } | Select-Object -First 1
    $s = [DeskMsaa]::Location([IntPtr]$list, $row.Id).Split(',')
    $t = $script:GroupPlaces['KeePassGate'].Split(',')
    $sx = [int]$s[0] + 30; $sy = [int]$s[1] + [int]$s[3] / 2; $tx = [int]$t[0] + 40; $ty = [int]$t[1] + [int]$t[3] / 2
    [void][DeskNative]::SetCursorPos($sx, $sy); Start-Sleep -Milliseconds 200
    [DeskNative]::mouse_event(0x2, 0, 0, 0, [UIntPtr]::Zero)
    for ($i = 1; $i -le 30; $i++) { [void][DeskNative]::SetCursorPos([int]($sx + ($tx - $sx) * $i / 30), [int]($sy + ($ty - $sy) * $i / 30)); Start-Sleep -Milliseconds 40 }
    Start-Sleep -Milliseconds 600
    [DeskNative]::mouse_event(0x4, 0, 0, 0, [UIntPtr]::Zero)
    Say "dragged Mail account from $sx,$sy onto KeePassGate at $tx,$ty"
    Start-Sleep -Milliseconds 800
    [void](Kp-Group $kp 'KeePassGate')
    Say "KeePassGate now lists: $((Kp-Entries $kp | ForEach-Object Name) -join ', ')"
    Kp-Save $kp 'move'
    Copy-Item $db (Join-Path $dir 'after.kdbx')
    Probe @('dump', (Join-Path $dir 'after.kdbx'), (Join-Path $dir 'after.dump.txt')) | Out-Null
    Remove-Item (Join-Path $dir 'after.kdbx')
    Summary "move-to-group | $((Dump-Titles (Join-Path $dir 'after.dump.txt') 'Mail account*') -join ' ; ')"
}

# Copying through the clipboard and dragging, into a second KeePass window holding M3-other.
foreach ($how in 'clipboard', 'drag') {
    Run-Case "other-window-$how" {
        param($kp, $dir, $db)
        $other = Join-Path $dir 'other.kdbx'
        Copy-Item (Join-Path $base 'M3-other.kdbx') $other
        $kp2 = Start-Kp $other "$how-other"
        # The first window on the left half of the screen, the second on the right.
        [void][DeskNative]::ShowWindow([IntPtr](Get-Handle $kp.Main), 9)
        Add-Type -AssemblyName System.Windows.Forms
        $sig = '[DllImport("user32.dll")] public static extern bool MoveWindow(IntPtr h, int x, int y, int w, int ht, bool r);'
        if (-not ('DeskMove' -as [type])) { Add-Type -MemberDefinition $sig -Name DeskMove -Namespace W32 }
        [void][W32.DeskMove]::MoveWindow([IntPtr](Get-Handle $kp.Main), 0, 0, 640, 760, $true)
        [void][W32.DeskMove]::MoveWindow([IntPtr](Get-Handle $kp2.Main), 640, 0, 640, 760, $true)
        Start-Sleep -Seconds 1
        $script:GroupPlaces = @{}
        [void](Kp-SelectEntry $kp 'Passwords' 'Mail account')
        if ($how -eq 'clipboard') {
            Kp-Menu $kp @('Entry', 'Data Exchange', 'Copy Entr')
            Start-Sleep -Milliseconds 800
            $saved = $script:GroupPlaces
            $script:GroupPlaces = @{}
            $th = Get-Handle (Kp-Tree $kp2)
            foreach ($it in @(Kp-Groups $kp2)) { $script:GroupPlaces[$it.Name] = [DeskMsaa]::Location([IntPtr]$th, $it.Id) }
            [void](Kp-Group $kp2 'Imported')
            Kp-Menu $kp2 @('Entry', 'Data Exchange', 'Paste Entr')
        } else {
            $listLoc = [DeskMsaa]::Location([IntPtr](Get-Handle (Kp-List $kp)), (Kp-Entries $kp | Where-Object { $_.Name -eq 'Mail account' } | Select-Object -First 1).Id).Split(',')
            $th = Get-Handle (Kp-Tree $kp2)
            $dst = (Get-MsaaItems $th | Where-Object { $_.Name -eq 'Imported' } | Select-Object -First 1)
            $dl = [DeskMsaa]::Location([IntPtr]$th, $dst.Id).Split(',')
            $sx = [int]$listLoc[0] + 30; $sy = [int]$listLoc[1] + [int]$listLoc[3] / 2
            $tx = [int]$dl[0] + 30; $ty = [int]$dl[1] + [int]$dl[3] / 2
            [void][DeskNative]::SetCursorPos($sx, $sy); Start-Sleep -Milliseconds 200
            [DeskNative]::mouse_event(0x2, 0, 0, 0, [UIntPtr]::Zero)
            for ($i = 1; $i -le 30; $i++) { [void][DeskNative]::SetCursorPos([int]($sx + ($tx - $sx) * $i / 30), [int]($sy + ($ty - $sy) * $i / 30)); Start-Sleep -Milliseconds 40 }
            Start-Sleep -Milliseconds 600
            [DeskNative]::mouse_event(0x4, 0, 0, 0, [UIntPtr]::Zero)
            Say "dragged from $sx,$sy to $tx,$ty"
            Start-Sleep -Seconds 1
        }
        $known2 = Get-Handles $kp2.Process.Id
        [void](Kp-Dialogs $kp2 $known2 "$how-other" @() 2)
        Save-Shot $kp2.Main "$how-other"
        Say "second window title: <<$($kp2.Main.Current.Name)>>; entries shown: $((Kp-Entries $kp2 | ForEach-Object Name) -join ', ')"
        Send-To $kp2.Main '^s' 1200
        [void](Kp-Exit $kp2 "$how-other-exit")
        Probe @('dump', $other, (Join-Path $dir 'other.dump.txt')) | Out-Null
        Summary "other-window-$how | $(@(Dump-Titles (Join-Path $dir 'other.dump.txt') '*') -join ' ; ')"
    }
}

Run-Case 'print' {
    param($kp, $dir, $db)
    [void](Kp-Group $kp 'Passwords')
    $known = Get-Handles $kp.Process.Id
    Kp-Menu $kp @('File', 'Print', 'Print.')
    $d = Need (Wait-Window $kp.Process.Id '*Print*' 20 $known) 'print dialog'
    Start-Sleep -Seconds 2
    [void](Read-Window $d 'print')
    Save-Shot $d 'print-preview'
    Kp-Tab $d 'Layout'
    Summary "print | layout: $((Get-Checks (Get-Handle $d) | ForEach-Object { "$($_.Text)=$($_.Checked)$(if (-not $_.Enabled) { '(disabled)' })" }) -join ', ')"
    Save-Shot $d 'print-layout'
    [void](Click-Kid (Get-Handle $d) 'Cancel')
}

Run-Case 'export' {
    param($kp, $dir, $db)
    $known = Get-Handles $kp.Process.Id
    Kp-Menu $kp @('File', 'Export')
    $d = Need (Wait-Window $kp.Process.Id 'Export File/Data*' 10 $known) 'export dialog'
    Start-Sleep -Milliseconds 800
    $list = Get-Kids (Get-Handle $d) | Where-Object { $_.Class -like '*SysListView32*' } | Select-Object -First 1
    $formats = @(Get-MsaaItems $list.H | Where-Object { $_.Role -eq '34' } | ForEach-Object Name)
    Summary "export | formats offered: $($formats -join ', ')"
    Save-Shot $d 'export-dialog'
    [void](Click-Kid (Get-Handle $d) 'Cancel')
    [void](Wait-Gone $d)
    $n = 0
    foreach ($fmt in $formats) {
        $n++
        if (-not ($ExportOnly | Where-Object { $fmt.StartsWith($_) })) { Summary "export | $fmt | not run here"; continue }
        $out = Join-Path $dir ("export-$n" + $(switch -Wildcard ($fmt) { '*CSV*' { '.csv' } '*XML*' { '.xml' } '*KDBX*' { '.kdbx' } '*KDB *' { '.kdb' } '*HTML*' { '.html' } default { '.out' } }))
        try {
            $known = Get-Handles $kp.Process.Id
            Kp-Menu $kp @('File', 'Export')
            $d = Need (Wait-Window $kp.Process.Id 'Export File/Data*' 10 $known) 'export dialog'
            Start-Sleep -Milliseconds 600
            $list = Get-Kids (Get-Handle $d) | Where-Object { $_.Class -like '*SysListView32*' } | Select-Object -First 1
            Click-Row $list.H (Get-MsaaItems $list.H | Where-Object { $_.Name -eq $fmt } | Select-Object -First 1)
            $box = Get-Kids (Get-Handle $d) | Where-Object { $_.Class -like '*EDIT*' -and $_.Enabled } | Select-Object -First 1
            if ($box) { Type-Hwnd $box.H $out }
            $known = Get-Handles $kp.Process.Id
            [void](Click-Kid (Get-Handle $d) 'OK')
            # What follows an export: the master key asked again, a format's own options, an XSL
            # file to pick; each read, pictured and answered.
            for ($k = 0; $k -lt 4; $k++) {
                $w = Wait-NewWindow $kp.Process.Id $known 4
                if (-not $w) { break }
                Start-Sleep -Milliseconds 800
                $r = Read-Window $w "export-$n-$k"
                Save-Shot $w "export-$n-$k"
                $known += (Get-Handle $w)
                $wh = Get-Handle $w
                if (Find-BoxBy $wh 'Master password:') { Type-Hwnd (Find-BoxBy $wh 'Master password:').H $script:KpPassword; if (Find-Kid $wh 'Confirm' 'BUTTON' -Prefix -Seconds 0.3) { [void](Click-Kid $wh 'Confirm' -Prefix) } else { [void](Click-Kid $wh 'OK') } }
                elseif ($r.Title -like '*XSL*' -or $r.Title -like 'Open*') {
                    [System.Windows.Forms.SendKeys]::SendWait('%n'); Start-Sleep -Milliseconds 300
                    $xsl = Join-Path (Split-Path -Parent $script:KpExe) 'XSL\KDBX_DetailsFull_HTML.xsl'
                    [System.Windows.Forms.SendKeys]::SendWait(($xsl.ToCharArray() | ForEach-Object { if ('+^%~(){}[]'.Contains([string]$_)) { '{' + $_ + '}' } else { [string]$_ } }) -join '')
                    [System.Windows.Forms.SendKeys]::SendWait('{ENTER}')
                }
                elseif ($r.Title -like 'Save*' -or $r.Title -like '*Export*File*') {
                    [System.Windows.Forms.SendKeys]::SendWait('%n'); Start-Sleep -Milliseconds 300
                    [System.Windows.Forms.SendKeys]::SendWait(($out.ToCharArray() | ForEach-Object { if ('+^%~(){}[]'.Contains([string]$_)) { '{' + $_ + '}' } else { [string]$_ } }) -join '')
                    [System.Windows.Forms.SendKeys]::SendWait('{ENTER}')
                }
                elseif (Find-Kid $wh 'Export' 'BUTTON' -Seconds 0.3) { [void](Click-Kid $wh 'Export') }
                elseif (Find-Kid $wh 'OK' 'BUTTON' -Seconds 0.3) { [void](Click-Kid $wh 'OK') }
                elseif (Find-Kid $wh 'Yes' 'BUTTON' -Seconds 0.3) { [void](Click-Kid $wh 'Yes') }
                Start-Sleep -Seconds 2
            }
            Start-Sleep -Seconds 4
            $files = @(Get-ChildItem $dir -Filter "export-$n*" | Where-Object { $_.Extension -ne '.png' })
            foreach ($f in $files) {
                try { $probeOpen = [IO.File]::Open($f.FullName, 'Open', 'Read', 'ReadWrite, Delete'); $probeOpen.Dispose() }
                catch { $script:Deferred += [pscustomobject]@{ Format = $fmt; File = $f.FullName }; Summary "export | $fmt | $($f.Name) held open by KeePass, read after it stops"; continue }
                $text = if ($f.Extension -eq '.kdbx') {
                    (Probe @('dump', $f.FullName)) -join "`n"
                } else {
                    # Read with every sharing allowed: the HTML export's own preview still holds its file.
                    $fs = [IO.File]::Open($f.FullName, 'Open', 'Read', 'ReadWrite, Delete')
                    try { (New-Object IO.StreamReader $fs).ReadToEnd() } finally { $fs.Dispose() }
                }
                $hits = @($tokens + @('KPG.Ref') | Where-Object { $text.Contains($_) })
                Summary "export | $fmt | $($f.Name) $($f.Length) bytes | contains: $($hits -join ', ')"
            }
            if ($files.Count -eq 0) { Summary "export | $fmt | no file written" }
        } catch {
            Summary "export | $fmt | FAILED: $($_.Exception.Message)"
            foreach ($w in (Get-ProcWindows $kp.Process.Id)) { if ((Get-Handle $w) -ne (Get-Handle $kp.Main)) { try { [System.Windows.Forms.SendKeys]::SendWait('{ESC}') } catch { } } }
            Start-Sleep -Seconds 1
        }
    }
}

Run-Case 'autotype' {
    param($kp, $dir, $db)
    # The selected entry's auto-type into the window that was in front before KeePass.
    $t = Start-Target 'Desk target' (Join-Path $dir 'typed-selected.txt')
    Show-Front $t.Window; Start-Sleep -Milliseconds 600
    [void](Kp-SelectEntry $kp 'Passwords' 'Mail account')
    Send-To $null '^v' 4000
    Summary "autotype | selected Mail account into Desk target | typed: <<$((Get-Content (Join-Path $dir 'typed-selected.txt') -Raw -ErrorAction SilentlyContinue) -replace "`r", '\r' -replace "`n", '\n' -replace "`t", '\t')>>"
    Stop-Process -Id $t.Process.Id -Force
    # Global auto-type, Ctrl+Alt+A, with the target's title naming a store entry, then a user's.
    foreach ($title in 'KPG grant 1', 'Mail account') {
        $out = Join-Path $dir ("typed-global-" + ($title -replace '\W', '') + '.txt')
        $t = Start-Target $title $out
        Show-Front $t.Window; Start-Sleep -Milliseconds 600
        $known = Get-Handles $kp.Process.Id
        [System.Windows.Forms.SendKeys]::SendWait('^%a')
        Start-Sleep -Seconds 4
        $seen = @(Kp-Dialogs $kp $known ("global-" + ($title -replace '\W', '')) @() 2)
        foreach ($s in $seen) { Summary "autotype | global, target <<$title>> | KeePass showed <<$($s.Title)>> $(($s.Texts | Select-Object -First 3) -join ' / ')" }
        Summary "autotype | global, target <<$title>> | typed: <<$((Get-Content $out -Raw -ErrorAction SilentlyContinue) -replace "`r", '\r' -replace "`n", '\n' -replace "`t", '\t')>>"
        foreach ($w in (Get-ProcWindows $kp.Process.Id)) { if ((Get-Handle $w) -ne (Get-Handle $kp.Main)) { Show-Front $w; [System.Windows.Forms.SendKeys]::SendWait('{ESC}') } }
        Stop-Process -Id $t.Process.Id -Force
    }
}
