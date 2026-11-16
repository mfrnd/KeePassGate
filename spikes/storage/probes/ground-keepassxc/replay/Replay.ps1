param([string[]] $Only)
# Written on this branch, not by the agent. The record's steps outside `Run-Merge.ps1` and
# `Run-Save.ps1` were typed by hand, through `scripts\Common.ps1`, and no script of them was kept.
# Each step below replays one of them from what the record left: the command lines and their order
# in the step's `cli.log`, the inputs that the record's databases were read back to have been made
# from, and the helper scripts in `scripts\`. Steps that print counts the record never kept write
# them to a file of their own, so that the claims resting on them can be checked; those files have
# no recorded counterpart. Runs under PowerShell 7, as `Common.ps1` needs.
. (Join-Path $PSScriptRoot '..\scripts\Common.ps1')
$work = Join-Path $script:Root 'work'
$scripts = Join-Path $script:Root 'scripts'
$baseA = Join-Path $work 'base-fast\A.kdbx'
$baseB = Join-Path $work 'base-fast\B.kdbx'

# Python through uv, isolated, as the record ran it. Only standard output is kept; uv's own
# messages go to the console.
function Invoke-Py([string[]] $A) {
    $out = & uv run --no-project --with pykeepass python -I -X utf8 @A
    if ($LASTEXITCODE -ne 0) { throw "python failed: $($A -join ' ')" }
    , [string[]] $out
}

function Step([string] $Name) { (-not $Only) -or ($Only -contains $Name) }

# The record's `save-fast\A-orig.txt`: the dump the plain save is compared against.
if (Step 'orig') {
    [System.IO.File]::WriteAllLines((Join-Path $work 'save-fast\A-orig.txt'), [string[]] (Invoke-Dump @($baseA)))
}

# Not in the record: dumps of the inputs the probe made, so that what the report states of them (the
# format of each copy, the groups and entries of each) can be read off a file.
if (Step 'inputs') {
    $d = Join-Path $work 'inputs'
    Reset-Dir $d
    foreach ($set in 'fast', 'extra') {
        foreach ($name in 'base', 'A', 'B') {
            $lines = Invoke-Dump @((Join-Path $work "base-$set\$name.kdbx"))
            [System.IO.File]::WriteAllLines((Join-Path $d "$set-$name.txt"), [string[]] $lines)
        }
    }
}

# The record's `help-<release>.txt` and `.err`: each release's top-level help, which shows that
# 2.4.2 has no `mv` and no `export`.
if (Step 'help') {
    foreach ($v in $script:Releases) {
        & $script:Cli[$v] --help > (Join-Path $work "help-$v.txt") 2> (Join-Path $work "help-$v.err")
    }
}

# The release checks `logs\download.log` recorded, made again by `tools\KeePassXC.ps1`.
if (Step 'releases') {
    $lines = & {
        . (Join-Path $PSScriptRoot '..\..\..\..\..\tools\KeePassXC.ps1')
        foreach ($v in $script:Releases) {
            $dir = Install-KeePassXC -Version $v
            $pin = Get-KeePassXCPin | Where-Object { $_.Version -eq $v }
            foreach ($exe in 'KeePassXC.exe', 'keepassxc-cli.exe') {
                $path = Join-Path $dir $exe
                $sig = Get-AuthenticodeSignature -LiteralPath $path
                $cn = if ($sig.SignerCertificate.Subject -match 'CN="?([^",]+)') { $Matches[1] } else { '?' }
                '{0} {1} archive={2} sha256={3} sig={4} signer={5} fileversion={6}' -f $v, $exe, $pin.Archive, $pin.Sha256,
                    $sig.Status, $cn, (Get-Item -LiteralPath $path).VersionInfo.FileVersion
            }
        }
    }
    $py = Invoke-Py @('-c', "import importlib.metadata as m; print('pykeepass', m.version('pykeepass')); print('lxml', m.version('lxml'))")
    [System.IO.File]::WriteAllLines((Join-Path $work 'releases.txt'), [string[]] ($lines + $py))
}

# `work\unknown`: KpgUnknown* elements put into a copy of A, one `add` by each release, then read.
if (Step 'unknown') {
    $d = Join-Path $work 'unknown'
    Reset-Dir $d
    Invoke-Py @((Join-Path $scripts 'unknown_xml.py'), 'make', $baseA, (Join-Path $d 'U.kdbx')) | Out-Null
    $files = @((Join-Path $d 'U.kdbx'))
    foreach ($v in $script:Releases) {
        $f = Join-Path $d "U-$v.kdbx"
        Copy-Item (Join-Path $d 'U.kdbx') $f
        Invoke-Cli $v @('add', $f, 'General/xc-added') (Join-Path $d 'cli.log') $d | Out-Null
        $files += $f
    }
    [System.IO.File]::WriteAllLines((Join-Path $d 'check.txt'), (Invoke-Py (@((Join-Path $scripts 'unknown_xml.py'), 'check') + $files)))
}

# `work\tags`: the raw tag text of entry `tag-A` and group `GTagsA` set, one `add` by each release,
# then read raw.
if (Step 'tags') {
    $d = Join-Path $work 'tags'
    Reset-Dir $d
    Invoke-Py @((Join-Path $scripts 'tags_raw.py'), 'set', $baseA, (Join-Path $d 'T.kdbx'), 'zeta; Alpha;alpha, b:c,  spaced tag ;zeta') | Out-Null
    $files = @((Join-Path $d 'T.kdbx'))
    foreach ($v in $script:Releases) {
        $f = Join-Path $d "T-$v.kdbx"
        Copy-Item (Join-Path $d 'T.kdbx') $f
        Invoke-Cli $v @('add', $f, 'General/xc-added') (Join-Path $d 'cli.log') $d | Out-Null
        $files += $f
    }
    [System.IO.File]::WriteAllLines((Join-Path $d 'get.txt'), (Invoke-Py (@((Join-Path $scripts 'tags_raw.py'), 'get') + $files)))
}

# `work\histmax`: a copy of A with the history limit set to 3, one `add` by each release, then the
# raw history of `hist-many`, and what KeePassLib makes of the same file.
if (Step 'histmax') {
    $d = Join-Path $work 'histmax'
    Reset-Dir $d
    $set = Invoke-Py @((Join-Path $scripts 'set_history_max.py'), $baseA, (Join-Path $d 'H.kdbx'), '3')
    [System.IO.File]::WriteAllLines((Join-Path $d 'set.txt'), $set)
    $files = @((Join-Path $d 'H.kdbx'))
    foreach ($v in $script:Releases) {
        $f = Join-Path $d "H-$v.kdbx"
        Copy-Item (Join-Path $d 'H.kdbx') $f
        Invoke-Cli $v @('add', $f, 'General/xc-added') (Join-Path $d 'cli.log') $d | Out-Null
        $files += $f
    }
    [System.IO.File]::WriteAllLines((Join-Path $d 'count.txt'), (Invoke-Py (@((Join-Path $scripts 'count_hist.py'), 'hist-many') + $files)))
    [System.IO.File]::WriteAllLines((Join-Path $d 'H-dump.txt'), [string[]] (Invoke-Dump @((Join-Path $d 'H.kdbx'))))
}

# `work\export`: CSV, HTML and XML export of a copy of A, by the three releases whose command line
# has `export`; each file is what the command printed, as the record's are.
if (Step 'export') {
    $d = Join-Path $work 'export'
    Reset-Dir $d
    Copy-Item $baseA (Join-Path $d 'A.kdbx')
    foreach ($v in '2.6.6', '2.7.12', '2.8.0') {
        foreach ($format in 'csv', 'html', 'xml') {
            $r = Invoke-Cli $v @('export', '-f', $format, (Join-Path $d 'A.kdbx')) (Join-Path $d "cli-$v.log") $d
            [System.IO.File]::WriteAllText((Join-Path $d "$v.$format"), $r.Text)
        }
    }
}

# `work\roundtrip`: A from B, then B from A, then A from B, by 2.4.2 and by 2.8.0.
if (Step 'roundtrip') {
    foreach ($v in '2.4.2', '2.8.0') {
        $d = Join-Path $work "roundtrip\$v"
        Reset-Dir $d
        $a = Join-Path $d 'A.kdbx'
        $b = Join-Path $d 'B.kdbx'
        Copy-Item $baseA $a
        Copy-Item $baseB $b
        $log = Join-Path $d 'cli.log'
        foreach ($pair in @(@($a, $b), @($b, $a), @($a, $b))) {
            Invoke-Cli $v @('merge', '-s', $pair[0], $pair[1]) $log $d | Out-Null
        }
        [System.IO.File]::WriteAllLines((Join-Path $d 'A.txt'), [string[]] (Invoke-Dump @($a)))
        [System.IO.File]::WriteAllLines((Join-Path $d 'B.txt'), [string[]] (Invoke-Dump @($b)))
    }
}

# `work\kpsync`: KeePassLib's Synchronize over the original copies and over 2.8.0's results. That
# the record used 2.8.0's is read off `A-sync-xcB.txt`, whose marker date is in English, as 2.8.0
# writes it and 2.7.12 does not.
if (Step 'kpsync') {
    $d = Join-Path $work 'kpsync'
    Reset-Dir $d
    $xcA = Join-Path $work 'merge-fast\2.8.0-intoA\A.kdbx'
    $xcB = Join-Path $work 'merge-fast\2.8.0-intoB\B.kdbx'
    foreach ($job in @(@('A-sync-B', $baseA, $baseB), @('xcA-sync-B', $xcA, $baseB), @('A-sync-xcB', $baseA, $xcB))) {
        $out = Join-Path $d "$($job[0]).kdbx"
        & $script:Probe kpsync $job[1] $job[2] $out
        if ($LASTEXITCODE -ne 0) { throw "kpsync failed: $($job[0])" }
        [System.IO.File]::WriteAllLines((Join-Path $d "$($job[0]).txt"), [string[]] (Invoke-Dump @($out)))
    }
}

# Raw history counts through pykeepass, which the report quotes and the record never kept.
if (Step 'counts') {
    $d = Join-Path $work 'counts'
    Reset-Dir $d
    $hist = Join-Path $scripts 'count_hist.py'
    foreach ($set in 'fast', 'slow') {
        $targets = foreach ($v in $script:Releases) { foreach ($into in 'A', 'B') { Join-Path $work "merge-$set\$v-into$into\$into.kdbx" } }
        [System.IO.File]::WriteAllLines((Join-Path $d "merge-$set.txt"), (Invoke-Py (@($hist, 'hist,hist-many,hist-big') + $targets)))
    }
    $targets = foreach ($v in $script:Releases) { foreach ($into in 'A', 'B') { Join-Path $work "merge-extra\$v-into$into\$into.kdbx" } }
    [System.IO.File]::WriteAllLines((Join-Path $d 'merge-extra.txt'), (Invoke-Py (@($hist, 'meta,meta2') + $targets)))
    $targets = @($baseA) + @(foreach ($v in $script:Releases) { Join-Path $work "save-fast\$v\S.kdbx" })
    [System.IO.File]::WriteAllLines((Join-Path $d 'save-fast.txt'), (Invoke-Py (@($hist, 'hist-many,hist-big,cd-both') + $targets)))
    $targets = @((Join-Path $work 'base-fast\base.kdbx'), $baseA, $baseB)
    [System.IO.File]::WriteAllLines((Join-Path $d 'base.txt'), (Invoke-Py (@($hist, 'hist,hist-many,hist-big') + $targets)))
}
