# KeePassXC's window, driven. The settings a run does not name are the release's own defaults for
# saving and reloading (2.8.0's Config.cpp: save after every change, save on exit, reload on
# change, safe saves on, no backup). Its configuration is a file of the run's own, given with --config
# and --localconfig, so that nothing is read from or written to the user's profile; the database
# is unlocked with --pw-stdin. Qt exposes its widgets to UI automation, so they are found by name.
# Dot-sourced after Ui.ps1.

$script:XcExe = $null
$script:XcPassword = 'desk-probe-invented-password'

function Write-XcConfig([string] $Dir, [hashtable] $Options = @{}) {
    $general = [ordered]@{ ConfigVersion = '2'; SingleInstance = 'false'; RememberLastDatabases = 'false'; RememberLastKeyFiles = 'false'
        OpenPreviousDatabasesOnStartup = 'false'; AutoSaveAfterEveryChange = 'true'; AutoReloadOnChange = 'true'; AutoSaveOnExit = 'true'; AutoSaveNonDataChanges = 'true'
        BackupBeforeSave = 'false'; UseAtomicSaves = 'true'; UseDirectWriteSaves = 'false' }
    $gui = [ordered]@{ Language = 'en_US'; ShowTrayIcon = 'false'; CheckForUpdates = 'false'; MinimizeOnStartup = 'false'; ShowExpiredEntriesOnDatabaseUnlock = 'false'; MinimizeOnClose = 'false' }
    $security = [ordered]@{ LockDatabaseIdle = 'false'; LockDatabaseMinimize = 'false'; LockDatabaseScreenLock = 'false'; ClearClipboard = 'false' }
    foreach ($k in $Options.Keys) {
        $s, $n = $k.Split('/')
        switch ($s) { 'General' { $general[$n] = $Options[$k] } 'GUI' { $gui[$n] = $Options[$k] } 'Security' { $security[$n] = $Options[$k] } }
    }
    $lines = @('[General]') + @($general.Keys | ForEach-Object { "$_=$($general[$_])" }) +
        @('', '[GUI]') + @($gui.Keys | ForEach-Object { "$_=$($gui[$_])" }) +
        @('', '[Security]') + @($security.Keys | ForEach-Object { "$_=$($security[$_])" })
    [IO.File]::WriteAllLines((Join-Path $Dir 'keepassxc.ini'), [string[]]$lines)
    [IO.File]::WriteAllLines((Join-Path $Dir 'keepassxc-local.ini'), [string[]]@('[General]', 'ConfigVersion=2'))
    [IO.File]::WriteAllText((Join-Path $Dir 'pw.txt'), $script:XcPassword + "`r`n")
    Say "keepassxc config: $(($Options.Keys | ForEach-Object { "$_=$($Options[$_])" }) -join ', ')"
}

# Starts KeePassXC on the database and unlocks it by typing the password into its unlock view.
# Not with --pw-stdin: a redirected standard input made the launching shell's output pipe an
# inherited handle of KeePassXC, so the launcher waited for KeePassXC to exit. The question
# whether to check for updates, which the release asks at a first start whatever the
# configuration holds, is answered No.
function Start-Xc([string] $Dir, [string] $Db, [string] $Tag) {
    $args = @('--config', "$Dir\keepassxc.ini", '--localconfig', "$Dir\keepassxc-local.ini", '--allow-screencapture', $Db)
    $p = Start-Unelevated -Exe $script:XcExe -Arguments $args -Tag $Tag
    $main = $null
    $deadline = (Get-Date).AddSeconds(40)
    while (-not $main -and (Get-Date) -lt $deadline) {
        foreach ($w in (Get-ProcWindows $p.Id)) {
            try {
                $n = $w.Current.Name
                if ($n -like 'Check for updates*') {
                    $no = Find-El $w -Name 'No' -ControlType $CT::Button -Seconds 2
                    if ($no) { $xy = Center $no; Show-Front $w; Click-At $xy[0] $xy[1]; Say 'answered the update question No'; Start-Sleep -Milliseconds 800 }
                }
                elseif ($n -like '*KeePassXC*') { $main = $w }
            } catch { }
        }
        Start-Sleep -Milliseconds 400
    }
    if (-not $main) { throw 'KeePassXC showed no window' }
    # Maximized, so that every row of the entry list is on the screen.
    [void][DeskNative]::ShowWindow([IntPtr](Get-Handle $main), 3)
    Start-Sleep -Milliseconds 800
    $pw = $null
    $deadline = (Get-Date).AddSeconds(15)
    while (-not $pw -and (Get-Date) -lt $deadline) {
        foreach ($e in (Find-All $main $null $CT::Edit $null)) { try { if ($e.Current.AutomationId -like '*passwordEdit' -and -not $e.Current.IsOffscreen) { $pw = $e; break } } catch { } }
        Start-Sleep -Milliseconds 300
    }
    if (-not $pw) { throw 'no password field in the unlock view' }
    Show-Front $main
    $xy = Center $pw; Click-At $xy[0] $xy[1]; Start-Sleep -Milliseconds 200
    [System.Windows.Forms.SendKeys]::SendWait($script:XcPassword)
    [System.Windows.Forms.SendKeys]::SendWait('{ENTER}')
    $deadline = (Get-Date).AddSeconds(20)
    do { Start-Sleep -Milliseconds 500; $still = @(Find-All $main $null $CT::Edit $null | Where-Object { try { $_.Current.AutomationId -like '*enterPasswordComponent*' -and -not $_.Current.IsOffscreen } catch { $false } }) } while ($still.Count -gt 0 -and (Get-Date) -lt $deadline)
    Start-Sleep -Milliseconds 800
    Say "KeePassXC window: <<$($main.Current.Name)>>"
    [pscustomobject]@{ Process = $p; Main = $main }
}