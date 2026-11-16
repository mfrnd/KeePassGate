# Runs the store-entries probe: KeePassLib 2.62 merges (five methods, both directions) and
# keepassxc-cli merges (four releases, both directions) over the sets Sep.exe makes.
# Console only. Invented databases only.
$ErrorActionPreference = 'Stop'
$Root = Split-Path -Parent $PSScriptRoot
$Probe = Join-Path $Root 'probe\out\Sep.exe'
$Work = Join-Path $Root 'work'
$Out = Join-Path $Root 'out'
. (Join-Path $PSScriptRoot '..\..\..\..\..\..\tools\KeePassXC.ps1')
$Cli = [ordered]@{
    '2.4.2'  = (Get-KeePassXCCli -Version '2.4.2')
    '2.6.6'  = (Get-KeePassXCCli -Version '2.6.6')
    '2.7.12' = (Get-KeePassXCCli -Version '2.7.12')
    '2.8.0'  = (Get-KeePassXCCli -Version '2.8.0')
}
$Password = 'probe-only'

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

if (Test-Path -LiteralPath $Work) { Remove-Item -LiteralPath $Work -Recurse -Force }
New-Item -ItemType Directory -Path $Work | Out-Null
New-Item -ItemType Directory -Force -Path $Out | Out-Null
$base = Join-Path $Work 'base'
& $Probe make $base
if ($LASTEXITCODE -ne 0) { throw 'make failed' }

$report = New-Object System.Collections.Generic.List[string]
$report.Add('## signatures')
foreach ($v in $Cli.Keys) {
    $s = Get-AuthenticodeSignature -LiteralPath $Cli[$v]
    $report.Add("keepassxc-cli $v : $($s.Status) $($s.SignerCertificate.Subject)")
}
$report.Add('')
$report.Add('## inputs')
$report.AddRange([string[]] (& $Probe dump (Join-Path $base 'I-A.kdbx') (Join-Path $base 'I-B.kdbx') (Join-Path $base 'T-A.kdbx') (Join-Path $base 'T-B.kdbx') (Join-Path $base 'D-base.kdbx') (Join-Path $base 'D-A.kdbx') (Join-Path $base 'D-B.kdbx')))

foreach ($set in 'I', 'T', 'D') {
    foreach ($into in 'A', 'B') {
        $from = if ($into -eq 'A') { 'B' } else { 'A' }
        foreach ($method in 'Synchronize', 'OverwriteIfNewer', 'OverwriteExisting', 'KeepExisting', 'CreateNewUuids') {
            $cell = Join-Path $Work "kp-$set-into$into-$method"
            New-Item -ItemType Directory -Path $cell | Out-Null
            $result = Join-Path $cell 'result.kdbx'
            & $Probe kpmerge (Join-Path $base "$set-$into.kdbx") (Join-Path $base "$set-$from.kdbx") $method $result
            if ($LASTEXITCODE -ne 0) { throw "kpmerge failed: $cell" }
            $report.Add('')
            $report.Add("## KeePassLib 2.62, set $set, $from merged into $into, $method")
            $report.AddRange([string[]] (& $Probe dump $result))
        }
        foreach ($v in $Cli.Keys) {
            $cell = Join-Path $Work "xc-$set-into$into-$v"
            New-Item -ItemType Directory -Path $cell | Out-Null
            Copy-Item (Join-Path $base "$set-$into.kdbx") (Join-Path $cell 'target.kdbx')
            Copy-Item (Join-Path $base "$set-$from.kdbx") (Join-Path $cell 'source.kdbx')
            $m = Invoke-Cli $v @('merge', '-s', (Join-Path $cell 'target.kdbx'), (Join-Path $cell 'source.kdbx')) $cell
            [System.IO.File]::WriteAllText((Join-Path $cell 'merge.txt'), $m.Text)
            $report.Add('')
            $report.Add("## keepassxc-cli $v merge, set $set, $from merged into $into, exit $($m.Exit)")
            $report.AddRange([string[]] (& $Probe dump (Join-Path $cell 'target.kdbx')))
        }
    }
}

# A plain KeePassXC save of a file holding the store, then KeePassLib reads it back.
foreach ($v in $Cli.Keys) {
    $cell = Join-Path $Work "xc-save-$v"
    New-Item -ItemType Directory -Path $cell | Out-Null
    Copy-Item (Join-Path $base 'I-A.kdbx') (Join-Path $cell 'saved.kdbx')
    $m = Invoke-Cli $v @('add', (Join-Path $cell 'saved.kdbx'), 'General/xc-added') $cell
    $report.Add('')
    $report.Add("## keepassxc-cli $v add (a plain save), on I-A, exit $($m.Exit)")
    $report.AddRange([string[]] (& $Probe dump (Join-Path $cell 'saved.kdbx')))
}

# A chain: KeePassXC 2.8.0 merges the stale D-B into D-A, then KeePassLib synchronizes the result with the stale copy.
$chain = Join-Path $Work 'chain'
New-Item -ItemType Directory -Path $chain | Out-Null
Copy-Item (Join-Path $Work 'xc-D-intoA-2.8.0\target.kdbx') (Join-Path $chain 'xc-result.kdbx')
& $Probe kpmerge (Join-Path $chain 'xc-result.kdbx') (Join-Path $base 'D-B.kdbx') Synchronize (Join-Path $chain 'then-kp-sync.kdbx')
$report.Add('')
$report.Add('## chain: keepassxc-cli 2.8.0 merged stale D-B into D-A, then KeePassLib 2.62 Synchronize of that result with D-B')
$report.AddRange([string[]] (& $Probe dump (Join-Path $chain 'then-kp-sync.kdbx')))

[System.IO.File]::WriteAllLines((Join-Path $Out 'merges.txt'), $report)
& $Probe walk | Set-Content -LiteralPath (Join-Path $Out 'walk.txt') -Encoding utf8
& $Probe uuids | Set-Content -LiteralPath (Join-Path $Out 'uuids.txt') -Encoding utf8
Write-Host "done: $($report.Count) lines"
