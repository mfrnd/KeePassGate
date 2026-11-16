<#
.SYNOPSIS
    The desktop-hide-and-lock measurements: whether a plugin can hide the store from the person at
    KeePass 2.62's window, and whether it can keep the store from being changed.

.DESCRIPTION
    Runs over a work directory that Run.ps1 prepared ($Work, normally the subst drive W:\): the
    KeePass copy kp-hl with the probe plugin HideLock in Plugins\HideLock, and the probe Desk.exe.
    Each case makes a fresh database with the probe (hlbase), writes HideLock.ini with the case's
    switches, starts KeePass under a basic user token, does what a person does, reads and pictures
    the window, exits without saving unless the case saves, and dumps the file. The helpers are the
    desktop-windows family's Ui.ps1 and Kp.ps1, dot-sourced from that family unchanged.
#>
param(
    [Parameter(Mandatory)] [string] $Work,
    [string] $Case = 'all',
    [string] $Unelevated,
    [int] $Run = 1
)

$ErrorActionPreference = 'Stop'
$dw = Join-Path $PSScriptRoot '..\..\desktop-windows\scripts'
. (Join-Path $dw 'Ui.ps1')
. (Join-Path $dw 'Kp.ps1')

$script:UnelevatedScript = $Unelevated
$script:WorkRoot = $Work
$script:KpExe = Join-Path $Work 'kp-hl\KeePass.exe'
$script:Probe = Join-Path $Work 'bin\Desk.exe'
$script:Ini = Join-Path $Work 'kp-hl\Plugins\HideLock\HideLock.ini'
$script:PluginLog = $null
New-Item -ItemType Directory -Force (Join-Path $Work 'logs'), (Join-Path $Work 'runs') | Out-Null

# Kp.ps1's Show-Front taps Alt before SetForegroundWindow, which Windows needs to let a window
# come to the front; tapped while KeePass is already in front, that Alt opens KeePass's menu bar,
# and the keys sent next drive the menu (the first run of a-base opened Tools, Options by the
# letters of a search). So a window already in front is left alone.
function Show-Front($w) {
    $h = [IntPtr](Get-Handle $w)
    if ($h -eq [IntPtr]::Zero) { return }
    if ([DeskNative]::GetForegroundWindow() -eq $h) { return }
    if ([DeskNative]::IsIconic($h)) { [void][DeskNative]::ShowWindow($h, 9) }
    for ($i = 0; $i -lt 5; $i++) {
        [DeskNative]::keybd_event(0x12, 0, 0, [UIntPtr]::Zero)
        [DeskNative]::keybd_event(0x12, 0, 2, [UIntPtr]::Zero)
        [void][DeskNative]::SetForegroundWindow($h)
        [void][DeskNative]::BringWindowToTop($h)
        Start-Sleep -Milliseconds 150
        if ([DeskNative]::GetForegroundWindow() -eq $h) { break }
    }
    # An Alt tap that reached KeePass's main window leaves its menu bar armed; Escape disarms it
    # there (the runs set Esc to do nothing in the main window). A dialog has no menu bar, and an
    # Escape would cancel it.
    $title = try { $w.Current.Name } catch { '' }
    if ($title -like '*- KeePass*') {
        Start-Sleep -Milliseconds 100
        [System.Windows.Forms.SendKeys]::SendWait('{ESC}')
        Start-Sleep -Milliseconds 100
    }
}

# ------------------------------------------------------------------ observing

function Mark([string] $What) {
    Say "== $What"
    if ($script:PluginLog) {
        $line = [DateTime]::UtcNow.ToString('HH:mm:ss.fff') + " DRIVER $What`r`n"
        for ($i = 0; $i -lt 20; $i++) {
            try { [IO.File]::AppendAllText($script:PluginLog, $line); break } catch { Start-Sleep -Milliseconds 15 }
        }
    }
}

# Every visible window of KeePass other than the main window, read: KeePass's own error dialogs
# and anything a step left open.
function Other-Windows($kp, [string] $Label) {
    $main = Get-Handle $kp.Main
    $n = 0
    foreach ($w in (Get-ProcWindows $kp.Process.Id)) {
        $h = Get-Handle $w
        if ($h -eq 0 -or $h -eq $main) { continue }
        try { $c = $w.Current.ClassName } catch { continue }
        if ($c -in 'SysShadow', 'tooltips_class32') { continue }
        if (-not [DeskNative]::IsWindowVisible([IntPtr]$h)) { continue }
        $n++
        [void](Read-Window $w "$Label-other$n")
        Save-Shot $w "$Label-other$n"
    }
    $n
}

function Look($kp, [string] $Label, [switch] $NoShot) {
    $g = try { @(Kp-Groups $kp | ForEach-Object Name) -join ', ' } catch { "unreadable: $($_.Exception.Message)" }
    $e = try { @(Kp-Entries $kp | ForEach-Object Name) -join ', ' } catch { "unreadable: $($_.Exception.Message)" }
    Say "[$Label] title <<$($kp.Main.Current.Name)>>; tree [$g]; list [$e]; status <<$(Kp-StatusText $kp)>>"
    if (-not $NoShot) { Save-Shot $kp.Main $Label }
    [void](Other-Windows $kp $Label)
}

# The items of a menu path, opened by clicks, each with its enabled state; closed with Escape.
function Menu-Items($kp, [string[]] $Path, [string] $Label) {
    try {
        Kp-Menu $kp $Path
        Start-Sleep -Milliseconds 500
        $seen = @{}
        foreach ($w in (Get-ProcWindows $kp.Process.Id)) {
            foreach ($e in (Find-All $w $null $CT::MenuItem $null)) {
                try {
                    if ($e.Current.IsOffscreen -or $e.Current.BoundingRectangle.Width -le 0) { continue }
                    $k = $e.Current.Name
                    if ($seen.ContainsKey($k)) { continue }
                    $seen[$k] = 1
                    Say "[$Label] item <<$k>> enabled=$($e.Current.IsEnabled)"
                } catch { }
            }
        }
    } catch { Say "[$Label] menu failed: $($_.Exception.Message)" }
    foreach ($i in 1..4) { [System.Windows.Forms.SendKeys]::SendWait('{ESC}'); Start-Sleep -Milliseconds 150 }
}

function Quick-Search($kp, [string] $Text) {
    Send-To $kp.Main '^e' 400
    [System.Windows.Forms.SendKeys]::SendWait('^a')
    $escaped = ($Text.ToCharArray() | ForEach-Object { if ('+^%~(){}[]'.Contains([string]$_)) { '{' + $_ + '}' } else { [string]$_ } }) -join ''
    [System.Windows.Forms.SendKeys]::SendWait($escaped)
    [System.Windows.Forms.SendKeys]::SendWait('{ENTER}')
    Start-Sleep -Milliseconds 1200
}

# The entry view pane's text: the main window's rich edit control.
function View-Text($kp) {
    $k = Get-Kids (Get-Handle $kp.Main) | Where-Object { $_.Class -like '*RICHEDIT*' } | Select-Object -First 1
    if ($k) { [DeskNative]::Text([IntPtr]$k.H) -replace "`r?`n", ' / ' } else { '' }
}

function Press-Keys($kp, [string] $Keys, [int] $After = 700) {
    Send-To $kp.Main $Keys $After
}

# Clicks the entry list's column header at the first column, which sorts by it.
function Sort-List($kp) {
    $list = Get-Handle (Kp-List $kp)
    $hdr = Get-Kids $list | Where-Object { $_.Class -like '*SysHeader32*' } | Select-Object -First 1
    if (-not $hdr) { Say 'no header'; return }
    Click-At ([int]($hdr.L + 40)) ([int](($hdr.T + $hdr.B) / 2))
    Start-Sleep -Milliseconds 800
}

# ------------------------------------------------------------------ one case

function Run-Case([string] $Name, [hashtable] $Switches, [string] $Place, [hashtable] $Config, [scriptblock] $Body, [switch] $Save) {
    if ($Case -ne 'all' -and -not ($Case.Split(',') -contains $Name)) { return }
    $dir = Join-Path $Work "runs\$Name-$Run"
    if (Test-Path $dir) { Remove-Item -Recurse -Force $dir }
    New-Item -ItemType Directory -Force $dir | Out-Null
    $script:LogFile = Join-Path $dir 'run.log'
    $script:ShotDir = $dir
    $script:PluginLog = Join-Path $dir 'plugin.log'
    $t0 = Get-Date
    Say "case $Name run $Run place=$Place switches=$(($Switches.GetEnumerator() | Sort-Object Name | ForEach-Object { "$($_.Name)=$($_.Value)" }) -join ',')"
    $db = Join-Path $dir 'db.kdbx'
    & $script:Probe hlbase $db $Place | Out-Null
    & $script:Probe hlbase (Join-Path $dir 'other.kdbx') $Place new | Out-Null
    & $script:Probe dump $db (Join-Path $dir 'dump-before.txt') | Out-Null
    $lines = @("log=$($script:PluginLog)") + @($Switches.GetEnumerator() | ForEach-Object { "$($_.Name)=$($_.Value)" })
    Set-Content -LiteralPath $script:Ini -Value $lines -Encoding ASCII
    if (-not $Config) { $Config = @{} }
    Write-KpConfig $Config
    $kp = $null
    try {
        $kp = Start-Kp $db $Name
        Start-Sleep -Milliseconds 800
        & $Body $kp $dir
        Say 'FLOW DONE'
    } catch {
        Say "FLOW FAILED: $($_.Exception.Message) at $($_.InvocationInfo.ScriptLineNumber)"
    } finally {
        if ($kp -and -not $kp.Process.HasExited) {
            if ($Save) { Mark 'save at the end'; Press-Keys $kp '^s' 1500 }
            [void](Kp-Exit $kp "$Name-exit")
        }
        Stop-Started
        $script:LogFile = Join-Path $dir 'run.log'
        & $script:Probe dump $db (Join-Path $dir 'dump-after.txt') | Out-Null
        $before = Get-Content (Join-Path $dir 'dump-before.txt')
        $after = Get-Content (Join-Path $dir 'dump-after.txt')
        # Written whether or not the files differ, so that every run has its diff.
        $d = @(Compare-Object $before $after | ForEach-Object { "$($_.SideIndicator) $($_.InputObject)" })
        if ($d.Count -eq 0) { $d = @('no difference') }
        $d | Set-Content (Join-Path $dir 'diff.txt') -Encoding UTF8
        Remove-Item (Join-Path $dir '*.kdbx') -Force -ErrorAction SilentlyContinue
        Say "case $Name done in $([int]((Get-Date) - $t0).TotalSeconds) s"
    }
}

# ------------------------------------------------------------------ cases

. (Join-Path $PSScriptRoot 'Cases.ps1')
