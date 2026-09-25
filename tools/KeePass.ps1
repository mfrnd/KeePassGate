<#
.SYNOPSIS
    Provides the exact KeePass this branch's probe is compiled against, as a portable install
    nobody had to put there by hand.

.DESCRIPTION
    The probe measures one KeePass, and which one is a fact about this branch rather than about
    somebody's machine. This is the mainline's script, less the advisory check for a newer release,
    because a measurement is re-run against the version it was taken on. `keepass.version` says
    which, and
    this fetches it into `.keepass\<version>\`, gitignored and named after the version so that
    changing the pin provides the new one rather than silently reusing the old.

    **What it verifies, in order, before anything is put in place.** The archive against a SHA-256
    pinned in `keepass.sha256`. Then, after extracting, that `KeePass.exe` says it is KeePass, that
    its file version is the version asked for, and that its Authenticode signature is valid and
    belongs to KeePass's author. The last of those is the one that does not depend on where the
    bytes came from, which is why it is done even when the hash already matched.

    **Where it fetches from, and why not a package manager.** The portable archive from
    SourceForge, which is KeePass's own distribution channel, at a URL built from the pinned
    version. Scoop and Chocolatey both fetch from the same place; what they add is a dependency, a
    manifest carrying a SHA-1, and an installer to unpack rather than a portable build, and neither
    solves pinning an exact version, because their manifests only ever carry the current one.
    Pinning the hash here is the stronger half of what they offer, and it costs nothing to keep.

    **Measuring a later KeePass** is an edit to `keepass.version` and a new line in
    `keepass.sha256`, deliberately, by a person, and then a re-run recorded beside the old one.

.EXAMPLE
    . .\tools\KeePass.ps1
    Install-KeePass
#>

Set-StrictMode -Version Latest

$script:Root = Split-Path -Parent $PSScriptRoot
$script:Signer = 'Dominik Reichl'

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
        Whether the pinned version is already here and is what it claims to be.
    #>
    [CmdletBinding()]
    param([string] $Directory, [string] $Version)

    $exe = Join-Path $Directory 'KeePass.exe'
    if (-not (Test-Path $exe)) {
        return $false
    }

    # A directory named after a version whose executable says something else is worse than an empty
    # one, because everything downstream would believe the name.
    $onDisk = (Get-Item $exe).VersionInfo.FileVersion
    return $onDisk -like "$Version*"
}

function Install-KeePass {
    <#
    .SYNOPSIS
        Provides the pinned KeePass and returns the directory holding it.

    .DESCRIPTION
        Does nothing when it is already there. Everything it fetches is verified before it is put
        in place, and a failure anywhere leaves no half-populated directory behind.
    #>
    [CmdletBinding()]
    param([switch] $Force)

    $pin = Get-KeePassPin
    $directory = Get-KeePassDirectory -Version $pin.Version

    if (-not $Force -and (Test-KeePassProvided -Directory $directory -Version $pin.Version)) {
        # Checked every time, not only when it was fetched. `Test-KeePassProvided` asks whether an
        # executable is there and says the right version, which a replaced one also answers, and the
        # signature check below is the one that does not depend on where the bytes came from. A
        # directory verified once and trusted for ever is the wrong shape for the copy every build
        # compiles against, and it is gitignored, so nothing else would notice either.
        Assert-KeePassAuthentic -Directory $directory -Version $pin.Version
        return $directory
    }

    $major = $pin.Version.Split('.')[0]
    $url = "https://sourceforge.net/projects/keepass/files/KeePass%20$major.x/$($pin.Version)/$($pin.Archive)/download"

    $staging = Join-Path ([System.IO.Path]::GetTempPath()) ("kpg-keepass-" + [System.Guid]::NewGuid().ToString('n'))
    New-Item -ItemType Directory -Path $staging | Out-Null

    try {
        $archive = Join-Path $staging $pin.Archive
        Write-Host "  fetching KeePass $($pin.Version)" -ForegroundColor DarkGray

        # A browser user agent gets SourceForge's interstitial page rather than the file, which
        # arrives as a plausible 137 KB of HTML. Anything else gets the archive.
        Invoke-WebRequest -Uri $url -OutFile $archive -UseBasicParsing -TimeoutSec 300 `
            -Headers @{ 'User-Agent' = 'KeePassGate-build' }

        $actual = (Get-FileHash $archive -Algorithm SHA256).Hash.ToUpperInvariant()

        if (-not $pin.Sha256) {
            throw @"
KeePass $($pin.Version) is not pinned in keepass.sha256, so what arrived cannot be checked.
What arrived this time hashes to:

$actual  $($pin.Archive)

Add that line to keepass.sha256 if that is the version intended, and run again. Pinning a hash is
a deliberate act on purpose: it is the difference between taking a version and taking whatever a
download happened to give.
"@
        }

        if ($actual -ne $pin.Sha256) {
            throw "KeePass $($pin.Version) hashed to $actual, and keepass.sha256 pins $($pin.Sha256). Nothing was installed."
        }

        $unpacked = Join-Path $staging 'unpacked'
        Expand-Archive -Path $archive -DestinationPath $unpacked -Force

        Assert-KeePassAuthentic -Directory $unpacked -Version $pin.Version

        # Only now, and as one move, so a failure above never leaves a directory that looks
        # provided.
        if (Test-Path $directory) {
            Remove-Item $directory -Recurse -Force
        }

        New-Item -ItemType Directory -Path (Split-Path -Parent $directory) -Force | Out-Null
        Move-Item -Path $unpacked -Destination $directory

        Write-Host "  KeePass $($pin.Version) is in $directory" -ForegroundColor DarkGray
        return $directory
    }
    finally {
        Remove-Item $staging -Recurse -Force -ErrorAction SilentlyContinue
    }
}

function Assert-KeePassAuthentic {
    <#
    .SYNOPSIS
        Refuses anything that is not a signed KeePass of the version asked for.

    .DESCRIPTION
        The check that does not depend on where the bytes came from. A hash says the archive is the
        one that was pinned; this says the program inside it is KeePass, built by KeePass's author,
        and not modified since.
    #>
    [CmdletBinding()]
    param([string] $Directory, [string] $Version)

    $exe = Join-Path $Directory 'KeePass.exe'
    if (-not (Test-Path $exe)) {
        throw "The archive holds no KeePass.exe."
    }

    $info = (Get-Item $exe).VersionInfo
    if ($info.ProductName -ne 'KeePass') {
        throw "That KeePass.exe reports the product name '$($info.ProductName)'."
    }

    if ($info.FileVersion -notlike "$Version*") {
        throw "That KeePass.exe is version $($info.FileVersion), not $Version."
    }

    $signature = Get-AuthenticodeSignature $exe
    if ($signature.Status -ne 'Valid') {
        throw "That KeePass.exe is not validly signed: $($signature.Status). Nothing was installed."
    }

    $subject = $signature.SignerCertificate.Subject
    if ($subject -notlike "*$script:Signer*") {
        throw "That KeePass.exe is signed by '$subject' rather than by KeePass's author."
    }
}
