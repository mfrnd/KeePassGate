# Prints what a trx counted, how long the run took, and every test that did not pass, with the
# first lines of its message. -Names prints only the failing tests' full names, a name per line.
param([Parameter(Mandatory)] [string] $Trx, [int] $Lines = 3, [switch] $Names)
$run = ([xml] (Get-Content -LiteralPath $Trx -Raw)).DocumentElement
$c = $run.ResultSummary.Counters
$times = $run.Times
$start = [DateTime] $times.start; $finish = [DateTime] $times.finish
$results = @($run.SelectNodes("*[local-name()='Results']/*[local-name()='UnitTestResult']"))
$defs = @{}
foreach ($d in $run.SelectNodes("*[local-name()='TestDefinitions']/*[local-name()='UnitTest']")) {
    $m = $d.SelectSingleNode("*[local-name()='TestMethod']")
    $defs[$d.GetAttribute('id')] = $m.GetAttribute('className') + '.' + $m.GetAttribute('name')
}
$failed = @($results | Where-Object { $_.GetAttribute('outcome') -eq 'Failed' })
$other = @($results | Where-Object { $_.GetAttribute('outcome') -ne 'Failed' -and $_.GetAttribute('outcome') -ne 'Passed' })
if ($Names) {
    foreach ($r in $failed) { $full = $defs[$r.GetAttribute('testId')]; if ($full) { $full } else { $r.GetAttribute('testName') } }
    exit 0
}
"total $($c.total) executed $($c.executed) passed $($c.passed) failed $($c.failed) not-executed $([int]$c.total - [int]$c.executed)"
"duration " + ($finish - $start).ToString()
"outcomes besides Passed/Failed: " + (($other | Group-Object { $_.GetAttribute('outcome') } | ForEach-Object { "$($_.Name)=$($_.Count)" }) -join ' ')
foreach ($r in $failed) {
    $full = $defs[$r.GetAttribute('testId')]
    if (-not $full) { $full = $r.GetAttribute('testName') }
    $msg = $r.SelectSingleNode("*[local-name()='Output']/*[local-name()='ErrorInfo']/*[local-name()='Message']")
    $text = if ($msg) { (($msg.InnerText.Trim() -split "\r?\n") | Select-Object -First $Lines) -join ' | ' } else { '' }
    "FAILED $full ($($r.GetAttribute('duration'))): $text"
}
