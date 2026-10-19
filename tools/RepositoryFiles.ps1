<#
.SYNOPSIS
    The files this repository has, which is what git tracks, text rather than binary.

.DESCRIPTION
    What `Markers.ps1`, `Dashes.ps1` and `Anchors.ps1` read, answered once for the three of them,
    because three copies of how a question is asked are three answers to it.

    **Git says which files are ours**, as it does for `Links.ps1`. A list of extensions is a list
    that has to be added to, and the file it forgets is always one with prose in it:
    `.editorconfig`, `.gitignore` and `.gitattributes` carry comments like any other file here.

    **Tracked, and nothing else in the tree.** A file nobody has added is not yet the repository's,
    and the tree holds more of those than anybody put there on purpose: what a build or a tool
    left behind, where no `.gitignore` covers it yet. An early commit checked out in a tree that
    has built a later one has whole directories of build output its own ignore files predate, and
    a check that read them would fail that commit for somebody else's files. A new file is read
    from the moment it is added, which is before the commit that adds it.

    **Text only.** A file with a zero byte among its first eight thousand is left out, which is
    how git itself tells a binary: a picture has no sentence in it to check.

.PARAMETER Root
    The repository root.
#>

[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)] [string] $Root
)

$ErrorActionPreference = 'Stop'

# Asked under `Continue` and judged by the exit code alone, as `Anchors.ps1` asks git everything.
# Under `Stop` a line git writes to stderr ends the script the moment anything upstream merges the
# streams, which is what capturing the gate's output does, and git writes warnings on commands
# that succeed.
$quietly = $ErrorActionPreference
Push-Location $Root
try {
    $ErrorActionPreference = 'Continue'
    $names = @(& git -c core.quotePath=off ls-files --cached)
    $listed = ($LASTEXITCODE -eq 0)
}
finally {
    $ErrorActionPreference = $quietly
    Pop-Location
}

if (-not $listed) {
    throw 'git could not list the files of this repository.'
}

foreach ($name in $names) {
    # Tracked and deleted in the working tree, which leaves nothing to read. What is read is the
    # working tree's copy, so a change not yet committed is checked as it stands.
    $path = Join-Path $Root $name
    if (-not (Test-Path -LiteralPath $path -PathType Leaf)) {
        continue
    }

    $head = New-Object byte[] 8000
    $stream = [IO.File]::OpenRead($path)
    try {
        $read = $stream.Read($head, 0, $head.Length)
    }
    finally {
        $stream.Dispose()
    }

    if ([Array]::IndexOf($head, [byte] 0, 0, $read) -ge 0) {
        continue
    }

    Get-Item -LiteralPath $path
}
