<#
.SYNOPSIS
    Builds, tests and deploys the KeePassGate plugin.

.DESCRIPTION
    This script is the gate, and CI runs the same one. Run it before every commit.

    Tasks:
      KeePass  provide the pinned KeePass, which everything else needs
      Build    compile everything, in Debug and in Release
      Lint     check the prose: markdown rules and spelling
      Client   check and test PyKeePassGate, the Python reference client
      Unit     run the tests that never load KeePass
      Host     run the tests that drive the real KeePass types in this process
      Coverage run both test tiers and hold what they reach above a floor
      Test     lint, compile, and run every test that does not need a running KeePass (the default)
      Live     run the end to end tests, which start a throwaway KeePass on this desktop
      All      Test then Live
      Deploy   copy the plugin into the KeePass used for development
      Run      deploy, then start that KeePass with the plugin in it
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
    [ValidateSet('KeePass', 'Build', 'Lint', 'Client', 'Unit', 'Host', 'Coverage', 'Test', 'Live', 'All', 'Deploy', 'Run', 'Clean')]
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

    # Print the tasks that are checks, one line for each runner CI gives one, and exit. This is
    # what CI asks for.
    [switch] $ListGates
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

# Which tasks are not checks: a prerequisite, the two that only compose others, and the ones that
# change something rather than judging it. Everything else is a check, which is the way round that
# fails closed, because a task added and forgotten about is then checked rather than skipped.
$notGates = @('KeePass', 'Test', 'All', 'Deploy', 'Run', 'Clean')

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

# **Where CI runs each check**, which `-ListGates` says beside it so that the workflow reads that
# from here too. This is a Windows repository and every check runs on Windows. The client is a
# Python package that installs anywhere, so its check runs on Linux as well, which is also the only
# place the tests of a credential file's mode can run at all: Windows answers those with an ACL.
# A name here that is not a check is refused, as a stray in `notGates` is.
$runners = @{ 'Client' = @('windows-latest', 'ubuntu-latest') }

$gates = @($declaredTasks | Where-Object { $notGates -notcontains $_ })
$misplaced = @($runners.Keys | Where-Object { $gates -notcontains $_ })
if ($misplaced.Count -gt 0) {
    throw "runners names $($misplaced -join ', '), which is not a check."
}

if ($ListGates) {
    foreach ($gate in $gates) {
        $on = @('windows-latest')
        if ($runners.ContainsKey($gate)) {
            $on = $runners[$gate]
        }

        foreach ($runner in $on) {
            "$gate $runner"
        }
    }

    exit 0
}

$root = Split-Path -Parent $MyInvocation.MyCommand.Definition

# Joined a part at a time, because the client's check also runs under PowerShell 7 on Linux, and a
# backslash written into a path is a separator only on Windows.
. (Join-Path (Join-Path $root 'tools') 'KeePass.ps1')
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
        # The pathspec is what makes this the whole repository rather than whatever
        # directory the caller stood in: git scopes a bare `ls-files` to the working
        # directory, so from `src` this checked 112 files instead of 247 and said so
        # in exactly the same words.
        $stowaways = @(& git ls-files --cached --ignored --exclude-standard -- $root)
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

# The Python half. `uv` owns the environment, so nothing here installs anything by hand and the
# versions are whatever `uv.lock` pins. The three checks are the same three the client's own
# `pyproject.toml` configures, run in the order that fails cheapest first.
function Invoke-Client {
    if (-not (Get-Command uv -ErrorAction SilentlyContinue)) {
        throw 'uv was not found. PyKeePassGate is managed with uv: https://docs.astral.sh/uv/'
    }

    # Every other task joins its paths against $root. These four are bare commands that read
    # the working directory, and they fail in the direction that reports success: from a
    # subdirectory `ruff check .` prints 'All checks passed!' having never opened
    # tests/pykeepassgate, and .coverage lands somewhere .gitignore does not cover. CI always
    # runs from the root, so nothing there would ever have shown it.
    Push-Location $root
    try {
        # `--locked` rather than `--frozen`, and only here. `--frozen` uses the lockfile without
        # checking it still agrees with pyproject.toml, so a dependency edited and never
        # re-locked passes the whole gate green. Catching that is the one thing committing a
        # lockfile is for. The three steps below stay `--frozen`: they should use the
        # environment this one just settled, not re-resolve it.
        Invoke-Step 'client environment' {
            uv sync --locked --quiet
        }

        Invoke-Step 'client style' {
            uv run --frozen ruff check .
        }

        Invoke-Step 'client types' {
            uv run --frozen mypy
        }

        Invoke-Step 'client tests' {
            uv run --frozen pytest --cov --cov-report=term-missing -q
        }

        # What PyPI is handed, not what the working tree holds. `uv build` builds the sdist and
        # then builds the wheel *from that sdist*, which is the step that catches a source
        # distribution nobody can install: a packaging table that reads the working tree's layout
        # and ships a different one passes every other check here. Into .tmp, which is ignored,
        # because the artifacts are the gate's business and not the repository's.
        Invoke-Step 'client package' {
            # A part at a time, for the reason the helper above is joined that way: this one goes
            # to `uv`, which reads a backslash on Linux as part of a name.
            $distribution = Join-Path (Join-Path $root '.tmp') 'dist'
            if (Test-Path $distribution) { Remove-Item $distribution -Recurse -Force }
            uv build --out-dir $distribution
        }
    }
    finally {
        Pop-Location
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

# **The client is checked beside the plugin rather than before it.** Neither half reads anything
# the other writes, and one after the other the client's minute or two was spent with every core
# but one idle. So it runs as this same script asked for `Client`, in a process of its own, while
# the plugin builds and its tiers run here. Its output goes to a file and is printed once the
# plugin's half is done, in one piece rather than interleaved line by line with the other half's.
#
# A failure on either side fails the run, and the client's is reported even when the plugin's
# half failed too, because the second failure is the one that would otherwise surface only on
# the next run. The process is waited for in every case, so nothing is left running.
function Start-Client {
    $directory = Join-Path $root '.tmp'
    New-Item -ItemType Directory -Force -Path $directory | Out-Null
    $output = Join-Path $directory 'client.log'
    $errors = Join-Path $directory 'client.err'
    Remove-Item $output, $errors -Force -ErrorAction SilentlyContinue

    # The PowerShell running this script, whichever it is: Windows PowerShell here, PowerShell 7
    # on a runner. The execution policy is a Windows setting, so it is only named there.
    $shell = [System.Diagnostics.Process]::GetCurrentProcess().MainModule.FileName
    $arguments = @('-NoProfile')
    if ($env:OS -eq 'Windows_NT') {
        $arguments += @('-ExecutionPolicy', 'Bypass')
    }
    $arguments += @('-File', "`"$PSCommandPath`"", '-Task', 'Client')
    $process = Start-Process -FilePath $shell -ArgumentList $arguments -WorkingDirectory $root -NoNewWindow -PassThru `
        -RedirectStandardOutput $output -RedirectStandardError $errors

    # Read once now, because `Process.ExitCode` comes back empty for a process started this way
    # unless its handle was taken before it exited.
    $null = $process.Handle
    return [pscustomobject] @{ Process = $process; Output = $output; Errors = $errors }
}

function Complete-Client {
    param($Client)

    $Client.Process.WaitForExit()

    Write-Host ''
    Write-Host '==> client (beside the plugin)' -ForegroundColor Cyan
    foreach ($file in $Client.Output, $Client.Errors) {
        if (Test-Path $file) {
            Get-Content -LiteralPath $file | ForEach-Object { Write-Host $_ }
        }
    }

    return $Client.Process.ExitCode
}

function Invoke-Test {
    Invoke-Lint
    $client = Start-Client
    $failed = $null
    try {
        Invoke-Coverage
    }
    catch {
        $failed = $_
    }

    $code = Complete-Client $client
    if ($failed) {
        if ($code -ne 0) {
            Write-Host "The client failed too, with exit code $code." -ForegroundColor Red
        }

        throw $failed
    }

    if ($code -ne 0) {
        throw "client failed with exit code $code."
    }
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

    # Asked before anything is compiled or deleted, because it is the cheapest question here and
    # nothing below can change the answer. Asked last, a refusal arrived after the output directory
    # had already been removed and the whole thing rebuilt, on the one path whose point is a short
    # edit-and-run loop.
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

    # Emptied before the build, not after it. `dotnet build` leaves an assembly behind when a
    # package reference is dropped or downgraded, and what is copied below is a directory rather
    # than a list, so an assembly nothing references any more would be deployed forever.
    $output = Split-Path $pluginDll
    if (Test-Path $output) {
        Remove-Item $output -Recurse -Force

        # The memo goes with it. `Invoke-Build` answers once per project, and a deletion it does not
        # know about would have it say "already built" about a directory this line just removed.
        # The product check below then fails naming the assembly rather than the deletion. The two
        # mechanisms sit a hundred lines apart and neither comment would mention the other.
        $script:compiled.Remove($pluginProject)
    }

    # The plugin's own project rather than the solution. What is copied below comes out of one
    # output directory, and building the solution here compiles both test assemblies that no deploy
    # ever reads.
    Invoke-Build -Project $pluginProject

    $product = (Get-Item $pluginDll).VersionInfo.ProductName
    if ($product -ne 'KeePass Plugin') {
        throw "$pluginDll reports the product name '$product'. KeePass silently ignores any plugin that does not say 'KeePass Plugin'."
    }

    # **A directory of its own under `plugins`, never `plugins` itself.** KeePass scans `plugins`
    # recursively, and loads a plugin with `Assembly.LoadFrom`, which probes the plugin's own
    # directory for what it references, so what sits beside the plugin is what it binds to. In
    # `plugins` itself that is shared with every plugin installed there: one `Newtonsoft.Json.dll`
    # for all of them, where a strong-named reference binds only to the version it was built
    # against, since KeePass's own configuration redirects nothing but KeePass. Whichever plugin was
    # there first then breaks, and a copy made here with `-Force` would be what broke it. Measured
    # with KeePass 2.61.1's own file scan and a walk of every method: a foreign 10.0.2 in `plugins`
    # fails 21 of this plugin's methods installed flat and none installed here.
    $plugins = Join-Path $keePass 'plugins'
    $target = Join-Path $plugins 'KeePassGate'
    if (-not (Test-Path $target)) {
        New-Item -ItemType Directory -Path $target -Force | Out-Null
    }

    # What the last deploy wrote, taken away before this one writes. The copy below is a directory
    # rather than a list, so a dependency that stops being referenced would otherwise sit here for
    # ever. `Assembly.LoadFrom` probes exactly this directory, so the developer would be debugging
    # against an assembly no build produces. `Invoke-Clean` never reaches here either.
    #
    # **Only what this script recorded writing**, never the directory's contents, although the
    # directory is named after this plugin. `-KeePassDir` can name a real installation, and deleting
    # something in it this script did not put there is a far worse failure than the stale assembly
    # this is here to prevent.
    $manifest = Join-Path $target 'KeePassGate.deployed.txt'
    if (Test-Path $manifest) {
        foreach ($name in @(Get-Content $manifest)) {
            if (-not $name) {
                continue
            }

            $stale = Join-Path $target $name
            if (Test-Path $stale) {
                Remove-Item $stale -Force
            }
        }
    }

    # Everything the plugin needs, not only the plugin. KeePass loads a plugin with
    # Assembly.LoadFrom, which probes the plugin's own directory for what it references, so a
    # dependency belongs beside it rather than beside KeePass.exe, and in a directory no other
    # plugin shares.
    $deployed = @()
    Get-ChildItem $output -Filter '*.dll' | ForEach-Object {
        Copy-Item $_.FullName $target -Force -ErrorAction Stop
        $deployed += $_.Name
        Write-Host "  $($_.Name)"
    }

    $symbols = [System.IO.Path]::ChangeExtension($pluginDll, '.pdb')
    if (Test-Path $symbols) {
        Copy-Item $symbols $target -Force
        $deployed += Split-Path $symbols -Leaf
    }

    # And the notices of the assemblies copied above, which each of their licenses asks to go with
    # every copy. From the repository, since the build makes no such file, and recorded like
    # everything else here, so the next deploy takes it away before writing it again.
    $notices = Join-Path $root 'THIRD-PARTY-NOTICES.md'
    Copy-Item $notices $target -Force -ErrorAction Stop
    $deployed += Split-Path $notices -Leaf
    Write-Host "  $(Split-Path $notices -Leaf)"

    Set-Content -Path $manifest -Value $deployed

    Write-Host "Deployed to $target" -ForegroundColor Green
}

# Deploy, then start the KeePass that was deployed into. The one thing this does that a person
# doing it by hand forgets is starting the *provided* KeePass rather than whichever one is on the
# `PATH`, which is how a plugin gets tested against a version nobody pinned.
function Invoke-Run {
    Invoke-Deploy
    $keePass = Resolve-KeePass
    $exe = Join-Path $keePass 'KeePass.exe'

    # **An enforced configuration outranks every other**, the per-user one included, and this is the
    # first task that executes out of that directory rather than only compiling against it. The
    # pinned archive ships none, so one being there means somebody put it there, and KeePass would
    # honor whatever it declares. What it can declare includes triggers that run programs.
    # Refused rather than removed: deleting a file this script did not write is the worse of the
    # two answers.
    $enforced = Join-Path $keePass 'KeePass.config.enforced.xml'
    if (Test-Path $enforced) {
        throw "$enforced is not part of the pinned KeePass, and an enforced configuration outranks every other. Remove it, or name a different KeePass with -KeePassDir."
    }

    # **A configuration beside the executable**, which is the thing that keeps this KeePass off the
    # one somebody keeps their real database in. The archive ships none, so without this there is
    # no configuration in the application directory at all. KeePass then falls back to the per-user
    # file for reading *and* for writing, and a launcher promising isolation would open an
    # unreleased build inside the operator's own settings and save them back on the way out.
    #
    # Both settings are the end to end fixture's, for the reasons it gives: the update check blocks
    # behind a modal dialog before any plugin loads, and single instance handling would hand the
    # command line to a KeePass that is already running, so this would exit at once having started
    # nothing.
    $configPath = Join-Path $keePass 'KeePass.config.xml'
    if (-not (Test-Path $configPath)) {
        $document = @'
<?xml version="1.0" encoding="utf-8"?>
<Configuration>
  <Application>
    <Start>
      <CheckForUpdate>false</CheckForUpdate>
      <CheckForUpdateConfigured>true</CheckForUpdateConfigured>
    </Start>
  </Application>
  <Integration>
    <LimitToSingleInstance>false</LimitToSingleInstance>
  </Integration>
</Configuration>
'@
        Set-Content -Path $configPath -Value $document -Encoding UTF8
    }

    Write-Host ''
    Write-Host "Starting $exe" -ForegroundColor Cyan

    # The plugin's own two files, beside the KeePass being started rather than in this account's
    # application data. Unset, both go where a real deployment on this machine writes its own, and
    # a development session's lines push genuine ones out of files that roll.
    #
    # Put back afterwards, because `build.ps1` runs in the caller's own session and an environment
    # this script set for one child should not outlive it.
    $previousAudit = $env:KEEPASSGATE_AUDIT_FILE
    $previousLog = $env:KEEPASSGATE_LOG_FILE
    $log = Join-Path $keePass 'keepassgate.log'

    try {
        $env:KEEPASSGATE_AUDIT_FILE = Join-Path $keePass 'keepassgate-audit.jsonl'
        $env:KEEPASSGATE_LOG_FILE = $log

        # Started detached rather than waited on, because this is a launcher: the point is to get a
        # KeePass with the plugin in it and hand the desktop back.
        $process = Start-Process -FilePath $exe -WorkingDirectory $keePass -PassThru
    }
    finally {
        $env:KEEPASSGATE_AUDIT_FILE = $previousAudit
        $env:KEEPASSGATE_LOG_FILE = $previousLog
    }

    if (-not $process) {
        throw "Nothing started from $exe."
    }

    # Asked rather than announced. A configuration KeePass will not read and a plugin that takes
    # the process down both make it start and die at once, and that printed the same green line as
    # a KeePass that is running. Telling those apart is the whole reason this task exists.
    Start-Sleep -Milliseconds 750

    if ($process.HasExited) {
        throw "KeePass started and exited at once, with code $($process.ExitCode). $log is where the plugin would have said why."
    }

    Write-Host "KeePass $($process.Id) is running with the $Configuration build." -ForegroundColor Green
    # This commit is what makes the answer the Tools menu rather than the database settings
    # dialog: the window it adds says whether the plugin loaded without a database being open
    # at all, which is the question somebody starting a launcher is asking.
    Write-Host 'To check that it loaded, open Tools, KeePassGate (KPG).'
    Write-Host "The plugin writes its log to $log."
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

    # What the client tier leaves behind, which is the same list `.gitignore` had to learn.
    # A clean that removes half of what a build makes is a clean nobody can rely on.
    $caches = '.venv', '.mypy_cache', '.pytest_cache', '.ruff_cache', '.coverage'
    foreach ($cache in $caches) {
        $path = Join-Path $root $cache
        if (Test-Path $path) {
            Write-Host "removing $path"
            Remove-Item $path -Recurse -Force
        }
    }

    Get-ChildItem -Path (Join-Path $root 'src'), (Join-Path $root 'tests') -Include '__pycache__' -Recurse -Directory |
        ForEach-Object {
            Write-Host "removing $($_.FullName)"
            Remove-Item $_.FullName -Recurse -Force
        }
}

switch ($Task) {
    'KeePass' { Resolve-KeePass | Out-Null; Test-KeePassLatest }
    'Build' { Invoke-Compile }
    'Lint' { Invoke-Lint }
    'Client' { Invoke-Client }
    'Unit' { Invoke-Unit }
    'Host' { Invoke-Host }
    'Coverage' { Invoke-Coverage }
    'Test' { Invoke-Test }
    'Live' { Invoke-Live }
    'All' { Invoke-Test; Invoke-Live }
    'Deploy' { Invoke-Deploy }
    'Run' { Invoke-Run }
    'Clean' { Invoke-Clean }
    default { throw "There is no case for task '$Task'. Every name the Task parameter allows needs one here, or a task would report success having run nothing." }
}

Write-Host ''
Write-Host "$Task finished." -ForegroundColor Green
