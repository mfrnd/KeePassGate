<#
.SYNOPSIS
    Checks everything this repository can be checked for.

.DESCRIPTION
    This script is the gate. Run it before every commit, and let CI run it on every pull request
    and on every push to `master`: both do the same thing, because there is only one definition of
    clean and it is here.

    Tasks:
      Lint     check the prose: markdown rules, spelling, markers and anchors (the default)

    There is one task today because there is one thing to check. The list grows with the
    repository, and a task added to it is to be checked by CI without anybody editing a workflow:
    see `-ListGates` below.

.PARAMETER Task
    What to do. Defaults to Lint.

.PARAMETER ListGates
    Print the tasks that are checks, one per line, and exit. This is the list CI is to ask for,
    and it is derived from `Task`'s own declaration rather than written down a second time, so a
    task cannot exist without CI knowing about it and the two cannot disagree.

.EXAMPLE
    .\build.ps1
    .\build.ps1 -Task Lint
    .\build.ps1 -ListGates
#>

[CmdletBinding()]
param(
    [ValidateSet('Lint')]
    [string] $Task = 'Lint',

    [switch] $ListGates
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

$root = Split-Path -Parent $MyInvocation.MyCommand.Definition

# Which tasks are not checks. Everything else is one, which is the way round that fails closed: a
# task added and forgotten about is checked, rather than quietly skipped. There is nothing here yet
# because there is nothing yet that changes anything or composes anything else.
$notGates = @()

# Read off the parameter's own attribute, so this list and the one above are the only two facts and
# neither is a copy of the other. Captured at script scope because inside a function
# `$MyInvocation` describes the function.
$declaredTasks = ($MyInvocation.MyCommand.Parameters['Task'].Attributes |
    Where-Object { $_ -is [System.Management.Automation.ValidateSetAttribute] } |
    Select-Object -First 1).ValidValues

if ($ListGates) {
    $declaredTasks | Where-Object { $notGates -notcontains $_ }
    exit 0
}

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
# `spikes/build-environment` reproduces it.
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

# The prose checks. Node rather than PowerShell because markdownlint and cspell are the tools that
# do this well, and both are pinned in `package.json` so that every machine agrees on what clean
# means.
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
}

switch ($Task) {
    'Lint' { Invoke-Lint }
    default { throw "There is no case for task '$Task'. Every name the Task parameter allows needs one here, or a task would report success having run nothing." }
}

Write-Host ''
Write-Host "$Task finished." -ForegroundColor Green
