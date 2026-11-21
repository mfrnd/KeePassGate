# Runs the unit or the host tier of a built KeePassGate worktree on a private desktop, with the
# arguments `build.ps1` gives it (`--no-build`, the coverage collector, the trx logger, and for the
# host tier `TestCategory!=Live`), the results in a directory of the run's own, and the visible
# desktop sampled once a second meanwhile. Prints what the trx counted when it is done.
#
#   run-tier.ps1 -Tier unit|host -Worktree <built worktree> -Out <directory for this run>
#                [-Name <desktop name>] [-DesktopMutex] [-NoWatch]
#
# -DesktopMutex sets `KPG_MEASURE_DESKTOP` to the run's name before the tests start. It does
# nothing unless the worktree's `DesktopAttribute` was changed to add that variable to its mutex's
# name, which is how `RESULTS.md` measured the unit tier with the mutex named per run.
param(
    [Parameter(Mandatory)] [ValidateSet('unit', 'host')] [string] $Tier,
    [Parameter(Mandatory)] [string] $Worktree,
    [Parameter(Mandatory)] [string] $Out,
    [string] $Name,
    [switch] $DesktopMutex,
    [switch] $NoWatch)

$root = Split-Path -Parent (Split-Path -Parent $PSCommandPath)
if (-not $Name) { $Name = Split-Path -Leaf $Out }
if (Test-Path $Out) { Remove-Item $Out -Recurse -Force }
New-Item -ItemType Directory -Force $Out | Out-Null
$version = (Get-Content (Join-Path $Worktree 'keepass.version') -Raw).Trim()
$project = if ($Tier -eq 'unit') { "$Worktree\tests\KeePassGate.Tests\KeePassGate.Tests.csproj" } else { "$Worktree\tests\KeePassGate.Host.Tests\KeePassGate.Host.Tests.csproj" }
$filter = if ($Tier -eq 'host') { " --filter 'TestCategory!=Live'" } else { '' }
$command = "dotnet test '$project' -c Debug -p:KeePassDevDir='$Worktree\.keepass\$version' --no-build --nologo$filter " +
    "--collect:'Code Coverage;Format=cobertura' --results-directory '$Out\results' --logger 'trx;LogFileName=$Tier.trx'"
if ($DesktopMutex) { $command = "`$env:KPG_MEASURE_DESKTOP = '.$Name'; " + $command }

$stop = "$Out\watch.stop"
if (-not $NoWatch) {
    $watch = Start-Process powershell.exe -WindowStyle Hidden -PassThru -ArgumentList @(
        '-NoProfile', '-ExecutionPolicy', 'Bypass', '-File', "`"$root\runs\watch-visible.ps1`"", '-Out', "`"$Out\visible.csv`"", '-Stop', "`"$stop`"", '-Minutes', '40')
}
$started = Get-Date
& "$root\launcher\private-desktop.ps1" -Dir $Worktree -Log "$Out\output.log" -Command $command -Name $Name
$took = (Get-Date) - $started
if (-not $NoWatch) { New-Item $stop -ItemType File | Out-Null; $watch.WaitForExit(15000) | Out-Null }
"$Name wall clock $took" | Tee-Object "$Out\wallclock.txt"
$trx = "$Out\results\$Tier.trx"
if (Test-Path $trx) { & "$root\runs\trx-summary.ps1" -Trx $trx | Tee-Object "$Out\summary.txt" } else { "no trx" }
