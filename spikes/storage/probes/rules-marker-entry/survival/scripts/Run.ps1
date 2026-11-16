# Survival runs against the rules-marker-entry design that need a second program: keepassxc-cli
# (2.4.2 and 2.8.0, the copies the KeePassXC ground report verified, checked again here) and
# pykeepass through uv. Console only, invented databases, throwaway password.
$ErrorActionPreference = 'Stop'
$Root = Split-Path -Parent $PSScriptRoot
$Probe = Join-Path $Root 'probe\out\Rms.exe'
$Work = Join-Path $Root 'work'
. (Join-Path $PSScriptRoot '..\..\..\..\..\..\tools\KeePassXC.ps1')
$Cli = [ordered]@{
    '2.4.2' = Get-KeePassXCCli -Version 2.4.2
    '2.8.0' = Get-KeePassXCCli -Version 2.8.0
}
$Password = 'probe-only'
$report = New-Object System.Collections.Generic.List[string]
function Add-Lines($lines) { foreach ($l in @($lines)) { if ($null -ne $l) { $report.Add([string] $l) } } }

function Invoke-Cli([string] $Version, [string[]] $Arguments, [string] $WorkDir) {
    $info = New-Object System.Diagnostics.ProcessStartInfo
    $info.FileName = $Cli[$Version]
    $info.Arguments = ($Arguments | ForEach-Object { '"' + $_ + '"' }) -join ' '
    $info.WorkingDirectory = $WorkDir
    $info.UseShellExecute = $false
    $info.CreateNoWindow = $true
    $info.RedirectStandardInput = $true
    $info.RedirectStandardOutput = $true
    $info.RedirectStandardError = $true
    $info.StandardInputEncoding = New-Object System.Text.UTF8Encoding $false
    $p = [System.Diagnostics.Process]::Start($info)
    $o = $p.StandardOutput.ReadToEndAsync()
    $e = $p.StandardError.ReadToEndAsync()
    foreach ($line in ($Password, $Password)) { $p.StandardInput.WriteLine($line) }
    $p.StandardInput.Close()
    $p.WaitForExit()
    [pscustomobject]@{ Exit = $p.ExitCode; Text = ($o.Result + $e.Result) }
}

foreach ($v in $Cli.Keys) {
    $s = Get-AuthenticodeSignature -LiteralPath $Cli[$v]
    $report.Add("keepassxc-cli $v : $($s.Status) $($s.SignerCertificate.Subject)")
}

# ---- s4: the same first rule text decided later in another copy ----
$s4 = Join-Path $Work 's4'
if (Test-Path -LiteralPath $s4) { Remove-Item -LiteralPath $s4 -Recurse -Force }
Add-Lines (& $Probe s4make $s4)
foreach ($into in 'A', 'B') {
    $from = if ($into -eq 'A') { 'B' } else { 'A' }
    foreach ($method in 'Synchronize', 'OverwriteIfNewer', 'KeepExisting') {
        $out = Join-Path $s4 "kp-into$into-$method.kdbx"
        & $Probe kpmerge (Join-Path $s4 "$into.kdbx") (Join-Path $s4 "$from.kdbx") $method $out
        Add-Lines (& $Probe eval $out)
    }
    foreach ($v in $Cli.Keys) {
        $cell = Join-Path $s4 "xc-$v-into$into"
        New-Item -ItemType Directory -Path $cell | Out-Null
        Copy-Item -LiteralPath (Join-Path $s4 "$into.kdbx") -Destination (Join-Path $cell 'target.kdbx')
        Copy-Item -LiteralPath (Join-Path $s4 "$from.kdbx") -Destination (Join-Path $cell 'source.kdbx')
        $r = Invoke-Cli $v @('merge', '-s', (Join-Path $cell 'target.kdbx'), (Join-Path $cell 'source.kdbx')) $cell
        $report.Add("keepassxc-cli $v merge into $into exit $($r.Exit)")
        Add-Lines (& $Probe eval (Join-Path $cell 'target.kdbx'))
    }
}

# ---- s6: the rule entry and the ruled record removed by hand ----
$s6 = Join-Path $Work 's6'
if (Test-Path -LiteralPath $s6) { Remove-Item -LiteralPath $s6 -Recurse -Force }
Add-Lines (& $Probe s6make $s6)
$pkScript = Join-Path $Root 'scripts\pk_delete.py'
$pkOut = Join-Path $s6 's6-pk-deleted.kdbx'
$pk = & uv run --no-project --with pykeepass python -I -X utf8 $pkScript (Join-Path $s6 's6.kdbx') $pkOut 2>&1
Add-Lines ($pk | ForEach-Object { "$_" })
Add-Lines (& $Probe eval $pkOut)
Add-Lines (& $Probe s6dx (Join-Path $s6 's6.kdbx') (Join-Path $s6 's6-kp-deleted.kdbx') (Join-Path $s6 's6-dx-merged.kdbx'))
# The copy that deleted them, merged with the intact copy by keepassxc-cli (no deletion applied).
foreach ($v in $Cli.Keys) {
    $cell = Join-Path $s6 "xc-$v"
    New-Item -ItemType Directory -Path $cell | Out-Null
    Copy-Item -LiteralPath (Join-Path $s6 's6.kdbx') -Destination (Join-Path $cell 'target.kdbx')
    Copy-Item -LiteralPath (Join-Path $s6 's6-kp-deleted.kdbx') -Destination (Join-Path $cell 'source.kdbx')
    $r = Invoke-Cli $v @('merge', '-s', (Join-Path $cell 'target.kdbx'), (Join-Path $cell 'source.kdbx')) $cell
    $report.Add("keepassxc-cli $v merge of the deleting copy into the intact one, exit $($r.Exit)")
    Add-Lines (& $Probe eval (Join-Path $cell 'target.kdbx'))
}

# ---- s1: the file holding two entries with one identifier, through keepassxc-cli ----
$s1 = Join-Path $Work 's1'
foreach ($name in 's1-bin-before-N', 's1-N-before-bin') {
    foreach ($v in $Cli.Keys) {
        $cell = Join-Path $s1 "xc-$v-$name"
        if (Test-Path -LiteralPath $cell) { Remove-Item -LiteralPath $cell -Recurse -Force }
        New-Item -ItemType Directory -Path $cell | Out-Null
        $f = Join-Path $cell 'db.kdbx'
        Copy-Item -LiteralPath (Join-Path $s1 "$name.kdbx") -Destination $f
        $r = Invoke-Cli $v @('ls', '-R', '-f', $f) $cell
        $report.Add("keepassxc-cli $v ls of $name exit $($r.Exit)")
        Add-Lines (($r.Text -split "`r?`n") | Where-Object { $_ -match 'KeePassGate rule|Recycle|^N/|^\s*N/' } | ForEach-Object { '    ' + $_ })
        $r = Invoke-Cli $v @('add', $f, 'xc-added') $cell
        $report.Add("keepassxc-cli $v add (a plain save) of $name exit $($r.Exit)")
        Add-Lines (& $Probe eval $f)
    }
}

$report | Set-Content -LiteralPath (Join-Path $Root 'out\run.txt') -Encoding UTF8
$report
