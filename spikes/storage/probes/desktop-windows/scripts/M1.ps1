<#
.SYNOPSIS
    Measurement 1: KeePass 2.62's window when the file changes underneath an open database.

.DESCRIPTION
    Each case opens a fresh copy of OLD in KeePass's window, makes the window's own changes through
    the real dialogs, replaces the file on disk by the other writer's NEW as a synchronizer does
    (written beside it and renamed over it, its time set later, equal or earlier), and then takes the
    case's way to a save: Ctrl+S, the toolbar's Save button, closing, locking or exiting with save,
    Save As over the same path, File then Synchronize, or nothing at all. Every window KeePass shows
    is read through UI automation and pictured, and answered as the case names; the file is then read
    back with the probe. Takes the desktop for a minute or two per case.

    The window's changes ("full"): P2, Q1, Q2 and Q3 get new passwords through their edit dialogs,
    Q3's OK pressed in the first quarter of a second so that its second is known, and a new entry
    "window-new" is added to Passwords. NEW is then made with Q3's other password at exactly that
    second (an equal time as a file stores it). "p2" changes P2 alone, against NEW-fixed.
#>
param(
    [string] $Work = 'W:\',
    [string[]] $Case = @('all'),
    [int] $Repeat = 1,
    [int] $From = 1,
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
New-Item -ItemType Directory -Force (Join-Path $Work 'logs'), (Join-Path $Work 'runs') | Out-Null
if (-not (Test-Path (Join-Path $base 'OLD.kdbx'))) { & $probe m1base $base | Out-Null }

$syncText = '*changed since it was loaded*'

function Probe([string[]] $a) {
    $out = & $probe @a
    if ($LASTEXITCODE -ne 0) { throw "probe $($a -join ' ') failed" }
    $out
}

function Run-M1([hashtable] $c, [int] $Attempt) {
    $id = "$($c['Id'])-$Attempt"
    $dir = Join-Path $Work "runs\m1\$id"
    if (Test-Path $dir) { Remove-Item -LiteralPath $dir -Recurse -Force }
    New-Item -ItemType Directory -Force $dir | Out-Null
    $script:LogFile = Join-Path $dir 'run.log'
    $script:ShotDir = $dir
    $db = Join-Path $dir 'db.kdbx'
    Copy-Item (Join-Path $base 'OLD.kdbx') $db
    Say "==== $id : $($c['What'])"
    $opts = if ($c['Options']) { $c['Options'] } else { @{} }
    Write-KpConfig $opts
    $kp = Start-Kp $db $id
    try {
        $new = if ($c['New']) { Join-Path $base $c['New'] } else { Join-Path $base 'NEW-fixed.kdbx' }
        $answers = @()
        if ($c['Answers']) { $answers = $c['Answers'] }

        if ($c['Flow'] -eq 'locked-arrival') { Send-To $kp.Main '^l' 1500; Kp-Look $kp 'locked' }

        switch ($c['Edits']) {
            'full' {
                [void](Kp-EditPassword $kp 'Passwords' 'P2' 'p2-window')
                [void](Kp-EditPassword $kp $null 'Q1' 'q1-window')
                [void](Kp-EditPassword $kp $null 'Q2' 'q2-window')
                $t = Kp-EditPassword $kp $null 'Q3' 'q3-window' -Exact
                Kp-AddEntry $kp 'Passwords' 'window-new'
                $q3 = $t[0].ToString('yyyy-MM-ddTHH:mm:ssZ')
                $same = $t[0].ToString('yyyy-MM-ddTHH:mm:ss') -eq $t[1].ToString('yyyy-MM-ddTHH:mm:ss')
                Say "Q3's edit second: $q3 (the press and the dialog's closing in the same second: $same)"
                $new = Join-Path $dir 'NEW-run.kdbx'
                Probe @('m1new', (Join-Path $base 'OLD.kdbx'), $new, $q3) | ForEach-Object { Say "probe: $_" }
            }
            'p2' { [void](Kp-EditPassword $kp 'Passwords' 'P2' 'p2-window') }
            default { }
        }

        Copy-Item $new (Join-Path $dir 'arriving.kdbx')
        Replace-File $db $new $(if ($c['Time']) { $c['Time'] } else { 'later' })
        Start-Sleep -Milliseconds 800

        $known = Get-Handles $kp.Process.Id
        switch ($c['Flow']) {
            'ctrl-s' { Send-To $kp.Main '^s' 300 }
            'toolbar' {
                # A click on the button: invoking it through UI automation held the driver until the
                # dialog it opened was closed.
                $b = Need (Find-El $kp.Main -Name 'Save Database' -ControlType $CT::Button) 'toolbar Save'
                Show-Front $kp.Main
                $xy = Center $b
                Click-At $xy[0] $xy[1]
            }
            'close' { Send-To $kp.Main '^w' 300 }
            'lock' { Send-To $kp.Main '^l' 300 }
            'exit' { Send-To $kp.Main '%{F4}' 300 }
            'saveas' {
                Kp-Menu $kp @('File', 'Save As', 'Save to File')
                [void](Kp-FileDialog $kp $known '*' $db $id)
            }
            'syncmenu' {
                Kp-Menu $kp @('File', 'Synchronize', 'Synchronize with File')
                [void](Kp-FileDialog $kp $known 'Synchronize*' $db $id)
            }
            'focus' {
                $tray = [System.Windows.Automation.AutomationElement]::RootElement.FindFirst($TS::Children,
                    (New-Object System.Windows.Automation.PropertyCondition($AE::ClassNameProperty, 'Shell_TrayWnd')))
                if ($tray) { Show-Front $tray; Start-Sleep -Seconds 3 }
                Show-Front $kp.Main
                Start-Sleep -Seconds 5
            }
            'lockunlock' {
                Send-To $kp.Main '^l' 1500
                Kp-Look $kp 'locked'
                Kp-Unlock $kp $id
                Kp-Look $kp 'unlocked'
                $known = Get-Handles $kp.Process.Id
                [void](Kp-EditPassword $kp 'Passwords' 'P2' 'p2-window')
                Send-To $kp.Main '^s' 300
            }
            'locked-arrival' {
                Kp-Unlock $kp $id
                Kp-Look $kp 'unlocked'
                $known = Get-Handles $kp.Process.Id
                [void](Kp-EditPassword $kp 'Passwords' 'P2' 'p2-window')
                Send-To $kp.Main '^s' 300
            }
            'none' { Start-Sleep -Seconds 2 }
            'edit-after' { [void](Kp-EditPassword $kp 'Passwords' 'P2' 'p2-window') }
            default { throw "unknown flow $($c['Flow'])" }
        }
        $seen = @(Kp-Dialogs $kp $known $id $answers 6)
        Say "dialogs after the flow: $($seen.Count)"
        Start-Sleep -Seconds 1
        if (-not $kp.Process.HasExited) { Kp-Look $kp 'after' }
        Say "file after the flow: $(Get-FileState $db)"
        if (-not $kp.Process.HasExited) {
            $closing = @(Kp-Exit $kp "$id-exit")
            Say "dialogs at the exit (answered Discard): $($closing.Count)"
        }
        Say "file after the exit: $(Get-FileState $db)"
    }
    finally { Stop-Started }

    Probe @('dump', $db, (Join-Path $dir 'db.dump.txt')) | Out-Null
    Probe @('dump', $new, (Join-Path $dir 'arriving.dump.txt')) | Out-Null
    $v = Probe @('verdict', $db, $new)
    $v | ForEach-Object { Say "verdict: $_" }
    Add-Content (Join-Path $Work 'logs\m1-verdicts.txt') "$id | $($v[0])" -Encoding UTF8
}

$sync = @($syncText, 'Synchronize')
$over = @($syncText, 'Overwrite')
$cancel = @($syncText, 'Cancel')
$saveq = '*Save database changes before*'
$cases = @(
    @{ Id = 'a-sync'; What = 'full edits, NEW later, Ctrl+S, Synchronize'; Edits = 'full'; Flow = 'ctrl-s'; Answers = $sync }
    @{ Id = 'a-over'; What = 'full edits, NEW later, Ctrl+S, Overwrite'; Edits = 'full'; Flow = 'ctrl-s'; Answers = $over }
    @{ Id = 'a-cancel'; What = 'full edits, NEW later, Ctrl+S, Cancel'; Edits = 'full'; Flow = 'ctrl-s'; Answers = $cancel }
    @{ Id = 't-equal'; What = 'P2, NEW-fixed at the old time, Ctrl+S, Overwrite'; Edits = 'p2'; Flow = 'ctrl-s'; Time = 'equal'; Answers = $over }
    @{ Id = 't-earlier'; What = 'P2, NEW-fixed an hour earlier, Ctrl+S, Overwrite'; Edits = 'p2'; Flow = 'ctrl-s'; Time = 'earlier'; Answers = $over }
    @{ Id = 't-samelen-equal'; What = 'P2, NEW-same (OLD length), equal time, Ctrl+S, Overwrite'; Edits = 'p2'; Flow = 'ctrl-s'; New = 'NEW-same.kdbx'; Time = 'equal'; Answers = $over }
    @{ Id = 't-samelen-later'; What = 'P2, NEW-same, later, Ctrl+S, Synchronize'; Edits = 'p2'; Flow = 'ctrl-s'; New = 'NEW-same.kdbx'; Answers = $sync }
    @{ Id = 't-ident'; What = 'P2, OLD own bytes later (control), Ctrl+S'; Edits = 'p2'; Flow = 'ctrl-s'; New = 'NEW-ident.kdbx'; Answers = $sync }
    @{ Id = 's-toolbar'; What = 'P2, toolbar Save, Synchronize'; Edits = 'p2'; Flow = 'toolbar'; Answers = $sync }
    @{ Id = 's-close'; What = 'P2, Ctrl+W, Save, Synchronize'; Edits = 'p2'; Flow = 'close'; Answers = @($saveq, 'Save', $syncText, 'Synchronize') }
    @{ Id = 's-lock'; What = 'P2, Ctrl+L, Save, Overwrite'; Edits = 'p2'; Flow = 'lock'; Answers = @($saveq, 'Save', $syncText, 'Overwrite') }
    @{ Id = 's-exit'; What = 'P2, Alt+F4, Save, Synchronize'; Edits = 'p2'; Flow = 'exit'; Answers = @($saveq, 'Save', $syncText, 'Synchronize') }
    @{ Id = 's-saveas'; What = 'P2, Save As over the same path'; Edits = 'p2'; Flow = 'saveas'; Answers = @('*already exists*', 'Yes', '*replace*', 'Yes', $syncText, 'Synchronize') }
    @{ Id = 'n-focus'; What = 'nothing changed, focus away and back'; Edits = 'none'; Flow = 'focus' }
    @{ Id = 'n-lockunlock'; What = 'nothing changed, lock and unlock, then P2 and Ctrl+S'; Edits = 'none'; Flow = 'lockunlock'; Answers = $sync }
    @{ Id = 'n-syncmenu'; What = 'nothing changed, File Synchronize with the same file'; Edits = 'none'; Flow = 'syncmenu'; Answers = $sync }
    @{ Id = 'n-ctrls'; What = 'nothing changed, Ctrl+S, Overwrite'; Edits = 'none'; Flow = 'ctrl-s'; Answers = $over }
    @{ Id = 'n-lockedarrival'; What = 'locked before the replacement, unlocked after, then P2 and Ctrl+S'; Edits = 'none'; Flow = 'locked-arrival'; Answers = $sync }
    @{ Id = 'o-forcesync'; What = 'SaveForceSync on, P2, Ctrl+S'; Edits = 'p2'; Flow = 'ctrl-s'; Options = @{ 'Application/SaveForceSync' = 'true' }; Answers = $over }
    @{ Id = 'o-autosave-lock'; What = 'FileClosing AutoSave on, P2, Ctrl+L'; Edits = 'p2'; Flow = 'lock'; Options = @{ 'Application/FileClosing/AutoSave' = 'true' }; Answers = $sync }
    @{ Id = 'o-autosave-edit'; What = 'AutoSaveAfterEntryEdit on, replacement first, then P2'; Edits = 'none'; Flow = 'edit-after'; Options = @{ 'Application/AutoSaveAfterEntryEdit' = 'true' }; Answers = $sync }
    @{ Id = 'o-notx'; What = 'UseTransactedFileWrites off, P2, Ctrl+S, Overwrite'; Edits = 'p2'; Flow = 'ctrl-s'; Options = @{ 'Application/UseTransactedFileWrites' = 'false' }; Answers = $over }
    @{ Id = 'o-txextra'; What = 'FileTxExtra on, P2, Ctrl+S, Synchronize'; Edits = 'p2'; Flow = 'ctrl-s'; Options = @{ 'Application/FileTxExtra' = 'true' }; Answers = $sync }
    @{ Id = 'o-locks'; What = 'UseFileLocks on, P2, Ctrl+S, Synchronize'; Edits = 'p2'; Flow = 'ctrl-s'; Options = @{ 'Application/UseFileLocks' = 'true' }; Answers = $sync }
    @{ Id = 'o-noverify'; What = 'VerifyWrittenFileAfterSaving off, P2, Ctrl+S, Overwrite'; Edits = 'p2'; Flow = 'ctrl-s'; Options = @{ 'Application/VerifyWrittenFileAfterSaving' = 'false' }; Answers = $over }
)

foreach ($c in $cases) {
    if ($Case -notcontains 'all' -and $Case -notcontains $c['Id']) { continue }
    for ($r = $From; $r -lt $From + $Repeat; $r++) {
        try { Run-M1 $c $r }
        catch {
            Say "RUN FAILED $($c['Id'])-${r}: $($_.Exception.Message) $($_.ScriptStackTrace)"
            Add-Content (Join-Path $Work 'logs\m1-verdicts.txt') "$($c['Id'])-$r | FAILED: $($_.Exception.Message)" -Encoding UTF8
            Stop-Started
            # A run whose flow failed is kept apart and repeated, never read.
            $failed = Join-Path $Work "runs\m1\$($c['Id'])-$r"
            if (Test-Path $failed) { Move-Item $failed (Join-Path $Work "runs\m1-not-counted\$($c['Id'])-$r-$((Get-Date).ToString('HHmmss'))") -Force }
        }
    }
}
