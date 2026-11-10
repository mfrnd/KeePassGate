<#
.SYNOPSIS
    Changes the key derivation's rounds in KeePassXC's own window by UI automation, and writes what
    the saved file holds to `spikes/revocation/output/keepassxc-kdf.txt`.

.DESCRIPTION
    No `keepassxc-cli` among the pinned releases changes a database's KDF: `db-edit`, where it
    exists, sets credentials only. So this drives the program itself, on KeePassXC 2.7.12 and 2.8.0,
    over a fresh copy each of `P-plain-fast` and `P-moved-fast`: Database, Database Security,
    Security, Encryption Settings, Advanced, transform rounds from 2 to 3, OK. Autosave after every
    change is on in a configuration file of its own, so OK is also the save; the program is then
    stopped and the file read back by the probe, as `Measure-KeePassXC.ps1` reads its own.

    **Optional, and it takes the desktop.** Windows appear and are driven for a minute or two, and
    nothing else should use the desktop meanwhile. KeePassXC starts with its own configuration under
    the work directory: English, no tray icon, no update check, no single instance, no lock on idle,
    nothing remembered. Every process it starts is stopped before it returns, whatever happened.

    **What is recorded** is each step's answer that a re-run should repeat: the menu item it found,
    the rounds before and after, the format offered, and whether a save was still pending after OK.
    A start that shows no window within thirty seconds is stopped and tried again, up to `-Attempts`
    times, because KeePassXC 2.7.12 sometimes exits at startup without a window. How many attempts
    that took goes to the console, not into the output.

.PARAMETER Work
    Where the throwaway databases and KeePassXC's configuration go. Its `kdf` directory is emptied
    first.

.PARAMETER Release
    The KeePassXC releases to drive, 2.7.12 and 2.8.0 by default.

.PARAMETER Attempts
    How many starts a release gets before the run gives up on it.

.PARAMETER Output
    Where `keepassxc-kdf.txt` goes, `spikes/revocation/output` by default.

.EXAMPLE
    powershell.exe -NoProfile -File spikes\revocation\Measure-KeePassXCKdf.ps1 -Work work\keepassxc
#>
param(
    [Parameter(Mandatory)] [string] $Work,
    [string[]] $Release = @('2.7.12', '2.8.0'),
    [int] $Attempts = 3,
    [string] $Output
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

# Not a default in the parameter block: there, in an advanced script, Windows PowerShell 5.1 reads
# `$PSScriptRoot` as empty.
if (-not $Output) {
    $Output = Join-Path $PSScriptRoot 'output'
}

if ($PSVersionTable.PSEdition -ne 'Desktop') {
    throw 'Run this under Windows PowerShell 5.1, powershell.exe: UI automation is a .NET Framework library.'
}

# `powershell.exe -File` passes `a,b` as one string rather than a list.
$Release = @($Release | ForEach-Object { $_ -split ',' })

Add-Type -AssemblyName UIAutomationClient, UIAutomationTypes

$root = Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
. (Join-Path $root 'tools\KeePassXC.ps1')

$probe = Join-Path $PSScriptRoot 'out\Probe.exe'
if (-not (Test-Path $probe)) {
    throw "No probe at $probe. Build it first: dotnet build spikes\revocation -c Release -o spikes\revocation\out"
}

New-Item -ItemType Directory -Force -Path $Output | Out-Null
$Output = (Resolve-Path $Output).Path
New-Item -ItemType Directory -Force -Path $Work | Out-Null
$Work = (Resolve-Path $Work).Path

# The probe's own invented password, which every database it makes is locked with.
$password = 'probe-only'
$rounds = 3

$AE = [System.Windows.Automation.AutomationElement]
$Type = [System.Windows.Automation.ControlType]

function Find-All($Element, [string] $Name, $ControlType) {
    $conditions = @()
    if ($Name) { $conditions += New-Object System.Windows.Automation.PropertyCondition($AE::NameProperty, $Name) }
    if ($ControlType) { $conditions += New-Object System.Windows.Automation.PropertyCondition($AE::ControlTypeProperty, $ControlType) }
    if ($conditions.Count -eq 0) { $condition = [System.Windows.Automation.Condition]::TrueCondition }
    elseif ($conditions.Count -eq 1) { $condition = $conditions[0] }
    else { $condition = New-Object System.Windows.Automation.AndCondition($conditions) }
    $Element.FindAll([System.Windows.Automation.TreeScope]::Descendants, $condition)
}

function Find-One($Element, [string] $Name, $ControlType, [int] $Seconds = 10) {
    $deadline = (Get-Date).AddSeconds($Seconds)
    while ((Get-Date) -lt $deadline) {
        $found = @(Find-All $Element $Name $ControlType)
        if ($found.Count -gt 0) { return $found[0] }
        Start-Sleep -Milliseconds 250
    }
    $null
}

function Find-Prefix($Element, [string] $Prefix, $ControlType, [int] $Seconds = 10) {
    $deadline = (Get-Date).AddSeconds($Seconds)
    while ((Get-Date) -lt $deadline) {
        foreach ($candidate in (Find-All $Element $null $ControlType)) {
            if ($candidate.Current.Name.StartsWith($Prefix)) { return $candidate }
        }
        Start-Sleep -Milliseconds 250
    }
    $null
}

function Find-Window([System.Diagnostics.Process] $Process, [int] $Seconds) {
    $deadline = (Get-Date).AddSeconds($Seconds)
    $condition = New-Object System.Windows.Automation.PropertyCondition($AE::ProcessIdProperty, $Process.Id)
    while ((Get-Date) -lt $deadline -and -not $Process.HasExited) {
        foreach ($window in $AE::RootElement.FindAll([System.Windows.Automation.TreeScope]::Children, $condition)) {
            if ($window.Current.Name -like '*KeePassXC*') { return $window }
        }
        Start-Sleep -Milliseconds 300
    }
    $null
}

function Invoke-Element($Element) {
    $pattern = $null
    if ($Element.TryGetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern, [ref] $pattern)) { $pattern.Invoke(); return }
    if ($Element.TryGetCurrentPattern([System.Windows.Automation.SelectionItemPattern]::Pattern, [ref] $pattern)) { $pattern.Select(); return }
    if ($Element.TryGetCurrentPattern([System.Windows.Automation.ExpandCollapsePattern]::Pattern, [ref] $pattern)) { $pattern.Expand(); return }
    throw "Nothing to press on '$($Element.Current.Name)'."
}

function Get-Value($Element) {
    $pattern = $null
    if ($Element -and $Element.TryGetCurrentPattern([System.Windows.Automation.RangeValuePattern]::Pattern, [ref] $pattern)) { return $pattern.Current.Value }
    if ($Element -and $Element.TryGetCurrentPattern([System.Windows.Automation.ValuePattern]::Pattern, [ref] $pattern)) { return $pattern.Current.Value }
    'absent'
}

function Require($Element, [string] $What) {
    if (-not $Element) { throw "KeePassXC showed no $What." }
    $Element
}

# Every step that should come out the same on a re-run, in the order it was taken.
function Set-Rounds($Main) {
    $steps = New-Object System.Collections.Generic.List[string]

    $question = Find-One $Main 'Check for updates on startup?' $Type::Window 5
    if ($question) {
        Invoke-Element (Require (Find-One $question 'No' $Type::Button 5) 'No on the update question')
        Write-Host '    answered the update question' -ForegroundColor DarkGray
        Start-Sleep -Milliseconds 500
    }

    $menu = Require (Find-One $Main 'Database' $Type::MenuItem) 'Database menu'
    $menu.GetCurrentPattern([System.Windows.Automation.ExpandCollapsePattern]::Pattern).Expand()
    Start-Sleep -Milliseconds 700
    $item = Find-Prefix $Main 'Database Security' $Type::MenuItem
    if (-not $item) { $item = Find-Prefix $Main 'Database Settings' $Type::MenuItem }
    $item = Require $item 'Database Security or Database Settings menu item'
    $steps.Add("menu item: $($item.Current.Name)")
    Invoke-Element $item
    Start-Sleep -Seconds 1

    $category = Find-One $Main 'Security' $Type::ListItem 3
    if ($category) { Invoke-Element $category; Start-Sleep -Milliseconds 500 }
    Invoke-Element (Require (Find-One $Main 'Encryption Settings' $Type::TabItem 5) 'Encryption Settings tab')
    Start-Sleep -Milliseconds 500
    Invoke-Element (Require (Find-One $Main 'Advanced' $Type::TabItem 5) 'Advanced tab')
    Start-Sleep -Milliseconds 500

    $steps.Add("key derivation: $(Get-Value (Find-One $Main 'Key derivation function' $Type::ComboBox 5))")
    $spinner = Require (Find-One $Main 'Transform rounds' $Type::Spinner 5) 'Transform rounds spinner'
    $before = Get-Value $spinner
    $pattern = $null
    if ($spinner.TryGetCurrentPattern([System.Windows.Automation.RangeValuePattern]::Pattern, [ref] $pattern)) {
        $pattern.SetValue($rounds)
    }
    elseif ($spinner.TryGetCurrentPattern([System.Windows.Automation.ValuePattern]::Pattern, [ref] $pattern)) {
        $pattern.SetValue([string] $rounds)
    }
    else {
        throw 'The Transform rounds spinner takes no value.'
    }
    Start-Sleep -Milliseconds 500
    $steps.Add("transform rounds: $before, then $(Get-Value $spinner)")
    $steps.Add("memory usage: $(Get-Value (Find-One $Main 'Memory usage' $Type::Spinner 2)), parallelism: $(Get-Value (Find-One $Main 'Parallelism' $Type::Spinner 2))")
    $format = Find-One $Main 'Database format' $Type::ComboBox 2
    if ($format) { $steps.Add("database format: $(Get-Value $format), enabled $($format.Current.IsEnabled)") }

    Invoke-Element (Require (Find-One $Main 'OK' $Type::Button 5) 'OK button')
    Start-Sleep -Seconds 2
    $dialogs = @(Find-All $Main $null $Type::Window | ForEach-Object { $_.Current.Name })
    $steps.Add('windows after OK: ' + $(if ($dialogs.Count -eq 0) { 'none' } else { $dialogs -join ', ' }))
    $save = Require (Find-One $Main 'Save Database' $Type::Button 2) 'Save Database button'
    $steps.Add("Save Database after OK: enabled $($save.Current.IsEnabled)")
    if ($save.Current.IsEnabled) {
        Invoke-Element $save
        Start-Sleep -Seconds 2
        $steps.Add('pressed Save Database')
    }
    $steps
}

$configuration = @(
    '[General]', 'ConfigVersion=2', 'SingleInstance=false', 'RememberLastDatabases=false',
    'RememberLastKeyFiles=false', 'OpenPreviousDatabasesOnStartup=false', 'AutoSaveAfterEveryChange=true',
    'AutoReloadOnChange=false', 'BackupBeforeSave=false',
    '[GUI]', 'Language=en_US', 'ShowTrayIcon=false', 'CheckForUpdates=false',
    'ShowExpiredEntriesOnDatabaseUnlock=false', 'MinimizeOnStartup=false',
    '[Security]', 'LockDatabaseIdle=false'
)

$directory = Join-Path $Work 'kdf'
if (Test-Path $directory) { Remove-Item -LiteralPath $directory -Recurse -Force }
$base = Join-Path $directory 'base'
New-Item -ItemType Directory -Path $base | Out-Null
& $probe $base paired fast argon2d 2 1024 1 | Out-Null
if ($LASTEXITCODE -ne 0) { throw 'The probe could not make the fast set.' }

$lines = New-Object System.Collections.Generic.List[string]
try {
    foreach ($version in $Release) {
        $gui = Join-Path (Install-KeePassXC -Version $version) 'KeePassXC.exe'
        foreach ($variant in 'P-plain-fast', 'P-moved-fast') {
            $here = Join-Path $directory "$version-$variant"
            $steps = $null
            $failure = 'no window'
            for ($attempt = 1; $attempt -le $Attempts -and -not $steps; $attempt++) {
                if (Test-Path $here) { Remove-Item -LiteralPath $here -Recurse -Force }
                New-Item -ItemType Directory -Path $here | Out-Null
                $file = Join-Path $here 'P.kdbx'
                Copy-Item -LiteralPath (Join-Path $base "$variant.kdbx") -Destination $file
                [System.IO.File]::WriteAllLines((Join-Path $here 'keepassxc.ini'), [string[]] $configuration)
                [System.IO.File]::WriteAllText((Join-Path $here 'password.txt'), $password + "`r`n")

                # Standard output stays where it is: KeePassXC 2.7.12 exits at startup, without a
                # window, when it is redirected.
                $start = [DateTime]::UtcNow
                $process = Start-Process -FilePath $gui -PassThru -WorkingDirectory $Work `
                    -RedirectStandardInput (Join-Path $here 'password.txt') `
                    -ArgumentList @('--config', "`"$here\keepassxc.ini`"", '--localconfig', "`"$here\keepassxc-local.ini`"", '--pw-stdin', "`"$file`"")
                try {
                    $main = Find-Window $process 30
                    if ($main) {
                        $steps = Set-Rounds $main
                    }
                    else {
                        Write-Host "    KeePassXC $version showed no window on attempt $attempt" -ForegroundColor DarkGray
                    }
                }
                catch [System.Management.Automation.RuntimeException], [System.Windows.Automation.ElementNotAvailableException] {
                    # A window that went away under the automation, or a step it never showed.
                    $failure = $_.Exception.Message
                    Write-Host "    KeePassXC $version, attempt ${attempt}: $failure" -ForegroundColor DarkGray
                }
                finally {
                    Start-Sleep -Seconds 1
                    Stop-Process -Id $process.Id -Force -ErrorAction SilentlyContinue
                    $process.WaitForExit(10000) | Out-Null
                }
                $end = [DateTime]::UtcNow
            }
            if (-not $steps) { throw "KeePassXC $version gave up after $Attempts attempts, the last with: $failure" }

            Write-Host "  kdf: KeePassXC $version, $variant, attempt $($attempt - 1)" -ForegroundColor DarkGray
            if ($lines.Count -gt 0) { $lines.Add('') }
            $lines.Add("KeePassXC $version, $variant, transform rounds set to $rounds in Database Security")
            foreach ($step in $steps) { $lines.Add("  $step") }
            $window = 'save=' + $start.ToString('o') + '/' + $end.ToString('o')
            $read = @(& $probe $Work read $file $window)
            if ($LASTEXITCODE -ne 0) { throw "The probe could not read $file." }
            $lines.AddRange([string[]] $read)
        }
    }
}
finally {
    # Whatever happened above, no KeePassXC this started is left running.
    Get-Process -Name KeePassXC -ErrorAction SilentlyContinue |
        Where-Object { $_.Path -and $_.Path.StartsWith((Join-Path $root '.keepassxc'), [StringComparison]::OrdinalIgnoreCase) } |
        Stop-Process -Force -ErrorAction SilentlyContinue
}

[System.IO.File]::WriteAllLines((Join-Path $Output 'keepassxc-kdf.txt'), $lines, (New-Object System.Text.UTF8Encoding $false))
