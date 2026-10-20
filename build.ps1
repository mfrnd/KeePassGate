<#
.SYNOPSIS
    Builds, tests and deploys the KeePassGate plugin.

.DESCRIPTION
    This script is the gate, and CI runs the same one. Run it before every commit.

    Tasks:
      KeePass  provide the pinned KeePass, which everything else needs
      Build    compile everything, in Debug and in Release
      Lint     check the prose: markdown rules and spelling
      Unit     run the tests that never load KeePass
      Host     run the tests that drive the real KeePass types in this process
      Coverage run both test tiers and hold what they reach above a floor
      Test     lint, compile, and run every test that does not need a running KeePass (the default)
      Live     run the end to end tests, which start a throwaway KeePass on this desktop
      All      Test then Live
      Deploy   copy the plugin into the KeePass used for development
      Clean    remove build output

.PARAMETER Task
    What to do. Defaults to Test.

.PARAMETER Configuration
    Debug or Release. Defaults to Debug.

.PARAMETER KeePassDir
    A KeePass to use instead of the one this repository pins. No default: leave it off and the
    pinned version is provided, which is the only arrangement anything is tested against.

.PARAMETER CoverageFloor
    The percentage of the plugin's own lines the tests may not fall below. Defaults to 80.

.EXAMPLE
    .\build.ps1
    .\build.ps1 -Task All
    .\build.ps1 -Task Deploy -Configuration Release
#>

[CmdletBinding()]
param(
    [ValidateSet('KeePass', 'Build', 'Lint', 'Unit', 'Host', 'Coverage', 'Test', 'Live', 'All', 'Deploy', 'Clean')]
    [string] $Task = 'Test',

    [ValidateSet('Debug', 'Release')]
    [string] $Configuration = 'Debug',

    # No default, deliberately. A default here would be a second answer to a question
    # `keepass.version` already answers, and the two would drift.
    [string] $KeePassDir,

    # The percentage the plugin's own lines may not fall below. A parameter rather than a
    # constant so that finding out where the number sits costs nothing, and moving the floor is a
    # commit somebody has to write a reason into.
    [int] $CoverageFloor = 80,

    # Print the tasks that are checks and exit. This is what CI asks for.
    [switch] $ListGates
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

# Which tasks are not checks: a prerequisite, the two that only compose others, and the ones that
# change something rather than judging it. Everything else is a check, which is the way round that
# fails closed, because a task added and forgotten about is then checked rather than skipped.
$notGates = @('KeePass', 'Test', 'All', 'Deploy', 'Clean')

# **A PowerShell 7 on the machine can leave this one unable to find its own cmdlets.** `pwsh` puts
# its module directories at the front of `PSModulePath` and a Windows PowerShell started from it
# inherits that, whereupon `Microsoft.PowerShell.Utility` resolves to a copy marked for a runtime
# this is not, refuses to load, and takes `Get-FileHash` with it. Not an error: the name is simply
# not a command, which is what makes it look like a broken script rather than a borrowed path.
#
# Putting this runtime's own modules first costs nothing when they already are, and keeps whatever
# else the machine put there. `BuildStepProbe.ps1` on `spikes/build-environment` reproduces it.
$ownModules = Join-Path $PSHOME 'Modules'
$env:PSModulePath = $ownModules + [System.IO.Path]::PathSeparator +
    (($env:PSModulePath -split [System.IO.Path]::PathSeparator |
        Where-Object { $_ -and $_ -ne $ownModules }) -join [System.IO.Path]::PathSeparator)

# Before the first cmdlet this script calls, because `Select-Object` below is exactly what a
# borrowed `PSModulePath` takes away, and `-ListGates` returns before reaching anything else.
# That is the first thing CI asks for.
# Read off the parameter's own attribute, so the workflow's job list and this task list are one fact
# rather than two. Captured here because inside a function `$MyInvocation` describes the function.
$declaredTasks = ($MyInvocation.MyCommand.Parameters['Task'].Attributes |
    Where-Object { $_ -is [System.Management.Automation.ValidateSetAttribute] } |
    Select-Object -First 1).ValidValues

# Every name above has to be one the parameter declares. A name matching no task is silently
# dropped by the filter below, so the list can carry one indefinitely and mean nothing by it, and
# the reverse is worse: a name that drifts from the task it excluded puts that tier into CI, or
# takes one out, and the filter reports neither. `-ListGates` exists so that the workflow's job list
# and this one are a single fact; that is only true while every entry here refers to something.
$strays = @($notGates | Where-Object { $declaredTasks -notcontains $_ })
if ($strays.Count -gt 0) {
    throw "notGates names $($strays -join ', '), which no task declares."
}

if ($ListGates) {
    $declaredTasks | Where-Object { $notGates -notcontains $_ }
    exit 0
}

$root = Split-Path -Parent $MyInvocation.MyCommand.Definition
. (Join-Path $root 'tools\KeePass.ps1')
$solution = Join-Path $root 'KeePassGate.sln'
$unitTests = Join-Path $root 'tests\KeePassGate.Tests\KeePassGate.Tests.csproj'
$hostTests = Join-Path $root 'tests\KeePassGate.Host.Tests\KeePassGate.Host.Tests.csproj'
$coverageDir = Join-Path $root '.cov'
$pluginProject = Join-Path $root 'src\KeePassGate\KeePassGate.csproj'
$pluginDll = Join-Path $root "src\KeePassGate\bin\$Configuration\net48\KeePassGate.dll"

# KeePass is a compile time reference, so nothing builds without it. Resolved once per run.
$script:keePassHome = ''
$script:compiled = @{}

# A step is judged by the number its command exits with, and by nothing else.
#
# **Writing to stderr is not failing.** Tools put progress, warnings and diagnostics there as a
# matter of course, and `npm ci` warns about any configuration key it does not recognize. Under
# `Stop`, PowerShell turns each of those lines into a terminating error the moment anything upstream
# merges the streams, which is what capturing a build's output does. The gate then ends on a warning,
# reporting `RemoteException` and naming nothing, while the command it ran had succeeded.
#
# It cost a fresh clone: with no `node_modules` the gate installs the prose tools itself, and
# installing them is exactly the step that warns. `BuildStepProbe.ps1` on
# `spikes/build-environment` reproduces both halves.
function Invoke-Step {
    param([string] $Name, [scriptblock] $Body)

    Write-Host ''
    Write-Host "==> $Name" -ForegroundColor Cyan

    # Set on the script scope rather than here, because a scriptblock runs in a child of the scope
    # it was written in and would never see a variable local to this function.
    $before = $script:ErrorActionPreference
    $script:ErrorActionPreference = 'Continue'
    try {
        & $Body
    }
    finally {
        $script:ErrorActionPreference = $before
    }

    if ($LASTEXITCODE -ne 0) {
        throw "$Name failed with exit code $LASTEXITCODE."
    }
}

# Provides the pinned KeePass unless somebody named their own, and answers with where it is. Every
# task that needs KeePass goes through this rather than assuming a directory, so a fresh clone
# builds without anybody being told to go and find one first.
function Resolve-KeePass {
    if ($KeePassDir) {
        # **Without a trailing separator**, which is how a directory is often written and which
        # Windows PowerShell breaks. It puts an argument holding a space in quotes and leaves a
        # backslash before the closing quote as it is, so
        # `-p:KeePassDevDir=C:\Program Files\KeePass\` reaches `dotnet` with that quote escaped:
        # the property swallows the argument after it, and the build fails on a reference path
        # that names neither. MSBuild adds the separator where it needs one. A drive's root keeps
        # it, having no space to quote and meaning the current directory on that drive without it.
        $directory = $KeePassDir.TrimEnd('\', '/')
        if ($directory.EndsWith(':')) {
            $directory += '\'
        }

        $exe = Join-Path $directory 'KeePass.exe'
        if (-not (Test-Path $exe)) {
            throw "No KeePass.exe at $exe. Drop -KeePassDir to use the version this repository pins."
        }

        return $directory
    }

    # Answered once per run. Four tasks need it and the check is cheap, but a step printed four
    # times reads like four things happening.
    if (-not $script:keePassHome) {
        Invoke-Step 'keepass' {
            $script:keePassHome = Install-KeePass
            $global:LASTEXITCODE = 0
        }
    }

    return $script:keePassHome
}

function Invoke-Build {
    # Per project, not once per run. Both tiers provide for themselves, so without a memo the
    # composition would compile and then compile again for nothing. A memo that remembers only
    # *that* something was built answers yes to a different question than it was asked. The
    # tests run `--no-build`, so a run that compiled the plugin alone and then said "already built"
    # would have them testing assemblies nobody made.
    param([string] $Project = $solution)

    if ($script:compiled[$Project]) {
        return
    }

    $keePass = Resolve-KeePass
    $script:compiled[$Project] = $true
    Invoke-Step "build ($Configuration)" {
        dotnet build $Project -c $Configuration -p:KeePassDevDir=$keePass --nologo
    }
}

# **Both configurations, in the task that only compiles.** Every other task builds and runs the one
# `-Configuration` names, Debug unless told otherwise, and Release is the build a plugin ships as,
# a different program from a Debug one. Nothing in the source differs by configuration, no `#if`,
# no `Debug.Assert` and no `[Conditional]`, so compiling the other one is all the checking it
# needs, and this is what says it still compiles on the day something does.
function Invoke-Compile {
    Invoke-Build

    $other = @('Debug', 'Release') | Where-Object { $_ -ne $Configuration }
    $keePass = Resolve-KeePass
    Invoke-Step "build ($other)" {
        dotnet build $solution -c $other -p:KeePassDevDir=$keePass --nologo
    }
}

# The prose checks. Node rather than PowerShell because markdownlint and cspell are the tools that
# do this well, and both are pinned in `package.json` so that every machine agrees on what clean
# means. They are part of `Test` rather than a task somebody remembers, because a check nobody runs
# is not a check.
function Invoke-Lint {
    if (-not (Get-Command npm -ErrorAction SilentlyContinue)) {
        throw 'npm was not found. The prose checks are markdownlint and cspell, both from npm. Install Node, then run npm ci.'
    }

    # Whether the tools are there, rather than whether a directory is. An interrupted install, a
    # half-finished clean, or a `node_modules` somebody emptied all leave the directory behind, and
    # a check that asks only for it then skips the install and fails two steps later with
    # `markdownlint-cli2 is not recognized`. That names a missing command, not the missing
    # install that explains it. `npm ci` is the same answer either way, so asking the better
    # question costs nothing.
    $shims = Join-Path $root 'node_modules\.bin'
    $missing = @('markdownlint-cli2', 'cspell' | Where-Object { -not (Test-Path (Join-Path $shims $_)) })

    # **And whether they are the ones the lockfile pins.** A shim says that a tool is there, not
    # which version, so after a commit that moves a pin a machine that installed before it would go
    # on checking by the old rules while CI, installing afresh, checked by the new ones. So the
    # lockfile's hash is written down beside what `npm ci` installed, and a different one, or none,
    # installs again. It lives inside `node_modules`, which `npm ci` deletes before installing, so
    # it can never outlive the install it describes.
    $lock = Join-Path $root 'package-lock.json'
    $record = Join-Path $root 'node_modules\.installed-from'
    $wanted = (Get-FileHash -LiteralPath $lock -Algorithm SHA256).Hash
    $installed = ''
    if (Test-Path $record) {
        $installed = (Get-Content -LiteralPath $record -Raw).Trim()
    }

    if ($missing.Count -gt 0 -or $installed -ne $wanted) {
        Invoke-Step 'installing the prose tools' {
            npm ci --no-audit --no-fund
        }

        Set-Content -LiteralPath $record -Value $wanted
    }

    Invoke-Step 'markdown' {
        npm run --silent lint:md
    }

    Invoke-Step 'spelling' {
        npm run --silent lint:spelling
    }

    # A promise made in a comment is still a promise. `TODO(step N)` says which step makes the
    # reference around it true, and this is what collects on it: a marker naming a step the build
    # order already calls done is a note somebody forgot to take out, and the reader it misleads
    # is the next person to trust the sentence it sits in.
    Invoke-Step 'todo markers' {
        & (Join-Path $root 'tools/Markers.ps1') -Root $root
    }

    # **A rule nobody checks is a rule until the first person who has not read it.** The ban on
    # dashes as punctuation is checked from the first commit that can check anything, because
    # a dash found by eye is found long after it has been copied into the next file. Cheap to
    # check and expensive to find, which is the whole case for a gate rather than a habit.
    Invoke-Step 'dashes' {
        & (Join-Path $root 'tools/Dashes.ps1') -Root $root
    }

    # A decision is cited by number rather than by title, so that one grep finds the paragraph
    # that settles something and every place leaning on it. This is what keeps those numbers
    # unique, inside their own document's hundred, and pointing at something that exists.
    # **A file the repository ignores has no business being tracked.** One gets there by adding
    # with `-A` while the ignore rule was not there yet, or by forcing it, and from then on every
    # clone carries it and every checkout of an early commit puts it back. Asked of git rather
    # than of a list here, so the answer is whatever `.gitignore` currently says.
    Invoke-Step 'tracked but ignored' {
        $stowaways = @(& git ls-files --cached --ignored --exclude-standard)
        $global:LASTEXITCODE = 0

        if ($stowaways.Count -gt 0) {
            foreach ($file in $stowaways) {
                Write-Host "  $file" -ForegroundColor Red
            }

            throw "$($stowaways.Count) tracked file(s) are excluded by .gitignore. Add files by name rather than with -A, and `git rm --cached` these."
        }

        Write-Host '  nothing tracked that .gitignore excludes.' -ForegroundColor DarkGray
    }

    Invoke-Step 'section anchors' {
        & (Join-Path $root 'tools/Anchors.ps1') -Root $root
    }

    # A doc comment that describes two members describes neither. It happens when a member moves and
    # its documentation does not, and the compiler is no help: it takes the last `<summary>` it sees
    # and never mentions the rest, so the wrong description sits on the wrong member until somebody
    # reads both.
    Invoke-Step 'doc comments' {
        & (Join-Path $root 'tools/DocComments.ps1')
    }
}

function Invoke-Unit {
    Invoke-Build
    $keePass = Resolve-KeePass
    $trx = Join-Path $coverageDir 'unit.trx'
    if (Test-Path $trx) { Remove-Item $trx -Force }

    Invoke-Step 'unit tests' {
        dotnet test $unitTests -c $Configuration -p:KeePassDevDir=$keePass --no-build --nologo `
            --collect:'Code Coverage;Format=cobertura' --results-directory $coverageDir `
            --logger 'trx;LogFileName=unit.trx'
    }

    Assert-Executed $trx 'unit'
}

function Invoke-Host {
    Invoke-Build
    $keePass = Resolve-KeePass
    $trx = Join-Path $coverageDir 'host.trx'
    if (Test-Path $trx) { Remove-Item $trx -Force }

    Invoke-Step 'host tests' {
        dotnet test $hostTests -c $Configuration -p:KeePassDevDir=$keePass --no-build --nologo `
            --filter 'TestCategory!=Live' `
            --collect:'Code Coverage;Format=cobertura' --results-directory $coverageDir `
            --logger 'trx;LogFileName=host.trx'
    }

    Assert-Executed $trx 'host'
}

# **A tier that ignores itself exits zero.** `Assert.Ignore` in a one time setup skips every test
# in the fixture, and `dotnet test` exits zero just the same for a filter that matches nothing and
# for an adapter that never loaded. A tier judged by its exit code would then report the branch
# checked when nothing ran: the outcome `gate.yml` already calls worse than a red one, guarded
# there for an empty task list and not for this. So every tier writes a result file and is judged
# on the count in it, which is read from the file because the console text is localized.
function Assert-Executed {
    param([string] $Trx, [string] $Tier)

    if (-not (Test-Path $Trx)) {
        throw "The $Tier tests produced no result file, so nothing can say what ran."
    }

    $counters = ([xml] (Get-Content -LiteralPath $Trx -Raw)).TestRun.ResultSummary.Counters
    $executed = [int] $counters.executed
    if ($executed -lt 1) {
        throw ("The $Tier tests ran nothing: $([int] $counters.total) found, " +
            "$([int] $counters.notExecuted) skipped. A tier that ignored itself has not passed.")
    }

    Write-Host "  $executed $Tier test(s) ran." -ForegroundColor DarkGray
}

# **Both tiers, because neither answers on its own.** A line the host tests reach and the unit
# tests do not is covered, and read apart the two say far less than they do together. This runs
# them rather than reading whatever is lying in `.cov`, so the number is about this working tree,
# and the gate can be asked for on its own, which is how CI asks for every other one.
#
# **Emptied first, because nothing else ever empties it.** `dotnet test` adds a directory of
# reports beside whatever the results directory already holds, and `tools/Coverage.ps1` combines
# every report under it. Left in place, every earlier run's reports would be combined with this
# one's: a line some earlier test reached would still count as covered at the same file and line,
# and each run would read more reports than the last.
function Invoke-Coverage {
    if (Test-Path $coverageDir) {
        Remove-Item $coverageDir -Recurse -Force
    }

    Invoke-Unit
    Invoke-Host
    Invoke-Step 'coverage' {
        & (Join-Path $root 'tools/Coverage.ps1') -Reports $coverageDir -Floor $CoverageFloor
    }
}

function Invoke-Test {
    Invoke-Lint
    Invoke-Coverage
}

# Prints what a failed end to end run leaves behind, because on a hosted runner the console is all
# that is kept: the job uploads nothing, so `live.trx` and the plugin's log go with the machine.
# Each failed test's name, message and stack, then the tail of any plugin log a session of this run
# did not get to delete. Bounded, so a cascade reads as its first few failures and a count rather
# than a flood.
#
# **It only reports.** The run has failed already and the caller passes that failure on, so nothing
# here may end the script: whatever this cannot read is said and passed over.
function Write-LiveEvidence {
    param([string] $Trx, [datetime] $Since)

    $shownTests = 5
    $shownChars = 2000
    $shownLines = 40

    function Tail([string] $Text, [int] $Lines) {
        $all = @($Text -split "\r?\n")
        if ($all.Count -le $Lines) { return $Text }
        return "... ($($all.Count - $Lines) earlier lines not shown)`n" + (($all | Select-Object -Last $Lines) -join "`n")
    }

    function Clip([string] $Text) {
        if ($Text.Length -le $shownChars) { return $Text }
        return $Text.Substring(0, $shownChars) + "`n... ($($Text.Length - $shownChars) more characters)"
    }

    Write-Host ''
    Write-Host '==> what the failed end to end run left behind' -ForegroundColor Cyan

    try {
        if (-not (Test-Path -LiteralPath $Trx)) {
            Write-Host "  No result file at $Trx, so no test can be named."
        }
        else {
            # By local name, because the file carries a namespace and a result with no output
            # has none of the elements that dotted access would have to find under strict mode.
            $run = ([xml] (Get-Content -LiteralPath $Trx -Raw)).DocumentElement
            $failed = @($run.SelectNodes(
                "*[local-name()='Results']/*[local-name()='UnitTestResult'][@outcome='Failed']"))

            Write-Host "  $($failed.Count) test(s) failed."
            foreach ($result in ($failed | Select-Object -First $shownTests)) {
                Write-Host ''
                Write-Host "  FAILED $($result.GetAttribute('testName'))" -ForegroundColor Red
                $output = $result.SelectSingleNode("*[local-name()='Output']")
                if ($null -eq $output) { continue }

                foreach ($part in 'Message', 'StackTrace') {
                    $node = $output.SelectSingleNode("*[local-name()='ErrorInfo']/*[local-name()='$part']")
                    if ($null -ne $node -and $node.InnerText) {
                        Write-Host "  ${part}:"
                        Write-Host (Tail (Clip $node.InnerText) $shownLines)
                    }
                }

                # Where the fixtures print the plugin's log, when the adapter attaches it to a test.
                $out = $output.SelectSingleNode("*[local-name()='StdOut']")
                if ($null -ne $out -and $out.InnerText) {
                    Write-Host '  Output, the end of it:'
                    Write-Host (Tail $out.InnerText $shownLines)
                }
            }

            if ($failed.Count -gt $shownTests) {
                Write-Host ''
                Write-Host "  ... and $($failed.Count - $shownTests) more failed test(s), named in $Trx."
            }
        }
    }
    catch {
        Write-Host "  The result file could not be read: $($_.Exception.Message)" -ForegroundColor Yellow
    }

    # A live session works under the common application data directory and deletes itself on tear
    # down, so a log is here only when a session did not get that far, which is the case most worth
    # seeing.
    try {
        $sessions = Join-Path ([Environment]::GetFolderPath('CommonApplicationData')) 'KeePassGateLive'
        $logs = @(Get-ChildItem -Path (Join-Path $sessions '*\keepass\keepassgate.log') -File -ErrorAction SilentlyContinue)
        if ($logs.Count -eq 0) {
            Write-Host ''
            Write-Host "  No plugin log was left under $sessions."
        }

        foreach ($log in $logs) {
            Write-Host ''
            # One an earlier run left is somebody else's evidence, and printing it here would put
            # it beside failures it has nothing to do with.
            if ($log.LastWriteTime -lt $Since) {
                Write-Host "  Plugin log $($log.FullName) is from an earlier run, $($log.LastWriteTime), and is not shown."
                continue
            }

            Write-Host "  Plugin log $($log.FullName), the end of it:"
            $stream = New-Object IO.FileStream($log.FullName, 'Open', 'Read', 'ReadWrite')
            try {
                $text = (New-Object IO.StreamReader($stream)).ReadToEnd()
            }
            finally {
                $stream.Dispose()
            }

            Write-Host (Tail $text $shownLines)
        }
    }
    catch {
        Write-Host "  The plugin log could not be read: $($_.Exception.Message)" -ForegroundColor Yellow
    }
}

function Invoke-Live {
    # A trap covers the whole function wherever it is written, so it is written first, and it
    # prints only once the tests have been started: a build that fails has its own output. `break`
    # passes the same failure on, so the task fails exactly as it would without this.
    $started = $null
    trap {
        if ($started) { Write-LiveEvidence $trx $started }
        break
    }

    Invoke-Build
    $keePass = Resolve-KeePass
    Write-Host ''
    Write-Host 'The end to end tests start a throwaway KeePass and type into it.' -ForegroundColor Yellow
    Write-Host 'Leave the desktop alone for about a minute.' -ForegroundColor Yellow
    # Written to a file rather than read off the console, because what this has to judge is a
    # count and the console text is localized.
    $results = Join-Path $root '.tmp'
    $trx = Join-Path $results 'live.trx'
    if (Test-Path $trx) { Remove-Item $trx -Force }
    $started = Get-Date

    Invoke-Step 'end to end tests' {
        dotnet test $hostTests -c $Configuration -p:KeePassDevDir=$keePass --no-build --nologo `
            --filter 'TestCategory=Live' --results-directory $results --logger 'trx;LogFileName=live.trx'
    }

    Assert-Executed $trx 'end to end'
}

function Invoke-Deploy {
    $keePass = Resolve-KeePass

    # Asked before anything is compiled, because it is the cheapest question here and nothing below
    # can change the answer. Asked last, a refusal arrived after a full build that was then thrown
    # away, on the one path whose point is a short edit-and-run loop.
    #
    # **Scoped to the KeePass being deployed into.** `-Name 'KeePass'` matches every process of that
    # name on the machine: the one somebody keeps their real database in, and a throwaway copy an
    # end to end run left behind. Neither holds a file in the directory being written, so refusing
    # on either is refusing for a reason that is not true, and the reason given names a file those
    # processes do not have open.
    $running = @()
    foreach ($process in @(Get-Process -Name 'KeePass' -ErrorAction SilentlyContinue)) {
        # A process this account cannot open has no readable path, and is not one of ours.
        $path = $null
        try { $path = $process.Path } catch { }

        if ($path -and $path.StartsWith($keePass, [System.StringComparison]::OrdinalIgnoreCase)) {
            $running += $process
        }
    }

    if ($running.Count -gt 0) {
        throw "KeePass is running from $keePass. Close it first, or the plugin file cannot be replaced."
    }

    # The plugin's own project rather than the solution. What is copied below comes out of one
    # output directory, and building the solution here compiles both test assemblies that no deploy
    # ever reads.
    Invoke-Build -Project $pluginProject

    $product = (Get-Item $pluginDll).VersionInfo.ProductName
    if ($product -ne 'KeePass Plugin') {
        throw "$pluginDll reports the product name '$product'. KeePass silently ignores any plugin that does not say 'KeePass Plugin'."
    }

    $target = Join-Path $keePass 'plugins'
    if (-not (Test-Path $target)) {
        New-Item -ItemType Directory -Path $target | Out-Null
    }

    Copy-Item $pluginDll $target -Force
    $symbols = [System.IO.Path]::ChangeExtension($pluginDll, '.pdb')
    if (Test-Path $symbols) {
        Copy-Item $symbols $target -Force
    }

    Write-Host "Deployed to $target" -ForegroundColor Green
}

function Invoke-Clean {
    # The coverage reports go too: they are what the tests wrote, as the binaries are what the
    # build wrote.
    if (Test-Path $coverageDir) {
        Write-Host "removing $coverageDir"
        Remove-Item $coverageDir -Recurse -Force
    }

    Get-ChildItem -Path (Join-Path $root 'src'), (Join-Path $root 'tests') -Include 'bin', 'obj' -Recurse -Directory |
        ForEach-Object {
            Write-Host "removing $($_.FullName)"
            Remove-Item $_.FullName -Recurse -Force
        }
}

switch ($Task) {
    'KeePass' { Resolve-KeePass | Out-Null; Test-KeePassLatest }
    'Build' { Invoke-Compile }
    'Lint' { Invoke-Lint }
    'Unit' { Invoke-Unit }
    'Host' { Invoke-Host }
    'Coverage' { Invoke-Coverage }
    'Test' { Invoke-Test }
    'Live' { Invoke-Live }
    'All' { Invoke-Test; Invoke-Live }
    'Deploy' { Invoke-Deploy }
    'Clean' { Invoke-Clean }
    default { throw "There is no case for task '$Task'. Every name the Task parameter allows needs one here, or a task would report success having run nothing." }
}

Write-Host ''
Write-Host "$Task finished." -ForegroundColor Green
