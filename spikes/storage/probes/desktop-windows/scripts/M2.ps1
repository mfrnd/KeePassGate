<#
.SYNOPSIS
    Measurement 2: KeePassXC's window when the file changes underneath an open database, and its
    own Merge From Database against keepassxc-cli merge.

.DESCRIPTION
    Each case opens a fresh copy of OLD in KeePassXC (2.8.0 unless the case names 2.7.12) with a
    configuration of the run's own (--config, --localconfig), makes the window's changes through
    the real entry editor, replaces the file by NEW as a synchronizer does, waits for what the
    window does, answers its questions as the case names, and reads the file back with the probe.
    Settings a case does not name are the release's own defaults: save after every change, reload
    on change, safe saves, no backup.
#>
param(
    [string] $Work = 'W:\',
    [string[]] $Case = @('all'),
    [int] $Repeat = 1,
    [int] $From = 1,
    [string] $Unelevated = (Join-Path $env:TEMP 'unelevated.ps1')
)

$ErrorActionPreference = 'Stop'
$Case = @($Case | ForEach-Object { $_ -split ',' })
. (Join-Path $PSScriptRoot 'Ui.ps1')
. (Join-Path $PSScriptRoot 'Xc.ps1')
$family = Split-Path -Parent $PSScriptRoot
$probe = Join-Path $family 'probe\out\Desk.exe'
$script:WorkRoot = $Work
$script:UnelevatedScript = $Unelevated
$base = Join-Path $Work 'base'
New-Item -ItemType Directory -Force (Join-Path $Work 'logs'), (Join-Path $Work 'runs\m2'), (Join-Path $Work 'runs\m2-not-counted') | Out-Null

function Probe([string[]] $a) {
    $out = & $probe @a
    if ($LASTEXITCODE -ne 0) { throw "probe $($a -join ' ') failed" }
    $out
}

# ------------------------------------------------------------------ the window

function Xc-Items($xc, [string] $Name) {
    $r = @()
    foreach ($t in 'DataItem', 'ListItem', 'TreeItem') {
        foreach ($e in (Find-All $xc.Main $null ([System.Windows.Automation.ControlType]::$t) $null)) {
            try { if ($e.Current.Name -eq $Name -and -not $e.Current.IsOffscreen) { $r += $e } } catch { }
        }
    }
    $r
}

function Xc-ClickItem($xc, [string] $Name, [switch] $Double) {
    $deadline = (Get-Date).AddSeconds(8)
    do {
        $it = @(Xc-Items $xc $Name) | Select-Object -First 1
        if ($it) { break }
        Start-Sleep -Milliseconds 300
    } while ((Get-Date) -lt $deadline)
    if (-not $it) { Save-Shot $xc.Main ('fail-' + ($Name -replace '\W', '')); throw "no item $Name in KeePassXC's window" }
    Show-Front $xc.Main
    $xy = Center $it
    Click-At $xy[0] $xy[1] -Double:$Double
    Start-Sleep -Milliseconds 600
}

# A field of KeePassXC's entry editor, by the end of its automation identifier (Qt's object path):
# `titleEdit`, `passwordEdit.passwordEdit`.
function Xc-Field($xc, [string[]] $Ids) {
    $deadline = (Get-Date).AddSeconds(6)
    do {
        foreach ($n in $Ids) {
            foreach ($e in (Find-All $xc.Main $null $CT::Edit $null)) {
                try { if ($e.Current.AutomationId -like "*.$n" -and -not $e.Current.IsOffscreen -and $e.Current.IsEnabled) { return $e } } catch { }
            }
        }
        Start-Sleep -Milliseconds 300
    } while ((Get-Date) -lt $deadline)
    $null
}

function Xc-Button($xc, [string] $Name) {
    foreach ($e in (Find-All $xc.Main $Name $CT::Button $null)) {
        try { if (-not $e.Current.IsOffscreen -and $e.Current.IsEnabled) { return $e } } catch { }
    }
    $null
}

function Xc-SetText($e, [string] $Value) {
    $xy = Center $e
    Click-At $xy[0] $xy[1]
    Start-Sleep -Milliseconds 200
    [System.Windows.Forms.SendKeys]::SendWait('^a')
    $escaped = ($Value.ToCharArray() | ForEach-Object { if ('+^%~(){}[]'.Contains([string]$_)) { '{' + $_ + '}' } else { [string]$_ } }) -join ''
    [System.Windows.Forms.SendKeys]::SendWait($escaped)
    Start-Sleep -Milliseconds 200
}

# An entry's password changed through KeePassXC's entry editor: the row double-clicked, the
# password typed, OK. With -Exact OK is clicked in the first 150 ms of a second.
function Xc-EditPassword($xc, [string] $Group, [string] $Title, [string] $Value, [switch] $Exact) {
    if ($Group) { Xc-ClickItem $xc $Group }
    Xc-ClickItem $xc $Title -Double
    Start-Sleep -Milliseconds 800
    $pw = Xc-Field $xc @('passwordEdit.passwordEdit')
    if (-not $pw) { throw 'no password field in the entry editor' }
    Xc-SetText $pw $Value
    $ok = Need (Xc-Button $xc 'OK') 'OK in the entry editor'
    if ($Exact) { while ([DateTime]::UtcNow.Millisecond -gt 150 -or [DateTime]::UtcNow.Millisecond -lt 20) { Start-Sleep -Milliseconds 5 } }
    $t0 = [DateTime]::UtcNow
    $xy = Center $ok
    Click-At $xy[0] $xy[1]
    Start-Sleep -Milliseconds 300
    $t1 = [DateTime]::UtcNow
    Start-Sleep -Milliseconds 500
    Say "edited $Title password=$Value; OK clicked $($t0.ToString('o')) .. $($t1.ToString('o'))"
    @($t0, $t1)
}

function Xc-AddEntry($xc, [string] $Group, [string] $Title) {
    Xc-ClickItem $xc $Group
    $nb = Need (Xc-Button $xc 'New Entry') 'New Entry button'
    $xy = Center $nb; Click-At $xy[0] $xy[1]; Start-Sleep -Milliseconds 1000
    $t = Xc-Field $xc @('titleEdit')
    if (-not $t) { throw 'no title field in the new entry editor' }
    Xc-SetText $t $Title
    $ok = Need (Xc-Button $xc 'OK') 'OK in the entry editor'
    $xy = Center $ok
    Click-At $xy[0] $xy[1]
    Start-Sleep -Milliseconds 800
    Say "added entry $Title in $Group"
}

# Every new window of KeePassXC read, pictured, and answered by the first pair whose pattern
# matches and whose button it has.
function Xc-Dialogs($xc, [int64[]] $Known, [string] $Label, [object[]] $Answers = @(), [double] $Seconds = 6) {
    $seen = New-Object System.Collections.Generic.List[object]
    $deadline = (Get-Date).AddSeconds($Seconds)
    $n = 0
    while ((Get-Date) -lt $deadline) {
        $w = Wait-NewWindow $xc.Process.Id $Known 1
        if (-not $w) { continue }
        Start-Sleep -Milliseconds 700
        $n++
        $r = Read-Window $w "$Label-$n"
        Save-Shot $w "$Label-$n"
        $seen.Add($r)
        $Known += (Get-Handle $w)
        $all = $r.Title + ' ' + ($r.Texts -join ' ')
        for ($i = 0; $i + 1 -lt $Answers.Count; $i += 2) {
            if ($all -notlike $Answers[$i]) { continue }
            $b = $null
            foreach ($e in (Find-All $w $null $CT::Button $null)) { try { if ($e.Current.Name.Replace('&', '').StartsWith($Answers[$i + 1])) { $b = $e; break } } catch { } }
            if (-not $b) { continue }
            $xy = Center $b
            Show-Front $w
            Click-At $xy[0] $xy[1]
            Say "[$Label-$n] pressed <<$($b.Current.Name)>>"
            Start-Sleep -Milliseconds 1500
            break
        }
        $deadline = (Get-Date).AddSeconds($Seconds)
    }
    $seen
}

function Xc-Look($xc, [string] $Label) {
    try {
        $title = $xc.Main.Current.Name
        $names = @()
        foreach ($t in 'DataItem', 'ListItem', 'TreeItem') {
            foreach ($e in (Find-All $xc.Main $null ([System.Windows.Automation.ControlType]::$t) $null)) { try { if (-not $e.Current.IsOffscreen) { $names += $e.Current.Name } } catch { } }
        }
        Say "[$Label] window title <<$title>>; items shown: $(($names | Select-Object -Unique) -join ', ')"
        $status = @(Find-All $xc.Main $null $CT::Text $null | ForEach-Object { try { $_.Current.Name } catch { } } | Where-Object { $_ -match 'reload|merge|overwrit|changed|cancel|saved|Database' })
        if ($status) { Say "[$Label] messages: $($status -join ' | ')" }
    } catch { Say "[$Label] look failed: $($_.Exception.Message)" }
}

function Xc-Exit($xc, [string] $Label) {
    $known = Get-Handles $xc.Process.Id
    Send-To $xc.Main '^q' 500
    [void](Xc-Dialogs $xc $known $Label @('*', 'Discard', '*', 'No', '*', 'Close') 4)
    if (-not $xc.Process.WaitForExit(10000)) { Say 'KeePassXC did not exit; stopped'; Stop-Process -Id $xc.Process.Id -Force }
}

# ------------------------------------------------------------------ a run

function Run-M2([hashtable] $c, [int] $Attempt) {
    $id = "$($c['Id'])-$Attempt"
    $dir = Join-Path $Work "runs\m2\$id"
    if (Test-Path $dir) { Remove-Item -LiteralPath $dir -Recurse -Force }
    New-Item -ItemType Directory -Force $dir | Out-Null
    $script:LogFile = Join-Path $dir 'run.log'
    $script:ShotDir = $dir
    $db = Join-Path $dir 'db.kdbx'
    Copy-Item (Join-Path $base 'OLD.kdbx') $db
    $release = if ($c['Release']) { $c['Release'] } else { '2.8.0' }
    $script:XcExe = Join-Path $Work "kpxc-$release\KeePassXC.exe"
    Say "==== $id : KeePassXC $release, $($c['What'])"
    $opts = if ($c['Options']) { $c['Options'] } else { @{} }
    Write-XcConfig $dir $opts
    $xc = Start-Xc $dir $db $id
    $new = Join-Path $base 'NEW-fixed.kdbx'
    try {
        switch ($c['Edits']) {
            'full' {
                [void](Xc-EditPassword $xc 'Passwords' 'P2' 'p2-window')
                [void](Xc-EditPassword $xc $null 'Q1' 'q1-window')
                [void](Xc-EditPassword $xc $null 'Q2' 'q2-window')
                $t = Xc-EditPassword $xc $null 'Q3' 'q3-window' -Exact
                Xc-AddEntry $xc 'Passwords' 'window-new'
                $q3 = $t[0].ToString('yyyy-MM-ddTHH:mm:ssZ')
                Say "Q3's edit second: $q3 (click and settle in the same second: $($t[0].ToString('ss') -eq $t[1].ToString('ss')))"
                $new = Join-Path $dir 'NEW-run.kdbx'
                Probe @('m1new', (Join-Path $base 'OLD.kdbx'), $new, $q3) | ForEach-Object { Say "probe: $_" }
            }
            'p2' { [void](Xc-EditPassword $xc 'Passwords' 'P2' 'p2-window') }
            default { }
        }
        Say "file before the replacement: $(Get-FileState $db)"
        Copy-Item $db (Join-Path $dir 'window-before.kdbx')
        Copy-Item $new (Join-Path $dir 'arriving.kdbx')
        $known = Get-Handles $xc.Process.Id
        switch ($c['Flow']) {
            'merge-window' {
                # Database, Merge From Database, with NEW as a file of its own.
                $other = Join-Path $dir 'other.kdbx'
                Copy-Item $new $other
                Xc-MergeFrom $xc $other $id
            }
            'edit-open' {
                # P2's editor open with a new password typed, NEW arriving underneath, then OK:
                # KeePassXC defers a reload while an entry is being edited, and saves by itself
                # after the change under its defaults.
                Xc-ClickItem $xc 'Passwords'
                Xc-ClickItem $xc 'P2' -Double
                Start-Sleep -Milliseconds 800
                Xc-SetText (Need (Xc-Field $xc @('passwordEdit.passwordEdit')) 'password field') 'p2-window'
                Replace-File $db $new 'later'
                Start-Sleep -Seconds 3
                $known = Get-Handles $xc.Process.Id
                $ok = Need (Xc-Button $xc 'OK') 'OK in the entry editor'
                $xy = Center $ok; Click-At $xy[0] $xy[1]
                Say 'clicked OK in the entry editor, three seconds after the replacement'
            }
            default { Replace-File $db $new $(if ($c['Time']) { $c['Time'] } else { 'later' }) }
        }
        if ($c['Flow'] -eq 'save-now') { Send-To $xc.Main '^s' 200 }
        if ($c['Flow'] -eq 'edit-now') { [void](Xc-EditPassword $xc $null 'K' 'k-window') }
        $seen = @(Xc-Dialogs $xc $known $id $c['Answers'] $(if ($c['Wait']) { $c['Wait'] } else { 45 }))
        Say "dialogs after the replacement: $($seen.Count)"
        Xc-Look $xc 'after'
        if ($c['Then'] -eq 'edit') {
            $known = Get-Handles $xc.Process.Id
            [void](Xc-EditPassword $xc 'Passwords' 'P2' 'p2-window')
            $more = @(Xc-Dialogs $xc $known "$id-edit" @('*', 'Merge') 8)
            Say "dialogs after P2's edit: $($more.Count)"
        }
        if ($c['Then'] -eq 'save') {
            $known = Get-Handles $xc.Process.Id
            Send-To $xc.Main '^s' 1500
            $more = @(Xc-Dialogs $xc $known "$id-save" $c['SaveAnswers'] 8)
            Say "dialogs at Ctrl+S: $($more.Count)"
            Xc-Look $xc 'after-save'
        }
        Say "file after the flow: $(Get-FileState $db)"
        Copy-Item $db (Join-Path $dir 'after-flow.kdbx')
        Xc-Exit $xc "$id-exit"
        Say "file after the exit: $(Get-FileState $db)"
    }
    finally { Stop-Started }
    Probe @('dump', $db, (Join-Path $dir 'db.dump.txt')) | Out-Null
    Probe @('dump', (Join-Path $dir 'window-before.kdbx'), (Join-Path $dir 'window-before.dump.txt')) | Out-Null
    Probe @('dump', $new, (Join-Path $dir 'arriving.dump.txt')) | Out-Null
    Probe @('dump', (Join-Path $dir 'after-flow.kdbx'), (Join-Path $dir 'after-flow.dump.txt')) | Out-Null
    $v = Probe @('verdict', (Join-Path $dir 'after-flow.kdbx'), $db, (Join-Path $dir 'window-before.kdbx'), $new)
    $v | ForEach-Object { Say "verdict: $_" }
    Get-ChildItem $dir -Filter '*.old.kdbx' -ErrorAction SilentlyContinue | ForEach-Object { Say "backup file: $($_.Name) $(Get-FileState $_.FullName)"; Probe @('verdict', $_.FullName) | ForEach-Object { Say "backup verdict: $_" } }
    if ($c['Flow'] -eq 'merge-window') {
        # The same two copies merged by keepassxc-cli of the same release, for comparison.
        $cli = Join-Path (Split-Path -Parent $script:XcExe) 'keepassxc-cli.exe'
        $target = Join-Path $dir 'cli-merge.kdbx'
        Copy-Item (Join-Path $dir 'window-before.kdbx') $target
        $log = Join-Path $dir 'cli-merge.log'
        & $script:UnelevatedScript -Dir $dir -Log $log -Command "Get-Content '$dir\pw.txt' | & '$cli' merge -s '$target' '$dir\other.kdbx'" | Out-Null
        Get-Content $log | ForEach-Object { Say "cli: $($_ -replace "`0", '')" }
        Probe @('dump', $target, (Join-Path $dir 'cli-merge.dump.txt')) | Out-Null
        Probe @('verdict', $target) | ForEach-Object { Say "cli verdict: $_" }
        $a = Get-Content (Join-Path $dir 'db.dump.txt') | Where-Object { $_ -notmatch ' t\.accessed | usage |^# file' }
        $b = Get-Content (Join-Path $dir 'cli-merge.dump.txt') | Where-Object { $_ -notmatch ' t\.accessed | usage |^# file' }
        $d = @(Compare-Object $a $b | ForEach-Object { $(if ($_.SideIndicator -eq '<=') { 'window ' } else { 'cli    ' }) + $_.InputObject })
        [IO.File]::WriteAllLines((Join-Path $dir 'window-vs-cli.diff.txt'), [string[]]$d)
        Say "window merge against cli merge: $($d.Count) lines differ"
    }
    Add-Content (Join-Path $Work 'logs\m2-verdicts.txt') "$id | $($v[0])" -Encoding UTF8
}

function Xc-MergeFrom($xc, [string] $Other, [string] $Label) {
    Show-Front $xc.Main
    $menu = Need (Find-El $xc.Main -Name 'Database' -ControlType $CT::MenuItem -Seconds 3) 'Database menu'
    $xy = Center $menu; Click-At $xy[0] $xy[1]; Start-Sleep -Milliseconds 800
    $item = $null
    foreach ($w in (Get-ProcWindows $xc.Process.Id)) { foreach ($e in (Find-All $w $null $CT::MenuItem $null)) { try { if ($e.Current.Name -like 'Merge*' -and -not $e.Current.IsOffscreen) { $item = $e } } catch { } } }
    if (-not $item) { throw 'no Merge item in the Database menu' }
    Say "menu: <<$($item.Current.Name)>>"
    $known = Get-Handles $xc.Process.Id
    $xy = Center $item; Click-At $xy[0] $xy[1]
    $fd = Need (Wait-NewWindow $xc.Process.Id $known 10) 'file dialog'
    Start-Sleep -Milliseconds 800
    Say "file dialog <<$($fd.Current.Name)>>"
    [System.Windows.Forms.SendKeys]::SendWait('%n'); Start-Sleep -Milliseconds 300
    [System.Windows.Forms.SendKeys]::SendWait(($Other.ToCharArray() | ForEach-Object { if ('+^%~(){}[]'.Contains([string]$_)) { '{' + $_ + '}' } else { [string]$_ } }) -join '')
    [System.Windows.Forms.SendKeys]::SendWait('{ENTER}')
    Start-Sleep -Seconds 2
    # The unlock of the other file, in the main window or a dialog of its own.
    $pw = $null
    $deadline = (Get-Date).AddSeconds(10)
    while (-not $pw -and (Get-Date) -lt $deadline) {
        foreach ($w in (Get-ProcWindows $xc.Process.Id)) {
            foreach ($e in (Find-All $w $null $CT::Edit $null)) { try { if ($e.Current.AutomationId -like '*passwordEdit' -and -not $e.Current.IsOffscreen) { $pw = $e; $pwWin = $w } } catch { } }
        }
        Start-Sleep -Milliseconds 300
    }
    if (-not $pw) { throw 'no password field for the file to merge' }
    Save-Shot $pwWin "$Label-merge-unlock"
    Xc-SetText $pw $script:XcPassword
    [System.Windows.Forms.SendKeys]::SendWait('{ENTER}')
    Start-Sleep -Seconds 3
}

$merge = @('*modified externally*', 'Merge', '*has changed*', 'Yes')
$cases = @(
    @{ Id = 'd-clean'; What = 'defaults; nothing changed; NEW arrives; then P2 (saved by itself)'; Edits = 'none'; Answers = @(); Then = 'edit' }
    @{ Id = 'd-edit-open-merge'; What = 'defaults; P2''s editor open, NEW arrives, OK (saved by itself); Merge'; Edits = 'none'; Flow = 'edit-open'; Answers = @('*', 'Merge'); Wait = 20 }
    @{ Id = 'd-edit-open-overwrite'; What = 'the same; Overwrite'; Edits = 'none'; Flow = 'edit-open'; Answers = @('*', 'Overwrite'); Wait = 20 }
    @{ Id = 'n-merge'; What = 'save after every change off; full changes unsaved; NEW arrives; Merge; Ctrl+S'; Edits = 'full'; Options = @{ 'General/AutoSaveAfterEveryChange' = 'false' }; Answers = @('*', 'Merge'); Then = 'save' }
    @{ Id = 'n-ignore'; What = 'the same; Ignore; Ctrl+S'; Edits = 'p2'; Options = @{ 'General/AutoSaveAfterEveryChange' = 'false' }; Answers = @('*', 'Ignore'); Then = 'save'; SaveAnswers = @('*', 'Merge') }
    @{ Id = 'n-discard'; What = 'the same; Discard; Ctrl+S'; Edits = 'p2'; Options = @{ 'General/AutoSaveAfterEveryChange' = 'false' }; Answers = @('*', 'Discard'); Then = 'save' }
    @{ Id = 'n-cancel'; What = 'the same; Cancel; Ctrl+S'; Edits = 'p2'; Options = @{ 'General/AutoSaveAfterEveryChange' = 'false' }; Answers = @('*', 'Cancel'); Then = 'save'; SaveAnswers = @('*', 'Merge') }
    @{ Id = 'n-savenow-merge'; What = 'save after every change off; P2 unsaved; NEW arrives; Merge; then the exit, which saves by itself'; Edits = 'p2'; Flow = 'save-now'; Options = @{ 'General/AutoSaveAfterEveryChange' = 'false' }; Answers = @('*', 'Merge') }
    @{ Id = 'n-savenow-overwrite'; What = 'save after every change off; P2 unsaved; NEW arrives; Cancel; Ctrl+S; Overwrite'; Edits = 'p2'; Options = @{ 'General/AutoSaveAfterEveryChange' = 'false' }; Answers = @('*', 'Cancel'); Then = 'save'; SaveAnswers = @('*', 'Overwrite') }
    @{ Id = 'r-clean-yes'; What = 'reload on change off; nothing changed; NEW arrives; Yes'; Edits = 'none'; Options = @{ 'General/AutoReloadOnChange' = 'false' }; Answers = @('*', 'Yes') }
    @{ Id = 'r-clean-no'; What = 'reload on change off; nothing changed; NEW arrives; No; Ctrl+S'; Edits = 'none'; Options = @{ 'General/AutoReloadOnChange' = 'false' }; Answers = @('*', 'No'); Then = 'save'; SaveAnswers = @('*', 'Merge') }
    @{ Id = 'o-noatomic'; What = 'safe saves off, save after every change off; P2; NEW arrives; Merge; Ctrl+S'; Edits = 'p2'; Options = @{ 'General/AutoSaveAfterEveryChange' = 'false'; 'General/UseAtomicSaves' = 'false' }; Answers = @('*', 'Merge'); Then = 'save' }
    @{ Id = 'o-backup'; What = 'backup before save on, save after every change off; P2; NEW arrives; Cancel; Ctrl+S; Overwrite'; Edits = 'p2'; Options = @{ 'General/AutoSaveAfterEveryChange' = 'false'; 'General/BackupBeforeSave' = 'true' }; Answers = @('*', 'Cancel'); Then = 'save'; SaveAnswers = @('*', 'Overwrite') }
    @{ Id = 'w-merge'; What = 'defaults; full changes (saved by themselves); Database, Merge From Database with NEW'; Edits = 'full'; Flow = 'merge-window'; Answers = @('*', 'Merge'); Wait = 10 }
)

foreach ($c in $cases) {
    if ($Case -notcontains 'all' -and $Case -notcontains $c['Id']) { continue }
    for ($r = $From; $r -lt $From + $Repeat; $r++) {
        try { Run-M2 $c $r }
        catch {
            Say "RUN FAILED $($c['Id'])-${r}: $($_.Exception.Message) $($_.ScriptStackTrace)"
            Add-Content (Join-Path $Work 'logs\m2-verdicts.txt') "$($c['Id'])-$r | FAILED: $($_.Exception.Message)" -Encoding UTF8
            Stop-Started
            $failed = Join-Path $Work "runs\m2\$($c['Id'])-$r"
            if (Test-Path $failed) { Move-Item $failed (Join-Path $Work "runs\m2-not-counted\$($c['Id'])-$r-$((Get-Date).ToString('HHmmss'))") -Force }
        }
    }
}
