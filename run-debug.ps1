<#
.SYNOPSIS
    Builds the plugin, deploys it, and starts the pinned KeePass with it.

.DESCRIPTION
    The Debug build, which is what to reach for when something is being worked on: it carries the
    symbols, so a debugger attached to the running KeePass lands on real lines.

    Nothing here is a shortcut around `build.ps1`. It is that script's `Run` task with the
    configuration filled in. **It runs no tests**: it compiles, checks the plugin's product name
    before copying anything because KeePass silently ignores an assembly that does not say
    `KeePass Plugin`, puts every dependency beside it rather than beside `KeePass.exe`, and starts
    KeePass. Whether the thing works is what `build.ps1 -Task All` answers.

    **It starts the KeePass this repository pins**, fetched and checked against its pinned hash and
    its author's signature rather than whichever one is on the `PATH`. It writes a configuration
    beside that executable if there is none, which is what keeps this session out of the settings
    of the KeePass somebody keeps their real database in.

    A `-KeePassDir` is taken on trust: it is checked for a `KeePass.exe` and nothing else, so what
    that sentence promises is about the pinned one.

.PARAMETER KeePassDir
    A KeePass to deploy into and start instead of the pinned one. Passed straight through.

.EXAMPLE
    .\run-debug.ps1
#>

[CmdletBinding()]
param(
    # Empty is refused rather than ignored. `if ($KeePassDir)` treated an unset variable as "use the
    # pinned one", so a caller writing `-KeePassDir $env:SOMETHING_UNSET` got a different KeePass
    # than the one they named and was told nothing.
    [ValidateNotNullOrEmpty()]
    [string] $KeePassDir
)

$ErrorActionPreference = 'Stop'

$arguments = @{ Task = 'Run'; Configuration = 'Debug' }
if ($KeePassDir) {
    $arguments['KeePassDir'] = $KeePassDir
}

& (Join-Path $PSScriptRoot 'build.ps1') @arguments
