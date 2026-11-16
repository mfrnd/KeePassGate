# Runs the versioned-fields probe: KeePassLib 2.62 merges (five methods, both directions) and
# keepassxc-cli merges (four releases, both directions) over the sets Vf.exe makes, and prints
# what the design's fold reads out of every result.
# Console only. Invented databases only.
$ErrorActionPreference = 'Stop'
$Root = Split-Path -Parent $PSScriptRoot
$Probe = Join-Path $Root 'probe\out\Vf.exe'
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
$report.Add('## marks')
$report.AddRange([string[]] (& $Probe marks))
$report.Add('')
$report.Add('## inputs, as the fold reads them')
$inputs = Get-ChildItem -LiteralPath $base -Filter '*.kdbx' | Sort-Object Name | ForEach-Object { $_.FullName }
$report.AddRange([string[]] (& $Probe fold @inputs))

$pairs = @(
    @{ Name = 'R1'; A = 'R-A1'; B = 'R-B' },
    @{ Name = 'R2'; A = 'R-A2'; B = 'R-B' },
    @{ Name = 'C';  A = 'C-A';  B = 'C-B' },
    @{ Name = 'P';  A = 'P-A';  B = 'P-B' },
    @{ Name = 'H1'; A = 'H1';   B = 'R-B' },
    @{ Name = 'H2'; A = 'H2';   B = 'R-B' },
    @{ Name = 'H3'; A = 'H3';   B = 'R-B' },
    @{ Name = 'H4'; A = 'H4';   B = 'R-B' },
    @{ Name = 'H5'; A = 'H5';   B = 'R-B' },
    @{ Name = 'H6'; A = 'H6';   B = 'R-B' }
)

foreach ($pair in $pairs) {
    foreach ($dir in 'intoA', 'intoB') {
        $t = if ($dir -eq 'intoA') { $pair.A } else { $pair.B }
        $s = if ($dir -eq 'intoA') { $pair.B } else { $pair.A }
        foreach ($method in 'Synchronize', 'OverwriteIfNewer', 'OverwriteExisting', 'KeepExisting') {
            $cell = Join-Path $Work "kp-$($pair.Name)-$dir-$method"
            New-Item -ItemType Directory -Path $cell | Out-Null
            $result = Join-Path $cell 'result.kdbx'
            & $Probe kpmerge (Join-Path $base "$t.kdbx") (Join-Path $base "$s.kdbx") $method $result
            if ($LASTEXITCODE -ne 0) { throw "kpmerge failed: $cell" }
            $report.Add('')
            $report.Add("## KeePassLib 2.62, pair $($pair.Name), $s merged into $t, $method")
            $report.AddRange([string[]] (& $Probe fold $result))
        }
        foreach ($v in $Cli.Keys) {
            $cell = Join-Path $Work "xc-$($pair.Name)-$dir-$v"
            New-Item -ItemType Directory -Path $cell | Out-Null
            Copy-Item (Join-Path $base "$t.kdbx") (Join-Path $cell 'target.kdbx')
            Copy-Item (Join-Path $base "$s.kdbx") (Join-Path $cell 'source.kdbx')
            $m = Invoke-Cli $v @('merge', '-s', (Join-Path $cell 'target.kdbx'), (Join-Path $cell 'source.kdbx')) $cell
            [System.IO.File]::WriteAllText((Join-Path $cell 'merge.txt'), $m.Text)
            $report.Add('')
            $report.Add("## keepassxc-cli $v merge, pair $($pair.Name), $s merged into $t, exit $($m.Exit)")
            $report.AddRange([string[]] (& $Probe fold (Join-Path $cell 'target.kdbx')))
        }
    }
}

# A plain KeePassXC save of a file holding the tidied store, read back in full.
$dumps = New-Object System.Collections.Generic.List[string]
$dumps.AddRange([string[]] (& $Probe dump (Join-Path $base 'R-A1.kdbx') (Join-Path $base 'R-A2.kdbx') (Join-Path $base 'R-B.kdbx')))
foreach ($v in $Cli.Keys) {
    $cell = Join-Path $Work "xc-save-$v"
    New-Item -ItemType Directory -Path $cell | Out-Null
    Copy-Item (Join-Path $base 'R-A2.kdbx') (Join-Path $cell 'saved.kdbx')
    $m = Invoke-Cli $v @('add', (Join-Path $cell 'saved.kdbx'), 'General/xc-added') $cell
    $report.Add('')
    $report.Add("## keepassxc-cli $v add (a plain save), on R-A2, exit $($m.Exit)")
    $report.AddRange([string[]] (& $Probe fold (Join-Path $cell 'saved.kdbx')))
    $dumps.Add('')
    $dumps.Add("## keepassxc-cli $v add (a plain save), on R-A2")
    $dumps.AddRange([string[]] (& $Probe dump (Join-Path $cell 'saved.kdbx')))
}

# Chains.
$chain = Join-Path $Work 'chain'
New-Item -ItemType Directory -Path $chain | Out-Null
Copy-Item (Join-Path $Work 'xc-R2-intoA-2.8.0\target.kdbx') (Join-Path $chain 'xc-result.kdbx')
& $Probe kpmerge (Join-Path $chain 'xc-result.kdbx') (Join-Path $base 'R-B.kdbx') Synchronize (Join-Path $chain 'then-kp-sync.kdbx')
$report.Add('')
$report.Add('## chain: keepassxc-cli 2.8.0 merged stale R-B into tidied R-A2, then KeePassLib 2.62 Synchronize of that result with R-B')
$report.AddRange([string[]] (& $Probe fold (Join-Path $chain 'then-kp-sync.kdbx')))
$dumps.Add('')
$dumps.Add('## keepassxc-cli 2.8.0 merged stale R-B into tidied R-A2')
$dumps.AddRange([string[]] (& $Probe dump (Join-Path $chain 'xc-result.kdbx')))
$dumps.Add('')
$dumps.Add('## then KeePassLib 2.62 Synchronize of that result with R-B')
$dumps.AddRange([string[]] (& $Probe dump (Join-Path $chain 'then-kp-sync.kdbx')))
$dumps.Add('')
$dumps.Add('## KeePassLib 2.62 Synchronize, R-B merged into R-A2')
$dumps.AddRange([string[]] (& $Probe dump (Join-Path $Work 'kp-R2-intoA-Synchronize\result.kdbx')))
$dumps.Add('')
$dumps.Add('## KeePassLib 2.62 Synchronize, R-B merged into R-A1')
$dumps.AddRange([string[]] (& $Probe dump (Join-Path $Work 'kp-R1-intoA-Synchronize\result.kdbx')))

# Copies of ruled nodes.
$extra = Join-Path $Work 'extra'
New-Item -ItemType Directory -Path $extra | Out-Null
& $Probe dup (Join-Path $base 'R-A2.kdbx') (Join-Path $extra 'dup.kdbx')
$report.Add('')
$report.Add('## the ruled group U duplicated as KeePass Duplicate Group does (PwGroup.Duplicate), in R-A2')
$report.AddRange([string[]] (& $Probe fold (Join-Path $extra 'dup.kdbx')))
$report.Add('')
$report.Add('## File then Import with new identifiers: R-base imported into R-A2, then Delete Duplicate Entries')
$report.AddRange([string[]] (& $Probe newids (Join-Path $base 'R-A2.kdbx') (Join-Path $base 'R-base.kdbx') (Join-Path $extra 'newids.kdbx')))
$dumps.Add('')
$dumps.Add('## R-base imported into R-A2 with new identifiers, then Delete Duplicate Entries')
$dumps.AddRange([string[]] (& $Probe dump (Join-Path $extra 'newids.kdbx')))

[System.IO.File]::WriteAllLines((Join-Path $Out 'merges.txt'), $report)
[System.IO.File]::WriteAllLines((Join-Path $Out 'dumps.txt'), $dumps)
& $Probe walk | Set-Content -LiteralPath (Join-Path $Out 'walk.txt') -Encoding utf8
Write-Host "done: $($report.Count) lines"
