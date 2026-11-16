<#
.SYNOPSIS
    Measurement 5: what a plugin can observe, and when, around KeePass's own synchronize, import
    and save.

.DESCRIPTION
    Runs KeePass 2.62 with the logging plugin DeskLog (plugin\DeskLog), each case from a fresh
    start, over two copies whose difference the plugin's fingerprint shows: db.kdbx is OLD (9
    entries, 1 deletion record, 2 entries in KeePassGate) and other.kdbx is NEW-fixed (10, 3 and 3).
    The plugin writes a line per trigger event and per file event of the main window into
    events.log in the case's directory; with the marker switch it adds one entry at the first
    FileSavingPre of a save that comes inside a synchronize. Both files are read back afterwards.
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
$script:KpExe = Join-Path $Work 'kp-log\KeePass.exe'
$pluginDir = Join-Path $Work 'kp-log\Plugins\DeskLog'
$base = Join-Path $Work 'base'
New-Item -ItemType Directory -Force (Join-Path $Work 'logs'), (Join-Path $Work 'runs\m5') | Out-Null
if (-not (Test-Path (Join-Path $base 'OLD.kdbx'))) { & $probe m1base $base | Out-Null }

function Probe([string[]] $a) {
    $out = & $probe @a
    if ($LASTEXITCODE -ne 0) { throw "probe $($a -join ' ') failed" }
    $out
}

function B64([byte[]] $b) { [Convert]::ToBase64String($b) }

# A trigger that synchronizes the active database with the file given when KeePass has started.
function Trigger-Xml([string] $Path) {
    $appLoadPost = B64 ([byte[]](0xD8, 0xF3, 0x1E, 0xE9, 0xCC, 0x69, 0x48, 0x1B, 0x89, 0xC5, 0xFC, 0xE2, 0xEA, 0x4B, 0x6A, 0x97))
    $syncAction = B64 ([byte[]](0x22, 0xAD, 0x77, 0xE4, 0x17, 0x78, 0x4E, 0xED, 0x99, 0xB4, 0x57, 0x1D, 0x02, 0xB3, 0xAD, 0x4D))
    $guid = B64 ([Guid]::NewGuid().ToByteArray())
    "<Trigger><Guid>$guid</Guid><Name>Desk probe synchronize</Name><Events><Event><TypeGuid>$appLoadPost</TypeGuid><Parameters /></Event></Events><Conditions /><Actions><Action><TypeGuid>$syncAction</TypeGuid><Parameters><Parameter>$Path</Parameter><Parameter /><Parameter /><Parameter>false</Parameter><Parameter>false</Parameter></Parameters></Action></Actions></Trigger>"
}

function Run-M5([hashtable] $c, [int] $Attempt) {
    $id = "$($c['Id'])-$Attempt"
    $dir = Join-Path $Work "runs\m5\$id"
    if (Test-Path $dir) { Remove-Item -LiteralPath $dir -Recurse -Force }
    New-Item -ItemType Directory -Force $dir | Out-Null
    $script:LogFile = Join-Path $dir 'run.log'
    $script:ShotDir = $dir
    $db = Join-Path $dir 'db.kdbx'
    $other = Join-Path $dir 'other.kdbx'
    $events = Join-Path $dir 'events.log'
    if ($c['Open'] -eq 'merged') {
        Probe @('sync', (Join-Path $base 'OLD.kdbx'), (Join-Path $base 'NEW-fixed.kdbx'), $db) | ForEach-Object { Say "probe: $_" }
    } else { Copy-Item (Join-Path $base 'OLD.kdbx') $db }
    Copy-Item (Join-Path $base 'NEW-fixed.kdbx') $other
    Say "==== $id : $($c['What'])"
    Say "db: $(Get-FileState $db); other: $(Get-FileState $other)"
    Set-Content (Join-Path $pluginDir 'DeskLog.ini') @("log=$events", "marker=$(if ($c['Marker']) { 'true' } else { 'false' })") -Encoding ASCII
    # KeePass 2.62 moves an enabled trigger of a non-enforced configuration into
    # KeePass.config.enforced.xml beside the executable at its start (Program.cs:656-662, read), and
    # that file outlives the run: it is taken away before every run and after the trigger case.
    $enforced = Join-Path (Split-Path -Parent $script:KpExe) 'KeePass.config.enforced.xml'
    if (Test-Path $enforced) { Remove-Item $enforced -Force; Say 'removed an enforced configuration left by an earlier run' }
    $opts = @{}
    if ($c['NoTriggers']) { $opts['Application/TriggerSystem/Enabled'] = 'false' }
    Write-KpConfig $opts
    if ($c['Flow'] -eq 'trigger') {
        $cfg = Join-Path (Split-Path -Parent $script:KpExe) 'KeePass.config.xml'
        $x = Get-Content $cfg -Raw
        $x = $x.Replace('<Application>', "<Application><TriggerSystem><Enabled>true</Enabled><Triggers>$(Trigger-Xml $other)</Triggers></TriggerSystem>")
        Set-Content $cfg $x -Encoding UTF8
        Say 'config: a trigger at "Application started and ready" synchronizing with other.kdbx'
    }
    $kp = Start-Kp $db $id
    try {
        $known = Get-Handles $kp.Process.Id
        $answers = @('*changed since it was loaded*', 'Synchronize')
        switch ($c['Flow']) {
            'syncfile' {
                Kp-Menu $kp @('File', 'Synchronize', 'Synchronize with File')
                [void](Kp-FileDialog $kp $known 'Synchronize*' $other $id)
            }
            'syncurl' {
                Kp-Menu $kp @('File', 'Synchronize', 'Synchronize with URL')
                $dlg = Need (Wait-Window $kp.Process.Id '*URL*' 10 $known) 'URL dialog'
                [void](Read-Window $dlg "$id-url")
                Save-Shot $dlg "$id-url"
                $url = 'file:///' + $other.Replace('\', '/')
                $box = Find-Kid (Get-Handle $dlg) $null 'EDIT'
                Type-Hwnd $box.H $url
                Say "typed URL $url"
                [void](Click-Kid (Get-Handle $dlg) 'OK')
            }
            'import' {
                Kp-Menu $kp @('File', 'Import')
                $dlg = Need (Wait-Window $kp.Process.Id 'Import*' 10 $known) 'import dialog'
                Kp-ImportKdbx $kp $dlg $other $c['Method'] $id
            }
            'savechanged' {
                [void](Kp-EditPassword $kp 'Passwords' 'P2' 'p2-window')
                Replace-File $db $other 'later'
                $known = Get-Handles $kp.Process.Id
                Send-To $kp.Main '^s' 300
            }
            'trigger' { Start-Sleep -Seconds 3 }
            'open' { Start-Sleep -Seconds 2 }
        }
        $seen = @(Kp-Dialogs $kp $known $id $answers 6)
        Say "dialogs after the flow: $($seen.Count)"
        Start-Sleep -Seconds 1
        Kp-Look $kp 'after'
        Say "db after the flow: $(Get-FileState $db); other: $(Get-FileState $other)"
        $closing = @(Kp-Exit $kp "$id-exit")
        Say "dialogs at the exit (answered Discard): $($closing.Count)"
    }
    finally {
        Stop-Started
        if (Test-Path $enforced) { Copy-Item $enforced (Join-Path $dir 'KeePass.config.enforced.xml'); Remove-Item $enforced -Force; Say 'KeePass had written KeePass.config.enforced.xml: kept in the run directory and removed' }
    }
    Say "db after the exit: $(Get-FileState $db); other: $(Get-FileState $other)"
    Probe @('dump', $db, (Join-Path $dir 'db.dump.txt')) | Out-Null
    Probe @('dump', $other, (Join-Path $dir 'other.dump.txt')) | Out-Null
    $marker = @(Select-String -Path (Join-Path $dir 'db.dump.txt'), (Join-Path $dir 'other.dump.txt') -Pattern 'KPG marker' | ForEach-Object { Split-Path -Leaf $_.Path })
    Say "marker entry in: $(if ($marker.Count) { $marker -join ', ' } else { 'neither file' })"
    if (Test-Path $events) { Get-Content $events | ForEach-Object { Say "event: $_" } }
    Add-Content (Join-Path $Work 'logs\m5-summary.txt') "$id | marker in [$($marker -join ',')] | $((Probe @('verdict', $db, $other)) -join ' || ')" -Encoding UTF8
}

$cases = @(
    @{ Id = 'sync-file'; What = 'File, Synchronize, Synchronize with File'; Flow = 'syncfile' }
    @{ Id = 'sync-file-marker'; What = 'the same, the marker switch on'; Flow = 'syncfile'; Marker = $true }
    @{ Id = 'sync-file-notrig'; What = 'the same with the trigger system off, the marker switch on'; Flow = 'syncfile'; Marker = $true; NoTriggers = $true }
    @{ Id = 'import-CreateNewUuids'; What = 'File, Import, KDBX, Create new IDs'; Flow = 'import'; Method = 'CreateNewUuids'; Marker = $true }
    @{ Id = 'import-KeepExisting'; What = 'File, Import, KDBX, Keep existing'; Flow = 'import'; Method = 'KeepExisting'; Marker = $true }
    @{ Id = 'import-OverwriteExisting'; What = 'File, Import, KDBX, Overwrite existing'; Flow = 'import'; Method = 'OverwriteExisting'; Marker = $true }
    @{ Id = 'import-OverwriteIfNewer'; What = 'File, Import, KDBX, Overwrite if newer'; Flow = 'import'; Method = 'OverwriteIfNewer'; Marker = $true }
    @{ Id = 'import-Synchronize'; What = 'File, Import, KDBX, Overwrite if newer and apply deletions'; Flow = 'import'; Method = 'Synchronize'; Marker = $true }
    @{ Id = 'save-changed'; What = 'P2 edited, the file replaced, Ctrl+S, Synchronize'; Flow = 'savechanged' }
    @{ Id = 'save-changed-marker'; What = 'the same, the marker switch on'; Flow = 'savechanged'; Marker = $true }
    @{ Id = 'save-changed-notrig'; What = 'the same with the trigger system off, the marker switch on'; Flow = 'savechanged'; Marker = $true; NoTriggers = $true }
    @{ Id = 'trigger-sync'; What = 'a trigger action synchronizing at start, the marker switch on'; Flow = 'trigger'; Marker = $true }
    @{ Id = 'sync-url'; What = 'File, Synchronize, Synchronize with URL, a file:// URL, the marker switch on'; Flow = 'syncurl'; Marker = $true }
    @{ Id = 'open-merged'; What = 'opening a file another program merged'; Flow = 'open'; Open = 'merged' }
)

foreach ($c in $cases) {
    if ($Case -notcontains 'all' -and $Case -notcontains $c['Id']) { continue }
    for ($r = 1; $r -le $Repeat; $r++) {
        try { Run-M5 $c $r }
        catch {
            Say "RUN FAILED $($c['Id'])-${r}: $($_.Exception.Message) $($_.ScriptStackTrace)"
            Add-Content (Join-Path $Work 'logs\m5-summary.txt') "$($c['Id'])-$r | FAILED: $($_.Exception.Message)" -Encoding UTF8
            Stop-Started
            # A run whose flow failed is kept apart and repeated, never read.
            $failed = Join-Path $Work "runs\m5\$($c['Id'])-$r"
            if (Test-Path $failed) { Move-Item $failed (Join-Path $Work "runs\m5-not-counted\$($c['Id'])-$r-$((Get-Date).ToString('HHmmss'))") -Force }
        }
    }
}
