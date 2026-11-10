<#
.SYNOPSIS
    Measures what KeePassXC and KeePass 2.47 do to a paired database, and writes what came back
    to `spikes/revocation/output`.

.DESCRIPTION
    Two scenarios over throwaway databases the probe makes under the work directory: C, and P,
    which is C made newer in every way the plugin cares about. `RESULTS.md` section 5 is what they
    settled, and `README.md` is how to read the output.

    **save**: every pinned KeePassXC adds one entry at the root of a copy of P with
    `keepassxc-cli add`, which is a save touching no custom data, and so does KeePass 2.47 through
    its own KeePassLib. Five copies each: P with an Argon2d KDF (`P-plain-fast`), the same with an
    entry moved out of its group, which records a previous parent (`P-moved-fast`), P with AES-KDF
    (`P-plain-aes`), and both KDFs with no rule on any node (`P-plain-fastb`, `P-plain-aesb`).
    Written to `keepassxc-save.txt`, after the copies as KeePassLib wrote them.

    **merge**: every pinned KeePassXC merges C into P and P into C with `keepassxc-cli merge`, both
    opened with the same password. Each direction runs with C as KeePassLib wrote it (`fresh`) and
    with C saved once by that same KeePassXC first (`saved`), which gives it KeePassXC's own date
    marker; with a KDF slow enough that a load takes well over a second (`slow`) and with a KDF
    fast enough that both loads usually fall in the same second (`fast`); and, on the releases from
    2.7.0 that keep a previous parent, with both copies carrying a previous parent (`slowm`,
    `fastm`), so that the result is KDBX 4.1 and keeps its per-key times. Where the release has `--dry-run`, a dry run on the same
    copies comes first. Every cell runs `-Runs` times. Written to `keepassxc-merge.txt`, a line per
    run.

    Every program it starts runs with the work directory as its working directory, gets the
    password on standard input, and is waited for. What each program printed goes to a log under the
    work directory rather than into the output, since it names paths and is in the system's
    language.

.PARAMETER Work
    Where the throwaway databases go. Its `base`, `save` and `merge` directories are emptied first.

.PARAMETER Scenario
    `save`, `merge`, or both, which is the default.

.PARAMETER Runs
    How many times each merge cell runs. Three were recorded.

.PARAMETER Release
    The KeePassXC releases to measure, every release `keepassxc.sha256` pins by default.

.PARAMETER Output
    Where the output files go, `spikes/revocation/output` by default. Each scenario replaces its
    whole file, so a run of fewer releases or runs than were recorded belongs somewhere else.

.EXAMPLE
    powershell.exe -NoProfile -File spikes\revocation\Measure-KeePassXC.ps1 -Work work\keepassxc
#>
param(
    [Parameter(Mandatory)] [string] $Work,
    [string[]] $Scenario = @('save', 'merge'),
    [int] $Runs = 3,
    [string[]] $Release,
    [string] $Output
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

# Not a default in the parameter block: there, in an advanced script, Windows PowerShell 5.1 reads
# `$PSScriptRoot` as empty.
if (-not $Output) {
    $Output = Join-Path $PSScriptRoot 'output'
}

# `powershell.exe -File` passes `a,b` as one string rather than a list.
$Scenario = @($Scenario | ForEach-Object { $_ -split ',' })
$Release = @($Release | Where-Object { $_ } | ForEach-Object { $_ -split ',' })
foreach ($name in $Scenario) {
    if ($name -notin 'save', 'merge') {
        throw "There is no scenario '$name': save and merge are the two."
    }
}

if ($PSVersionTable.PSEdition -ne 'Desktop') {
    throw 'Run this under Windows PowerShell 5.1, powershell.exe: the KeePass 2.47 half loads a .NET Framework assembly.'
}

$root = Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
. (Join-Path $root 'tools\KeePass.ps1')
. (Join-Path $root 'tools\KeePassXC.ps1')

$probe = Join-Path $PSScriptRoot 'out\Probe.exe'
if (-not (Test-Path $probe)) {
    throw "No probe at $probe. Build it first: dotnet build spikes\revocation -c Release -o spikes\revocation\out"
}

New-Item -ItemType Directory -Force -Path $Output | Out-Null
$Output = (Resolve-Path $Output).Path
$powershell = Join-Path $env:SystemRoot 'System32\WindowsPowerShell\v1.0\powershell.exe'
$saver = Join-Path $PSScriptRoot 'Save-WithKeePass.ps1'

# The probe's own invented password, which every database it makes is locked with.
$password = 'probe-only'

# A started program's standard input is written in the console's input encoding, and in a UTF-8
# console that begins with a byte order mark, which then arrives as the first three characters of
# the password. KeePassLib refuses that password; Qt's reader drops the mark, which is why
# `keepassxc-cli` never noticed.
[Console]::InputEncoding = New-Object System.Text.UTF8Encoding $false

New-Item -ItemType Directory -Force -Path $Work | Out-Null
$Work = (Resolve-Path $Work).Path

function Reset-Directory([string] $Path) {
    if (Test-Path $Path) {
        Remove-Item -LiteralPath $Path -Recurse -Force
    }
    New-Item -ItemType Directory -Path $Path | Out-Null
}

# UTF-8 with no byte order mark, so that a re-run compares byte for byte.
function Write-Recorded([string] $Name, [System.Collections.Generic.List[string]] $Lines) {
    [System.IO.File]::WriteAllLines((Join-Path $Output $Name), $Lines, (New-Object System.Text.UTF8Encoding $false))
}

# Runs one program to its end and returns its exit code, everything it printed, and the span of UTC
# time it ran in, as the window the probe reads: `<label>=<start>/<end>`.
function Invoke-Timed([string] $Label, [string] $Program, [string[]] $Arguments, [string] $Log) {
    $info = New-Object System.Diagnostics.ProcessStartInfo
    $info.FileName = $Program
    $info.Arguments = ($Arguments | ForEach-Object { '"' + $_ + '"' }) -join ' '
    $info.WorkingDirectory = $Work
    $info.UseShellExecute = $false
    $info.CreateNoWindow = $true
    $info.RedirectStandardInput = $true
    $info.RedirectStandardOutput = $true
    $info.RedirectStandardError = $true

    # Each program starts in a second of its own, a little after it begins. KeePassXC stores times
    # to the second, and a time in a second two programs shared would name either of them.
    $now = [DateTime]::UtcNow
    $wait = [TimeSpan]::TicksPerSecond - $now.Ticks % [TimeSpan]::TicksPerSecond
    Start-Sleep -Milliseconds ([int] ([TimeSpan]::FromTicks($wait).TotalMilliseconds) + 20)

    $start = [DateTime]::UtcNow
    $process = [System.Diagnostics.Process]::Start($info)
    $stdout = $process.StandardOutput.ReadToEndAsync()
    $stderr = $process.StandardError.ReadToEndAsync()
    $process.StandardInput.WriteLine($password)
    $process.StandardInput.Close()
    $process.WaitForExit()
    $end = [DateTime]::UtcNow

    $text = $stdout.Result + $stderr.Result
    $span = $start.ToString('o') + '/' + $end.ToString('o')
    Add-Content -LiteralPath $Log -Value ("### {0}: {1} {2}`r`n### exit {3}, {4}`r`n{5}" -f $Label, $Program, $info.Arguments, $process.ExitCode, $span, $text)

    [pscustomobject]@{
        Exit   = $process.ExitCode
        Window = "$Label=$span"
        Text   = $text
    }
}

# The probe with a directory of its own as the first argument, which is where `paired` writes.
function Invoke-Probe([string] $Directory, [string[]] $Arguments) {
    $lines = @(& $probe $Directory @Arguments)
    if ($LASTEXITCODE -ne 0) {
        throw "The probe failed: $($Arguments -join ' ')"
    }
    $lines
}

# How many lines of a merge's own report name one of the plugin's keys.
function Measure-KpgLines([string] $Text) {
    @($Text -split "`n" | Where-Object { $_ -match 'KPG\.' }).Count
}

$releases = @(Get-KeePassXCPin | ForEach-Object { $_.Version })
if ($Release.Count -gt 0) {
    $releases = $Release
}
$cli = @{}
foreach ($version in $releases) {
    $cli[$version] = Join-Path (Install-KeePassXC -Version $version) 'keepassxc-cli.exe'
}
$keepass247 = Install-KeePass -Version '2.47'

$base = Join-Path $Work 'base'
Reset-Directory $base
$sets = [ordered]@{
    fast  = @('argon2d', '2', '1024', '1')
    aes   = @('aes', '100000')
    fastb = @('argon2d', '2', '1024', '1')
    aesb  = @('aes', '100000')
    fastm = @('argon2d', '2', '1024', '1')
    slow  = @('argon2d', '200', '65536', '2')
    slowm = @('argon2d', '200', '65536', '2')
}
foreach ($set in $sets.Keys) {
    Write-Host "  making the $set set" -ForegroundColor DarkGray
    Invoke-Probe $base (@('paired', $set) + $sets[$set]) | Out-Null
}

if ($Scenario -contains 'save') {
    $directory = Join-Path $Work 'save'
    Reset-Directory $directory
    $log = Join-Path $directory 'programs.log'
    $variants = 'P-plain-fast', 'P-moved-fast', 'P-plain-aes', 'P-plain-fastb', 'P-plain-aesb'
    $lines = New-Object System.Collections.Generic.List[string]

    $lines.Add('As KeePassLib 2.61.1 wrote them')
    foreach ($variant in @('C-fast') + $variants) {
        $lines.Add($variant)
        $lines.AddRange([string[]] (Invoke-Probe $Work @('read', (Join-Path $base "$variant.kdbx"))))
    }

    foreach ($version in $releases) {
        $lines.Add('')
        $lines.Add("KeePassXC $version, keepassxc-cli add")
        foreach ($variant in $variants) {
            Write-Host "  save: KeePassXC $version, $variant" -ForegroundColor DarkGray
            $copy = Join-Path $directory "$version-$variant.kdbx"
            Copy-Item -LiteralPath (Join-Path $base "$variant.kdbx") -Destination $copy
            $run = Invoke-Timed 'save' $cli[$version] @('add', $copy, 'xc-added') $log
            $lines.Add("$variant, exit $($run.Exit)")
            $lines.AddRange([string[]] (Invoke-Probe $Work @('read', $copy, $run.Window)))
        }
    }

    $lines.Add('')
    $lines.Add('KeePass 2.47, PwDatabase.Open, an entry added at the root, PwDatabase.Save')
    foreach ($variant in $variants) {
        Write-Host "  save: KeePass 2.47, $variant" -ForegroundColor DarkGray
        $copy = Join-Path $directory "2.47-$variant.kdbx"
        Copy-Item -LiteralPath (Join-Path $base "$variant.kdbx") -Destination $copy
        $run = Invoke-Timed 'save' $powershell @('-NoProfile', '-File', $saver, '-KeePass', $keepass247, $copy) $log
        $lines.Add("$variant, exit $($run.Exit), " + ($run.Text.Trim() -split "`r?`n")[-1])
        $lines.AddRange([string[]] (Invoke-Probe $Work @('read', $copy, $run.Window)))
    }

    Write-Recorded 'keepassxc-save.txt' $lines
}

if ($Scenario -contains 'merge') {
    $directory = Join-Path $Work 'merge'
    Reset-Directory $directory
    $lines = New-Object System.Collections.Generic.List[string]
    $lines.Add((@('release', 'set', 'C', 'target', 'run', 'exit touch/dry/merge', 'KPG lines dry/merge', 'target read back') -join "`t"))

    foreach ($version in $releases) {
        $log = Join-Path $directory "$version.log"
        $help = Invoke-Timed 'help' $cli[$version] @('merge', '--help') $log
        $hasDryRun = $help.Text -match '--dry-run'

        # Before 2.7.0 a save drops the previous parent, so the sets that carry a previous parent
        # would measure nothing their plain twins do not.
        $merged = @('slow', 'fast')
        if ([version] $version -ge [version] '2.7.0') {
            $merged += 'slowm', 'fastm'
        }

        foreach ($set in $merged) {
            foreach ($state in 'fresh', 'saved') {
                foreach ($target in 'P', 'C') {
                    foreach ($run in 1..$Runs) {
                        $cell = "$set-$state-into$target-$run"
                        Write-Host "  merge: KeePassXC $version, $cell" -ForegroundColor DarkGray
                        $here = Join-Path $directory "$version\$cell"
                        New-Item -ItemType Directory -Path $here -Force | Out-Null
                        $p = Join-Path $here 'P.kdbx'
                        $c = Join-Path $here 'C.kdbx'
                        Copy-Item -LiteralPath (Join-Path $base "P-plain-$set.kdbx") -Destination $p
                        Copy-Item -LiteralPath (Join-Path $base "C-$set.kdbx") -Destination $c
                        if ($target -eq 'P') { $into = $p; $from = $c } else { $into = $c; $from = $p }

                        $windows = @()
                        $exits = @('-', '-', '-')
                        $dry = '-'
                        if ($state -eq 'saved') {
                            $touch = Invoke-Timed 'touch' $cli[$version] @('add', $c, 'xc-touch') $log
                            $windows += $touch.Window
                            $exits[0] = $touch.Exit
                        }
                        if ($hasDryRun) {
                            $trial = Invoke-Timed 'dry' $cli[$version] @('merge', '-s', '--dry-run', $into, $from) $log
                            $windows += $trial.Window
                            $exits[1] = $trial.Exit
                            $dry = Measure-KpgLines $trial.Text
                        }
                        $merge = Invoke-Timed 'merge' $cli[$version] @('merge', '-s', $into, $from) $log
                        $windows += $merge.Window
                        $exits[2] = $merge.Exit

                        $read = @(Invoke-Probe $Work (@('summary', $into) + $windows))
                        $lines.Add(($version, $set, $state, "into $target", $run, ($exits -join '/'),
                                "$dry/$(Measure-KpgLines $merge.Text)", $read[0]) -join "`t")
                    }
                }
            }
        }
    }

    Write-Recorded 'keepassxc-merge.txt' $lines
}
