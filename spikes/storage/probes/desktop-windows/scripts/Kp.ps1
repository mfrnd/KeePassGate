# KeePass 2.62's window, driven: its configuration written before every start, the database opened
# from the command line, entries edited through the real entry dialog, and the dialogs it raises
# read and answered. Dot-sourced after Ui.ps1.

$script:KpExe = $null       # the copy of KeePass.exe in the work directory, through the W: drive
$script:KpPassword = 'desk-probe-invented-password'

# KeePass reads KeePass.config.xml beside its executable when that file sets PreferUserConfiguration
# to false, and then never looks at the user's own configuration; written fresh before every start,
# so that no run inherits another's state. Options are given as path=value, the path below
# <Configuration>.
function Write-KpConfig([hashtable] $Options = @{}) {
    $xml = New-Object System.Xml.XmlDocument
    $xml.LoadXml(@'
<?xml version="1.0" encoding="utf-8"?>
<Configuration>
  <Meta><PreferUserConfiguration>false</PreferUserConfiguration></Meta>
  <Application>
    <Start><CheckForUpdate>false</CheckForUpdate><CheckForUpdateConfigured>true</CheckForUpdateConfigured><OpenLastFile>false</OpenLastFile></Start>
  </Application>
  <MainWindow><EscAction>None</EscAction><MinimizeAfterLocking>false</MinimizeAfterLocking><X>40</X><Y>20</Y><Width>1100</Width><Height>720</Height></MainWindow>
  <Integration><LimitToSingleInstance>false</LimitToSingleInstance></Integration>
  <Security><KeyTransformWeakWarning>false</KeyTransformWeakWarning></Security>
</Configuration>
'@)
    foreach ($k in $Options.Keys) {
        $node = $xml.DocumentElement
        foreach ($part in $k.Split('/')) {
            $child = $node.SelectSingleNode($part)
            if (-not $child) { $child = $xml.CreateElement($part); [void]$node.AppendChild($child) }
            $node = $child
        }
        $node.InnerText = [string]$Options[$k]
    }
    $path = Join-Path (Split-Path -Parent $script:KpExe) 'KeePass.config.xml'
    $xml.Save($path)
    Say "config: $(($Options.Keys | ForEach-Object { "$_=$($Options[$_])" }) -join ', ')"
}

function Start-Kp([string] $Db, [string] $Tag, [string[]] $Extra = @()) {
    $args = @()
    if ($Db) { $args += $Db; $args += "-pw:$script:KpPassword" }
    $args += $Extra
    $p = Start-Unelevated -Exe $script:KpExe -Arguments $args -Tag $Tag
    $main = Wait-Window $p.Id '*KeePass*' 40
    if (-not $main) { throw 'KeePass showed no main window' }
    # The open with -pw: is done when the title names the file.
    $deadline = (Get-Date).AddSeconds(30)
    while ($Db -and (Get-Date) -lt $deadline) {
        if ($main.Current.Name -like '*.kdbx*') { break }
        Start-Sleep -Milliseconds 300
    }
    Say "main window: <<$($main.Current.Name)>>"
    $kp = [pscustomobject]@{ Process = $p; Main = $main }
    # The group rows' places, read while the tree still answers (see Kp-Group).
    $script:GroupPlaces = @{}
    if ($Db) {
        try {
            $th = Get-Handle (Kp-Tree $kp)
            foreach ($it in @(Kp-Groups $kp)) { $script:GroupPlaces[$it.Name] = [DeskMsaa]::Location([IntPtr]$th, $it.Id) }
            Say "group rows: $(($script:GroupPlaces.Keys | Sort-Object) -join ', ')"
        } catch { Say "group rows not read: $($_.Exception.Message)" }
    }
    $kp
}

function Kp-Tree($kp) { Need (Get-ChildByClass $kp.Main 'SysTreeView32') 'group tree' }
function Kp-List($kp) { Need (Get-ChildByClass $kp.Main 'SysListView32') 'entry list' }

# The group tree's and the entry list's items, through the accessibility interface (Ui.ps1).
function Kp-Groups($kp) { Get-MsaaItems (Get-Handle (Kp-Tree $kp)) }
function Kp-Entries($kp) { @(Get-MsaaItems (Get-Handle (Kp-List $kp)) | Where-Object { $_.Role -eq '34' }) }

# Selects a group by a click on its row. The tree's rows are read through the accessibility
# interface, which answered with no rows at all once KeePass had rebuilt the tree (after a lock and
# an unlock, after a synchronize), while the tree on screen was the same; the rows' places read
# while it still answered are kept and used then.
$script:GroupPlaces = @{}
function Kp-Group($kp, [string] $Name) {
    $tree = Kp-Tree $kp
    $th = Get-Handle $tree
    $place = $null
    $deadline = (Get-Date).AddSeconds(4)
    do {
        $items = @(Kp-Groups $kp)
        foreach ($it in $items) { $script:GroupPlaces[$it.Name] = [DeskMsaa]::Location([IntPtr]$th, $it.Id) }
        $item = $items | Where-Object { $_.Name -eq $Name } | Select-Object -First 1
        if ($item) { $place = $script:GroupPlaces[$Name]; break }
        if ($items.Count -eq 0 -and $script:GroupPlaces.ContainsKey($Name)) { $place = $script:GroupPlaces[$Name]; Say "group $Name from the places kept"; break }
        Start-Sleep -Milliseconds 250
    } while ((Get-Date) -lt $deadline)
    if (-not $place) { throw "no group $Name in the tree: $((Kp-Groups $kp | ForEach-Object Name) -join ', ')" }
    Show-Front $kp.Main
    $p = $place.Split(',')
    Click-At ([int]$p[0] + 40) ([int]$p[1] + [int]$p[3] / 2)
    Start-Sleep -Milliseconds 500
    $Name
}
function Kp-SelectEntry($kp, [string] $Group, [string] $Title) {
    if ($Group) { [void](Kp-Group $kp $Group) }
    $list = Kp-List $kp
    $deadline = (Get-Date).AddSeconds(5)
    do {
        $item = Kp-Entries $kp | Where-Object { $_.Name -eq $Title } | Select-Object -First 1
        if ($item) { break }
        Start-Sleep -Milliseconds 250
    } while ((Get-Date) -lt $deadline)
    if (-not $item) { throw "no entry $Title in the list: $((Kp-Entries $kp | ForEach-Object Name) -join ', ')" }
    Show-Front $kp.Main
    # A click on the row, which KeePass answers as a person's selection (the entry view follows);
    # selecting through the accessibility interface left the entry view on the group.
    $loc = [DeskMsaa]::Location([IntPtr](Get-Handle $list), $item.Id).Split(',')
    Click-At ([int]$loc[0] + 30) ([int]$loc[1] + [int]$loc[3] / 2)
    Start-Sleep -Milliseconds 400
    $item
}

# Opens the entry's edit dialog with Enter on its row, and answers the dialog.
function Kp-OpenEntry($kp, [string] $Group, [string] $Title) {
    $known = Get-Handles $kp.Process.Id
    $item = Kp-SelectEntry $kp $Group $Title
    # A double click on the row: Enter sent to the list after the selection did not open the
    # dialog here, the focus being elsewhere.
    $loc = [DeskMsaa]::Location([IntPtr](Get-Handle (Kp-List $kp)), $item.Id).Split(',')
    Click-At ([int]$loc[0] + 30) ([int]$loc[1] + [int]$loc[3] / 2) -Double
    $dlg = Wait-Window $kp.Process.Id 'Edit Entry*' 10 $known
    Need $dlg "edit dialog of $Title"
}

function Kp-SetPassword($dlg, [string] $Value) {
    $h = Get-Handle $dlg
    $pw = Find-BoxBy $h 'Password:'
    if (-not $pw) { throw 'no password box' }
    Type-Hwnd $pw.H $Value
    $rep = Find-BoxBy $h 'Repeat:'
    if ($rep -and $rep.Enabled) { Type-Hwnd $rep.H $Value }
}

function Wait-Gone($w, [double] $Seconds = 10) {
    $h = Get-Handle $w
    $deadline = (Get-Date).AddSeconds($Seconds)
    while ((Get-Date) -lt $deadline) {
        if (-not [DeskNative]::IsWindowVisible([IntPtr]$h)) { return $true }
        Start-Sleep -Milliseconds 100
    }
    $false
}

function Kp-Ok($dlg) {
    [void](Click-Kid (Get-Handle $dlg) 'OK')
    [void](Wait-Gone $dlg)
}

# Changes an entry's password through its dialog. With -Exact the OK is pressed in the first
# quarter of a second and the times on both sides of the dialog's closing are logged, so that the
# second KeePass gave the edit is known.
function Kp-EditPassword($kp, [string] $Group, [string] $Title, [string] $Value, [switch] $Exact) {
    $dlg = Kp-OpenEntry $kp $Group $Title
    Kp-SetPassword $dlg $Value
    $ok = Need (Find-Kid (Get-Handle $dlg) 'OK' 'BUTTON') 'OK button'
    if ($Exact) {
        while ([DateTime]::UtcNow.Millisecond -gt 150 -or [DateTime]::UtcNow.Millisecond -lt 20) { Start-Sleep -Milliseconds 5 }
    }
    $t0 = [DateTime]::UtcNow
    [void][DeskNative]::PostMessage([IntPtr]$ok.H, 0x00F5, [IntPtr]::Zero, [IntPtr]::Zero)
    [void](Wait-Gone $dlg)
    $t1 = [DateTime]::UtcNow
    Say "edited $Title password=$Value; OK posted $($t0.ToString('o')), dialog gone $($t1.ToString('o'))"
    @($t0, $t1)
}

function Kp-AddEntry($kp, [string] $Group, [string] $Title) {
    [void](Kp-Group $kp $Group)
    $known = Get-Handles $kp.Process.Id
    Send-To $kp.Main '^i' 300
    $dlg = Need (Wait-Window $kp.Process.Id 'Add Entry*' 10 $known) 'add entry dialog'
    $t = Need (Find-BoxBy (Get-Handle $dlg) 'Title:') 'title box'
    Type-Hwnd $t.H $Title
    Kp-Ok $dlg
    Say "added entry $Title in $Group"
}

# Presses the button whose caption starts with $Prefix: a Win32 button child by the message a
# click sends, or else an element UI automation can invoke (a task dialog's command link), invoked
# on another thread so that whatever it opens cannot hold this one.
function Press-Named($w, [string] $Prefix) {
    $h = Get-Handle $w
    Show-Front $w
    $k = Find-Kid $h $Prefix 'BUTTON' -Prefix -Seconds 0.5
    if ($k) {
        Click-At ([int](($k.L + $k.R) / 2)) ([int](($k.T + $k.B) / 2))
        return "$($k.Text) (clicked)"
    }
    # A button UI automation knows only (a task dialog's or a message box's), clicked at its
    # bounds: invoking it held the driver whenever what it started opened another dialog.
    foreach ($e in (Find-All $w $null $null $null)) {
        try {
            $n = $e.Current.Name
            $t = $e.Current.ControlType
            if (-not $n -or -not $n.Replace('&', '').StartsWith($Prefix)) { continue }
            if ($t -ne $CT::Button -and $t -ne $CT::Pane -and $t -ne $CT::SplitButton) { continue }
            $xy = Center $e
            Click-At $xy[0] $xy[1]
            return "$n (clicked at bounds)"
        } catch { }
    }
    throw "no button '$Prefix' on <<$(try { $w.Current.Name } catch { '?' })>>"
}
# Reads, pictures and answers every window that appears within $Seconds after a step. $Answers is
# a list of pairs: a pattern the window's title or text must match, and the prefix of the button
# to press; a window no pair matches is read and left standing. Answers the windows read.
function Kp-Dialogs($kp, [int64[]] $Known, [string] $Label, [object[]] $Answers = @(), [double] $Seconds = 6) {
    $seen = New-Object System.Collections.Generic.List[object]
    $deadline = (Get-Date).AddSeconds($Seconds)
    $n = 0
    while ((Get-Date) -lt $deadline) {
        $w = Wait-NewWindow $kp.Process.Id $Known 1
        if (-not $w) { continue }
        Start-Sleep -Milliseconds 700
        $n++
        $r = Read-Window $w "$Label-$n"
        Save-Shot $w "$Label-$n"
        $seen.Add($r)
        $all = $r.Title + ' ' + ($r.Texts -join ' ')
        $Known += (Get-Handle $w)
        # The first pair whose pattern matches and whose button the window has.
        for ($i = 0; $i + 1 -lt $Answers.Count; $i += 2) {
            if ($all -notlike $Answers[$i]) { continue }
            try { $pressed = Press-Named $w $Answers[$i + 1] } catch { continue }
            Say "[$Label-$n] pressed <<$pressed>>"
            Start-Sleep -Milliseconds 1500
            break
        }
        $deadline = (Get-Date).AddSeconds($Seconds)
    }
    $seen
}

# Closes KeePass with nothing left to decide: every dialog it shows on the way is pictured and the
# question whether to save is answered with what the run asks, discarding by default.
function Kp-Exit($kp, [string] $Label, [object[]] $Answers = @('*changes before exiting*', 'Discard')) {
    $known = Get-Handles $kp.Process.Id
    Send-To $kp.Main '%{F4}' 300
    $seen = Kp-Dialogs $kp $known $Label $Answers 5
    if (-not $kp.Process.WaitForExit(15000)) { Say 'KeePass did not exit; stopped'; Stop-Process -Id $kp.Process.Id -Force }
    $seen
}

function Kp-StatusText($kp) {
    $sb = Find-El $kp.Main -ControlType $CT::StatusBar -Seconds 1
    if (-not $sb) { return '' }
    (@(Find-All $sb $null $CT::Text $null | ForEach-Object { $_.Current.Name }) -join ' | ')
}

# What the window shows of the two groups the cases are about.
function Kp-Look($kp, [string] $Label) {
    try {
        $title = $kp.Main.Current.Name
        $groups = @(Kp-Groups $kp | ForEach-Object { $_.Name }) -join ', '
        [void](Kp-Group $kp 'KeePassGate')
        $kg = @(Kp-Entries $kp | ForEach-Object { $_.Name }) -join ', '
        [void](Kp-Group $kp 'Passwords')
        $pw = @(Kp-Entries $kp | ForEach-Object { $_.Name }) -join ', '
        Say "[$Label] window title <<$title>>; groups [$groups]; KeePassGate [$kg]; Passwords [$pw]; status <<$(Kp-StatusText $kp)>>"
    } catch { Say "[$Label] look failed: $($_.Exception.Message)" }
}

function Kp-Unlock($kp, [string] $Label) {
    $known = Get-Handles $kp.Process.Id
    Send-To $kp.Main '^l' 500
    $dlg = Wait-Window $kp.Process.Id 'Open Database*' 10 $known
    if (-not $dlg) { throw 'no key prompt at unlock' }
    [void](Read-Window $dlg "$Label-prompt")
    $box = Need (Find-BoxBy (Get-Handle $dlg) 'Master password:') 'master password box'
    Type-Hwnd $box.H $script:KpPassword
    [void](Click-Kid (Get-Handle $dlg) 'OK')
    [void](Wait-Gone $dlg 20)
    Start-Sleep -Milliseconds 800
}

function Kp-FileDialog($kp, [int64[]] $Known, [string] $Like, [string] $Path, [string] $Label) {
    $dlg = Wait-Window $kp.Process.Id $Like 10 $Known
    if (-not $dlg) { throw "no file dialog '$Like'" }
    Say "[$Label] file dialog <<$($dlg.Current.Name)>>"
    Start-Sleep -Milliseconds 700
    Show-Front $dlg
    [System.Windows.Forms.SendKeys]::SendWait('%n')
    Start-Sleep -Milliseconds 300
    $escaped = ($Path.ToCharArray() | ForEach-Object { if ('+^%~(){}[]'.Contains([string]$_)) { '{' + $_ + '}' } else { [string]$_ } }) -join ''
    [System.Windows.Forms.SendKeys]::SendWait($escaped)
    Start-Sleep -Milliseconds 300
    [System.Windows.Forms.SendKeys]::SendWait('{ENTER}')
    Start-Sleep -Milliseconds 800
    $dlg
}

# Opens a menu path with mouse clicks on each item's bounds: invoking a menu item through UI
# automation held the driver whenever the item opened a modal dialog.
function Kp-Menu($kp, [string[]] $Path) {
    Show-Front $kp.Main
    $top = Need (Find-El $kp.Main -Name $Path[0] -ControlType $CT::MenuItem -Seconds 3) "menu $($Path[0])"
    $xy = Center $top
    Click-At $xy[0] $xy[1]
    Start-Sleep -Milliseconds 700
    for ($i = 1; $i -lt $Path.Count; $i++) {
        $item = $null
        $deadline = (Get-Date).AddSeconds(5)
        while (-not $item -and (Get-Date) -lt $deadline) {
            # The open drop-down's items are found below the main window as well as in the
            # drop-down's own window; the first one shown on screen is taken.
            foreach ($w in (Get-ProcWindows $kp.Process.Id)) {
                foreach ($e in (Find-All $w $null $CT::MenuItem $null)) {
                    try { if ($e.Current.Name.StartsWith($Path[$i]) -and -not $e.Current.IsOffscreen -and $e.Current.BoundingRectangle.Width -gt 0) { $item = $e; break } } catch { }
                }
                if ($item) { break }
            }
        }
        if (-not $item) { throw "no menu item $($Path[$i])" }
        Say "menu: <<$($item.Current.Name)>>"
        $xy = Center $item
        Click-At $xy[0] $xy[1]
        Start-Sleep -Milliseconds 700
    }
}

# The check boxes and radio buttons of a dialog, with their state (BM_GETCHECK: 1 checked).
function Get-Checks([int64] $Hwnd) {
    # Check boxes and radio buttons only, with their state, read through the accessibility
    # interface: a WinForms check box draws itself, and BM_GETCHECK answered 0 for every one.
    @(Get-Kids $Hwnd | Where-Object { $_.Class -like '*BUTTON*' -and $_.Text } | ForEach-Object {
        $rs = ([DeskMsaa]::Self([IntPtr]$_.H)).Split("`t")
        $role = [int]$rs[0]; $state = [int]$rs[1]
        if ($role -eq 44 -or $role -eq 45) {
            [pscustomobject]@{ Text = $_.Text.Replace('&', ''); Checked = $(if ($state -band 0x10) { 1 } else { 0 }); H = $_.H; Enabled = -not ($state -band 0x1) }
        }
    })
}

# A button pressed by a mouse click at its center: the message a click sends (BM_CLICK) was
# ignored by the import dialog's OK.
function Click-KidAt([int64] $Hwnd, [string] $Text) {
    $b = Find-Kid $Hwnd $Text 'BUTTON'
    if (-not $b) { throw "no button '$Text' in window $Hwnd" }
    Click-At ([int](($b.L + $b.R) / 2)) ([int](($b.T + $b.B) / 2))
    Start-Sleep -Milliseconds 300
    $b
}

# Answers KeePass's key prompt with the invented password.
function Kp-KeyPrompt($kp, [int64[]] $Known, [string] $Label) {
    $dlg = Wait-NewWindow $kp.Process.Id $Known 12
    if (-not $dlg) { throw 'no key prompt' }
    Start-Sleep -Milliseconds 600
    Save-Shot $dlg "$Label-key"
    [void](Read-Window $dlg "$Label-key")
    $box = Need (Find-BoxBy (Get-Handle $dlg) 'Master password:') 'master password box'
    Type-Hwnd $box.H $script:KpPassword
    [void](Click-KidAt (Get-Handle $dlg) 'OK')
    [void](Wait-Gone $dlg 20)
}

# In KeePass's Import File/Data dialog: the format KeePass KDBX (2.x), the file, OK, the key of the
# file to import, and in Import Behavior the method named (a PwMergeMethod name), after logging
# which method the dialog had preselected.
function Kp-ImportKdbx($kp, $dlg, [string] $Path, [string] $Method, [string] $Label) {
    $h = Get-Handle $dlg
    Start-Sleep -Milliseconds 600
    [void](Read-Window $dlg "$Label-import")
    $list = Get-Kids $h | Where-Object { $_.Class -like '*SysListView32*' } | Select-Object -First 1
    $item = Get-MsaaItems $list.H | Where-Object { $_.Name -like 'KeePass KDBX*' } | Select-Object -First 1
    if (-not $item) { throw 'no KDBX format in the import list' }
    $loc = [DeskMsaa]::Location([IntPtr]$list.H, $item.Id).Split(',')
    Click-At ([int]$loc[0] + 20) ([int]$loc[1] + [int]$loc[3] / 2)
    Start-Sleep -Milliseconds 400
    $box = Get-Kids $h | Where-Object { $_.Class -like '*EDIT*' -and $_.Enabled } | Select-Object -First 1
    Type-Hwnd $box.H $Path
    Save-Shot $dlg "$Label-import"
    $known = Get-Handles $kp.Process.Id
    [void](Click-KidAt $h 'OK')
    Kp-KeyPrompt $kp $known $Label
    $m = Need (Wait-Window $kp.Process.Id 'Import Behavior*' 15) 'Import Behavior dialog'
    Start-Sleep -Milliseconds 500
    $mh = Get-Handle $m
    $checks = Get-Checks $mh
    Say "[$Label] Import Behavior preselected: $(($checks | Where-Object { $_.Checked -eq 1 } | ForEach-Object Text) -join ', ')"
    [void](Read-Window $m "$Label-method")
    Save-Shot $m "$Label-method"
    $text = @{ CreateNewUuids = 'Create new IDs'; KeepExisting = 'Keep existing'; OverwriteExisting = 'Overwrite existing'; OverwriteIfNewer = 'Overwrite if newer'; Synchronize = 'Overwrite if newer and apply deletions' }[$Method]
    $radio = $checks | Where-Object { $_.Text -eq $text } | Select-Object -First 1
    if (-not $radio) { throw "no method $text" }
    Focus-Hwnd $radio.H
    Start-Sleep -Milliseconds 300
    Say "[$Label] chose <<$text>>, now checked: $(((Get-Checks $mh) | Where-Object { $_.Checked -eq 1 } | ForEach-Object Text) -join ', ')"
    [void](Click-KidAt $mh 'OK')
    Start-Sleep -Seconds 2
}

# Drags an entry row of the shown group onto a group row of the same window, with the mouse
# button held, as a person moves an entry in KeePass.
function Kp-DragEntry($kp, [string] $Title, [string] $Group) {
    $list = Get-Handle (Kp-List $kp)
    $row = Kp-Entries $kp | Where-Object { $_.Name -eq $Title } | Select-Object -First 1
    $s = [DeskMsaa]::Location([IntPtr]$list, $row.Id).Split(',')
    $t = $script:GroupPlaces[$Group].Split(',')
    $sx = [int]$s[0] + 30; $sy = [int]$s[1] + [int]$s[3] / 2; $tx = [int]$t[0] + 40; $ty = [int]$t[1] + [int]$t[3] / 2
    [void][DeskNative]::SetCursorPos($sx, $sy); Start-Sleep -Milliseconds 200
    [DeskNative]::mouse_event(0x2, 0, 0, 0, [UIntPtr]::Zero)
    for ($i = 1; $i -le 30; $i++) { [void][DeskNative]::SetCursorPos([int]($sx + ($tx - $sx) * $i / 30), [int]($sy + ($ty - $sy) * $i / 30)); Start-Sleep -Milliseconds 40 }
    Start-Sleep -Milliseconds 600
    [DeskNative]::mouse_event(0x4, 0, 0, 0, [UIntPtr]::Zero)
    Say "dragged $Title from $sx,$sy onto $Group at $tx,$ty"
    Start-Sleep -Milliseconds 800
}
