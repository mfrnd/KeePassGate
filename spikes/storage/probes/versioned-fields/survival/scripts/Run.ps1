# Survival critique of the versioned-fields option. Console only, invented databases only.
# Uses my copy of the design's probe (Vfs.exe), KeePassLib 2.62 and keepassxc-cli 2.4.2 and 2.8.0.
$ErrorActionPreference = 'Stop'
$Root = Split-Path -Parent $PSScriptRoot
$Probe = Join-Path $Root 'probe\out\Vfs.exe'
$Work = Join-Path $Root 'work'
$Out = Join-Path $Root 'out'
. (Join-Path $PSScriptRoot '..\..\..\..\..\..\tools\KeePassXC.ps1')
$Cli = [ordered]@{
    '2.4.2' = (Get-KeePassXCCli -Version '2.4.2')
    '2.8.0' = (Get-KeePassXCCli -Version '2.8.0')
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
    if ($p.ExitCode -ne 0) { throw "keepassxc-cli $Version $($Arguments -join ' ') exited $($p.ExitCode): $($o.Result)$($e.Result)" }
}

function Run-Probe([string[]] $a) {
    $r = & $Probe @a
    if ($LASTEXITCODE -ne 0) { throw "probe failed: $($a -join ' ')" }
    return ,([string[]] @($r))
}

function Xc-Merge([string] $Version, [string] $Target, [string] $Source, [string] $Dir) {
    Invoke-Cli $Version @('merge', '-s', $Target, $Source) $Dir
}

if (Test-Path -LiteralPath $Work) { Remove-Item -LiteralPath $Work -Recurse -Force }
New-Item -ItemType Directory -Path $Work | Out-Null
New-Item -ItemType Directory -Force -Path $Out | Out-Null
$base = Join-Path $Work 'base'
Run-Probe @('make', $base) | Out-Null
$B = Join-Path $base 'R-B.kdbx'; $A1 = Join-Path $base 'R-A1.kdbx'; $A2 = Join-Path $base 'R-A2.kdbx'; $H5 = Join-Path $base 'H5.kdbx'

# ---- 1. H5 under the design's own history rule ------------------------------------------------
$r = New-Object System.Collections.Generic.List[string]
foreach ($dir in 'intoA', 'intoB') {
    $t = if ($dir -eq 'intoA') { $H5 } else { $B }
    $s = if ($dir -eq 'intoA') { $B } else { $H5 }
    foreach ($m in 'Synchronize', 'OverwriteIfNewer', 'OverwriteExisting', 'KeepExisting') {
        $cell = Join-Path $Work "h5-$dir-$m"; New-Item -ItemType Directory -Path $cell | Out-Null
        $res = Join-Path $cell 'result.kdbx'
        Run-Probe @('kpmerge', $t, $s, $m, $res) | Out-Null
        $r.Add(''); $r.Add("## H5, KeePassLib $m, ${dir}: the probe's fold (history always read)")
        $r.AddRange((Run-Probe @('fold', $res)))
        $r.Add("## H5, KeePassLib $m, ${dir}: the design's rule (history read only when the node's copy is newest and no write entry holds it)")
        $r.AddRange((Run-Probe @('fold2', $res)))
    }
    foreach ($v in $Cli.Keys) {
        $cell = Join-Path $Work "h5-$dir-xc$v"; New-Item -ItemType Directory -Path $cell | Out-Null
        Copy-Item $t (Join-Path $cell 'target.kdbx'); Copy-Item $s (Join-Path $cell 'source.kdbx')
        Xc-Merge $v (Join-Path $cell 'target.kdbx') (Join-Path $cell 'source.kdbx') $cell
        $r.Add(''); $r.Add("## H5, keepassxc-cli $v, ${dir}: the probe's fold (history always read)")
        $r.AddRange((Run-Probe @('fold', (Join-Path $cell 'target.kdbx'))))
        $r.Add("## H5, keepassxc-cli $v, ${dir}: the design's rule")
        $r.AddRange((Run-Probe @('fold2', (Join-Path $cell 'target.kdbx'))))
    }
}
[System.IO.File]::WriteAllLines((Join-Path $Out 'h5.txt'), $r)

# ---- 2. File then Import with new identifiers, the fresh copy into the stale one ---------------
$r = New-Object System.Collections.Generic.List[string]
$ni = Join-Path $Work 'newids'; New-Item -ItemType Directory -Path $ni | Out-Null
$rec = Join-Path $ni 'R-A2-recreated.kdbx'
Run-Probe @('recreate', $A2, $rec) | Out-Null
$r.Add('## inputs'); $r.AddRange((Run-Probe @('fold2', $B, $A1, $A2, $rec)))
foreach ($src in @($A1, $A2, $rec)) {
    foreach ($m in 'CreateNewUuids', 'Synchronize') {
        $res = Join-Path $ni ("B-import-" + [System.IO.Path]::GetFileNameWithoutExtension($src) + "-$m.kdbx")
        Run-Probe @('kpmerge', $B, $src, $m, $res) | Out-Null
        $r.Add(''); $r.Add("## $([System.IO.Path]::GetFileName($src)) merged into R-B (stale) by $m, the design's rule")
        $r.AddRange((Run-Probe @('fold2', $res)))
        if ($m -eq 'CreateNewUuids') {
            $r.Add("## the same file, if closing versions were also read from entries under other identifiers")
            $r.AddRange((Run-Probe @('foldcopies', $res)))
        }
    }
}
$r.Add(''); $r.Add('## today''s store for comparison (database custom data keys)')
$r.AddRange((Run-Probe @('cdcontrol', (Join-Path $Work 'cd'))))
[System.IO.File]::WriteAllLines((Join-Path $Out 'newids.txt'), $r)

# ---- 3. A ruled node deleted, the rules of nodes that are gone removed, a stale copy merged in ----
$r = New-Object System.Collections.Generic.List[string]
$g = Join-Path $Work 'gone'; New-Item -ItemType Directory -Path $g | Out-Null
$del = Join-Path $g 'A-deleted.kdbx'; $press = Join-Path $g 'A-pressed.kdbx'
Run-Probe @('gone', $A2, $del, $press) | Out-Null
$r.Add('## inputs: R-A2 with X deleted for good, then the press'); $r.AddRange((Run-Probe @('fold2', $del, $press)))
foreach ($start in @($del, $press)) {
    $name = [System.IO.Path]::GetFileNameWithoutExtension($start)
    foreach ($v in $Cli.Keys) {
        $cell = Join-Path $g "xc$v-B-into-$name"; New-Item -ItemType Directory -Path $cell | Out-Null
        Copy-Item $start (Join-Path $cell 'target.kdbx'); Copy-Item $B (Join-Path $cell 'source.kdbx')
        Xc-Merge $v (Join-Path $cell 'target.kdbx') (Join-Path $cell 'source.kdbx') $cell
        $r.Add(''); $r.Add("## keepassxc-cli $v merged stale R-B into $name, the design's rule")
        $r.AddRange((Run-Probe @('fold2', (Join-Path $cell 'target.kdbx'))))
    }
    foreach ($m in 'Synchronize', 'OverwriteIfNewer', 'OverwriteExisting', 'KeepExisting') {
        $res = Join-Path $g "kp-$m-B-into-$name.kdbx"
        Run-Probe @('kpmerge', $start, $B, $m, $res) | Out-Null
        $r.Add(''); $r.Add("## KeePassLib $m merged stale R-B into $name, the design's rule")
        $r.AddRange((Run-Probe @('fold2', $res)))
    }
}
[System.IO.File]::WriteAllLines((Join-Path $Out 'gone.txt'), $r)

# ---- 4. Renewals, with KeePassXC doing the merging and with KeePass doing it -------------------
$r = New-Object System.Collections.Generic.List[string]
$c = Join-Path $Work 'chain'; New-Item -ItemType Directory -Path $c | Out-Null
$xa = Join-Path $c 'xc-A.kdbx'; $xx = Join-Path $c 'xc-X.kdbx'; $ka = Join-Path $c 'kp-A.kdbx'; $kx = Join-Path $c 'kp-X.kdbx'
foreach ($f in $xa, $xx, $ka, $kx) { Copy-Item $A2 $f }
$r.Add('## start: both copies are R-A2'); $r.AddRange((Run-Probe @('count', $xa)))
for ($n = 1; $n -le 8; $n++) {
    # KeePass with the plugin renews one client (a small entry, then the old certificate given up: a state pair).
    Run-Probe @('close', $xa, "$n") | Out-Null
    # The other machine runs KeePassXC and merges both ways.
    Xc-Merge '2.8.0' $xx $xa $c
    Xc-Merge '2.8.0' $xa $xx $c
    $r.Add("## round $n, keepassxc-cli 2.8.0 merging both ways"); $r.AddRange((Run-Probe @('count', $xa, $xx)))
    # The same rounds with KeePass's Synchronize doing the merge, whose result is written to both files.
    Run-Probe @('close', $ka, "$n") | Out-Null
    $tmp = Join-Path $c 'kp-result.kdbx'
    Run-Probe @('kpmerge', $ka, $kx, 'Synchronize', $tmp) | Out-Null
    Copy-Item $tmp $ka -Force; Copy-Item $tmp $kx -Force
    $r.Add("## round $n, KeePassLib Synchronize"); $r.AddRange((Run-Probe @('count', $ka)))
}
$r.Add(''); $r.Add('## after round 8, what the design''s rule reads in the KeePassXC-merged copy')
$r.AddRange((Run-Probe @('fold2', $xa)))
[System.IO.File]::WriteAllLines((Join-Path $Out 'chain.txt'), $r)
Write-Host 'done'
