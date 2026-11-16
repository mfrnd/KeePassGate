<#
.SYNOPSIS
    Runs the family store-as-a-whole: the recommended store in the design's model, and over real
    files through KeePassLib 2.62 and keepassxc-cli 2.4.2, 2.6.6, 2.7.12 and 2.8.0.

.DESCRIPTION
    Windows PowerShell 5.1, from the branch root or anywhere. Provides and checks KeePass 2.62 and
    the four KeePassXC releases through the branch's tools, empties `out\` and `work\`, then runs
    each part as a process of its own, at most -Parallel at a time, each writing what it prints
    into `out\<part>.txt`. The databases go into `work\`, which is not committed; nothing shows a
    window. Each part's time goes to `out\times.txt`. Then `compare.py` holds `out\` to `recorded\`.

      design-full          model\crdt_model.py full: the copy with every new switch at its default
      store-search         model\store_runs.py search: the design's search on the store
      store-search-noauth  the same, with no hand deletion or edit of the authority's entry
      store-ablations      the cost of each change taken by decision
      store-ext            the extended model of the survival critique, on the store
      store-scenarios      the design's 20 replays on the store
      store-section6       the sequences of EVALUATION.md section 6
      store-repair-closes  histories the repair of change 7 closes, and those it leaves
      b-growth             probe\growth.py: B.7
      b-merges             probe\cases.py merges: B.1, B.3, B.4
      b-slow               probe\cases.py slow: the KeePassXC cells under the slow derivation
      b-dedupe             probe\cases.py dedupe: B.2
      b-deletions          probe\cases.py deletions: B.5
      b-clear              probe\cases.py clear: every deletion record removed (desktop measurement)
      b-dupes              probe\cases.py dupes: maintenance commands (desktop measurement)
      b-repair             probe\cases.py repair: B.6, with the import cases (desktop measurement)
      b-random             probe\cases.py random 300 7: the bridge over random histories (C)

    With -Decided, the store as decided on 2026-11-16 instead: no repair after a Synchronize, and
    every file read under the history rule. Its parts write into `out\decided\` and are compared
    with `recorded-decided\`; the databases go into `work\d-<part>`:

      store-search         store_runs.py --decided search: the search with no repair
      store-search-noauth  the same, with no hand deletion or edit of the authority's entry
      store-ext            the extended model with no repair
      b-growth             growth.py --decided: B.7, read under the history rule
      b-merges, b-dedupe, b-deletions, b-clear, b-dupes, b-repair, b-random
                           cases.py --decided: the same parts with no repair and the history rule
      b-history            cases.py --decided history: each kind of store entry edited, the history
                           rule beside the plain stop, then merged both ways

    With -Rewrite (ADDED, two readings), the store as decided and an edited record written again at
    the plugin's next own write, in two forms: `backup` (the edited version put into the history, as
    an edit through KeePass's own path does) and `cleared` (the entry's history emptied). Output in
    `out\rewrite\`, compared with `recorded-rewrite\`; databases in `work\wb-*`, `work\wc-*` and
    `work\wn-*`:

      one, alternate, two, prune
                           readings.py --rewrite backup: the parts of reading 1
      b-random             cases.py --rewrite backup random 300 7
      one-cleared, alternate-cleared, two-cleared, prune-cleared, b-random-cleared
                           the same in the form `cleared`
      two-none, prune-none the control: the same two-gate sequences with nothing written again

    With -Copies (ADDED, two readings), the store as decided read with a record that can only take
    access away counted from a copy under another identifier, held to the completeness check like
    any record (`held`) and, in the parts ending in `-exempt`, not (`exempt`). Output in
    `out\copies\`, compared with `recorded-copies\`; databases in `work\ch-*` and `work\ce-*`:

      b-merges, b-dedupe, b-deletions, b-clear, b-dupes, b-repair, b-random
                           cases.py --copies held: the parts of the store as decided
      copies               readings.py --copies held copies: copies that are not an import
      the same names ending in -exempt
                           the same with --copies exempt

.PARAMETER Parts
    Which parts to run; every part by default.

.PARAMETER Parallel
    How many parts run at once.

.PARAMETER Decided
    Runs the store as decided rather than the recorded configuration with the repair.

.PARAMETER Rewrite
    Runs reading 1 of the store as decided: an edited record written again at the next own write.

.PARAMETER Copies
    Runs reading 2 of the store as decided: a record that takes access away read from a copy.

.EXAMPLE
    powershell.exe -NoProfile -ExecutionPolicy Bypass -File spikes\storage\probes\store-as-a-whole\run.ps1

.EXAMPLE
    powershell.exe -NoProfile -ExecutionPolicy Bypass -File spikes\storage\probes\store-as-a-whole\run.ps1 -Decided
#>
param([string[]] $Parts = @(), [int] $Parallel = 6, [switch] $Decided, [switch] $Rewrite, [switch] $Copies)
if (@($Decided.IsPresent, $Rewrite.IsPresent, $Copies.IsPresent | Where-Object { $_ }).Count -gt 1) {
    throw 'One of -Decided, -Rewrite, -Copies.'     # ADDED (two readings)
}

$ErrorActionPreference = 'Stop'
$family = $PSScriptRoot
$root = (Resolve-Path (Join-Path $family '..\..\..\..')).Path
. (Join-Path $root 'tools\KeePass.ps1')
. (Join-Path $root 'tools\KeePassXC.ps1')
[void](Get-KeePassExe)
foreach ($version in '2.4.2', '2.6.6', '2.7.12', '2.8.0') { [void](Get-KeePassXCCli -Version $version) }

$model = Join-Path $family 'model'
$probe = Join-Path $family 'probe'
$all = [ordered]@{
    'design-full'         = @("$model\crdt_model.py", 'full')
    'store-search'        = @("$model\store_runs.py", 'search')
    'store-search-noauth' = @("$model\store_runs.py", 'search-noauth')
    'store-ablations'     = @("$model\store_runs.py", 'ablations')
    'store-ext'           = @("$model\store_runs.py", 'ext', '8000')
    'store-scenarios'     = @("$model\store_runs.py", 'scenarios')
    'store-section6'      = @("$model\store_runs.py", 'section6')
    'store-repair-closes' = @("$model\repair_closes.py", '20000')
    'b-growth'            = @("$probe\growth.py")
    'b-merges'            = @("$probe\cases.py", 'merges')
    'b-slow'              = @("$probe\cases.py", 'slow')
    'b-dedupe'            = @("$probe\cases.py", 'dedupe')
    'b-deletions'         = @("$probe\cases.py", 'deletions')
    'b-clear'             = @("$probe\cases.py", 'clear')
    'b-dupes'             = @("$probe\cases.py", 'dupes')
    'b-repair'            = @("$probe\cases.py", 'repair')
    'b-random'            = @("$probe\cases.py", 'random', '300', '7')
}
if ($Decided) {
    $all = [ordered]@{
        'store-search'        = @("$model\store_runs.py", '--decided', 'search')
        'store-search-noauth' = @("$model\store_runs.py", '--decided', 'search-noauth')
        'store-ext'           = @("$model\store_runs.py", '--decided', 'ext', '8000')
        'b-growth'            = @("$probe\growth.py", '--decided')
        'b-merges'            = @("$probe\cases.py", '--decided', 'merges')
        'b-dedupe'            = @("$probe\cases.py", '--decided', 'dedupe')
        'b-deletions'         = @("$probe\cases.py", '--decided', 'deletions')
        'b-clear'             = @("$probe\cases.py", '--decided', 'clear')
        'b-dupes'             = @("$probe\cases.py", '--decided', 'dupes')
        'b-repair'            = @("$probe\cases.py", '--decided', 'repair')
        'b-random'            = @("$probe\cases.py", '--decided', 'random', '300', '7')
        'b-history'           = @("$probe\cases.py", '--decided', 'history')
    }
}
# ADDED (two readings): the two readings, each a configuration of its own
$config = $null
if ($Rewrite) {
    $config = 'rewrite'
    $all = [ordered]@{}
    foreach ($form in 'backup', 'cleared') {
        $suffix = if ($form -eq 'backup') { '' } else { '-cleared' }
        foreach ($p in 'one', 'alternate', 'two', 'prune') {
            $all["$p$suffix"] = @("$probe\readings.py", '--rewrite', $form, $p)
        }
        $all["b-random$suffix"] = @("$probe\cases.py", '--rewrite', $form, 'random', '300', '7')
    }
    foreach ($p in 'two', 'prune') { $all["$p-none"] = @("$probe\readings.py", '--rewrite', 'none', $p) }
}
if ($Copies) {
    $config = 'copies'
    $all = [ordered]@{}
    foreach ($variant in 'held', 'exempt') {
        $suffix = if ($variant -eq 'held') { '' } else { '-exempt' }
        foreach ($p in 'merges', 'dedupe', 'deletions', 'clear', 'dupes', 'repair') {
            $all["b-$p$suffix"] = @("$probe\cases.py", '--copies', $variant, $p)
        }
        $all["b-random$suffix"] = @("$probe\cases.py", '--copies', $variant, 'random', '300', '7')
        $all["copies$suffix"] = @("$probe\readings.py", '--copies', $variant, 'copies')
    }
}
# Through -File a list arrives as one string, so commas are split here.
$Parts = @($Parts | ForEach-Object { $_ -split ',' } | ForEach-Object { $_.Trim() } | Where-Object { $_ })
if ($Parts.Count -eq 0) { $Parts = @($all.Keys) }
foreach ($p in $Parts) { if (-not $all.Contains($p)) { throw "No part named $p." } }

$out = Join-Path $family 'out'
$work = Join-Path $family 'work'
if ($Decided) { $out = Join-Path $out 'decided' }
if ($config) { $out = Join-Path $out $config }      # ADDED (two readings)
if ($Parts.Count -eq $all.Count) {
    if ($config) {
        # ADDED (two readings): the configuration's own output and databases only
        if (Test-Path -LiteralPath $out) { Remove-Item -LiteralPath $out -Recurse -Force }
        $prefixes = if ($Rewrite) { 'wb-*', 'wc-*', 'wn-*' } else { 'ch-*', 'ce-*' }
        if (Test-Path -LiteralPath $work) {
            foreach ($f in $prefixes) { Get-ChildItem -LiteralPath $work -Directory -Filter $f | Remove-Item -Recurse -Force }
        }
    } elseif ($Decided) {
        # the decided parts' own output and databases only
        if (Test-Path -LiteralPath $out) { Remove-Item -LiteralPath $out -Recurse -Force }
        if (Test-Path -LiteralPath $work) {
            Get-ChildItem -LiteralPath $work -Directory -Filter 'd-*' | Remove-Item -Recurse -Force
        }
    } else {
        foreach ($dir in $out, $work) {
            if (Test-Path -LiteralPath $dir) { Remove-Item -LiteralPath $dir -Recurse -Force }
        }
    }
}
foreach ($dir in $out, $work) {
    if (-not (Test-Path -LiteralPath $dir)) { [void](New-Item -ItemType Directory -Path $dir -Force) }
}
# A part named alone replaces only its own output; each part's databases are in a directory of
# its own under work\, which the part empties itself.
foreach ($p in $Parts) {
    foreach ($ext in '.txt', '.err') {
        $f = Join-Path $out "$p$ext"
        if (Test-Path -LiteralPath $f) { Remove-Item -LiteralPath $f -Force }
    }
}

$running = @{}
$times = New-Object System.Collections.Generic.List[string]
$queue = New-Object System.Collections.Queue
foreach ($p in $Parts) { $queue.Enqueue($p) }
while ($queue.Count -gt 0 -or $running.Count -gt 0) {
    while ($queue.Count -gt 0 -and $running.Count -lt $Parallel) {
        $p = $queue.Dequeue()
        $argv = @('run', '--no-project', 'python', '-I', '-X', 'utf8') + $all[$p]
        $proc = Start-Process -FilePath 'uv' -ArgumentList $argv -NoNewWindow -PassThru `
            -RedirectStandardOutput (Join-Path $out "$p.txt") -RedirectStandardError (Join-Path $out "$p.err")
        [void]$proc.Handle     # so that ExitCode is kept once the process has gone
        $running[$p] = @{ Process = $proc; Started = Get-Date }
        Write-Host "started $p"
    }
    Start-Sleep -Seconds 5
    foreach ($p in @($running.Keys)) {
        $r = $running[$p]
        if ($r.Process.HasExited) {
            $line = '{0}: {1:N0} s, exit {2}' -f $p, ((Get-Date) - $r.Started).TotalSeconds, $r.Process.ExitCode
            $times.Add($line)
            Write-Host $line
            $running.Remove($p)
        }
    }
}
$times | Add-Content -Encoding UTF8 (Join-Path $out 'times.txt')
if ($config) {     # ADDED (two readings)
    & uv run --no-project python -I -X utf8 (Join-Path $family 'compare.py') "--$config"
} elseif ($Decided) {
    & uv run --no-project python -I -X utf8 (Join-Path $family 'compare.py') --decided
} else {
    & uv run --no-project python -I -X utf8 (Join-Path $family 'compare.py')
}
