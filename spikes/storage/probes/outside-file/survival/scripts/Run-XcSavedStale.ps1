# Critique of option-outside-file, survival lens. Does a stale copy that only KeePassXC itself has
# saved (so that its database custom data holds nothing but KeePassXC's own keys) also remove the two
# keys the design leaves in the file, when KeePassXC merges it into a copy that holds them?
# Console only. Invented content: the design's own throwaway bases (password probe-only), copied here.
# Slow KDF (Argon2d, 200 iterations, 64 MiB), so the two loads of a merge fall in different seconds.
param([string[]] $Versions = @('2.4.2', '2.6.6', '2.7.12', '2.8.0'))
$ErrorActionPreference = 'Stop'
$me = Split-Path -Parent $PSScriptRoot
. (Join-Path $PSScriptRoot '..\..\..\..\..\..\tools\KeePassXC.ps1')
$cli = @{}
foreach ($v in '2.4.2', '2.6.6', '2.7.12', '2.8.0') { $cli[$v] = Get-KeePassXCCli -Version $v }
# The design's describe command, run read-only over files in my own directory.
$probe = Join-Path $me '..\design\probe\out\Probe.exe'
$base = Join-Path $me 'work\base'
$work = Join-Path $me 'work\xcsaved'
$password = 'probe-only'

function Invoke-Cli([string] $Version, [string[]] $Arguments, [string] $WorkDir, [string] $Stdin) {
    $info = New-Object System.Diagnostics.ProcessStartInfo
    $info.FileName = $cli[$Version]
    $info.Arguments = ($Arguments | ForEach-Object { '"' + $_ + '"' }) -join ' '
    $info.WorkingDirectory = $WorkDir
    $info.UseShellExecute = $false
    $info.CreateNoWindow = $true
    $info.RedirectStandardInput = $true
    $info.RedirectStandardOutput = $true
    $info.RedirectStandardError = $true
    $info.StandardInputEncoding = New-Object System.Text.UTF8Encoding $false
    $start = [DateTime]::UtcNow
    $p = [System.Diagnostics.Process]::Start($info)
    $o = $p.StandardOutput.ReadToEndAsync()
    $e = $p.StandardError.ReadToEndAsync()
    foreach ($line in ($Stdin -split "`n")) { $p.StandardInput.WriteLine($line) }
    $p.StandardInput.Close()
    $p.WaitForExit()
    [pscustomobject]@{ Exit = $p.ExitCode; Text = ($o.Result + $e.Result); Seconds = ([DateTime]::UtcNow - $start).TotalSeconds }
}

if (Test-Path -LiteralPath $work) { Remove-Item -LiteralPath $work -Recurse -Force }
New-Item -ItemType Directory -Path $work | Out-Null
"bases: " + (& $probe describe (Join-Path $base 'old.kdbx') (Join-Path $base 'anchored.kdbx') | Select-Object -Skip 1) -join ' | '
foreach ($v in $Versions) {
    $dir = Join-Path $work $v
    New-Item -ItemType Directory -Path $dir | Out-Null
    # A stale copy from before the first Create, saved once by this KeePassXC release.
    Copy-Item (Join-Path $base 'old.kdbx') (Join-Path $dir 'stale.kdbx')
    $r = Invoke-Cli $v @('add', (Join-Path $dir 'stale.kdbx'), 'xc-added') $dir $password
    $d = & $probe describe (Join-Path $dir 'stale.kdbx') | Select-Object -Last 1
    "{0} save of the stale copy, exit {1}: {2}" -f $v, $r.Exit, $d
    # Merged into the present copy, which holds the authority and the store key; the stale copy loads later.
    Copy-Item (Join-Path $base 'anchored.kdbx') (Join-Path $dir 'target.kdbx')
    $r = Invoke-Cli $v @('merge', '-s', (Join-Path $dir 'target.kdbx'), (Join-Path $dir 'stale.kdbx')) $dir $password
    $d = & $probe describe (Join-Path $dir 'target.kdbx') | Select-Object -Last 1
    "{0} merge into anchored from the KeePassXC-saved stale copy, exit {1}, {2:F1} s: {3}" -f $v, $r.Exit, $r.Seconds, $d
    ($r.Text -split "`n" | Where-Object { $_ -match 'custom|Custom|Benutzer|KPG' } | Select-Object -First 6) -join ' / '
}
