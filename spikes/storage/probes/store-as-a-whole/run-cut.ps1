<#
.SYNOPSIS
    Runs the parts cut from the run of rules per profile (`run.ps1 -Rows`), for the report
    `measured-rules-cut-parts.md`. ADDED (cut parts): a script of its own beside run.ps1.

.DESCRIPTION
    Windows PowerShell 5.1, from the branch root or anywhere. Provides and checks KeePass 2.62 and
    the four KeePassXC releases as run.ps1 does, then runs each part as a process of its own, at
    most -Parallel at a time, each writing what it prints into `out\cut-<Run>\<part>.txt` and the
    traces of the model's searches into `out\cut-<Run>\<part>.jsonl`. The databases go into
    `work\pe-r<Run>-*`, `work\po-r<Run>-*`, `work\d-r<Run>-*` and `work\r<Run>`, so that two runs can
    go at once; nothing shows a window. Each part's time goes to `out\cut-<Run>\times.txt`. Then
    `compare_cut.py` holds the run to `recorded-cut\` and makes the comparisons.

      b-deletions-<REL>, b-clear-<REL>, b-history-<REL>
                           probe\cut.py --rows empty: the part with keepassxc-cli REL in place of
                           2.8.0, for 2.4.2, 2.6.6, 2.7.12 and 2.8.0 (the last held to recorded-rows\)
      b-deletions-<REL>-whole, b-clear-<REL>-whole, b-history-<REL>-whole
                           probe\cut.py --decided: the same in the whole-rule form, for the three
                           releases recorded-decided\ does not hold
      b-random-rows-<REL>, b-random-rows-off-<REL>
                           probe\cut.py --rows empty|off random-rows REL 300 11, for every release
      b-random-draws       probe\cut.py draws 300 7: the merges b-random draws, by program
      b-slow, b-dedupe, b-dupes, b-repair, b-growth
                           probe\cut.py --rows empty: the parts of cases.py and growth.py, unchanged
      store-ext, store-scenarios, store-section6
                           model\cut_runs.py rows: the extended model and the replays per profile
      store-ext-whole, store-scenarios-whole, store-section6-whole
                           model\cut_runs.py whole: the same in the whole-rule form
      trace-<FORM>-<SEARCH>-<NREP>
                           model\search_trace.py: the searches traced history by history; rows with
                           search, acts and noauth, whole with search and noauth, 2 and 3 files

.PARAMETER Run
    The run, 1 or 2: its output directory and its databases.

.PARAMETER Parts
    Which parts to run; every part by default.

.PARAMETER Parallel
    How many parts run at once.
#>
param([Parameter(Mandatory)] [ValidateSet('1', '2')] [string] $Run, [string[]] $Parts = @(), [int] $Parallel = 4)

$ErrorActionPreference = 'Stop'
$family = $PSScriptRoot
$root = (Resolve-Path (Join-Path $family '..\..\..\..')).Path
. (Join-Path $root 'tools\KeePass.ps1')
. (Join-Path $root 'tools\KeePassXC.ps1')
[void](Get-KeePassExe)
foreach ($version in '2.4.2', '2.6.6', '2.7.12', '2.8.0') { [void](Get-KeePassXCCli -Version $version) }

$model = Join-Path $family 'model'
$probe = Join-Path $family 'probe'
$out = Join-Path $family "out\cut-$Run"
$all = [ordered]@{}
# the longest first, so that it does not end the run alone
$all['b-slow'] = @("$probe\cut.py", '--rows', 'empty', '--run', $Run, 'slow')
foreach ($form in 'rows', 'whole') {
    foreach ($search in 'search', 'acts', 'noauth') {
        if ($form -eq 'whole' -and $search -eq 'acts') { continue }
        foreach ($nrep in '2', '3') {
            $name = "trace-$form-$search-$nrep"
            $all[$name] = @("$model\search_trace.py", $form, $search, $nrep, (Join-Path $out "$name.jsonl"))
        }
    }
}
foreach ($rel in '2.4.2', '2.6.6', '2.7.12', '2.8.0') {
    foreach ($p in 'deletions', 'clear', 'history') {
        $all["b-$p-$rel"] = @("$probe\cut.py", '--rows', 'empty', '--run', $Run, $p, $rel)
        if ($rel -ne '2.8.0') {
            $all["b-$p-$rel-whole"] = @("$probe\cut.py", '--decided', '--run', $Run, $p, $rel)
        }
    }
    $all["b-random-rows-$rel"] = @("$probe\cut.py", '--rows', 'empty', '--run', $Run, 'random-rows', $rel, '300', '11')
    $all["b-random-rows-off-$rel"] = @("$probe\cut.py", '--rows', 'off', '--run', $Run, 'random-rows', $rel, '300', '11')
}
$all['b-random-draws'] = @("$probe\cut.py", '--rows', 'empty', '--run', $Run, 'draws', '300', '7')
foreach ($p in 'dedupe', 'dupes', 'repair', 'growth') {
    $all["b-$p"] = @("$probe\cut.py", '--rows', 'empty', '--run', $Run, $p)
}
foreach ($p in 'ext', 'scenarios', 'section6') {
    $all["store-$p"] = @("$model\cut_runs.py", 'rows', $p)
    $all["store-$p-whole"] = @("$model\cut_runs.py", 'whole', $p)
}

# Through -File a list arrives as one string, so commas are split here.
$Parts = @($Parts | ForEach-Object { $_ -split ',' } | ForEach-Object { $_.Trim() } | Where-Object { $_ })
if ($Parts.Count -eq 0) { $Parts = @($all.Keys) }
foreach ($p in $Parts) { if (-not $all.Contains($p)) { throw "No part named $p." } }

if (-not (Test-Path -LiteralPath $out)) { [void](New-Item -ItemType Directory -Path $out -Force) }
foreach ($p in $Parts) {
    foreach ($ext in '.txt', '.err', '.jsonl') {
        $f = Join-Path $out "$p$ext"
        if (Test-Path -LiteralPath $f) { Remove-Item -LiteralPath $f -Force }
    }
}

$running = @{}
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
            # A reader holding the file open makes the write fail; the run waits for it rather than ending.
            for ($try = 1; ; $try++) {
                try { Add-Content -Encoding UTF8 -LiteralPath (Join-Path $out 'times.txt') -Value $line; break }
                catch { if ($try -ge 30) { throw }; Start-Sleep -Seconds 2 }
            }
            Write-Host $line
            $running.Remove($p)
        }
    }
}
