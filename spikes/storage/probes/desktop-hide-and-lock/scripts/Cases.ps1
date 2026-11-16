# The cases of the desktop-hide-and-lock family, dot-sourced by Hl.ps1 after its helpers. Each
# Run-Case names the case, the plugin's switches, where the store group is placed (top, bin,
# deep), KeePass options, and what is done in the window. Every step is marked in the plugin's log
# (DRIVER lines), so that what the plugin saw is read against what was done.

# A step that fails is logged as failed and the case goes on; a case with a failed step that its
# table needs is repeated, not read.
function Step($kp, [string] $Label, [scriptblock] $Do) {
    Mark $Label
    try { & $Do } catch { Say "STEP FAILED [$Label]: $($_.Exception.Message) at $($_.InvocationInfo.ScriptLineNumber)" }
}

$script:Answers = @('*Unhandled exception*', 'Continue', '*{REF:*', 'Yes', '*recycle bin*', 'Yes', '*permanently delete*', 'Delete', '*Duplication Options*', 'OK', '*Are you sure*', 'Yes')

function Dialogs($kp, [int64[]] $Known, [string] $Label, [double] $Seconds = 4) {
    $seen = Kp-Dialogs $kp $Known $Label $script:Answers $Seconds
    Say "[$Label] windows: $(@($seen | ForEach-Object { $_.Title }) -join ' | ')"
}

# Every window that appears after a step is read and pictured; KeePass's question "Do you want to
# save the changes before closing?" is recorded and answered No. Answers the dialog left standing,
# if one is: a window of KeePass's that is new, visible and not a message box.
function Capture($kp, [int64[]] $Known, [string] $Label) {
    $seen = Kp-Dialogs $kp $Known $Label @('*save the changes*', 'No') 3
    Say "[$Label] windows: $(@($seen | ForEach-Object { $_.Title }) -join ' | ')"
    $main = Get-Handle $kp.Main
    foreach ($w in (Get-ProcWindows $kp.Process.Id)) {
        $h = Get-Handle $w
        if ($h -eq 0 -or $h -eq $main -or ($Known -contains $h)) { continue }
        if (-not [DeskNative]::IsWindowVisible([IntPtr]$h)) { continue }
        try { if ($w.Current.ClassName -in '#32770', 'SysShadow', 'tooltips_class32') { continue } } catch { continue }
        return $w
    }
    Say "[$Label] no dialog stands"
    $null
}

function Open-Entry($kp, [string] $Group, [string] $Title, [string] $Label) {
    $item = Kp-SelectEntry $kp $Group $Title
    $known = Get-Handles $kp.Process.Id
    $loc = [DeskMsaa]::Location([IntPtr](Get-Handle (Kp-List $kp)), $item.Id).Split(',')
    Click-At ([int]$loc[0] + 30) ([int]$loc[1] + [int]$loc[3] / 2) -Double
    Start-Sleep -Milliseconds 1200
    Capture $kp $known $Label
}

function Keys-Dialog($kp, [string] $Keys, [string] $Label) {
    $known = Get-Handles $kp.Process.Id
    [System.Windows.Forms.SendKeys]::SendWait($Keys)
    Start-Sleep -Milliseconds 1200
    Capture $kp $known $Label
}

function Ok-State($w, [string] $Label) {
    if (-not $w) { return }
    $ok = Find-Kid (Get-Handle $w) 'OK' 'BUTTON' -Seconds 0.5
    Say "[$Label] dialog visible=$([DeskNative]::IsWindowVisible([IntPtr](Get-Handle $w))) OK enabled=$(if ($ok) { $ok.Enabled } else { 'no OK' })"
}

function Close-Dialog($w, $kp) {
    if (-not $w) { return }
    foreach ($try in 1..2) {
        if (-not [DeskNative]::IsWindowVisible([IntPtr](Get-Handle $w))) { return }
        $known = if ($kp) { Get-Handles $kp.Process.Id } else { @() }
        Show-Front $w
        if ($try -eq 1) { [System.Windows.Forms.SendKeys]::SendWait('{ESC}') } else { try { [void](Click-KidAt (Get-Handle $w) 'Cancel') } catch { } }
        Start-Sleep -Milliseconds 800
        if ($kp) { [void](Kp-Dialogs $kp $known 'close-question' @('*save the changes*', 'No') 2) }
    }
}
function Type-Title($w, [string] $Text, [string] $Label) {
    $t = Find-BoxBy (Get-Handle $w) 'Title:'
    if (-not $t) { Say "[$Label] no title box"; return }
    $before = [DeskNative]::Text([IntPtr]$t.H)
    Type-Hwnd $t.H $Text
    Say "[$Label] title box before <<$before>> after typing <<$([DeskNative]::Text([IntPtr]$t.H))>>"
}

# The views a person has of the store, in order; used by every hiding case.
function Views($kp, [string] $P) {
    Step $kp "$P open" { Look $kp "$P-01-open" }
    Step $kp "$P Passwords" { [void](Kp-Group $kp 'Passwords'); Look $kp "$P-02-passwords" -NoShot }
    Step $kp "$P quick search zqxstore" { Quick-Search $kp 'zqxstore'; Look $kp "$P-03-quick-zqxstore" }
    Step $kp "$P quick search KPG" { Quick-Search $kp 'KPG'; Look $kp "$P-04-quick-KPG" -NoShot }
    Step $kp "$P Find All" { Kp-Menu $kp @('Find', 'All'); Start-Sleep -Milliseconds 800; Look $kp "$P-05-find-all" }
    Step $kp "$P Find Expired" { Kp-Menu $kp @('Find', 'Expired'); Start-Sleep -Milliseconds 800; Look $kp "$P-06-expired" -NoShot }
    Step $kp "$P Find Tag kpgtag" { Kp-Menu $kp @('Find', 'Tag', 'kpgtag'); Start-Sleep -Milliseconds 800; Look $kp "$P-07-tag" }
    Step $kp "$P Last Modified Entries" {
        $known = Get-Handles $kp.Process.Id
        Kp-Menu $kp @('Find', 'Last Modified Entries')
        Start-Sleep -Milliseconds 1500
        $w = Wait-NewWindow $kp.Process.Id $known 4
        if ($w) {
            [void](Read-Window $w "$P-08-lastmod"); Save-Shot $w "$P-08-lastmod"
            $lv = Get-Kids (Get-Handle $w) | Where-Object { $_.Class -like '*SysListView32*' } | Select-Object -First 1
            if ($lv) { Say "[$P-08-lastmod] rows: $(@(Get-MsaaItems $lv.H | Where-Object { $_.Role -eq '34' } | ForEach-Object Name) -join ', ')" }
            Close-Dialog $w $kp
        }
    }
    Step $kp "$P Find dialog, ignore group settings" {
        $known = Get-Handles $kp.Process.Id
        Press-Keys $kp '^f' 1200
        $w = Need (Wait-NewWindow $kp.Process.Id $known 5) 'find dialog'
        $h = Get-Handle $w
        [System.Windows.Forms.SendKeys]::SendWait('zqxstore')
        Start-Sleep -Milliseconds 300
        $checks = Get-Checks $h
        $checks | ForEach-Object { Say "[$P-09-find] check <<$($_.Text)>> checked=$($_.Checked)" }
        $ig = $checks | Where-Object { $_.Text -like '*group*' -and $_.Text -like '*ignore*' -or $_.Text -like 'Ignore*' } | Select-Object -First 1
        if ($ig -and $ig.Checked -eq 0) { Focus-Hwnd $ig.H; Start-Sleep -Milliseconds 300 }
        Say "[$P-09-find] after: $((Get-Checks $h | Where-Object { $_.Checked -eq 1 } | ForEach-Object Text) -join ', ')"
        Save-Shot $w "$P-09-find-dialog"
        [void](Click-KidAt $h 'Find'); $known += $h; Dialogs $kp $known "$P-09-find-after" 3
        Start-Sleep -Milliseconds 1500
        Look $kp "$P-09-find-ignore"
    }
    Step $kp "$P Show Entries of Subgroups, root" {
        Kp-Menu $kp @('View', 'Show Entries of Subgroups')
        [void](Kp-Group $kp 'Desk store')
        Look $kp "$P-10-root-subgroups"
        Kp-Menu $kp @('View', 'Show Entries of Subgroups')
    }
    Step $kp "$P field references in Linked" {
        [void](Kp-SelectEntry $kp 'Passwords' 'Linked')
        Say "[$P-11-ref] entry view <<$(View-Text $kp)>>"
        $k = Get-Handles $kp.Process.Id; Press-Keys $kp '^c' 600; Dialogs $kp $k "$P-11-ref-c" 3
        $c = try { [System.Windows.Forms.Clipboard]::GetText() } catch { "clipboard unreadable: $($_.Exception.Message)" }
        Say "[$P-11-ref] Ctrl+C on Linked copied <<$c>>"
        $k = Get-Handles $kp.Process.Id; Press-Keys $kp '^b' 600; Dialogs $kp $k "$P-11-ref-b" 3
        $c = try { [System.Windows.Forms.Clipboard]::GetText() } catch { 'unreadable' }
        Say "[$P-11-ref] Ctrl+B on Linked copied <<$c>>"
        Save-Shot $kp.Main "$P-11-ref"
    }
}

# What the window rebuilds, step by step, under a hiding switch: selection, save, lock and unlock,
# sort, a drag, a synchronize bringing a new store entry, the tree's keyboard.
function Rebuilds($kp, [string] $P, [string] $Dir) {
    Step $kp "$P select root" { [void](Kp-Group $kp 'Desk store'); Look $kp "$P-21-root" -NoShot }
    Step $kp "$P select Passwords" { [void](Kp-Group $kp 'Passwords'); Look $kp "$P-22-passwords" -NoShot }
    Step $kp "$P sort by title" { Sort-List $kp; Look $kp "$P-23-sorted" -NoShot }
    Step $kp "$P tree keyboard Home then Down x8" {
        [void](Kp-Group $kp 'Desk store')
        foreach ($i in 1..8) { [System.Windows.Forms.SendKeys]::SendWait('{DOWN}'); Start-Sleep -Milliseconds 400; Mark "$P tree Down $i" }
        Look $kp "$P-24-keyboard"
    }
    Step $kp "$P drag Bank onto root" {
        [void](Kp-Group $kp 'Passwords')
        Kp-DragEntry $kp 'Bank' 'Desk store'
        Look $kp "$P-25-drag" -NoShot
    }
    Step $kp "$P save" { Press-Keys $kp '^s' 2000; Look $kp "$P-26-saved" -NoShot }
    Step $kp "$P lock and unlock" {
        Press-Keys $kp '^l' 1500
        Kp-Unlock $kp "$P-27-unlock"
        Look $kp "$P-27-unlocked"
    }
    Step $kp "$P synchronize with other.kdbx" {
        $known = Get-Handles $kp.Process.Id
        Kp-Menu $kp @('File', 'Synchronize', 'Synchronize with File')
        [void](Kp-FileDialog $kp $known 'Synchronize*' (Join-Path $Dir 'other.kdbx') "$P-28-sync")
        Start-Sleep -Seconds 3
        Dialogs $kp $known "$P-28-sync-dialogs" 3
        Look $kp "$P-28-synced"
    }
}

# ------------------------------------------------------------------ A: hiding

Run-Case 'a-base' @{ watch = 'on' } 'top' @{} { param($kp, $dir) Views $kp 'base'; Rebuilds $kp 'base' $dir }
Run-Case 'a-hide-ui' @{ hide = 'ui' } 'top' @{} { param($kp, $dir) Views $kp 'ui'; Rebuilds $kp 'ui' $dir }
Run-Case 'a-hide-idle' @{ hide = 'idle' } 'top' @{} { param($kp, $dir) Views $kp 'idle'; Rebuilds $kp 'idle' $dir }

# The entry list's grouping on, the entries of subgroups shown, under the hiding switch.
Run-Case 'a-hide-grouped' @{ hide = 'ui' } 'top' @{ 'MainWindow/ListGrouping' = '1'; 'MainWindow/ShowEntriesOfSubGroups' = 'true' } {
    param($kp, $dir)
    Step $kp 'grouped open' { Look $kp 'grouped-01-open' }
    Step $kp 'grouped root' { [void](Kp-Group $kp 'Desk store'); Look $kp 'grouped-02-root' }
    Step $kp 'grouped Find All' { Kp-Menu $kp @('Find', 'All'); Start-Sleep -Milliseconds 800; Look $kp 'grouped-03-all' }
}

# The store under the recycle bin, and deep under another group: what the window shows, and what
# Empty Recycle Bin does to it.
Run-Case 'a-bin' @{ watch = 'on' } 'bin' @{} {
    param($kp, $dir)
    Step $kp 'bin open' { Look $kp 'bin-01-open' }
    Step $kp 'bin quick search' { Quick-Search $kp 'zqxstore'; Look $kp 'bin-02-quick' -NoShot }
    Step $kp 'bin Find All' { Kp-Menu $kp @('Find', 'All'); Start-Sleep -Milliseconds 800; Look $kp 'bin-03-all' -NoShot }
    Step $kp 'bin Empty Recycle Bin' {
        [void](Kp-Group $kp 'Recycle Bin')
        $known = Get-Handles $kp.Process.Id
        Kp-Menu $kp @('Group', 'Empty Recycle Bin')
        Dialogs $kp $known 'bin-04-empty' 4
        Look $kp 'bin-05-emptied'
    }
} -Save
Run-Case 'a-deep' @{ watch = 'on' } 'deep' @{} {
    param($kp, $dir)
    Step $kp 'deep open' { Look $kp 'deep-01-open' }
    Step $kp 'deep Passwords' { [void](Kp-Group $kp 'Passwords'); Look $kp 'deep-02-passwords' }
}

# ------------------------------------------------------------------ B.1: dialogs

function Dialog-Routes($kp, [string] $P) {
    Step $kp "$P entry dialog by double click" { $w = Open-Entry $kp 'KeePassGate' 'KPG grant 1' "$P-01-dblclick"; Ok-State $w "$P-01"; $script:W1 = $w }
    Step $kp "$P type into the title, then Enter" {
        $w = $script:W1
        if ($w -and [DeskNative]::IsWindowVisible([IntPtr](Get-Handle $w))) {
            Type-Title $w 'KPG grant 1 edited by hand' "$P-02"
            Ok-State $w "$P-02-typed"
            Show-Front $w; [System.Windows.Forms.SendKeys]::SendWait('{ENTER}'); Start-Sleep -Milliseconds 1200
            Ok-State $w "$P-02-after-enter"
            Save-Shot $w "$P-02-after-enter"
            Close-Dialog $w $kp
        }
        Look $kp "$P-02-main" -NoShot
    }
    Step $kp "$P entry dialog by Enter" { [void](Kp-SelectEntry $kp 'KeePassGate' 'KPG admission 1'); $w = Keys-Dialog $kp '{ENTER}' "$P-03-enter"; Ok-State $w "$P-03"; Close-Dialog $w $kp }
    Step $kp "$P several entries, Enter" { [void](Kp-SelectEntry $kp 'KeePassGate' 'KPG admission 1'); [System.Windows.Forms.SendKeys]::SendWait('^a'); Start-Sleep -Milliseconds 400; $w = Keys-Dialog $kp '{ENTER}' "$P-04-multi"; Ok-State $w "$P-04"; Close-Dialog $w $kp }
    Step $kp "$P Add Entry in KeePassGate (Ctrl+I)" { [void](Kp-Group $kp 'KeePassGate'); $w = Keys-Dialog $kp '^i' "$P-05-add"; Ok-State $w "$P-05"; Close-Dialog $w $kp }
    Step $kp "$P Duplicate Entry (Ctrl+D)" { [void](Kp-SelectEntry $kp 'KeePassGate' 'KPG admission 1'); $w = Keys-Dialog $kp '^d' "$P-06-dup"; Ok-State $w "$P-06"; Close-Dialog $w $kp }
    Step $kp "$P group dialog (F2 on the tree)" { [void](Kp-Group $kp 'KeePassGate'); $w = Keys-Dialog $kp '{F2}' "$P-07-group"; Ok-State $w "$P-07"; Close-Dialog $w $kp }
    Step $kp "$P group dialog of Archive" { [void](Kp-Group $kp 'Archive'); $w = Keys-Dialog $kp '{F2}' "$P-08-subgroup"; Ok-State $w "$P-08"; Close-Dialog $w $kp }
    Step $kp "$P a user's entry" { $w = Open-Entry $kp 'Passwords' 'Bank' "$P-09-user"; Ok-State $w "$P-09"; Close-Dialog $w $kp }
    Step $kp "$P end" { Look $kp "$P-10-end" -NoShot }
}

Run-Case 'b-base-dialogs' @{ watch = 'on' } 'top' @{} { param($kp, $dir) Dialog-Routes $kp 'dbase' } -Save
Run-Case 'b-close' @{ dialogs = 'close' } 'top' @{} { param($kp, $dir) Dialog-Routes $kp 'close' } -Save
Run-Case 'b-cancelsave' @{ dialogs = 'cancelsave' } 'top' @{} { param($kp, $dir) Dialog-Routes $kp 'cancel' } -Save
Run-Case 'b-disableok' @{ dialogs = 'disableok' } 'top' @{} { param($kp, $dir) Dialog-Routes $kp 'disok' } -Save
Run-Case 'b-readonly' @{ dialogs = 'readonly' } 'top' @{} { param($kp, $dir) Dialog-Routes $kp 'ro' } -Save
Run-Case 'b-lockall' @{ dialogs = 'lockall' } 'top' @{} { param($kp, $dir) Dialog-Routes $kp 'lock' } -Save

# ------------------------------------------------------------------ B.2 to B.4: commands

function Commands($kp, [string] $P) {
    Step $kp "$P menus with S1 selected" {
        [void](Kp-SelectEntry $kp 'KeePassGate' 'KPG grant 1')
        Menu-Items $kp @('Entry') "$P-00-entry-menu"
        Menu-Items $kp @('Entry', 'Edit Entry (Quick)') "$P-00-quick-menu"
        [void](Kp-SelectEntry $kp 'KeePassGate' 'KPG grant 1')
        [System.Windows.Forms.SendKeys]::SendWait('+{F10}'); Start-Sleep -Milliseconds 900
        foreach ($w in (Get-ProcWindows $kp.Process.Id)) { foreach ($e in (Find-All $w $null $CT::MenuItem $null)) { try { if (-not $e.Current.IsOffscreen -and $e.Current.BoundingRectangle.Width -gt 0) { Say "[$P-00-ctx] item <<$($e.Current.Name)>> enabled=$($e.Current.IsEnabled)" } } catch { } } }
        [System.Windows.Forms.SendKeys]::SendWait('{ESC}'); Start-Sleep -Milliseconds 300
        [void](Kp-Group $kp 'KeePassGate')
        Menu-Items $kp @('Group') "$P-00-group-menu"
    }
    Step $kp "$P S3 Delete key" { [void](Kp-SelectEntry $kp 'KeePassGate' 'KPG admission 1'); $k = Get-Handles $kp.Process.Id; [System.Windows.Forms.SendKeys]::SendWait('{DEL}'); Dialogs $kp $k "$P-01-del" }
    Step $kp "$P S4 Shift+Delete" { [void](Kp-SelectEntry $kp 'KeePassGate' 'KPG admission 2'); $k = Get-Handles $kp.Process.Id; [System.Windows.Forms.SendKeys]::SendWait('+{DEL}'); Dialogs $kp $k "$P-02-shiftdel" }
    Step $kp "$P S5 dragged onto Passwords" { [void](Kp-SelectEntry $kp 'KeePassGate' 'KPG revocation 1'); Kp-DragEntry $kp 'KPG revocation 1' 'Passwords'; Dialogs $kp @(Get-Handles $kp.Process.Id) "$P-03-drag" 1 }
    Step $kp "$P S6 Alt+Down" { [void](Kp-SelectEntry $kp 'KeePassGate' 'KPG profile 1'); [System.Windows.Forms.SendKeys]::SendWait('%{DOWN}'); Start-Sleep -Milliseconds 800 }
    Step $kp "$P S7 Ctrl+D" { [void](Kp-SelectEntry $kp 'KeePassGate' 'KPG authority 1'); $k = Get-Handles $kp.Process.Id; [System.Windows.Forms.SendKeys]::SendWait('^d'); Dialogs $kp $k "$P-05-dup" }
    Step $kp "$P S7 Entry menu Duplicate Entry" { [void](Kp-SelectEntry $kp 'KeePassGate' 'KPG authority 1'); $k = Get-Handles $kp.Process.Id; Kp-Menu $kp @('Entry', 'Duplicate Entry'); Dialogs $kp $k "$P-05m-dup" }
    Step $kp "$P S8 quick edit: Expires Now" { [void](Kp-SelectEntry $kp 'KeePassGate' 'KPG retirement 1'); Kp-Menu $kp @('Entry', 'Edit Entry (Quick)', 'Expires: Now'); Start-Sleep -Milliseconds 800 }
    Step $kp "$P S8 quick edit: color" { [void](Kp-SelectEntry $kp 'KeePassGate' 'KPG retirement 1'); Kp-Menu $kp @('Entry', 'Edit Entry (Quick)', 'Color', 'Light Red'); Start-Sleep -Milliseconds 800 }
    Step $kp "$P S12 copy, paste into KeePassGate" { [void](Kp-SelectEntry $kp 'KeePassGate' 'KPG profile 12'); [System.Windows.Forms.SendKeys]::SendWait('^+c'); Start-Sleep -Milliseconds 600; [void](Kp-Group $kp 'KeePassGate'); $k = Get-Handles $kp.Process.Id; [System.Windows.Forms.SendKeys]::SendWait('^+v'); Dialogs $kp $k "$P-07-paste" 2 }
    Step $kp "$P S1 edited in its dialog" {
        $w = Open-Entry $kp 'KeePassGate' 'KPG grant 1' "$P-08-edit"
        if ($w -and [DeskNative]::IsWindowVisible([IntPtr](Get-Handle $w))) { Kp-SetPassword $w 'zqxstore-pw1-edited-by-hand'; Show-Front $w; try { [void](Click-KidAt (Get-Handle $w) 'OK') } catch { Say "no OK: $($_.Exception.Message)" }; Start-Sleep -Milliseconds 1200; Ok-State $w "$P-08-after-ok"; Close-Dialog $w $kp }
    }
    Step $kp "$P S2 Move to Group Passwords" { [void](Kp-SelectEntry $kp 'KeePassGate' 'KPG grant 2'); Kp-Menu $kp @('Entry', 'Move to Group', 'Passwords'); Start-Sleep -Milliseconds 800 }
    Step $kp "$P Add Entry by Ctrl+I in KeePassGate" { [void](Kp-Group $kp 'KeePassGate'); $w = Keys-Dialog $kp '^i' "$P-12-add"; Ok-State $w "$P-12"; Close-Dialog $w $kp }
    Step $kp "$P Recycle Bin: Empty" { [void](Kp-Group $kp 'Recycle Bin'); $k = Get-Handles $kp.Process.Id; Kp-Menu $kp @('Group', 'Empty Recycle Bin'); Dialogs $kp $k "$P-13-empty" }
    Step $kp "$P KeePassGate: Ctrl+A, Delete" { [void](Kp-SelectEntry $kp 'KeePassGate' 'KPG grant 1'); [System.Windows.Forms.SendKeys]::SendWait('^a'); Start-Sleep -Milliseconds 400; $k = Get-Handles $kp.Process.Id; [System.Windows.Forms.SendKeys]::SendWait('{DEL}'); Dialogs $kp $k "$P-14-all-del" }
    Step $kp "$P KeePassGate: Move Group One Down (Alt+Down on the tree), then Delete on it" { [void](Kp-Group $kp 'KeePassGate'); [System.Windows.Forms.SendKeys]::SendWait('%{DOWN}'); Start-Sleep -Milliseconds 800; Mark "$P group Delete key"; $k = Get-Handles $kp.Process.Id; [System.Windows.Forms.SendKeys]::SendWait('{DEL}'); Dialogs $kp $k "$P-15-group-del" }
    Step $kp "$P root: Sort Direct Subgroups" { [void](Kp-Group $kp 'Desk store'); Kp-Menu $kp @('Group', 'Rearrange', 'Sort Direct Subgroups'); Start-Sleep -Milliseconds 800 }
    Step $kp "$P end" { Look $kp "$P-16-end" }
}

Run-Case 'b-watch' @{ watch = 'on' } 'top' @{} { param($kp, $dir) Commands $kp 'watch' } -Save
Run-Case 'b-block' @{ menus = 'disable'; keyfilter = 'on'; drag = 'block'; defaultaction = 'cancel' } 'top' @{} { param($kp, $dir) Commands $kp 'block' } -Save
Run-Case 'b-hideonly' @{ menus = 'hideonly' } 'top' @{} {
    param($kp, $dir)
    Step $kp 'hideonly menus' { [void](Kp-SelectEntry $kp 'KeePassGate' 'KPG grant 1'); Menu-Items $kp @('Entry') 'hideonly-00-entry-menu' }
    Step $kp 'hideonly Ctrl+D on a hidden item' { [void](Kp-SelectEntry $kp 'KeePassGate' 'KPG grant 1'); $w = Keys-Dialog $kp '^d' 'hideonly-01-dup'; Close-Dialog $w $kp }
    Step $kp 'hideonly Ctrl+I on a hidden item' { [void](Kp-Group $kp 'KeePassGate'); $w = Keys-Dialog $kp '^i' 'hideonly-02-add'; Close-Dialog $w $kp }
    Step $kp 'hideonly Delete' { [void](Kp-SelectEntry $kp 'KeePassGate' 'KPG grant 1'); $k = Get-Handles $kp.Process.Id; [System.Windows.Forms.SendKeys]::SendWait('{DEL}'); Dialogs $kp $k 'hideonly-03-del' }
    Step $kp 'hideonly end' { Look $kp 'hideonly-04-end' -NoShot }
}

# The question KeePass asks when an edited entry dialog is closed, answered Yes, under the two
# switches that leave the dialog open: whether the edit then reaches the entry.
function Close-Yes($kp, [string] $P) {
    Step $kp "$P edit S1, Escape, answer Yes" {
        $w = Open-Entry $kp 'KeePassGate' 'KPG grant 1' "$P-01-open"
        Ok-State $w "$P-01"
        Type-Title $w 'KPG grant 1 edited by hand' "$P-02"
        Kp-SetPassword $w 'zqxstore-pw1-edited-by-hand'
        Ok-State $w "$P-02-typed"
        Show-Front $w
        $known = Get-Handles $kp.Process.Id
        [System.Windows.Forms.SendKeys]::SendWait('{ESC}')
        Start-Sleep -Milliseconds 800
        [void](Kp-Dialogs $kp $known "$P-03-question" @('*save the changes*', 'Yes') 3)
        Ok-State $w "$P-03-after-yes"
        Close-Dialog $w $kp
        Look $kp "$P-04-main"
    }
}
Run-Case 'b-disableok-yes' @{ dialogs = 'disableok' } 'top' @{} { param($kp, $dir) Close-Yes $kp 'disyes' } -Save
Run-Case 'b-cancelsave-yes' @{ dialogs = 'cancelsave' } 'top' @{} { param($kp, $dir) Close-Yes $kp 'canyes' } -Save
# The drag of a store entry out of the store, judged from the selection, and Empty Recycle Bin
# disabled while the bin holds a store entry.
Run-Case 'b-block2' @{ menus = 'disable'; drag = 'block'; dragsel = 'on'; guardbin = 'on' } 'top' @{} {
    param($kp, $dir)
    Step $kp 'block2 S5 dragged onto Passwords' { [void](Kp-SelectEntry $kp 'KeePassGate' 'KPG revocation 1'); Kp-DragEntry $kp 'KPG revocation 1' 'Passwords'; Dialogs $kp @(Get-Handles $kp.Process.Id) 'block2-01-drag' 1 }
    Step $kp 'block2 Recycle Bin: Group menu' { [void](Kp-Group $kp 'Recycle Bin'); Menu-Items $kp @('Group') 'block2-02-group-menu' }
    Step $kp 'block2 Recycle Bin: Empty' { [void](Kp-Group $kp 'Recycle Bin'); $k = Get-Handles $kp.Process.Id; Kp-Menu $kp @('Group', 'Empty Recycle Bin'); Dialogs $kp $k 'block2-03-empty' }
    Step $kp 'block2 end' { Look $kp 'block2-04-end' -NoShot }
} -Save