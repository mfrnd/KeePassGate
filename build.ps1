<#
.SYNOPSIS
    Builds, tests and deploys the KeePassGate plugin.

.DESCRIPTION
    This script is the gate, and CI runs the same one. Run it before every commit.

    Tasks:
      KeePass  provide the pinned KeePass, which everything else needs
      Build    compile everything, in Debug and in Release
      Lint     check the prose, markdown rules and spelling, and the suppression comments
      Client   check and test the Python: PyKeePassGate, the reference client, and the tools
      Unit     run the tests that never load KeePass
      Host     run the tests that drive the real KeePass types in this process
      Coverage run both test tiers and hold what they reach above a floor
      Test     lint, compile, and run every test that does not need a running KeePass (the default)
      Live     run the end to end tests, which start a throwaway KeePass on this desktop
      All      Test then Live
      Screenshots  retake the pictures in docs/images by driving a throwaway KeePass
      Deploy   copy the plugin into the KeePass used for development
      Package  build the plugin as Release and zip it, with a SHA256SUMS, into dist
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
    .\build.ps1 -Task Screenshots
    .\build.ps1 -Task Deploy -Configuration Release
#>

[CmdletBinding()]
param(
    [ValidateSet('KeePass', 'Build', 'Lint', 'Client', 'Unit', 'Host', 'Coverage', 'Test', 'Live', 'All', 'Screenshots', 'Deploy', 'Package', 'Run', 'Clean')]
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
$notGates = @('KeePass', 'Test', 'All', 'Screenshots', 'Deploy', 'Package', 'Run', 'Clean')

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

    # **A suppression is a comment CodeQL reads, and CodeQL reads more spellings than one**: some
    # suppress more than they look like they do, and some nothing at all. `tools/suppressions.py`
    # finds every C# and Python comment as the language's grammar does and refuses all but the one
    # spelling. Python, because the grammars are its libraries, which `uv` provides from the
    # lockfile the client already uses.
    Invoke-Step 'suppression comments' {
        if (-not (Get-Command uv -ErrorAction SilentlyContinue)) {
            throw 'uv was not found. The suppression check is Python, managed with uv: https://docs.astral.sh/uv/'
        }

        Push-Location $root
        try {
            uv run --locked --quiet python (Join-Path 'tools' 'suppressions.py')
        }
        finally {
            Pop-Location
        }
    }

    # What markdownlint does not do: follow what a document points at. A broken image is invisible
    # in the source and obvious on the page, and the screenshots in `docs/` are generated, so an
    # image nobody refers to means a page forgot one or the fixture is still taking one.
    Invoke-Step 'documentation links' {
        & (Join-Path $root 'tools\Links.ps1') -Root $root
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
# Each failed test's name, message and stack, then the tail of every plugin log a session of this
# run kept, which a session does when its fixture fails, and what any KeePass it had to end was
# showing. Bounded, so a cascade reads as its first few failures and a count rather than a flood.
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

    # A live session works under the common application data directory. It deletes itself on tear
    # down when its fixture passed and keeps everything when the fixture failed, so a log here is
    # one of a fixture that failed, or of a session that never reached its tear down.
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

        # And what a KeePass that had to be ended rather than closing was showing, written beside
        # the log as it was ended: the kill takes the windows with it, and a close that ran out of
        # time says nothing else about why.
        foreach ($ended in @(Get-ChildItem -Path (Join-Path $sessions '*\keepass\ended.txt') -File -ErrorAction SilentlyContinue)) {
            if ($ended.LastWriteTime -lt $Since) { continue }
            Write-Host ''
            Write-Host "  A KeePass was ended while still running, $($ended.FullName):"
            Write-Host (Tail (Get-Content -LiteralPath $ended.FullName -Raw) $shownLines)
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
            --filter 'TestCategory=Live&TestCategory!=Screenshots' `
            --results-directory $results --logger 'trx;LogFileName=live.trx'
    }

    Assert-Executed $trx 'end to end'
}

# Retakes the pictures in `docs/images` by driving a throwaway KeePass through the whole operator
# flow. Separate from `Live` because it writes into the working tree: an ordinary end to end run
# that rewrote a dozen checked-in binaries would put a diff in front of somebody who asked for a
# test result. The directory is handed over rather than worked out by the test, which is also what
# stops the fixture running from an editor and quietly replacing them.
function Invoke-Screenshots {
    Invoke-Build
    $keePass = Resolve-KeePass
    $images = Join-Path $root 'docs\images'

    Write-Host ''
    Write-Host 'A throwaway KeePass is about to be driven and screenshotted.' -ForegroundColor Yellow
    Write-Host 'Leave the desktop alone, and keep other windows off it: what covers a dialog is in the picture.' -ForegroundColor Yellow

    $results = Join-Path $root '.tmp'
    $trx = Join-Path $results 'screenshots.trx'
    if (Test-Path $trx) { Remove-Item $trx -Force }

    $env:KPG_SCREENSHOT_DIR = $images
    try {
        Invoke-Step 'screenshots' {
            dotnet test $hostTests -c $Configuration -p:KeePassDevDir=$keePass --no-build --nologo `
                --filter 'TestCategory=Screenshots' `
                --results-directory $results --logger 'trx;LogFileName=screenshots.trx'
        }
    }
    finally {
        Remove-Item Env:\KPG_SCREENSHOT_DIR -ErrorAction SilentlyContinue
    }

    # The same guard `Invoke-Live` carries, and it is worth more here. A fixture that ignores
    # itself exits zero either way, but this task ends by naming a directory in green: an operator
    # who asked for the pictures to be retaken would read that, commit what is on disk, and publish
    # the old ones as though they were of the code they had just changed. `LiveSession.Begin`
    # ignores the whole fixture when the provided KeePass does not match what the tests were built
    # against, which `-KeePassDir` can produce without anything looking wrong.
    if (-not (Test-Path $trx)) {
        throw 'The screenshot run produced no result file, so nothing can say what was retaken.'
    }

    $counters = ([xml] (Get-Content -LiteralPath $trx -Raw)).TestRun.ResultSummary.Counters
    $executed = [int] $counters.executed
    if ($executed -lt 1) {
        throw ("The screenshot fixture ignored itself: $([int] $counters.total) found, " +
            "$([int] $counters.notExecuted) skipped. No picture was retaken.")
    }

    Write-Host "Images are in $images" -ForegroundColor Green
}

# **What goes beside the plugin wherever it is put**, one list for the two tasks that put it
# somewhere, so that what `Invoke-Package` zips is what `Invoke-Deploy` copies by construction
# rather than by two lists kept alike by eye.
#
# Everything the plugin needs, not only the plugin. KeePass loads a plugin with
# Assembly.LoadFrom, which probes the plugin's own directory for what it references, so a
# dependency belongs beside it rather than beside KeePass.exe, and in a directory no other plugin
# shares. Then its symbols, and the notices of the assemblies, which each of their licenses asks to
# go with every copy: from the repository, since the build makes no such file.
function Get-ShippedFiles {
    param([string] $Output)

    $files = @(Get-ChildItem $Output -Filter '*.dll' -File)
    $symbols = Join-Path $Output 'KeePassGate.pdb'
    if (Test-Path $symbols) {
        $files += Get-Item $symbols
    }

    $files += Get-Item (Join-Path $root 'THIRD-PARTY-NOTICES.md')
    return $files
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

    # Recorded like everything else here, so the next deploy takes each file away before writing it
    # again.
    $deployed = @()
    foreach ($file in Get-ShippedFiles $output) {
        Copy-Item $file.FullName $target -Force -ErrorAction Stop
        $deployed += $file.Name
        Write-Host "  $($file.Name)"
    }

    Set-Content -Path $manifest -Value $deployed

    Write-Host "Deployed to $target" -ForegroundColor Green
}

# **The source file names a portable PDB carries**, read the way `System.Reflection.Metadata` reads
# them, which Windows PowerShell does not have: ECMA-335's metadata root and stream headers, then
# the `Document` table, which is the first table a portable PDB holds since the type system tables
# live in the assembly. Each name is a blob holding a separator and a list of blob indices, one per
# segment, which is why no byte search finds a whole path in one. What this cannot read it refuses,
# because a file of symbols nobody could look inside is not one to publish.
function Get-PdbDocuments {
    param([string] $Path)

    $bytes = [System.IO.File]::ReadAllBytes($Path)
    $reader = New-Object System.IO.BinaryReader (New-Object System.IO.MemoryStream (, $bytes))

    if ($bytes.Length -lt 16 -or $reader.ReadUInt32() -ne 0x424A5342) {
        throw "$Path is not a portable PDB, so nothing can say which source paths it names."
    }

    # Signature, major and minor version and a reserved word, then the version string's length.
    $reader.BaseStream.Position = 12
    $versionLength = $reader.ReadUInt32()
    $reader.BaseStream.Position = 16 + $versionLength + 2
    $streamCount = $reader.ReadUInt16()

    $streams = @{}
    for ($i = 0; $i -lt $streamCount; $i++) {
        $offset = $reader.ReadUInt32()
        $size = $reader.ReadUInt32()
        $streamName = ''
        while ($true) {
            $character = $reader.ReadByte()
            if ($character -eq 0) { break }
            $streamName += [char] $character
        }

        # A stream's name is padded to a multiple of four bytes, terminator included.
        $reader.BaseStream.Position = ($reader.BaseStream.Position + 3) -band (-bnot 3)
        $streams[$streamName] = $offset
    }

    foreach ($wanted in '#~', '#Blob') {
        if (-not $streams.ContainsKey($wanted)) {
            throw "$Path has no $wanted stream, so nothing can say which source paths it names."
        }
    }

    $tables = $streams['#~']
    $blobs = $streams['#Blob']

    $reader.BaseStream.Position = $tables + 6
    $heapSizes = $reader.ReadByte()
    $reader.BaseStream.Position = $tables + 8
    $present = $reader.ReadUInt64()

    $documentBit = [uint64] 1 -shl 0x30
    if (($present -band $documentBit) -eq 0 -or ($present -band ($documentBit - 1)) -ne 0) {
        throw "$Path does not begin its tables with its documents, so nothing can say which source paths it names."
    }

    # One row count for each table present, in the order of their numbers, and the rows after.
    $reader.BaseStream.Position = $tables + 24
    $rowCounts = @()
    for ($bit = 0; $bit -lt 64; $bit++) {
        if ($present -band ([uint64] 1 -shl $bit)) {
            $rowCounts += $reader.ReadUInt32()
        }
    }

    $wideGuid = ($heapSizes -band 2) -ne 0
    $wideBlob = ($heapSizes -band 4) -ne 0

    function Read-Compressed([System.IO.BinaryReader] $From) {
        $first = [int] $From.ReadByte()
        if (($first -band 0x80) -eq 0) { return $first }
        if (($first -band 0xC0) -eq 0x80) { return (($first -band 0x3F) -shl 8) -bor $From.ReadByte() }
        $value = ($first -band 0x1F) -shl 24
        $value = $value -bor ([int] $From.ReadByte() -shl 16)
        $value = $value -bor ([int] $From.ReadByte() -shl 8)
        return $value -bor $From.ReadByte()
    }

    function Read-Blob([int] $Index) {
        $reader.BaseStream.Position = $blobs + $Index
        $length = Read-Compressed $reader
        return , $reader.ReadBytes($length)
    }

    # A row is the name and the hash, both blob indices, and the hash algorithm and the language,
    # both GUID indices, each two bytes wide or four as the heap sizes say.
    $rowStart = $reader.BaseStream.Position
    $rowSize = 2 * $(if ($wideBlob) { 4 } else { 2 }) + 2 * $(if ($wideGuid) { 4 } else { 2 })
    $documents = @()
    for ($row = 0; $row -lt $rowCounts[0]; $row++) {
        $reader.BaseStream.Position = $rowStart + $row * $rowSize
        $nameIndex = $(if ($wideBlob) { $reader.ReadUInt32() } else { $reader.ReadUInt16() })
        $blob = Read-Blob $nameIndex
        if ($blob.Length -lt 1 -or $blob[0] -ge 0x80) {
            throw "$Path names a document in a form this cannot read, so nothing can say which source paths it names."
        }

        $separator = ''
        if ($blob[0] -ne 0) {
            $separator = [string] [char] $blob[0]
        }

        $segments = New-Object System.IO.BinaryReader (New-Object System.IO.MemoryStream (, $blob))
        $segments.BaseStream.Position = 1
        $parts = @()
        while ($segments.BaseStream.Position -lt $blob.Length) {
            $parts += [System.Text.Encoding]::UTF8.GetString((Read-Blob (Read-Compressed $segments)))
        }

        $documents += ($parts -join $separator)
    }

    return $documents
}

# **The package read back the way whoever downloads it reads it**, every time one is made, because
# nothing else ever runs this task before a release does. The zip is opened again and has to hold
# exactly the files it was made from, under `KeePassGate/`, each with the bytes of its source rather
# than of the copy that was staged. `SHA256SUMS` has to be what `sha256sum -c` reads, lower case hex,
# two spaces, forward slashes and a line feed after each line, naming every entry and the zip once
# each, and every digest in it has to be the one computed here from what the zip holds.
function Assert-Package {
    param([string] $Dist, [string] $ZipName, [System.IO.FileInfo[]] $Sources)

    $sha = [System.Security.Cryptography.SHA256]::Create()
    function Digest([System.IO.Stream] $Stream) {
        return ([System.BitConverter]::ToString($sha.ComputeHash($Stream)) -replace '-', '').ToLowerInvariant()
    }

    $zipPath = Join-Path $Dist $ZipName
    $wanted = New-Object System.Collections.Hashtable ([StringComparer]::Ordinal)
    foreach ($source in $Sources) {
        $stream = [System.IO.File]::OpenRead($source.FullName)
        try {
            $wanted["KeePassGate/$($source.Name)"] = Digest $stream
        }
        finally {
            $stream.Dispose()
        }
    }

    $held = New-Object System.Collections.Hashtable ([StringComparer]::Ordinal)
    $zip = [System.IO.Compression.ZipFile]::OpenRead($zipPath)
    try {
        foreach ($entry in $zip.Entries) {
            if ($held.ContainsKey($entry.FullName)) {
                throw "$ZipName holds $($entry.FullName) twice."
            }

            $stream = $entry.Open()
            try {
                $held[$entry.FullName] = Digest $stream
            }
            finally {
                $stream.Dispose()
            }
        }
    }
    finally {
        $zip.Dispose()
    }

    foreach ($path in $wanted.Keys) {
        if (-not $held.ContainsKey($path)) {
            throw "$ZipName is missing $path, which it was made from."
        }

        if ($held[$path] -cne $wanted[$path]) {
            throw "$ZipName holds $path with bytes that differ from the file it was made from."
        }
    }

    foreach ($path in $held.Keys) {
        if (-not $wanted.ContainsKey($path)) {
            throw "$ZipName holds $path, which nothing put there."
        }
    }

    $stream = [System.IO.File]::OpenRead($zipPath)
    try {
        $held[$ZipName] = Digest $stream
    }
    finally {
        $stream.Dispose()
    }

    $text = [System.IO.File]::ReadAllText((Join-Path $Dist 'SHA256SUMS'))
    if ($text.Contains("`r") -or -not $text.EndsWith("`n")) {
        throw 'SHA256SUMS does not end each line with a line feed alone, which is what sha256sum -c reads.'
    }

    $listed = New-Object System.Collections.Hashtable ([StringComparer]::Ordinal)
    foreach ($line in $text.TrimEnd("`n").Split("`n")) {
        $match = [regex]::Match($line, '^([0-9a-f]{64})  ([^\\]+)$')
        if (-not $match.Success) {
            throw "SHA256SUMS has the line '$line', which is not lower case hex, two spaces and a path with forward slashes."
        }

        $path = $match.Groups[2].Value
        if ($listed.ContainsKey($path)) {
            throw "SHA256SUMS names $path twice."
        }

        if (-not $held.ContainsKey($path)) {
            throw "SHA256SUMS names $path, which is neither the zip nor in it."
        }

        if ($match.Groups[1].Value -cne $held[$path]) {
            throw "SHA256SUMS gives $path a digest that is not the one it has."
        }

        $listed[$path] = $true
    }

    foreach ($path in $held.Keys) {
        if (-not $listed.ContainsKey($path)) {
            throw "SHA256SUMS does not name $path."
        }
    }

    $sha.Dispose()
}

# **What a release would publish, with nothing published.** The plugin built as Release and what
# `Get-ShippedFiles` puts beside it, which is the set `Invoke-Deploy` copies: every assembly it
# loads, its symbols and the notices those assemblies' licenses ask to travel with every copy. And
# this project's own license, which only a package carries. Zipped under the version into `dist\`,
# which is ignored, beside a `SHA256SUMS` covering every file in the zip and the zip itself, written
# the way `sha256sum -c` reads it so that the zip extracted where it lies can be checked file by
# file.
#
# **Not a gate.** It judges nothing a check does not already judge, and every run of the gate
# rebuilding and zipping the plugin would only make it slower.
function Invoke-Package {
    $keePass = Resolve-KeePass
    $output = Join-Path $root 'src\KeePassGate\bin\Release\net48'
    $dist = Join-Path $root 'dist'
    $staging = Join-Path $root '.tmp\package'

    # All three emptied first. The output for the reason `Invoke-Deploy` empties it, since what is
    # collected below is a directory rather than a list; `dist` because a `SHA256SUMS` beside a zip
    # it does not describe is worse than none.
    foreach ($path in $output, $dist, $staging) {
        if (Test-Path $path) {
            Remove-Item $path -Recurse -Force
        }
    }

    # **Built the way a build machine builds**, which is what `ContinuousIntegrationBuild` says.
    # Without it the compiler writes the full path of the symbols into the plugin, which is wherever
    # the checkout is and on most machines begins with the account that built it; with it every
    # path is `/_/` and what follows inside the repository. Release whatever `-Configuration` says,
    # because a package is the build that ships, and a rebuild rather than an incremental one,
    # because the switch changes what is compiled without touching anything an incremental build
    # compares.
    #
    # **The switch alone maps nothing.** The SDK builds the mapping from the source roots its git
    # support reads out of the repository, so a tree with no `.git`, a source download or a
    # `git archive`, is compiled with every full path in it, and the check below refuses the result
    # by blaming the directory. Asked first, so that the refusal names what is missing: a clone of
    # this repository with this script at its top.
    # cspell:ignore toplevel
    $before = $ErrorActionPreference
    $ErrorActionPreference = 'Continue'
    try {
        $top = $null
        if (Get-Command git -ErrorAction SilentlyContinue) {
            $top = & git -C $root rev-parse --show-toplevel 2>$null
        }
    }
    finally {
        $ErrorActionPreference = $before
    }

    if (-not $top -or $LASTEXITCODE -ne 0 -or
        -not [string]::Equals(
            [System.IO.Path]::GetFullPath(([string] $top).Trim()).TrimEnd('\', '/'),
            [System.IO.Path]::GetFullPath($root).TrimEnd('\', '/'),
            [StringComparison]::OrdinalIgnoreCase)) {
        throw "Package needs a git checkout with $root at its top, and git to read it: the build maps every source path to /_/ from the repository's own record, and without one the plugin would name the directory it was built in. Package from a clone rather than a source download."
    }

    # **And a commit with nothing changed since.** The SDK writes HEAD's commit into the plugin's
    # informational version, and the plugin reports it on its status route and in its log as the
    # commit it was built from. A change nobody committed, or a file nobody added, which the SDK
    # compiles if it is a source file, builds a plugin that commit does not reproduce. What
    # `.gitignore` excludes is left out of the question, being what the tasks write and the KeePass
    # this repository pins and verifies.
    $ErrorActionPreference = 'Continue'
    try {
        $changes = @(& git -C $root status --porcelain 2>$null)
    }
    finally {
        $ErrorActionPreference = $before
    }

    if ($LASTEXITCODE -ne 0) {
        throw "git could not say whether $root has changes nobody committed, and Package builds only a commit."
    }

    if ($changes.Count -gt 0) {
        throw "Package builds a commit, and $root has $($changes.Count) change(s) nobody committed, $($changes[0].Substring(3)) first among them. Commit them or set them aside, then package."
    }

    # **The commit's own time, for every entry in the zip.** The plugin and its symbols are built
    # deterministically, so every file in the zip is the same from the same commit and toolchain,
    # and a zip entry would otherwise carry the moment the build wrote its file: the zip's own line
    # in `SHA256SUMS` would then match only the zip from the same run. With the commit's time it
    # is the same from every run on the same runtime, whose deflate is the one other input.
    $ErrorActionPreference = 'Continue'
    try {
        $committed = & git -C $root log -1 --format=%ct 2>$null
    }
    finally {
        $ErrorActionPreference = $before
    }

    if ($LASTEXITCODE -ne 0 -or -not $committed) {
        throw "git could not say when HEAD was committed, which is the time every entry in the package carries."
    }

    $stamp = [DateTimeOffset]::FromUnixTimeSeconds([long] ([string] $committed).Trim())

    $global:LASTEXITCODE = 0
    Invoke-Step 'build (Release, to package)' {
        dotnet build $pluginProject -c Release -p:KeePassDevDir=$keePass -p:ContinuousIntegrationBuild=true `
            --no-incremental --nologo
    }

    $plugin = Join-Path $output 'KeePassGate.dll'
    $product = (Get-Item $plugin).VersionInfo.ProductName
    if ($product -ne 'KeePass Plugin') {
        throw "$plugin reports the product name '$product'. KeePass silently ignores any plugin that does not say 'KeePass Plugin'."
    }

    # The version the plugin carries, without the commit the SDK appends after a plus sign, so the
    # zip is named by the one place the number is written, by way of the build that read it.
    $version = ((Get-Item $plugin).VersionInfo.ProductVersion -split '\+')[0]

    $files = @(Get-ShippedFiles $output)
    $files += Get-Item (Join-Path $root 'LICENSE')

    # **Every assembly beside the plugin named in the notices**, because a package is where the one
    # condition each of their licenses sets binds: the copyright and permission notice go with every
    # copy. The notices file is kept by hand, and its closing section is the list it keeps of what
    # ships, so an assembly a new reference brings in and nobody wrote a notice for is refused here,
    # by name, rather than zipped.
    $noticesText = Get-Content -LiteralPath (Join-Path $root 'THIRD-PARTY-NOTICES.md') -Raw
    $section = [regex]::Match($noticesText, '(?ms)^## What is actually redistributed\r?$(.*?)(?=^## |\z)')
    if (-not $section.Success) {
        throw "THIRD-PARTY-NOTICES.md has no section 'What is actually redistributed', which is the list of assemblies a package may carry."
    }

    $named = @([regex]::Matches($section.Groups[1].Value, '`([^`]+)`') | ForEach-Object { $_.Groups[1].Value })
    foreach ($file in $files) {
        if ($file.Extension -ne '.dll' -or $file.Name -eq 'KeePassGate.dll') {
            continue
        }

        $assembly = [System.IO.Path]::GetFileNameWithoutExtension($file.Name)
        if ($named -cnotcontains $assembly) {
            throw "$($file.Name) would ship without a notice: THIRD-PARTY-NOTICES.md does not name $assembly under 'What is actually redistributed'. Add its license there before packaging."
        }
    }

    # **Taken away however this ends.** A refusal below is about a file that has just been copied
    # into the staging directory, so leaving that directory behind keeps exactly the file that was
    # refused, the plugin naming the build path among them. And a zip that a failure interrupted, or
    # one with no sums beside it, is not a package, so `dist` goes too unless all of it was written.
    $packaged = $false
    try {
        # Inside a directory named for the plugin, so that extracting the zip into `plugins` makes
        # `plugins\KeePassGate`, which is where `Invoke-Deploy` and `docs/getting-started.md` put it.
        $folder = Join-Path $staging 'KeePassGate'
        New-Item -ItemType Directory -Path $folder -Force | Out-Null

        foreach ($file in $files) {
            Copy-Item $file.FullName $folder -ErrorAction Stop
        }

        $names = [string[]] @(Get-ChildItem $folder -File | ForEach-Object { $_.Name })
        [Array]::Sort($names, [StringComparer]::Ordinal)

        # **Checked rather than trusted to the switch above**, because a package is published and
        # the switch is one property a later change could drop. A path to this checkout names the
        # machine and usually the account; finding one refuses the package and says which file.
        #
        # Looked for as a binary keeps it: as UTF-8, and as UTF-16 starting at either an even or an
        # odd byte, since a string in an assembly's heap is a length and its characters with no
        # alignment, so decoding from the first byte alone sees only half of them. And spelled with
        # forward slashes, and with every backslash doubled, which is how the symbols' Source Link
        # record writes a path inside its JSON.
        $spellings = @($root, $root.Replace('\', '/'), $root.Replace('\', '\\'))
        foreach ($name in $names) {
            $bytes = [System.IO.File]::ReadAllBytes((Join-Path $folder $name))
            $texts = @(
                [System.Text.Encoding]::UTF8.GetString($bytes),
                [System.Text.Encoding]::Unicode.GetString($bytes),
                [System.Text.Encoding]::Unicode.GetString($bytes, 1, [Math]::Max(0, $bytes.Length - 1)))
            foreach ($text in $texts) {
                foreach ($spelling in $spellings) {
                    if ($text.IndexOf($spelling, [StringComparison]::OrdinalIgnoreCase) -ge 0) {
                        throw "$name names the directory it was built in, $root. A package is published, and a path like that names the machine that built it."
                    }
                }
            }
        }

        # **And the symbols read as a debugger reads them**, because no search of their bytes can
        # find a source path in them. A portable PDB keeps each document's name as a list of
        # separately stored segments, so `C:`, `dev` and the rest are never one string. Every
        # document has to be under `/_/`, the root the switch maps the repository to, which is a
        # stronger answer than not finding this checkout: a path anywhere else on the machine that
        # built it is refused too.
        foreach ($name in $names) {
            if ([System.IO.Path]::GetExtension($name) -ne '.pdb') {
                continue
            }

            foreach ($document in Get-PdbDocuments (Join-Path $folder $name)) {
                if (-not $document.StartsWith('/_/', [StringComparison]::Ordinal)) {
                    throw "$name names the source file $document, which is not under /_/. A package is published, and a path like that names the machine that built it."
                }
            }
        }

        New-Item -ItemType Directory -Path $dist -Force | Out-Null
        $zipName = "KeePassGate-$version.zip"
        $zipPath = Join-Path $dist $zipName

        # Through the framework's own zip classes rather than `Compress-Archive`, so that every
        # entry is named with the forward slash the format requires, whichever PowerShell runs this.
        Add-Type -AssemblyName System.IO.Compression
        Add-Type -AssemblyName System.IO.Compression.FileSystem
        $zip = [System.IO.Compression.ZipFile]::Open($zipPath, [System.IO.Compression.ZipArchiveMode]::Create)
        try {
            foreach ($name in $names) {
                # Made and stamped before it is written, since an entry of an archive being created
                # cannot be changed once its stream has been opened.
                $entry = $zip.CreateEntry("KeePassGate/$name", [System.IO.Compression.CompressionLevel]::Optimal)
                $entry.LastWriteTime = $stamp
                $source = [System.IO.File]::OpenRead((Join-Path $folder $name))
                try {
                    $target = $entry.Open()
                    try {
                        $source.CopyTo($target)
                    }
                    finally {
                        $target.Dispose()
                    }
                }
                finally {
                    $source.Dispose()
                }
            }
        }
        finally {
            $zip.Dispose()
        }

        # Lower case hex, two spaces and a path relative to `dist`, one line each with a line feed,
        # which is what `sha256sum -c` reads on any system. `Get-FileHash` answers the same digests
        # on Windows.
        $lines = @()
        foreach ($name in $names) {
            $hash = (Get-FileHash -LiteralPath (Join-Path $folder $name) -Algorithm SHA256).Hash.ToLowerInvariant()
            $lines += "$hash  KeePassGate/$name"
        }

        $lines += (Get-FileHash -LiteralPath $zipPath -Algorithm SHA256).Hash.ToLowerInvariant() + "  $zipName"
        [System.IO.File]::WriteAllText(
            (Join-Path $dist 'SHA256SUMS'), (($lines -join "`n") + "`n"), (New-Object System.Text.UTF8Encoding $false))

        Assert-Package $dist $zipName $files
        $packaged = $true
    }
    finally {
        if (Test-Path $staging) {
            Remove-Item $staging -Recurse -Force
        }

        if (-not $packaged -and (Test-Path $dist)) {
            Remove-Item $dist -Recurse -Force
        }
    }

    foreach ($name in $names) {
        Write-Host "  KeePassGate/$name"
    }

    Write-Host "Packaged $zipPath, with SHA256SUMS beside it." -ForegroundColor Green
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

    # And what the other tasks write beside the solution: `dist` is the package and its sums, and
    # `.tmp` the package's staging, the client's `uv build` and the result files of the end to end
    # and screenshot runs. A zip left in `dist` after a clean is a package of whichever commit last
    # made one.
    foreach ($written in 'dist', '.tmp') {
        $path = Join-Path $root $written
        if (Test-Path $path) {
            Write-Host "removing $path"
            Remove-Item $path -Recurse -Force
        }
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
    'Screenshots' { Invoke-Screenshots }
    'All' { Invoke-Test; Invoke-Live }
    'Deploy' { Invoke-Deploy }
    'Package' { Invoke-Package }
    'Run' { Invoke-Run }
    'Clean' { Invoke-Clean }
    default { throw "There is no case for task '$Task'. Every name the Task parameter allows needs one here, or a task would report success having run nothing." }
}

Write-Host ''
Write-Host "$Task finished." -ForegroundColor Green
