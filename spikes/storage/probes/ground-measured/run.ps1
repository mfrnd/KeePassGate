<#
.SYNOPSIS
    Runs the re-read of the ground report `measured` from the branch, as `RUN.md` describes, into
    `out\`.

.DESCRIPTION
    Added on the branch. The probe is `scripts\reread.ps1`, unchanged; everything around it the
    agent typed as console commands, which are rebuilt here from the session transcript, step for
    step, with only the paths turned into parameters:

      copy        every result database of the Android run (`A`, `B`, `base`, `base2`), and of the
                  earlier KeePassXC run the saves and bases (`A`, `A247`, `base`) and run 1 of
                  every merge cell (`C\<release>\*-r1`), copied into `work\data\droid` and
                  `work\data\xc`; it prints how many of each.
      files       how many databases each of the two work directories holds, copied or not.
      lists       the four lists `reread.ps1` reads: every Android file; the KeePassXC bases and
                  saves and, of each merge cell, the target (`C.kdbx` of a `CP` cell, `P.kdbx` of a
                  `PC` cell); that list without the slow key derivation; and six slow targets.
      reread      `reread.ps1` over the Android list, the fast list and the slow list.
      counts      how many targets and saves the fast list holds, by release, and the cells of
                  one release's merge directory.
      cells       the Android merge results grouped by cell: E1's time and rule, and its last
                  history item.
      tally       counts over the three re-reads: E1 and E2 lines, entries lacking the title or the
                  17-character password, lines naming `FAILED`, deletions, binaries or tags, and a
                  second history item.
      xc-merge     the 216 result lines of the spike's `keepassxc-merge.txt`, grouped by cell.
      hashes      the hash of every Android result file the spike's recorded outputs print, checked
                  against the file in the Android run's directory.
      downloads   the hash of every KeePassXC and KeePass archive and Android APK under the
                  directories given, checked against the pins.
      wsl         `wsl.exe -l -v`.

    Each writes `out\<name>`, the name its recorded copy has under `recorded\`. Two more steps
    are added on the branch, with no record beside them:

      slow-bases   `reread.ps1` over the five slow bases, whose `SettingsChanged` no other list
                  shows.
      limits      how many files of the three re-reads show history limits other than 10 items,
                  6291456 bytes and 365 days.

    **PowerShell 7, because the agent's commands ran there**: the session's PowerShell was 7, and
    only `reread.ps1` ran in Windows PowerShell 5.1, which this starts for it as the agent did.
    `Sort-Object` orders the paths differently in the two, so under Windows PowerShell 5.1 the
    lists and the re-reads hold the same lines in another order. From the branch root:

        pwsh -NoProfile -File spikes\storage\probes\ground-measured\run.ps1 -DroidWork <dir> -XcWork <dir> [-Downloads <dir>,<dir>]

.PARAMETER DroidWork
    The work directory of the Android run of `spikes/revocation-merge`, the one its scripts call
    `$KPG_ANDROID_WORK/work`, holding `A`, `B`, `base` and `base2`.

.PARAMETER XcWork
    The work directory of the earlier KeePassXC run, holding `A`, `A247`, `base` and `C`.

.PARAMETER Downloads
    Directories searched for the `.zip` and `.apk` files `downloads` hashes, separated by commas.
    Without it, that step is skipped.

.PARAMETER SpikeOutput
    A directory holding the recorded outputs of `spikes/revocation-merge`, its
    `spikes\revocation\output`. Without it they are read out of that branch with `git show` into
    `out\spike-output\`.

.PARAMETER Pins
    The directory holding `keepass.sha256`, `keepassxc.sha256` and `android.sha256`. The branch
    root unless given; the recorded run read the pins of `spikes/revocation-merge`.

.PARAMETER Only
    Runs the named steps alone, over what an earlier run left in `work\` and `out\`.
#>
param(
    [string] $DroidWork,
    [string] $XcWork,
    [string[]] $Downloads,
    [string] $SpikeOutput,
    [string] $Pins,
    [string] $Only
)
$ErrorActionPreference = 'Stop'
$known = 'copy', 'files', 'lists', 'reread', 'counts', 'cells', 'tally', 'xc-merge', 'hashes', 'downloads', 'wsl', 'slow-bases', 'limits'
# `-File` hands a list over as one string, so a comma separates the steps.
$picked = @($Only -split '[,\s]+' | ForEach-Object { $_.Trim() } | Where-Object { $_ })
foreach ($name in $picked) { if ($known -notcontains $name) { throw "No step '$name'; the steps are $($known -join ', ')." } }

$here = $PSScriptRoot
$root = (Resolve-Path (Join-Path $here '..\..\..\..')).Path
if (-not $Pins) { $Pins = $root }
$out = Join-Path $here 'out'
$m = Join-Path $here 'work\data'
$ps = Join-Path $env:SystemRoot 'System32\WindowsPowerShell\v1.0\powershell.exe'
$reread = Join-Path $here 'scripts\reread.ps1'
$password = 'dummy-password-for-a-throwaway-file'
$steps = if ($picked) { $picked } else { $known }
$utf8 = New-Object System.Text.UTF8Encoding($false)
[void](New-Item -ItemType Directory -Force $out)

$KeePassDir = & { . (Join-Path $root 'tools\KeePass.ps1'); Split-Path -Parent (Get-KeePassExe) }

function Invoke-Native([scriptblock] $Command) {
    $saved = $ErrorActionPreference
    $ErrorActionPreference = 'Continue'
    try { & $Command 2>&1 | ForEach-Object { "$_" } }
    finally { $ErrorActionPreference = $saved }
}

function Write-Out([string] $Name, [object[]] $Lines) {
    [IO.File]::WriteAllLines((Join-Path $out $Name), [string[]] @($Lines | ForEach-Object { "$_" }), $utf8)
}

function Step([string] $Name, [scriptblock] $Run) {
    if ($steps -notcontains $Name) { return }
    $clock = [Diagnostics.Stopwatch]::StartNew()
    & $Run
    Write-Host ([string]::Format([Globalization.CultureInfo]::InvariantCulture, '{0}: {1:0.0} s', $Name, $clock.Elapsed.TotalSeconds))
}

function Get-SpikeOutput([string] $Name) {
    if ($SpikeOutput) { return Get-Content -LiteralPath (Join-Path $SpikeOutput $Name) }
    $cache = Join-Path $out 'spike-output'
    $file = Join-Path $cache $Name
    if (-not (Test-Path $file)) {
        [void](New-Item -ItemType Directory -Force $cache)
        $text = $null
        foreach ($ref in 'spikes/revocation-merge', 'origin/spikes/revocation-merge') {
            $text = Invoke-Native { git -C $root show "${ref}:spikes/revocation/output/$Name" }
            if ($LASTEXITCODE -eq 0) { break }
            $text = $null
        }
        if ($null -eq $text) { throw "Neither spikes/revocation-merge nor its remote branch is in this repository; pass -SpikeOutput." }
        [IO.File]::WriteAllLines($file, [string[]] $text, $utf8)
    }
    Get-Content -LiteralPath $file
}

Step 'copy' {
    if (-not $DroidWork -or -not $XcWork) { throw 'copy needs -DroidWork and -XcWork.' }
    if (Test-Path $m) { Remove-Item $m -Recurse -Force }
    # Android results and bases, as recorded
    foreach ($sub in 'A', 'B', 'base', 'base2') { $from = Join-Path $DroidWork $sub; Get-ChildItem -LiteralPath $from -Recurse -Filter *.kdbx | ForEach-Object { $rel = $_.FullName.Substring((Join-Path $DroidWork '').Length); $to = Join-Path "$m\droid" $rel; New-Item -ItemType Directory -Force (Split-Path $to) | Out-Null; Copy-Item -LiteralPath $_.FullName -Destination $to } }
    # KeePassXC earlier run: every r1 merge cell, the saves, the bases
    Get-ChildItem -LiteralPath (Join-Path $XcWork 'C') -Recurse -Filter *.kdbx | Where-Object { $_.Directory.Name -like '*-r1' } | ForEach-Object { $rel = $_.FullName.Substring((Join-Path $XcWork '').Length); $to = Join-Path "$m\xc" $rel; New-Item -ItemType Directory -Force (Split-Path $to) | Out-Null; Copy-Item -LiteralPath $_.FullName -Destination $to }
    foreach ($sub in 'A', 'A247', 'base') { Get-ChildItem -LiteralPath (Join-Path $XcWork $sub) -Recurse -Filter *.kdbx | ForEach-Object { $rel = $_.FullName.Substring((Join-Path $XcWork '').Length); $to = Join-Path "$m\xc" $rel; New-Item -ItemType Directory -Force (Split-Path $to) | Out-Null; Copy-Item -LiteralPath $_.FullName -Destination $to } }
    Write-Out 'copy.txt' @((Get-ChildItem -Recurse -Filter *.kdbx "$m\droid").Count, (Get-ChildItem -Recurse -Filter *.kdbx "$m\xc").Count)
}

Step 'files' {
    if (-not $DroidWork -or -not $XcWork) { throw 'files needs -DroidWork and -XcWork.' }
    # The first two counts of the agent's survey of what the runs left: every database under each
    # work directory, whether or not it was copied.
    Write-Out 'files.txt' @((Get-ChildItem -Force $XcWork -Recurse -Filter *.kdbx).Count, (Get-ChildItem -Force $DroidWork -Recurse -Filter *.kdbx).Count)
}

Step 'lists' {
    $droid = Get-ChildItem -Recurse -Filter *.kdbx "$m\droid" | Sort-Object FullName | ForEach-Object FullName
    [IO.File]::WriteAllLines("$out\droid.lst", [string[]]$droid, $utf8)
    $xc = @(Get-ChildItem -Recurse -Filter *.kdbx "$m\xc\base", "$m\xc\A", "$m\xc\A247" | Sort-Object FullName | ForEach-Object FullName)
    $xc += Get-ChildItem -Recurse -Filter *.kdbx "$m\xc\C" | Where-Object { ($_.Directory.Name -like '*-CP-r1' -and $_.Name -eq 'C.kdbx') -or ($_.Directory.Name -like '*-PC-r1' -and $_.Name -eq 'P.kdbx') } | Sort-Object FullName | ForEach-Object FullName
    [IO.File]::WriteAllLines("$out\xc.lst", [string[]]$xc, $utf8)
    $fast = Get-Content "$out\xc.lst" | Where-Object { $_ -notmatch '\\slow' -and $_ -notmatch 'C-slow|P-plain-slow|P-moved-slow' }
    [IO.File]::WriteAllLines("$out\xc-fast.lst", [string[]]$fast, $utf8)
    $sel = Get-Content "$out\xc.lst" | Where-Object { $_ -match '\\(2\.4\.2|2\.8\.0)\\slow-fresh-(CP|PC)-r1\\' -or $_ -match '\\2\.8\.0\\slowm-fresh-(CP|PC)-r1\\' }
    [IO.File]::WriteAllLines("$out\xc-slow.lst", [string[]]$sel, $utf8)
}

Step 'reread' {
    foreach ($run in @(@('droid.lst', 'droid-reread.txt', "$m\droid\"), @('xc-fast.lst', 'xc-reread-fast.txt', "$m\xc\"), @('xc-slow.lst', 'xc-reread-slow.txt', "$m\xc\"))) {
        $list = Join-Path $out $run[0]
        $rootCut = $run[2]
        $clock = [Diagnostics.Stopwatch]::StartNew()
        Write-Out $run[1] (Invoke-Native { & $ps -NoProfile -ExecutionPolicy Bypass -File $reread -KeePass $KeePassDir -List $list -Password $password -Root $rootCut })
        Write-Host ([string]::Format([Globalization.CultureInfo]::InvariantCulture, '  {0}: {1:0.0} s', $run[1], $clock.Elapsed.TotalSeconds))
    }
}

Step 'counts' {
    $l = Get-Content "$out\xc-fast.lst"
    $lines = @()
    $lines += ($l | Where-Object { $_ -match '\\data\\xc\\C\\' }).Count
    $lines += ($l | Where-Object { $_ -match '\\data\\xc\\(A|A247|base)\\' }).Count
    $lines += ($l | Where-Object { $_ -match '\\data\\xc\\C\\' }) | ForEach-Object { ($_ -split '\\')[-3] } | Group-Object | ForEach-Object { "$($_.Name) $($_.Count)" }
    if ($XcWork) { $lines += Get-ChildItem (Join-Path $XcWork 'C\2.4.1') | Select-Object -ExpandProperty Name }
    Write-Out 'counts.txt' $lines
}

Step 'cells' {
    $l = Get-Content "$out\droid-reread.txt"; $cur = $null; $rows = @{}; $e1 = ''
    foreach ($x in $l) {
        if ($x -match '^(\S+\.kdbx) \|') { $cur = $Matches[1]; continue }
        if (-not $cur -or $cur -notmatch '^B\\' -or $cur -match '-src\.kdbx$') { continue }
        if ($x -match '^\s+entry E1 \S+ in G mtime (\S+) rule (\S)') { $e1 = "E1 $($Matches[1].Substring(0,10)) $($Matches[2])"; $rows[$cur] = $e1 + ' | no history'; continue }
        if ($x -match '^\s+history (\d+): mtime (\S+) rule (\S) strings \{(.*)\}' -and $rows.ContainsKey($cur)) { $rows[$cur] = $e1 + " | h$($Matches[1]) $($Matches[2].Substring(0,10)) $($Matches[3]) {$($Matches[4])}" }
    }
    $g = $rows.GetEnumerator() | ForEach-Object { $cell = ($_.Key -split '\\')[1] + ' ' + (($_.Key -split '\\')[2] -replace '-r\d$', ''); [pscustomobject]@{Cell = $cell; Value = $_.Value } } | Group-Object Cell, Value | Sort-Object Name
    Write-Out 'cells-android.txt' ($g | ForEach-Object { "{0}  x{1}" -f $_.Name, $_.Count })
}

Step 'tally' {
    $all = Get-Content "$out\droid-reread.txt", "$out\xc-reread-fast.txt", "$out\xc-reread-slow.txt"
    $e = $all | Where-Object { $_ -match '^\s+(entry (E1|E2)|history)' }
    $lines = @()
    $lines += "lines $($e.Count)"
    $lines += ($e | Where-Object { $_ -match '^\s+entry' -and ($_ -notmatch 'Password=<protected 17>' -or $_ -notmatch 'Title=E[12]') }).Count
    $lines += ($all | Where-Object { $_ -match 'FAILED' }).Count
    $lines += ($all | Where-Object { $_ -match '\| deleted [1-9]' }).Count
    $lines += ($all | Where-Object { $_ -match 'binaries [1-9]' -or $_ -match 'tags \[[^\]]' }).Count
    $lines += ($all | Where-Object { $_ -match '^\s+history 2' }).Count
    # The agent's next command: the lines that matched `FAILED`.
    $lines += $all | Where-Object { $_ -match 'FAILED' }
    Write-Out 'tally.txt' $lines
}

Step 'xc-merge' {
    $l = Get-SpikeOutput 'keepassxc-merge.txt'
    # print distinct "shapes" with release/set/C/target collapsed over runs
    $seen = @{}; foreach ($x in $l[1..($l.Count - 1)]) { $f = $x -split "`t"; $short = ($f[0..3] -join ' '); if (-not $seen.ContainsKey($short)) { $seen[$short] = @() }; if ($seen[$short] -notcontains ($f[5] + ' | ' + $f[6] + ' | ' + $f[7])) { $seen[$short] += ($f[5] + ' | ' + $f[6] + ' | ' + $f[7]) } }
    Write-Out 'cells-keepassxc.txt' $(foreach ($k in $seen.Keys | Sort-Object) { "$k  [variants: $($seen[$k].Count)]" })
}

Step 'hashes' {
    if (-not $DroidWork) { throw 'hashes needs -DroidWork.' }
    $w = $DroidWork; $ok = 0; $bad = @()
    foreach ($name in 'android-merge.txt', 'android-save.txt', 'android-base.txt') {
        $l = @(Get-SpikeOutput $name)
        for ($i = 0; $i -lt $l.Count; $i++) {
            if ($l[$i] -match '^\s+path: (\S+)$') {
                $p = $Matches[1]
                if ($l[$i + 1] -match 'sha256: ([0-9A-F]{64})') {
                    $want = $Matches[1]; $f = Join-Path $w $p
                    if (Test-Path -LiteralPath $f) { $h = (Get-FileHash -LiteralPath $f).Hash; if ($h -eq $want) { $ok++ } else { $bad += "$p differs" } } else { $bad += "$p missing" }
                }
            }
        }
    }
    Write-Out 'hash-results.txt' (@("match $ok") + $bad)
}

Step 'downloads' {
    if (-not $Downloads) { Write-Host '  downloads: skipped, no -Downloads given'; return }
    # `-File` hands a list over as one string, so a comma separates the directories.
    $Downloads = @($Downloads | ForEach-Object { $_ -split ',' } | Where-Object { $_ })
    $pinned = @{}
    foreach ($line in Get-Content (Join-Path $Pins 'keepassxc.sha256'), (Join-Path $Pins 'keepass.sha256')) { if ($line -match '^([0-9a-fA-F]{64})\s+\*?(\S+)$') { $pinned[$Matches[2]] = $Matches[1].ToUpper() } }
    foreach ($line in Get-Content (Join-Path $Pins 'android.sha256')) { if ($line -match '^([0-9a-f]{64})\s+(\S+)/(\S+)\s') { $pinned[$Matches[3]] = $Matches[1].ToUpper() } }
    $files = Get-ChildItem -Recurse -File $Downloads | Where-Object { $_.Extension -in '.zip', '.apk' }
    Write-Out 'hash-downloads.txt' $(foreach ($f in $files) { $h = (Get-FileHash -LiteralPath $f.FullName -Algorithm SHA256).Hash; $want = $pinned[$f.Name]; "{0}  {1}" -f $f.Name, $(if ($want -eq $null) { 'NOT PINNED' } elseif ($want -eq $h) { 'MATCH' } else { "MISMATCH $h" }) })
}

Step 'wsl' {
    Write-Out 'wsl.txt' (Invoke-Native { wsl.exe -l -v } | ForEach-Object { $_ -replace "`0", '' })
}

Step 'slow-bases' {
    # Added on the branch: the five slow bases, which no list of the agent's names, so that the
    # `SettingsChanged` the slow merge targets carry can be traced to the base they came from.
    $list = Join-Path $out 'xc-slow-bases.lst'
    [IO.File]::WriteAllLines($list, [string[]] @(Get-Content "$out\xc.lst" | Where-Object { $_ -match '\\base\\' -and $_ -match 'slow' }), $utf8)
    Write-Out 'xc-reread-slow-bases.txt' (Invoke-Native { & $ps -NoProfile -ExecutionPolicy Bypass -File $reread -KeePass $KeePassDir -List $list -Password $password -Root "$m\xc\" })
}

Step 'limits' {
    $all = Get-Content "$out\droid-reread.txt", "$out\xc-reread-fast.txt", "$out\xc-reread-slow.txt"
    $heads = @($all | Where-Object { $_ -match '\.kdbx \|' })
    $other = @($heads | Where-Object { $_ -notmatch '\| HistoryMaxItems 10 HistoryMaxSize 6291456 MaintenanceHistoryDays 365 \|' })
    Write-Out 'limits.txt' (@("files $($heads.Count)", "with other limits $($other.Count)") + $other)
}
