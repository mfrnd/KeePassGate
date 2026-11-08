<#
.SYNOPSIS
    Provides the exact KeePass this repository is pinned to, as a portable install nobody had to
    put there by hand.

.DESCRIPTION
    Everything here builds and tests against one KeePass, and which one is a fact about the
    repository rather than about somebody's machine. `keepass.version` at the root says which, and
    this fetches it into `.keepass\<version>\`, gitignored and named after the version so that
    changing the pin provides the new one rather than silently reusing the old.

    **What it verifies is the archive, against the SHA-256 pinned in `keepass.sha256`, and that is
    the whole of it.** Nothing is extracted from an archive whose hash is not the pin. The archive
    is kept beside the directory, as `.keepass\KeePass-<version>.zip`, so the directory can be made
    again from it without the network, and it is hashed again every time it is used. That is what
    CI caches, rather than the directory: a cache restores the archive, and the directory is
    extracted from it afresh once its hash has matched, so nothing a cache brings back is believed
    until it has. A directory already in place is taken as it is, which on somebody's own machine
    is code running as them, conceded throughout.

    **Whether an archive is KeePass at all is asked once, by a person**, when a version is pinned:
    whoever moves `keepass.version` checks that the new archive's `KeePass.exe` carries a valid
    Authenticode signature of KeePass's author before writing its hash down. After that the hash
    answers for the bytes, and checking the signature again on every build checked nothing the
    hash had not already settled.

    **Where it fetches from, and why not a package manager.** The portable archive from
    SourceForge, which is KeePass's own distribution channel, at a URL built from the pinned
    version. Scoop and Chocolatey both fetch from the same place; what they add is a dependency, a
    manifest carrying a SHA-1, and an installer to unpack rather than a portable build, and neither
    solves pinning an exact version, because their manifests only ever carry the current one.
    Pinning the hash here is the stronger half of what they offer, and it costs nothing to keep.

    **What it never does is decide.** `Test-KeePassLatest` asks KeePass's own version feed what the
    newest release is and says so, and that is all: taking a new version is an edit to
    `keepass.version` and a new hash, deliberately, by a person.
#>

Set-StrictMode -Version Latest

$script:Root = Split-Path -Parent $PSScriptRoot

function Get-KeePassPin {
    <#
    .SYNOPSIS
        The version this repository is pinned to, and the hash it is expected to arrive with.
    #>
    [CmdletBinding()]
    param()

    $versionFile = Join-Path $script:Root 'keepass.version'
    if (-not (Test-Path $versionFile)) {
        throw "No keepass.version at $versionFile. That file is what says which KeePass this repository builds against."
    }

    $version = (Get-Content $versionFile -Raw).Trim()
    if ($version -notmatch '^\d+\.\d+(\.\d+)?$') {
        throw "keepass.version says '$version', which is not a KeePass version."
    }

    # The sha256sum format, so the same file can be checked with `sha256sum -c` and so more than
    # one version can be pinned at once. Going back to an earlier one then needs no network.
    $expected = $null
    $hashFile = Join-Path $script:Root 'keepass.sha256'
    if (Test-Path $hashFile) {
        foreach ($line in Get-Content $hashFile) {
            if ($line -match "^([0-9a-fA-F]{64})\s+\*?KeePass-$([regex]::Escape($version))\.zip\s*$") {
                $expected = $Matches[1].ToUpperInvariant()
            }
        }
    }

    [pscustomobject]@{
        Version = $version
        Sha256  = $expected
        Archive = "KeePass-$version.zip"
    }
}

function Get-KeePassDirectory {
    <#
    .SYNOPSIS
        Where a given version lives. Named after the version, so two of them cannot be confused.
    #>
    [CmdletBinding()]
    param([string] $Version)

    if (-not $Version) {
        $Version = (Get-KeePassPin).Version
    }

    Join-Path (Join-Path $script:Root '.keepass') $Version
}

function Test-KeePassProvided {
    <#
    .SYNOPSIS
        Whether the pinned version is already here and is what its directory is named for.
    #>
    [CmdletBinding()]
    param([string] $Directory, [string] $Version)

    $exe = Join-Path $Directory 'KeePass.exe'
    if (-not (Test-Path -LiteralPath $exe)) {
        return $false
    }

    # A directory named after a version whose executable says something else is worse than an empty
    # directory, because everything downstream would believe the name. Compared as four numbers,
    # those the pin leaves out being zero, so that 2.61.1 is 2.61.1.0 and neither 2.61.10 nor 2.61.
    $info = (Get-Item -LiteralPath $exe).VersionInfo
    $onDisk = New-Object System.Version $info.FileMajorPart, $info.FileMinorPart, $info.FileBuildPart, $info.FilePrivatePart
    $parts = @($Version.Split('.') | ForEach-Object { [int] $_ }) + @(0, 0, 0)
    $wanted = New-Object System.Version $parts[0], $parts[1], $parts[2], $parts[3]
    return $onDisk -eq $wanted
}

function Install-KeePass {
    <#
    .SYNOPSIS
        Provides the pinned KeePass and returns the directory holding it.

    .DESCRIPTION
        Does nothing when it is already there. Otherwise extracts it from an archive whose hash is
        the pin, the archive kept from an earlier run when its hash still is and a fresh download
        when it is not, and a failure anywhere leaves no half-populated directory behind.
    #>
    [CmdletBinding()]
    param()

    $pin = Get-KeePassPin
    $directory = Get-KeePassDirectory -Version $pin.Version
    $kept = Join-Path (Split-Path -Parent $directory) $pin.Archive

    # Asked before anything else, a directory already in place included, so that a version with no
    # hash written down is refused on every machine alike and not only on those that have to fetch
    # it. The download goes ahead all the same, since what it hashes to is what the refusal names.
    if ($pin.Sha256 -and (Test-KeePassProvided -Directory $directory -Version $pin.Version)) {
        return $directory
    }

    $staging = Join-Path ([System.IO.Path]::GetTempPath()) ("kpg-keepass-" + [System.Guid]::NewGuid().ToString('n'))
    New-Item -ItemType Directory -Path $staging | Out-Null

    try {
        # **The bytes hashed are the bytes extracted.** A kept archive is copied in first and the
        # copy is what is hashed and unpacked, so nothing can change between the two.
        $archive = Join-Path $staging $pin.Archive
        $actual = $null
        if ($pin.Sha256 -and (Test-Path -LiteralPath $kept)) {
            Copy-Item -LiteralPath $kept -Destination $archive
            $actual = (Get-FileHash -LiteralPath $archive -Algorithm SHA256).Hash.ToUpperInvariant()
            if ($actual -ne $pin.Sha256) {
                Write-Host "  the kept $($pin.Archive) hashed to $actual rather than the pin, so it is fetched again" -ForegroundColor DarkGray
                Remove-Item -LiteralPath $kept, $archive -Force
                $actual = $null
            }
        }

        if (-not $actual) {
            $major = $pin.Version.Split('.')[0]
            $url = "https://sourceforge.net/projects/keepass/files/KeePass%20$major.x/$($pin.Version)/$($pin.Archive)/download"
            Write-Host "  fetching KeePass $($pin.Version)" -ForegroundColor DarkGray

            # A browser user agent gets SourceForge's interstitial page rather than the file, which
            # arrives as a plausible 137 KB of HTML. Anything else gets the archive.
            Invoke-WebRequest -Uri $url -OutFile $archive -UseBasicParsing -TimeoutSec 300 `
                -Headers @{ 'User-Agent' = 'KeePassGate-build' }

            $actual = (Get-FileHash -LiteralPath $archive -Algorithm SHA256).Hash.ToUpperInvariant()

            # Kept where an archive is always hashed again before it is used, so that its signature
            # can be checked before it is pinned, and so that pinning it fetches nothing more.
            if (-not $pin.Sha256) {
                New-Item -ItemType Directory -Path (Split-Path -Parent $kept) -Force | Out-Null
                Move-Item -LiteralPath $archive -Destination $kept -Force
                $inspect = Join-Path ([System.IO.Path]::GetTempPath()) "keepass-$($pin.Version)"
                throw @"
KeePass $($pin.Version) is not pinned in keepass.sha256, so what arrived cannot be checked.
What arrived this time hashes to:

$actual  $($pin.Archive)

It is kept as $kept. Before pinning it, check that the KeePass.exe inside carries a valid
Authenticode signature of KeePass's author, which nothing here checks again:

Expand-Archive -LiteralPath '$kept' -DestinationPath '$inspect'
Get-AuthenticodeSignature '$inspect\KeePass.exe' | Format-List Status, SignerCertificate

Then add that line to keepass.sha256 if it is the version intended, and run again. Pinning a hash
is a deliberate act on purpose: it is the difference between taking a version and taking whatever
a download happened to give.
"@
            }

            if ($actual -ne $pin.Sha256) {
                throw "KeePass $($pin.Version) hashed to $actual, and keepass.sha256 pins $($pin.Sha256). Nothing was installed."
            }
        }

        $unpacked = Join-Path $staging 'unpacked'
        Expand-Archive -LiteralPath $archive -DestinationPath $unpacked -Force

        # Only now, and as one move, so a failure above never leaves a directory that looks
        # provided. The archive is kept once its directory is in place, for the next time the
        # directory has to be made: the next run in CI, whose cache holds the archive alone.
        if (Test-Path -LiteralPath $directory) {
            Remove-Item -LiteralPath $directory -Recurse -Force
        }

        New-Item -ItemType Directory -Path (Split-Path -Parent $directory) -Force | Out-Null
        Move-Item -LiteralPath $unpacked -Destination $directory
        if (-not (Test-Path -LiteralPath $kept)) {
            Move-Item -LiteralPath $archive -Destination $kept
        }

        Write-Host "  KeePass $($pin.Version) is in $directory" -ForegroundColor DarkGray
        return $directory
    }
    finally {
        Remove-Item -LiteralPath $staging -Recurse -Force -ErrorAction SilentlyContinue
    }
}

function Test-KeePassLatest {
    <#
    .SYNOPSIS
        Says whether a newer KeePass exists. Advisory, and never acts on the answer.

    .DESCRIPTION
        Reads KeePass's own version feed, which is on KeePass's own domain rather than on whatever
        is distributing the files. Taking a new version is an edit to `keepass.version` and a new
        line in `keepass.sha256`, by a person who meant it and who checked the new archive's
        signature first.
    #>
    [CmdletBinding()]
    param()

    $pin = Get-KeePassPin

    try {
        $feed = Invoke-WebRequest -Uri 'https://keepass.info/update/version2x.txt' -UseBasicParsing -TimeoutSec 30
    } catch {
        Write-Host "  could not reach KeePass's version feed: $($_.Exception.Message)" -ForegroundColor DarkGray
        return
    }

    if ($feed.Content -match 'KeePass:([\d.]+)') {
        $latest = $Matches[1]
        if ($latest -ne $pin.Version) {
            Write-Host "  KeePass $latest is out; this repository is pinned to $($pin.Version)." -ForegroundColor Yellow
            Write-Host "  Taking it means editing keepass.version and keepass.sha256, once its archive's signature is checked." -ForegroundColor Yellow
        }
    }
}
