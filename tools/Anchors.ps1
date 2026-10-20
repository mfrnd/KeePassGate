<#
.SYNOPSIS
    Checks the section anchors the design documents are referred to by.

.DESCRIPTION
    A reference to "the part of `DESIGN.md` about resolution" is a reference nobody can follow
    mechanically: it survives the section being renamed, moved or split without ever pointing
    anywhere different, and nothing anywhere reports that it has stopped being true. An anchor is
    the same reference with a handle on it. Every section of the four documents that settle things
    carries a number, every number is unique and permanent, and one of them is a single grep away
    from the paragraph that defines it and from every place that leans on it.

    The routing document is a fifth, and anchored differently. `openapi.yaml` has no headings and
    a key on every line, so a number there sits at the end of a comment line between keys, beside
    what it names, and only what something cites carries one. Numbering every operation, schema
    and response would put a handle on every line of the routing table for the sake of the few
    that are ever pointed at.

    The numbers are flat rather than hierarchical, and that is the whole point of them. A section
    numbered by where it sits has to be renumbered when something is inserted above it, which
    silently rewrites every reference that was correct the day before. A flat number is allocated
    once, above the highest one its document has used, and never changes and is never reused, so a
    reference is wrong only when somebody deletes what it points at, which is the case worth
    hearing about.

    Each document owns a hundred, so a number says which document it is in before anything is
    looked up.

    What is checked here:

      - every section of an anchored Markdown document carries an anchor;
      - every anchor is inside its own document's hundred;
      - no anchor is defined twice, anywhere;
      - every reference resolves to an anchor that exists;
      - no new anchor takes a number at or below the highest the document has ever used.

    The last of those is what the working tree cannot see on its own, so it is asked of the history.
    A gap is not evidence of anything: a number is never reused after the section holding it is
    deleted, so gaps accumulate and are supposed to. What makes a low number wrong is that it is
    new, and the ceiling it has to clear is the highest number the document has **ever** carried,
    not the highest it carries now. Where there is no repository, no earlier commit, or no earlier
    copy of the document, nothing is said, because those are states where nothing can be known
    rather than states where something is wrong.

    The section sign is built from its code point rather than written down, because this file is
    UTF-8 without a mark and Windows PowerShell reads such a file as the system code page. A
    literal would work in the editor, work in the tests, and match nothing on somebody else's
    machine.

.PARAMETER Root
    The repository root. Everything is reported relative to it.
#>

[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)] [string] $Root
)

$ErrorActionPreference = 'Stop'

$sign = [char] 0x00A7

# The documents that settle things, each with the hundred it allocates from. A document is checked
# only if it is present: `API.md` arrives with the routes it describes, several steps after the
# other three, and a gate demanding it from the first commit would fail every commit before it.
# Keys are written with forward slashes, which `Join-Path` and git both take on every platform.
$routing = 'src/KeePassGate/Spec/openapi.yaml'
$blocks = [ordered] @{
    'DESIGN.md'  = 100
    'CA.md'      = 200
    'PAIRING.md' = 300
    'API.md'     = 400
    $routing     = 500
}

# Where a definition may sit. A Markdown document defines one at the end of a heading below the
# title, and every such heading has to carry one. The routing document defines one at the end of a
# comment line, and a comment without one is only a comment. A `#` inside a block scalar is text a
# client reads rather than a comment, and the pattern cannot tell the two apart by itself: an
# indented line opening with `#` inside a description would be read as a site, which is one more
# reason a description holds no section sign at all.
function Get-Site([string] $document) {
    if ($document -eq $routing) {
        return '^\s*#'
    }

    return '^#{2,3} '
}

# Every text file the repository has, as git answers it, which is what `RepositoryFiles.ps1` says.
$files = @(& (Join-Path $PSScriptRoot 'RepositoryFiles.ps1') -Root $Root)

$definition = " $sign(\d{3})`$"
$reference = "$sign(\d{3})"

$problems = @()

# Pass one: the definitions. An anchor is defined by sitting at the end of a heading, and nowhere
# else, so that what a number names is always a section and never a sentence about one. In the
# routing document the comment beside a key stands in for the heading, for the same reason.
$defined = @{}
$missing = @{}
$highest = @{}
foreach ($document in $blocks.Keys) {
    $path = Join-Path $Root $document
    if (-not (Test-Path $path)) {
        continue
    }

    $base = $blocks[$document]
    $highest[$document] = $base
    $missing[$document] = @()
    $site = Get-Site $document

    $number = 0
    foreach ($line in Get-Content -Path $path -Encoding UTF8) {
        $number++

        # Level one is the document's own title, and the one divider inside `DESIGN.md`. Neither is
        # a section anybody cites, and both would take a number that then has to mean something.
        if ($line -notmatch $site) {
            continue
        }

        # A comment in the routing document that ends in a number is a definition, so one ending in
        # a citation of another document is reported below as outside this document's block, which
        # is the right answer: a citation in a comment is written where it does not end the line.
        $anchor = [regex]::Match($line, $definition)
        if (-not $anchor.Success) {
            if ($document -ne $routing) {
                $missing[$document] += $number
            }

            continue
        }

        $value = [int] $anchor.Groups[1].Value
        if ($value -lt $base -or $value -gt ($base + 99)) {
            $problems += "${document}:${number}: section $value is outside this document's block, which is $base to $($base + 99)."
            continue
        }

        if ($defined.ContainsKey($value)) {
            $problems += "${document}:${number}: section $value is already defined at $($defined[$value])."
            continue
        }

        $defined[$value] = "${document}:${number}"
        if ($value -gt $highest[$document]) {
            $highest[$document] = $value
        }
    }
}

# Reported after the whole document has been read, so that the number to use is the one that is
# actually free rather than the one that was free when the first gap was found.
foreach ($document in $missing.Keys) {
    foreach ($line in $missing[$document]) {
        $next = $highest[$document] + 1
        $problems += "${document}:${line}: this section has no anchor. The next free number here is $next."
    }
}

# Pass one and a half: a number that fills a gap.
#
# Asked of the history rather than of the tree. A gap is legitimate and says nothing on its own, and
# the ceiling a new number has to clear is the highest the document has ever carried, including
# numbers whose sections have since gone. `git log -p` over the one file collects every anchor ever
# added to it, which is one call per document.
#
# Compared against the commit when the document has been edited, and against the commit before it
# when it has not, so the same check answers both "before I commit this" and "did that commit do
# it".
#
# Every git call runs under `Continue`, and reads as UTF-8. A native command writing to stderr
# becomes a terminating error under `Stop`, and git warns about line endings whenever it touches a
# file; and a native command's output is decoded with the console's encoding, which on a system code
# page turns every section sign into two characters and makes the ceiling collapse to the document's
# base. Both of those were met while writing this. `BuildStepProbe.ps1` on
# `spikes/build-environment` reproduces the first.
$repository = $false
$quietly = $ErrorActionPreference
Push-Location $Root
try {
    $ErrorActionPreference = 'Continue'
    $null = & git rev-parse --git-dir 2>$null
    $repository = ($LASTEXITCODE -eq 0)

    # A git call this script answers for itself is not this script's result. `Invoke-Step` reads
    # `$LASTEXITCODE` once the script returns, so a failure handled here has to be cleared, or the
    # gate fails naming a check that found nothing wrong.
    $global:LASTEXITCODE = 0
}
finally {
    $ErrorActionPreference = $quietly
    Pop-Location
}

if ($repository) {
    foreach ($document in $blocks.Keys) {
        $path = Join-Path $Root $document
        if (-not (Test-Path $path)) {
            continue
        }

        # The same site as pass one, read off a line `git log -p` added.
        $site = Get-Site $document
        $addition = '^\+' + $site.Substring(1) + ".* $sign(\d{3})$"

        $history = $null
        $spelling = [Console]::OutputEncoding
        Push-Location $Root
        try {
            $ErrorActionPreference = 'Continue'
            [Console]::OutputEncoding = [System.Text.Encoding]::UTF8

            & git diff --quiet -- $document 2>$null
            $edited = ($LASTEXITCODE -ne 0)
            $baseline = if ($edited) { 'HEAD' } else { 'HEAD^' }

            $history = & git log -p --format= $baseline -- $document 2>$null
            if ($LASTEXITCODE -ne 0) {
                $history = $null
            }

            # As above: a shallow clone has no `HEAD^`, and that is answered here rather than
            # reported as this script's exit code.
            $global:LASTEXITCODE = 0
        }
        finally {
            [Console]::OutputEncoding = $spelling
            $ErrorActionPreference = $quietly
            Pop-Location
        }

        if ($null -eq $history) {
            continue
        }

        $spent = @{}
        $ceiling = $blocks[$document]
        foreach ($line in $history) {
            $anchor = [regex]::Match($line, $addition)
            if (-not $anchor.Success) {
                continue
            }

            $value = [int] $anchor.Groups[1].Value
            $spent[$value] = $true
            if ($value -gt $ceiling) {
                $ceiling = $value
            }
        }

        # A history that yields no anchors at all is a history this could not read. Saying nothing is
        # right; carrying on would compare every number against the document's base and approve all
        # of them.
        if ($spent.Count -eq 0) {
            continue
        }

        $number = 0
        foreach ($line in Get-Content -Path $path -Encoding UTF8) {
            $number++
            if ($line -notmatch $site) {
                continue
            }

            $anchor = [regex]::Match($line, $definition)
            if (-not $anchor.Success) {
                continue
            }

            $value = [int] $anchor.Groups[1].Value
            if ($spent.ContainsKey($value) -or $value -gt $ceiling) {
                continue
            }

            $problems += "${document}:${number}: section $value is new, and $ceiling is the highest this document has ever used. A number is taken above that and never reused, so the number here is $($ceiling + 1)."
        }
    }
}

# Pass two: the references. Everything that is not a definition is one, including the ones inside
# the anchored documents themselves, which cite each other more than anything else does.
$references = 0
foreach ($file in $files) {
    # A tool that looks for a token holds the token. This one explains the form in its own header,
    # and an explanation owes nobody a section.
    if ($file.FullName.EndsWith([IO.Path]::Combine('tools', 'Anchors.ps1'))) {
        continue
    }

    $relative = $file.FullName.Substring($Root.Length).TrimStart([IO.Path]::DirectorySeparatorChar)
    $key = $relative.Replace([IO.Path]::DirectorySeparatorChar, '/')
    $anchored = $blocks.Contains($key)
    $site = Get-Site $key

    $number = 0
    foreach ($line in Get-Content -Path $file.FullName -Encoding UTF8) {
        $number++
        if ($line.IndexOf($sign) -lt 0) {
            continue
        }

        # A definition is not a reference to itself.
        $body = if ($anchored -and $line -match $site) {
            [regex]::Replace($line, $definition, '')
        }
        else {
            $line
        }

        foreach ($hit in [regex]::Matches($body, $reference)) {
            $references++
            $value = [int] $hit.Groups[1].Value
            if (-not $defined.ContainsKey($value)) {
                $problems += "${relative}:${number}: section $value is not a section of any document."
            }
        }

        # A section sign with no number after it is somebody meaning to cite and not citing.
        if ([regex]::IsMatch($body, "$sign(?!\d{3})")) {
            $problems += "${relative}:${number}: a reference is the section sign followed by three digits."
        }
    }
}

if ($problems.Count -gt 0) {
    foreach ($problem in $problems) {
        Write-Host "  $problem" -ForegroundColor Red
    }

    throw "$($problems.Count) anchor problem(s)."
}

Write-Host "  $($defined.Count) anchors, $references references, all resolving."
