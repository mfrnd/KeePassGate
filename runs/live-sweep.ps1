# Runs each fixture of the end to end tier alone on a private desktop, in sequence, and
# writes a line for each to `sweep.txt`: what the trx counted, how long it took, and the first
# failure's message. A fixture still running after -Minutes is stopped, its process tree only.
#
#   live-sweep.ps1 -Worktree <built worktree> -Out <directory> [-Fixtures a,b] [-Minutes 10]
#                  [-Before <script>] [-After <script>]
#
# -Before and -After name scripts run around each fixture, which is where a machine whose desktop
# is shared takes and gives back its lock. They are not optional in spirit: a Live session ends
# every KeePass running from under `%ProgramData%\KeePassGateLive`, so a fixture run here ends any
# other Live run's KeePass on the machine, whatever desktop that run is on.
#
# `DocumentationScreenshots` is run with `KPG_SCREENSHOT_DIR` set to a directory under -Out, so
# that it writes its pictures there and nowhere in the worktree.
param(
    [Parameter(Mandatory)] [string] $Worktree,
    [Parameter(Mandatory)] [string] $Out,
    [string[]] $Fixtures = @(
        'LiveStoreTests', 'LiveGrantPickerCloseTests', 'LiveAbandonedSaveTests', 'LiveApiTests',
        'LiveAttachmentEditorTests', 'LiveAuthorityChooserTests', 'LiveAuthorityTests',
        'LiveCertificateTests', 'LiveChooserOverTheMainWindowTests', 'LiveCloseAfterTheFileTests',
        'LiveCloseBeforeTheFileTests', 'LiveExitUnderARenewalTests', 'LiveExitUnderAPairingTests',
        'LiveGateTests', 'LiveGrantEditorTests', 'LiveKeePassTests', 'LiveLockTests',
        'LiveLockTimerTests', 'LiveOwedAnswerAtExitTests', 'LivePairingTests',
        'LiveReferenceClientTests', 'LiveRenewalTests', 'LiveRuleOkInsideASaveTests',
        'LiveRuleSeededStatesTests', 'LiveRuleWaitsForTheSaveTests', 'LiveSessionLockTests',
        'LiveStoreSetBackTests', 'LiveTanWizardTests', 'DocumentationScreenshots'),
    [int] $Minutes = 10,
    [string] $Before,
    [string] $After)

$root = Split-Path -Parent (Split-Path -Parent $PSCommandPath)
New-Item -ItemType Directory -Force $Out | Out-Null
$summary = Join-Path $Out 'sweep.txt'
$version = (Get-Content (Join-Path $Worktree 'keepass.version') -Raw).Trim()

foreach ($fixture in $Fixtures) {
    $dir = Join-Path $Out $fixture
    if (Test-Path $dir) { Remove-Item $dir -Recurse -Force }
    New-Item -ItemType Directory -Force $dir | Out-Null
    if ($Before) { & $Before }
    try {
        $pictures = Join-Path $dir 'pictures'
        $command = "`$env:KPG_SCREENSHOT_DIR = '$pictures'; dotnet test '$Worktree\tests\KeePassGate.Host.Tests\KeePassGate.Host.Tests.csproj' " +
            "-c Debug -p:KeePassDevDir='$Worktree\.keepass\$version' --no-build --nologo " +
            "--filter 'TestCategory=Live&FullyQualifiedName~.$fixture.' " +
            "--results-directory '$dir\results' --logger 'trx;LogFileName=live.trx'"
        $started = Get-Date
        $job = Start-Job -ScriptBlock { param($p, $w, $log, $c, $n) & $p -Dir $w -Log $log -Command $c -Name $n -SampleSeconds 5 } `
            -ArgumentList (Join-Path $root 'launcher\private-desktop.ps1'), $Worktree, (Join-Path $dir 'output.log'), $command, "live-$($fixture.ToLowerInvariant())"
        $stopped = ''
        if (-not (Wait-Job $job -Timeout ($Minutes * 60))) {
            $launcher = Get-Content (Join-Path $dir 'output.log.launcher') -Raw
            $child = [regex]::Match($launcher, 'started pid (\d+)').Groups[1].Value
            $stopped = " STOPPED after $Minutes min (tree of pid $child)"
            if ($child) { & taskkill.exe /T /F /PID $child 2>&1 | Out-File (Join-Path $dir 'stopped.txt') }
            Wait-Job $job -Timeout 120 | Out-Null
        }
        Remove-Job $job -Force
        $took = (Get-Date) - $started
        $trx = Join-Path $dir 'results\live.trx'
        $line = if (Test-Path $trx) {
            $run = ([xml] (Get-Content -LiteralPath $trx -Raw)).DocumentElement
            $c = $run.ResultSummary.Counters
            $first = $run.SelectSingleNode("*[local-name()='Results']/*[local-name()='UnitTestResult'][@outcome='Failed']/*[local-name()='Output']/*[local-name()='ErrorInfo']/*[local-name()='Message']")
            $other = $run.SelectSingleNode("*[local-name()='Results']/*[local-name()='UnitTestResult'][@outcome!='Failed' and @outcome!='Passed']/*[local-name()='Output']/*[local-name()='ErrorInfo']/*[local-name()='Message']")
            $why = if ($first) { ($first.InnerText.Trim() -split "\r?\n")[0] } elseif ($other) { 'not run: ' + ($other.InnerText.Trim() -split "\r?\n")[0] } else { '' }
            "$fixture | total $($c.total) passed $($c.passed) failed $($c.failed) not run $([int]$c.total - [int]$c.executed) | $([int]$took.TotalSeconds) s$stopped | $why"
        }
        else { "$fixture | no result file | $([int]$took.TotalSeconds) s$stopped" }
        $line | Add-Content $summary
        $line
    }
    finally {
        if ($After) { & $After }
    }
}
