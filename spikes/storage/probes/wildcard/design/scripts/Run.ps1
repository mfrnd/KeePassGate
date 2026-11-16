# Runs the wildcard probe: KeePassLib 2.62 merges (five methods, both directions) and
# keepassxc-cli merges and saves (four releases) over the sets Wc.exe makes.
# Console only. Invented databases only.
$ErrorActionPreference = 'Stop'
$Root = Split-Path -Parent $PSScriptRoot
$Probe = Join-Path $Root 'probe\out\Wc.exe'
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
$env:WC_NAMES = Join-Path $base 'names.txt'

$sum = New-Object System.Collections.Generic.List[string]
$full = New-Object System.Collections.Generic.List[string]
function Note([string] $label, [string] $file) {
    $sum.Add([string] (& $Probe sum $label $file))
    $full.Add('')
    $full.Add("## $label")
    $full.AddRange([string[]] (& $Probe dump $file))
}

$sum.Add('## signatures')
foreach ($v in $Cli.Keys) {
    $s = Get-AuthenticodeSignature -LiteralPath $Cli[$v]
    $sum.Add("keepassxc-cli $v : $($s.Status) $($s.SignerCertificate.Subject)")
}
$sum.Add('')
$sum.Add('## inputs')
$sets = 'U', 'C', 'H', 'R', 'D', 'X', 'K'
Note 'base' (Join-Path $base 'base.kdbx')
foreach ($set in $sets) { foreach ($side in 'A', 'B') { Note "$set-$side" (Join-Path $base "$set-$side.kdbx") } }

foreach ($set in $sets) {
    $sum.Add('')
    $sum.Add("## set $set")
    foreach ($into in 'A', 'B') {
        $from = if ($into -eq 'A') { 'B' } else { 'A' }
        foreach ($method in 'Synchronize', 'OverwriteIfNewer', 'OverwriteExisting', 'KeepExisting', 'CreateNewUuids') {
            $cell = Join-Path $Work "kp-$set-into$into-$method"
            New-Item -ItemType Directory -Path $cell | Out-Null
            $result = Join-Path $cell 'result.kdbx'
            & $Probe kpmerge (Join-Path $base "$set-$into.kdbx") (Join-Path $base "$set-$from.kdbx") $method $result
            if ($LASTEXITCODE -ne 0) { throw "kpmerge failed: $cell" }
            Note "KeePass $set into $into $method" $result
        }
        foreach ($v in $Cli.Keys) {
            $cell = Join-Path $Work "xc-$set-into$into-$v"
            New-Item -ItemType Directory -Path $cell | Out-Null
            Copy-Item (Join-Path $base "$set-$into.kdbx") (Join-Path $cell 'target.kdbx')
            Copy-Item (Join-Path $base "$set-$from.kdbx") (Join-Path $cell 'source.kdbx')
            $m = Invoke-Cli $v @('merge', '-s', (Join-Path $cell 'target.kdbx'), (Join-Path $cell 'source.kdbx')) $cell
            [System.IO.File]::WriteAllText((Join-Path $cell 'merge.txt'), $m.Text)
            Note "KeePassXC $v $set into $into (exit $($m.Exit))" (Join-Path $cell 'target.kdbx')
        }
    }
}

# A plain KeePassXC save of a file holding the store, then KeePassLib reads it back.
$sum.Add('')
$sum.Add('## saves')
$big = Join-Path $Work 'big'
New-Item -ItemType Directory -Path $big | Out-Null
$sum.Add([string] (& $Probe big (Join-Path $big 'big.kdbx') 1048576))
Note 'big, as KeePassLib wrote it' (Join-Path $big 'big.kdbx')
foreach ($v in $Cli.Keys) {
    $cell = Join-Path $Work "xc-save-$v"
    New-Item -ItemType Directory -Path $cell | Out-Null
    Copy-Item (Join-Path $base 'U-A.kdbx') (Join-Path $cell 'saved.kdbx')
    $m = Invoke-Cli $v @('add', (Join-Path $cell 'saved.kdbx'), 'General/xc-added') $cell
    Note "KeePassXC $v add on U-A (exit $($m.Exit))" (Join-Path $cell 'saved.kdbx')
    Copy-Item (Join-Path $big 'big.kdbx') (Join-Path $cell 'big.kdbx')
    $m = Invoke-Cli $v @('add', (Join-Path $cell 'big.kdbx'), 'General/xc-added') $cell
    Note "KeePassXC $v add on big (exit $($m.Exit))" (Join-Path $cell 'big.kdbx')
}

# Chains: a KeePassXC result, then KeePass, then the plugin's next write.
$sum.Add('')
$sum.Add('## chains')
$chain = Join-Path $Work 'chain'
New-Item -ItemType Directory -Path $chain | Out-Null
foreach ($set in 'U', 'C', 'H') {
    foreach ($v in '2.4.2', '2.8.0') {
        $xc = Join-Path $Work "xc-$set-intoA-$v\target.kdbx"
        $then = Join-Path $chain "$set-$v-then-kp-sync.kdbx"
        & $Probe kpmerge $xc (Join-Path $base "$set-B.kdbx") Synchronize $then
        Note "$set KeePassXC $v into A, then KeePass Synchronize with B" $then
        $xcb = Join-Path $Work "xc-$set-intoB-$v\target.kdbx"
        $thenb = Join-Path $chain "$set-$v-B-then-kp-sync-A.kdbx"
        & $Probe kpmerge $xcb (Join-Path $base "$set-A.kdbx") Synchronize $thenb
        Note "$set KeePassXC $v into B, then KeePass Synchronize with A" $thenb
    }
}
foreach ($pair in @(@('C after KeePass Synchronize into A', 'kp-C-intoA-Synchronize\result.kdbx'),
                    @('C after KeePassXC 2.8.0 into B', 'xc-C-intoB-2.8.0\target.kdbx'),
                    @('U after KeePassXC 2.8.0 into A', 'xc-U-intoA-2.8.0\target.kdbx'),
                    @('D after KeePass Synchronize into B', 'kp-D-intoB-Synchronize\result.kdbx'),
                    @('K after KeePass Synchronize into A', 'kp-K-intoA-Synchronize\result.kdbx'))) {
    $next = Join-Path $chain (($pair[0] -replace '[^A-Za-z0-9]', '-') + '-next.kdbx')
    & $Probe write (Join-Path $Work $pair[1]) $next
    Note "$($pair[0]), then the plugin's next write" $next
}

# What a person can do by hand in KeePassXC, from its command line.
$sum.Add('')
$sum.Add('## by hand in KeePassXC')
$entryPath = 'KeePassGate/KeePassGate store'
foreach ($v in '2.6.6', '2.8.0') {
    $cell = Join-Path $Work "xc-hand-$v"
    New-Item -ItemType Directory -Path $cell | Out-Null
    $f = Join-Path $cell 'rm.kdbx'
    Copy-Item (Join-Path $base 'U-A.kdbx') $f
    $m = Invoke-Cli $v @('rm', $f, $entryPath) $cell
    Note "KeePassXC $v rm of the snapshot, once (exit $($m.Exit))" $f
    $ls = Invoke-Cli $v @('ls', '-R', '-f', $f) $cell
    [System.IO.File]::WriteAllText((Join-Path $cell 'ls.txt'), $ls.Text)
    $binPath = ($ls.Text -split "`r?`n" | Where-Object { $_ -match '/KeePassGate store$' -and $_ -notmatch '^KeePassGate/' } | Select-Object -First 1)
    if ($binPath) {
        $m = Invoke-Cli $v @('rm', $f, $binPath.Trim()) $cell
        Note "KeePassXC $v rm of the snapshot, twice (exit $($m.Exit))" $f
        Copy-Item $f (Join-Path $cell 'rm-then-merge.kdbx')
        Copy-Item (Join-Path $base 'U-B.kdbx') (Join-Path $cell 'stale.kdbx')
        $m = Invoke-Cli $v @('merge', '-s', (Join-Path $cell 'rm-then-merge.kdbx'), (Join-Path $cell 'stale.kdbx')) $cell
        Note "KeePassXC $v rm twice, then the stale copy merged in (exit $($m.Exit))" (Join-Path $cell 'rm-then-merge.kdbx')
    } else { $sum.Add("KeePassXC $v : no path found for the binned snapshot") }
    $f = Join-Path $cell 'edit.kdbx'
    Copy-Item (Join-Path $base 'U-A.kdbx') $f
    $m = Invoke-Cli $v @('edit', '-t', 'renamed by hand', $f, $entryPath) $cell
    Note "KeePassXC $v edit of the snapshot's title (exit $($m.Exit))" $f
    $f = Join-Path $cell 'mv.kdbx'
    Copy-Item (Join-Path $base 'U-A.kdbx') $f
    $m = Invoke-Cli $v @('mv', $f, $entryPath, 'General') $cell
    Note "KeePassXC $v mv of the snapshot into General (exit $($m.Exit))" $f
}

# KeePass's Delete Duplicate Entries after an import with new identifiers.
$sum.Add('')
$sum.Add('## KeePass, Delete Duplicate Entries after Create new IDs')
foreach ($into in 'A', 'B') {
    $src = Join-Path $Work "kp-U-into$into-CreateNewUuids\result.kdbx"
    $dst = Join-Path $chain "U-into$into-newids-dde.kdbx"
    $sum.Add([string] (& $Probe dde $src $dst))
    Note "U into $into Create new IDs, then Delete Duplicate Entries" $dst
}

# pykeepass: a plain save, and a deletion that leaves no deletion record.
$sum.Add('')
$sum.Add('## pykeepass')
$pk = Join-Path $Root 'scripts\pk.py'
$pkdir = Join-Path $Work 'pk'
New-Item -ItemType Directory -Path $pkdir | Out-Null
& uv run --no-project --with pykeepass python -I -X utf8 $pk resave (Join-Path $base 'U-A.kdbx') (Join-Path $pkdir 'resaved.kdbx') | Out-Null
Note 'pykeepass resave of U-A' (Join-Path $pkdir 'resaved.kdbx')
& uv run --no-project --with pykeepass python -I -X utf8 $pk delete-newest (Join-Path $Work 'kp-U-intoB-OverwriteIfNewer\result.kdbx') (Join-Path $pkdir 'deleted.kdbx') | Out-Null
Note 'pykeepass deletes the newest snapshot where an older one is still present (no deletion record)' (Join-Path $pkdir 'deleted.kdbx')
& uv run --no-project --with pykeepass python -I -X utf8 $pk delete-newest (Join-Path $base 'U-A.kdbx') (Join-Path $pkdir 'deleted-A.kdbx') | Out-Null
Note 'pykeepass deletes the only snapshot of U-A (no deletion record for it)' (Join-Path $pkdir 'deleted-A.kdbx')

[System.IO.File]::WriteAllLines((Join-Path $Out 'summary.txt'), $sum)
[System.IO.File]::WriteAllLines((Join-Path $Out 'merges.txt'), $full)
Write-Host "done: $($sum.Count) summary lines, $($full.Count) lines in full"
