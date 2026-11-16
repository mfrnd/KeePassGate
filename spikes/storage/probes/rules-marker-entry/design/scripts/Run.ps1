# Runs the rules-marker-entry probe: KeePassLib 2.62 merges (five methods, both directions) and
# keepassxc-cli merges (four releases, both directions) over the copies Rme.exe makes, and
# evaluates every result with the design's own read path.
# Console only. Invented databases only.
$ErrorActionPreference = 'Stop'
$Root = Split-Path -Parent $PSScriptRoot
$Probe = Join-Path $Root 'probe\out\Rme.exe'
$Work = Join-Path $Root 'work'
$Out = Join-Path $Root 'out'
. (Join-Path $PSScriptRoot '..\..\..\..\..\..\tools\KeePassXC.ps1')
$Cli = [ordered]@{
    '2.4.2'  = Get-KeePassXCCli -Version 2.4.2
    '2.6.6'  = Get-KeePassXCCli -Version 2.6.6
    '2.7.12' = Get-KeePassXCCli -Version 2.7.12
    '2.8.0'  = Get-KeePassXCCli -Version 2.8.0
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
$report.AddRange([string[]] (& $Probe eval (Join-Path $base 'base.kdbx') (Join-Path $base 'A.kdbx') (Join-Path $base 'B.kdbx')))

foreach ($into in 'A', 'B') {
    $from = if ($into -eq 'A') { 'B' } else { 'A' }
    foreach ($method in 'Synchronize', 'OverwriteIfNewer', 'OverwriteExisting', 'KeepExisting', 'CreateNewUuids') {
        $cell = Join-Path $Work "kp-into$into-$method"
        New-Item -ItemType Directory -Path $cell | Out-Null
        $result = Join-Path $cell 'result.kdbx'
        & $Probe kpmerge (Join-Path $base "$into.kdbx") (Join-Path $base "$from.kdbx") $method $result
        if ($LASTEXITCODE -ne 0) { throw "kpmerge failed: $cell" }
        $report.Add('')
        $report.Add("## KeePassLib 2.62, $from merged into $into, $method")
        $report.AddRange([string[]] (& $Probe eval $result))
    }
    foreach ($v in $Cli.Keys) {
        $cell = Join-Path $Work "xc-into$into-$v"
        New-Item -ItemType Directory -Path $cell | Out-Null
        Copy-Item (Join-Path $base "$into.kdbx") (Join-Path $cell 'target.kdbx')
        Copy-Item (Join-Path $base "$from.kdbx") (Join-Path $cell 'source.kdbx')
        $m = Invoke-Cli $v @('merge', '-s', (Join-Path $cell 'target.kdbx'), (Join-Path $cell 'source.kdbx')) $cell
        [System.IO.File]::WriteAllText((Join-Path $cell 'merge.txt'), $m.Text)
        $report.Add('')
        $report.Add("## keepassxc-cli $v merge, $from merged into $into, exit $($m.Exit)")
        $report.AddRange([string[]] (& $Probe eval (Join-Path $cell 'target.kdbx')))
    }
}

# A plain KeePassXC save of each copy, then the design's read of it.
foreach ($v in $Cli.Keys) {
    $cell = Join-Path $Work "xc-save-$v"
    New-Item -ItemType Directory -Path $cell | Out-Null
    Copy-Item (Join-Path $base 'A.kdbx') (Join-Path $cell 'saved.kdbx')
    $m = Invoke-Cli $v @('add', (Join-Path $cell 'saved.kdbx'), 'General/xc-added') $cell
    $report.Add('')
    $report.Add("## keepassxc-cli $v add (a plain save), on A, exit $($m.Exit)")
    $report.AddRange([string[]] (& $Probe eval (Join-Path $cell 'saved.kdbx')))
}

# Chains: a KeePassXC merge, then a KeePassLib synchronize with the other copy, both ways round.
$chain = Join-Path $Work 'chain'
New-Item -ItemType Directory -Path $chain | Out-Null
foreach ($v in '2.4.2', '2.8.0') {
    & $Probe kpmerge (Join-Path $Work "xc-intoA-$v\target.kdbx") (Join-Path $base 'B.kdbx') Synchronize (Join-Path $chain "xcA-$v-then-kp-sync-B.kdbx")
    $report.Add('')
    $report.Add("## chain: keepassxc-cli $v merged B into A, then KeePassLib 2.62 Synchronize of that result with B")
    $report.AddRange([string[]] (& $Probe eval (Join-Path $chain "xcA-$v-then-kp-sync-B.kdbx")))
    & $Probe kpmerge (Join-Path $Work "xc-intoB-$v\target.kdbx") (Join-Path $base 'A.kdbx') Synchronize (Join-Path $chain "xcB-$v-then-kp-sync-A.kdbx")
    $report.Add('')
    $report.Add("## chain: keepassxc-cli $v merged A into B, then KeePassLib 2.62 Synchronize of that result with A")
    $report.AddRange([string[]] (& $Probe eval (Join-Path $chain "xcB-$v-then-kp-sync-A.kdbx")))
}

# Back and forth in KeePassXC alone: B into A, that result into B, that result into A.
foreach ($v in '2.4.2', '2.8.0') {
    $cell = Join-Path $Work "xc-roundtrip-$v"
    New-Item -ItemType Directory -Path $cell | Out-Null
    Copy-Item (Join-Path $base 'A.kdbx') (Join-Path $cell 'A.kdbx')
    Copy-Item (Join-Path $base 'B.kdbx') (Join-Path $cell 'B.kdbx')
    $null = Invoke-Cli $v @('merge', '-s', (Join-Path $cell 'A.kdbx'), (Join-Path $cell 'B.kdbx')) $cell
    $null = Invoke-Cli $v @('merge', '-s', (Join-Path $cell 'B.kdbx'), (Join-Path $cell 'A.kdbx')) $cell
    $null = Invoke-Cli $v @('merge', '-s', (Join-Path $cell 'A.kdbx'), (Join-Path $cell 'B.kdbx')) $cell
    $report.Add('')
    $report.Add("## keepassxc-cli $v, back and forth three times: A then B after it")
    $report.AddRange([string[]] (& $Probe eval (Join-Path $cell 'A.kdbx') (Join-Path $cell 'B.kdbx')))
}

# KeePass's own Delete Duplicate Entries, on copy B as it is, and on a copy where a person
# duplicated one rule entry by hand.
$dd = Join-Path $Work 'dedupe'
New-Item -ItemType Directory -Path $dd | Out-Null
$report.Add('')
$report.Add('## KeePassLib 2.62 DeleteDuplicateEntries')
$report.AddRange([string[]] (& $Probe dedupe (Join-Path $base 'B.kdbx') (Join-Path $dd 'B-deduped.kdbx')))
$report.AddRange([string[]] (& $Probe eval (Join-Path $dd 'B-deduped.kdbx')))
& $Probe handcopy (Join-Path $base 'A.kdbx') (Join-Path $dd 'A-handcopy.kdbx')
$report.Add('-- a rule entry of G01 duplicated by hand, before the tool runs')
$report.AddRange([string[]] (& $Probe eval (Join-Path $dd 'A-handcopy.kdbx')))
$report.AddRange([string[]] (& $Probe dedupe (Join-Path $dd 'A-handcopy.kdbx') (Join-Path $dd 'A-handcopy-deduped.kdbx')))
$report.AddRange([string[]] (& $Probe eval (Join-Path $dd 'A-handcopy-deduped.kdbx')))

# After a KeePassXC merge left a replaced rule entry beside the one in force, a person deletes
# the one in force for good. The replaced one must not come back into force.
foreach ($v in '2.4.2', '2.8.0') {
    foreach ($side in 'A', 'B') {
        $result = Join-Path $dd "xc$side-$v-head-deleted.kdbx"
        & $Probe killhead (Join-Path $Work "xc-into$side-$v/target.kdbx") $result 'G01-supersede-A'
        $report.Add('')
        $report.Add("## keepassxc-cli $v merge into $side, then the rule entry in force in G01 deleted for good by hand")
        $report.AddRange([string[]] (& $Probe eval $result))
    }
}

# A person edits a rule entry by hand: the rule text of one, the title and notes of another.
& $Probe tamper (Join-Path $base 'A.kdbx') (Join-Path $dd 'A-tampered.kdbx')
$report.Add('')
$report.Add('## hand edit: the rule text of G02, the title and notes of G11, on A')
$report.AddRange([string[]] (& $Probe eval (Join-Path $dd 'A-tampered.kdbx')))

# A person removes or moves a rule entry in KeePassXC from its command line.
foreach ($v in '2.4.2', '2.8.0') {
    $cell = Join-Path $Work "xc-hand-$v"
    New-Item -ItemType Directory -Path $cell | Out-Null
    Copy-Item (Join-Path $base 'B.kdbx') (Join-Path $cell 'B.kdbx')
    $m = Invoke-Cli $v @('rm', (Join-Path $cell 'B.kdbx'), 'G11-group-touched-B/KeePassGate rule (this group)') $cell
    $report.Add('')
    $report.Add("## keepassxc-cli $v rm of the rule entry of G11 on B (to the recycle bin), exit $($m.Exit)")
    $report.AddRange([string[]] (& $Probe eval (Join-Path $cell 'B.kdbx')))
    $binName = if ($v -eq '2.8.0') { 'Papierkorb' } else { 'Recycle Bin' }
    $m = Invoke-Cli $v @('rm', (Join-Path $cell 'B.kdbx'), ($binName + '/KeePassGate rule (this group)')) $cell
    $report.Add('')
    $report.Add("## keepassxc-cli $v rm of the same entry again, now in the recycle bin (for good), exit $($m.Exit)")
    $report.AddRange([string[]] (& $Probe eval (Join-Path $cell 'B.kdbx')))
    if ($v -ne '2.4.2') {
        $m = Invoke-Cli $v @('mv', (Join-Path $cell 'B.kdbx'), 'G01-supersede-A/KeePassGate rule (this group)', 'General') $cell
        $report.Add('')
        $report.Add("## keepassxc-cli $v mv of the rule entry of G01 into General on B, exit $($m.Exit)")
        $report.AddRange([string[]] (& $Probe eval (Join-Path $cell 'B.kdbx')))
    }
}

[System.IO.File]::WriteAllLines((Join-Path $Out 'merges.txt'), $report)
& $Probe cost | Set-Content -LiteralPath (Join-Path $Out 'cost.txt') -Encoding utf8
& $Probe dump (Join-Path $base 'A.kdbx') | Set-Content -LiteralPath (Join-Path $Out 'dump-A.txt') -Encoding utf8
Write-Host "done: $($report.Count) lines"
