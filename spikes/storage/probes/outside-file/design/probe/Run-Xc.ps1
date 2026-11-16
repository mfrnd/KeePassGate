# What keepassxc-cli does to the two keys the design leaves in the file: a plain save, and a merge
# in both directions, between a copy that holds them and one that does not, and between two copies
# that both hold them. Console only; the releases are the ones the keepassxc ground report
# downloaded and verified against the spike's pins. Slow KDF, so that the two loads of a merge fall
# in different seconds, which is when a KeePassXC merge replaces the database's custom data.
param([string[]] $Versions = @('2.4.2', '2.6.6', '2.7.12', '2.8.0'))
$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
. (Join-Path $PSScriptRoot '..\..\..\..\..\..\tools\KeePassXC.ps1')
$cli = @{}
foreach ($v in '2.4.2', '2.6.6', '2.7.12', '2.8.0') { $cli[$v] = Get-KeePassXCCli -Version $v }
$probe = Join-Path $root 'probe\out\Probe.exe'
$base = Join-Path $root 'work\kp'
$work = Join-Path $root 'work\xc'
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
$cells = @(
    @('anchored', 'old'), @('old', 'anchored'), @('anchored', 'anchored2'), @('anchored2', 'anchored'),
    @('anchored-notimes', 'old'), @('anchored-notimes', 'anchored2')
)
foreach ($v in $Versions) {
    # A plain save: add one entry to a copy of the anchored file.
    $dir = Join-Path $work "$v-save"
    New-Item -ItemType Directory -Path $dir | Out-Null
    Copy-Item (Join-Path $base 'anchored.kdbx') (Join-Path $dir 'anchored.kdbx')
    $r = Invoke-Cli $v @('add', (Join-Path $dir 'anchored.kdbx'), 'xc-added') $dir $password
    $d = & $probe describe (Join-Path $dir 'anchored.kdbx') | Select-Object -Last 1
    "{0} save, exit {1}: {2}" -f $v, $r.Exit, $d
    foreach ($cell in $cells) {
        $t = $cell[0]; $s = $cell[1]
        $dir = Join-Path $work "$v-$t+$s"
        New-Item -ItemType Directory -Path $dir | Out-Null
        Copy-Item (Join-Path $base "$t.kdbx") (Join-Path $dir 'target.kdbx')
        Copy-Item (Join-Path $base "$s.kdbx") (Join-Path $dir 'source.kdbx')
        # -s: the same password for both; the target is loaded first, the source after it.
        $r = Invoke-Cli $v @('merge', '-s', (Join-Path $dir 'target.kdbx'), (Join-Path $dir 'source.kdbx')) $dir $password
        $d = & $probe describe (Join-Path $dir 'target.kdbx') | Select-Object -Last 1
        "{0} merge into {1} from {2}, exit {3}, {4:F1} s: {5}" -f $v, $t, $s, $r.Exit, $r.Seconds, $d
    }
}
