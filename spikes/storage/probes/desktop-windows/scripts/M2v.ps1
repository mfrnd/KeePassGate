<#
.SYNOPSIS
    What a person sees of the store and of a plugin string in KeePassXC 2.8.0's window: the search
    box against each token, the entry preview of a user's entry and of a store entry, and global
    auto-type with a target window titled as a store entry and as a user's entry.

.DESCRIPTION
    On a fresh copy of M3.kdbx (probe `m3base`). Global auto-type needs a hot key, which the
    release sets none of by default: Ctrl+Alt+A is set in the run's own configuration.
#>
param(
    [string] $Work = 'W:\',
    [string[]] $Case = @('all'),
    [string] $Unelevated = (Join-Path $env:TEMP 'unelevated.ps1')
)

$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'Ui.ps1')
. (Join-Path $PSScriptRoot 'Xc.ps1')
$family = Split-Path -Parent $PSScriptRoot
$probe = Join-Path $family 'probe\out\Desk.exe'
$target = Join-Path $family 'target\out\DeskTarget.exe'
$script:WorkRoot = $Work
$script:UnelevatedScript = $Unelevated
$script:XcExe = Join-Path $Work 'kpxc-2.8.0\KeePassXC.exe'
$base = Join-Path $Work 'base'
$dir = Join-Path $Work 'runs\m2v\look-1'
if (Test-Path $dir) { Remove-Item -LiteralPath $dir -Recurse -Force }
New-Item -ItemType Directory -Force $dir, (Join-Path $Work 'logs') | Out-Null
$script:LogFile = Join-Path $dir 'run.log'
$script:ShotDir = $dir
$db = Join-Path $dir 'db.kdbx'
Copy-Item (Join-Path $base 'M3.kdbx') $db
function Summary([string] $Line) { Add-Content (Join-Path $Work 'logs\m2v-summary.txt') $Line -Encoding UTF8 }

function Shown($xc) {
    $names = @()
    foreach ($t in 'DataItem', 'ListItem', 'TreeItem') {
        foreach ($e in (Find-All $xc.Main $null ([System.Windows.Automation.ControlType]::$t) $null)) {
            try { if (-not $e.Current.IsOffscreen -and $e.Current.AutomationId -like '*entryView*') { $names += $e.Current.Name } } catch { }
        }
    }
    @($names | Where-Object { $_ } | Select-Object -Unique)
}

# Qt's key code of A and the control and alt modifiers, as KeePassXC stores its global hot key.
Write-XcConfig $dir @{ 'General/AutoSaveAfterEveryChange' = 'false'; 'General/GlobalAutoTypeKey' = '65'; 'General/GlobalAutoTypeModifiers' = '201326592' }
$xc = Start-Xc $dir $db 'm2v-look'
try {
    $search = $null
    foreach ($e in (Find-All $xc.Main $null $CT::Edit $null)) { try { if ($e.Current.AutomationId -like '*searchEdit*' -or $e.Current.Name -like '*earch*') { $search = $e; break } } catch { } }
    if (-not $search) { throw 'no search box' }
    foreach ($t in 'zqxref-u1', 'zqxref-u2-protected', 'zqxstore-grant', 'zqxstore-secret', 'KPG grant', 'Mail account') {
        $xy = Center $search; Show-Front $xc.Main; Click-At $xy[0] $xy[1]
        [System.Windows.Forms.SendKeys]::SendWait('^a'); [System.Windows.Forms.SendKeys]::SendWait($t); Start-Sleep -Seconds 2
        $found = (Shown $xc) -join ', '
        Say "search <<$t>>: [$found]"
        Summary "search | $t | [$found]"
        if ($t -eq 'zqxstore-grant' -or $t -eq 'zqxref-u1') { Save-Shot $xc.Main ("search-" + $t) }
    }
    $xy = Center $search; Click-At $xy[0] $xy[1]; [System.Windows.Forms.SendKeys]::SendWait('^a{DEL}{ESC}'); Start-Sleep 1
    # The preview of a user's entry and of a store entry, its Advanced tab.
    foreach ($pair in @(@('Passwords', 'Mail account'), @('Passwords', 'Bank'), @('KeePassGate', 'KPG grant 1'))) {
        foreach ($t in 'TreeItem') { foreach ($e in (Find-All $xc.Main $pair[0] ([System.Windows.Automation.ControlType]::$t) $null)) { $xy = Center $e; Click-At $xy[0] $xy[1]; break } }
        Start-Sleep 1
        $row = $null
        foreach ($t in 'DataItem', 'ListItem', 'TreeItem') { foreach ($e in (Find-All $xc.Main $pair[1] ([System.Windows.Automation.ControlType]::$t) $null)) { if (-not $e.Current.IsOffscreen) { $row = $e } } }
        Say "row $($pair[1]) in $($pair[0]): $([bool]$row)"
        Summary "list | $($pair[0]) | $((Shown $xc) -join ', ')"
        if (-not $row) { continue }
        $xy = Center $row; Click-At $xy[0] $xy[1]; Start-Sleep 1
        $adv = Find-El $xc.Main -Name 'Advanced' -ControlType $CT::TabItem -Seconds 2
        if ($adv) { $xy = Center $adv; Click-At $xy[0] $xy[1]; Start-Sleep 1 }
        $texts = @(Find-All $xc.Main $null $null $null | Where-Object { try { $_.Current.AutomationId -like '*entryPreviewWidget*' -and -not $_.Current.IsOffscreen } catch { $false } } | ForEach-Object { try { ($_.Current.Name + ' ' + (Get-Text $_)).Trim() } catch { '' } } | Where-Object { $_ })
        Summary "preview | $($pair[1]) | $(($texts | Select-Object -Unique) -join ' / ')"
        Save-Shot $xc.Main ("preview-" + ($pair[1] -replace '\W', ''))
    }
    # Global auto-type into a target window of the probe's own.
    $copy = Join-Path $Work 'target\DeskTarget.exe'
    if (-not (Test-Path $copy)) { New-Item -ItemType Directory -Force (Split-Path $copy) | Out-Null; Copy-Item $target $copy }
    foreach ($title in 'KPG grant 1', 'Mail account') {
        $out = Join-Path $dir ("typed-global-" + ($title -replace '\W', '') + '.txt')
        [IO.File]::WriteAllText("$out.title", $title)
        $tp = Start-Unelevated -Exe $copy -Arguments @(('@' + "$out.title"), $out) -Tag ('xctarget-' + ($title -replace '\W', ''))
        $tw = Need (Wait-Window $tp.Id $title 15) "target $title"
        Show-Front $tw; Start-Sleep -Milliseconds 600
        $known = Get-Handles $xc.Process.Id
        [System.Windows.Forms.SendKeys]::SendWait('^%a')
        Start-Sleep -Seconds 4
        $w = Wait-NewWindow $xc.Process.Id $known 3
        if ($w) { $r = Read-Window $w ("global-" + ($title -replace '\W', '')); Save-Shot $w ("global-" + ($title -replace '\W', '')); Summary "autotype | global, target <<$title>> | KeePassXC showed <<$($r.Title)>> $(($r.Texts | Select-Object -First 4) -join ' / ')"; Show-Front $w; [System.Windows.Forms.SendKeys]::SendWait('{ESC}') }
        Start-Sleep 1
        Summary "autotype | global, target <<$title>> | typed: <<$((Get-Content $out -Raw -ErrorAction SilentlyContinue) -replace "`r", '\r' -replace "`n", '\n' -replace "`t", '\t')>>"
        Stop-Process -Id $tp.Id -Force
    }
}
finally { Stop-Started }
