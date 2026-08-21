<#
.SYNOPSIS
    Reproduces the two ways the environment can break `build.ps1` while nothing is actually wrong.

.DESCRIPTION
    The odd one out among the probes: PowerShell rather than C#, because what it measures is
    PowerShell. There is no CI, so `build.ps1` is the only gate this project has, and a gate that
    fails on a warning or cannot find its own cmdlets is worse than no gate, because it teaches
    whoever runs it to disbelieve the result.

    **First: a warning is not a failure.** A native command writing to stderr becomes a terminating
    error under `$ErrorActionPreference = 'Stop'` as soon as anything upstream merges the streams,
    which is what capturing a build's output does. `npm ci` warns about any configuration key it
    does not recognize, and installing the prose tools is exactly what a fresh clone does, which is
    how a gate ends on `RemoteException` with a successful command behind it.

    **Second: PowerShell 7 can take `Get-FileHash` away.** `pwsh` leads `PSModulePath` with its own
    module directories and a Windows PowerShell started from it inherits them, so
    `Microsoft.PowerShell.Utility` resolves to a copy marked for a runtime this is not. It refuses to
    load and the cmdlet is not missing so much as never was: the name is not a command at all.

    It measures rather than asserts: it runs both shapes of the step itself, and both module paths,
    so it says the same thing whatever `build.ps1` currently does. Run it from Windows PowerShell,
    like the others, and expect six lines.

.EXAMPLE
    powershell -NoProfile -File spikes\BuildStepProbe.ps1
#>

[CmdletBinding()]
param()

Set-StrictMode -Version Latest

function Write-Answer {
    param([string] $Question, [string] $Answer)
    Write-Host ("  {0,-46} {1}" -f $Question, $Answer)
}

# Two shapes for the same step, differing only in the preference in force while the command runs.
$stepScript = @'
$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

function Invoke-StepUnderStop {
    param([scriptblock] $Body)
    & $Body
    if ($LASTEXITCODE -ne 0) { throw "failed with exit code $LASTEXITCODE." }
}

function Invoke-StepUnderContinue {
    param([scriptblock] $Body)
    $before = $script:ErrorActionPreference
    $script:ErrorActionPreference = 'Continue'
    try { & $Body } finally { $script:ErrorActionPreference = $before }
    if ($LASTEXITCODE -ne 0) { throw "failed with exit code $LASTEXITCODE." }
}

# Warns and succeeds, which is what installing anything from npm does.
$noisy = { & cmd /c 'echo a warning that is not a failure 1>&2 & exit 0' }

# Fails, and has to keep failing under either preference. A gate that stops throwing is not a fix.
$broken = { & cmd /c 'echo this one really failed 1>&2 & exit 3' }

try { Invoke-StepUnderStop     $noisy;  'stop:noisy=survived' }     catch { 'stop:noisy=threw ' + $_.Exception.GetType().Name }
try { Invoke-StepUnderContinue $noisy;  'continue:noisy=survived' }  catch { 'continue:noisy=threw ' + $_.Exception.GetType().Name }
try { Invoke-StepUnderContinue $broken; 'continue:broken=survived' } catch { 'continue:broken=threw' }
'@

Write-Host ''
Write-Host 'A step that warns and succeeds' -ForegroundColor Cyan

$file = Join-Path ([System.IO.Path]::GetTempPath()) ('kpg-step-' + [System.Guid]::NewGuid().ToString('n') + '.ps1')
Set-Content -Path $file -Value $stepScript -Encoding UTF8

try {
    # The merge and a consumer are both the point. Stderr on its own goes to the console and
    # nothing throws; merged into a pipeline that somebody reads, each line becomes an error record
    # and `Stop` ends the run. Capturing a build's output is exactly that shape, which is why this
    # never showed up while the output was only being looked at.
    $lines = & powershell -NoProfile -Command "& '$file' 2>&1 | Out-String" 2>&1 | ForEach-Object { "$_" }
}
finally {
    Remove-Item $file -Force -ErrorAction SilentlyContinue
}

function Find-Answer {
    param([string] $Key)
    $hit = $lines | Where-Object { $_ -like "$Key=*" } | Select-Object -First 1
    if ($hit) { return $hit.Substring($Key.Length + 1) }
    return '(nothing said)'
}

Write-Answer 'a warning, under Stop' (Find-Answer 'stop:noisy')
Write-Answer 'a warning, under Continue' (Find-Answer 'continue:noisy')
Write-Answer 'a real failure, under Continue' (Find-Answer 'continue:broken')

Write-Host ''
Write-Host 'A cmdlet this runtime ships with' -ForegroundColor Cyan

$own = Join-Path $PSHOME 'Modules'

# Borrowed from PowerShell 7 rather than read off this process. A Windows PowerShell started from a
# shortcut has a perfectly good `PSModulePath` and would answer that nothing is wrong, which is true
# and not the question. Asking `pwsh` what it exports is the only reliable way to name its module
# directory: the `pwsh` on `PATH` is often an execution alias, and where that points is not where
# the modules are.
$pwsh = Get-Command pwsh -ErrorAction SilentlyContinue
$borrowed = if ($pwsh) {
    (& $pwsh.Source -NoProfile -Command '$env:PSModulePath' 2>&1 | Select-Object -First 1)
} else {
    ''
}

$probe = @'
param([switch] $Fix)
if ($Fix) {
    $own = Join-Path $PSHOME 'Modules'
    $env:PSModulePath = $own + ';' + (($env:PSModulePath -split ';' | Where-Object { $_ -and $_ -ne $own }) -join ';')
}
if (Get-Command Get-FileHash -ErrorAction SilentlyContinue) { 'found' } else { 'not a command at all' }
'@

$probeFile = Join-Path ([System.IO.Path]::GetTempPath()) ('kpg-hash-' + [System.Guid]::NewGuid().ToString('n') + '.ps1')
Set-Content -Path $probeFile -Value $probe -Encoding UTF8

try {
    # Windows PowerShell explicitly, because running this under PowerShell 7 asks a different
    # question and answers it happily.
    $shell = Join-Path $env:SystemRoot 'system32\WindowsPowerShell\v1.0\powershell.exe'

    if (-not $borrowed) {
        $leading = '(no pwsh on this machine)'
        $without = 'not measured'
        $with = 'not measured'
    }
    else {
        # Name the directory that actually shadows it rather than whatever happens to be first,
        # because the first entry is usually somebody's own modules and innocent.
        $leading = ($borrowed -split ';' |
            Where-Object { $_ -and (Test-Path (Join-Path $_ 'Microsoft.PowerShell.Utility')) } |
            Select-Object -First 1)
        if (-not $leading) { $leading = ($borrowed -split ';' | Select-Object -First 1) }
        $keep = $env:PSModulePath
        try {
            $env:PSModulePath = $borrowed
            $without = & $shell -NoProfile -File $probeFile 2>&1 | Select-Object -Last 1
            $with = & $shell -NoProfile -File $probeFile -Fix 2>&1 | Select-Object -Last 1
        }
        finally {
            $env:PSModulePath = $keep
        }
    }
}
finally {
    Remove-Item $probeFile -Force -ErrorAction SilentlyContinue
}

Write-Answer 'the copy that shadows it' $leading
Write-Answer 'Get-FileHash, as inherited' "$without"
Write-Answer 'Get-FileHash, own modules first' "$with"

Write-Host ''
Write-Host 'This runtime is PowerShell ' -NoNewline -ForegroundColor DarkGray
Write-Host $PSVersionTable.PSVersion -ForegroundColor DarkGray
Write-Host "and its own modules are at $own" -ForegroundColor DarkGray
Write-Host ''
