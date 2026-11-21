# Runs two tiers at once, each through `run-tier.ps1` on a private desktop of its own, and waits for
# both. Each run is given as tier, worktree and output directory, separated by `|`.
#
#   parallel.ps1 -A 'unit|C:\work\first|C:\results\a' -B 'host|C:\work\second|C:\results\b' [-DesktopMutex]
param([Parameter(Mandatory)] [string] $A, [Parameter(Mandatory)] [string] $B, [switch] $DesktopMutex)
$here = Split-Path -Parent $PSCommandPath
$started = Get-Date
$processes = foreach ($spec in $A, $B) {
    $tier, $worktree, $out = $spec.Split('|')
    # Quoted by hand: `Start-Process` joins its arguments with spaces and quotes none of them.
    $arguments = @('-NoProfile', '-ExecutionPolicy', 'Bypass', '-File', "`"$here\run-tier.ps1`"", '-Tier', $tier, '-Worktree', "`"$worktree`"", '-Out', "`"$out`"")
    if ($DesktopMutex) { $arguments += '-DesktopMutex' }
    Start-Process powershell.exe -WindowStyle Hidden -PassThru -ArgumentList $arguments
}
foreach ($p in $processes) { $p.WaitForExit() }
"both done in $((Get-Date) - $started)"
foreach ($spec in $A, $B) {
    $out = $spec.Split('|')[2]
    "--- $out"
    Get-Content "$out\wallclock.txt"
    Get-Content "$out\summary.txt"
}
