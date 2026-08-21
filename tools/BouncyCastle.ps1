<#
.SYNOPSIS
    Provides the Bouncy Castle assembly `ManagedTlsProbe` is compiled against.

.DESCRIPTION
    One probe on this branch references something outside the framework, and a measurement is about
    this branch rather than about whatever happened to be on somebody's machine. So the library is
    provided the way the mainline provides the KeePass it builds against: `bouncycastle.version`
    says which release, `bouncycastle.sha256` says what it must arrive as, and this fetches it into
    `.bouncycastle\<version>\`, which is not committed.

    Verified twice, because the two checks fail differently. The hash says the package is the one
    that was pinned. The Authenticode signature says the assembly inside it was built by Bouncy
    Castle and not modified since, which is the check that does not depend on where the bytes came
    from.

    The directory is named after the version, so changing the pin cannot quietly reuse the last one,
    and a version with no line in `bouncycastle.sha256` is refused with the hash that arrived
    printed, so pinning it is a deliberate act.

.EXAMPLE
    . .\tools\BouncyCastle.ps1
    $bc = Install-BouncyCastle
#>

Set-StrictMode -Version Latest

# `Get-FileHash` and `Get-AuthenticodeSignature` both live in modules, and a `PSModulePath` that a
# .NET Core PowerShell put its own entries on first hides them: the call then reports a command
# found in a module that would not load, which reads like a broken script rather than a borrowed
# path. Putting this runtime's own modules first costs nothing when they already are.
# `spikes/build-environment` reproduces it.
$ownModules = Join-Path $PSHOME 'Modules'
$env:PSModulePath = $ownModules + [System.IO.Path]::PathSeparator +
    (($env:PSModulePath -split [System.IO.Path]::PathSeparator |
        Where-Object { $_ -and $_ -ne $ownModules }) -join [System.IO.Path]::PathSeparator)

$script:Root = Split-Path -Parent $PSScriptRoot
$script:Package = 'bouncycastle.cryptography'
$script:Assembly = 'BouncyCastle.Cryptography.dll'
$script:Signer = 'Legion of the Bouncy Castle Inc.'

# `net461` rather than `netstandard2.0`, because every probe here runs on .NET Framework and that is
# the one NuGet would pick for it.
$script:Framework = 'net461'

function Get-BouncyCastlePin {
    <#
    .SYNOPSIS
        The version this branch is pinned to, and the hash it is expected to arrive with.
    #>
    [CmdletBinding()]
    param()

    $versionFile = Join-Path $script:Root 'bouncycastle.version'
    if (-not (Test-Path $versionFile)) {
        throw "No bouncycastle.version at $versionFile. That file is what says which release this branch measures."
    }

    $version = (Get-Content $versionFile -Raw).Trim()
    if ($version -notmatch '^\d+\.\d+\.\d+$') {
        throw "bouncycastle.version says '$version', which is not a Bouncy Castle version."
    }

    # The sha256sum format, so the same file can be checked with `sha256sum -c` and so more than one
    # version can be pinned at once. Going back to an earlier one then needs no network.
    $archive = "$script:Package.$version.nupkg"
    $expected = $null
    $hashFile = Join-Path $script:Root 'bouncycastle.sha256'
    if (Test-Path $hashFile) {
        foreach ($line in Get-Content $hashFile) {
            if ($line -match "^([0-9a-fA-F]{64})\s+\*?$([regex]::Escape($archive))\s*$") {
                $expected = $Matches[1].ToUpperInvariant()
            }
        }
    }

    [pscustomobject]@{
        Version = $version
        Sha256  = $expected
        Archive = $archive
    }
}

function Get-BouncyCastleDirectory {
    <#
    .SYNOPSIS
        Where a given version lives. Named after the version, so two of them cannot be confused.
    #>
    [CmdletBinding()]
    param([string] $Version)

    if (-not $Version) {
        $Version = (Get-BouncyCastlePin).Version
    }

    Join-Path (Join-Path $script:Root '.bouncycastle') $Version
}

function Test-BouncyCastleProvided {
    <#
    .SYNOPSIS
        Whether the pinned version is already here and is what it claims to be.
    #>
    [CmdletBinding()]
    param([string] $Directory, [string] $Version)

    $dll = Join-Path $Directory $script:Assembly
    if (-not (Test-Path $dll)) {
        return $false
    }

    # A directory named after a version whose assembly says something else is worse than an empty
    # one, because everything downstream would believe the name.
    $onDisk = (Get-Item $dll).VersionInfo.FileVersion
    return $onDisk -like "$Version*"
}

function Install-BouncyCastle {
    <#
    .SYNOPSIS
        Provides the pinned assembly and returns the path to it.

    .DESCRIPTION
        Does nothing when it is already there. Everything it fetches is verified before it is put in
        place, and a failure anywhere leaves no half-populated directory behind.
    #>
    [CmdletBinding()]
    param([switch] $Force)

    $pin = Get-BouncyCastlePin
    $directory = Get-BouncyCastleDirectory -Version $pin.Version

    if (-not $Force -and (Test-BouncyCastleProvided -Directory $directory -Version $pin.Version)) {
        return (Join-Path $directory $script:Assembly)
    }

    $url = "https://api.nuget.org/v3-flatcontainer/$script:Package/$($pin.Version)/$($pin.Archive)"

    $staging = Join-Path ([System.IO.Path]::GetTempPath()) ("kpg-bc-" + [System.Guid]::NewGuid().ToString('n'))
    New-Item -ItemType Directory -Path $staging | Out-Null

    try {
        # `Expand-Archive` goes by the extension and refuses a `.nupkg`, though a `.nupkg` is a zip.
        $archive = Join-Path $staging ($pin.Archive + '.zip')
        Write-Host "  fetching Bouncy Castle $($pin.Version)" -ForegroundColor DarkGray

        Invoke-WebRequest -Uri $url -OutFile $archive -UseBasicParsing -TimeoutSec 300 `
            -Headers @{ 'User-Agent' = 'KeePassGate-spikes' }

        $actual = (Get-FileHash $archive -Algorithm SHA256).Hash.ToUpperInvariant()

        if (-not $pin.Sha256) {
            throw @"
Bouncy Castle $($pin.Version) is not pinned in bouncycastle.sha256, so what arrived cannot be
checked. What arrived this time hashes to:

$actual  $($pin.Archive)

Add that line to bouncycastle.sha256 if that is the version intended, and run again. Pinning a hash
is a deliberate act on purpose: it is the difference between taking a version and taking whatever a
download happened to give.
"@
        }

        if ($actual -ne $pin.Sha256) {
            throw "Bouncy Castle $($pin.Version) hashed to $actual, and bouncycastle.sha256 pins $($pin.Sha256). Nothing was installed."
        }

        $unpacked = Join-Path $staging 'unpacked'
        Expand-Archive -Path $archive -DestinationPath $unpacked -Force

        $source = Join-Path (Join-Path (Join-Path $unpacked 'lib') $script:Framework) $script:Assembly
        if (-not (Test-Path $source)) {
            throw "The package holds no lib\$script:Framework\$script:Assembly."
        }

        $staged = Join-Path $staging 'provided'
        New-Item -ItemType Directory -Path $staged | Out-Null
        Copy-Item $source $staged

        Assert-BouncyCastleAuthentic -Directory $staged -Version $pin.Version

        # Only now, and as one move, so a failure above never leaves a directory that looks
        # provided.
        if (Test-Path $directory) {
            Remove-Item $directory -Recurse -Force
        }

        New-Item -ItemType Directory -Path (Split-Path -Parent $directory) -Force | Out-Null
        Move-Item -Path $staged -Destination $directory

        Write-Host "  Bouncy Castle $($pin.Version) is in $directory" -ForegroundColor DarkGray
        return (Join-Path $directory $script:Assembly)
    }
    finally {
        Remove-Item $staging -Recurse -Force -ErrorAction SilentlyContinue
    }
}

function Assert-BouncyCastleAuthentic {
    <#
    .SYNOPSIS
        Refuses anything that is not a signed Bouncy Castle of the version asked for.

    .DESCRIPTION
        The check that does not depend on where the bytes came from. A hash says the package is the
        one that was pinned; this says the assembly inside it was built by Bouncy Castle and not
        modified since.
    #>
    [CmdletBinding()]
    param([string] $Directory, [string] $Version)

    $dll = Join-Path $Directory $script:Assembly
    if (-not (Test-Path $dll)) {
        throw "The package holds no $script:Assembly."
    }

    $onDisk = (Get-Item $dll).VersionInfo.FileVersion
    if ($onDisk -notlike "$Version*") {
        throw "The assembly says it is $onDisk, and bouncycastle.version asks for $Version."
    }

    $signature = Get-AuthenticodeSignature $dll
    if ($signature.Status -ne 'Valid') {
        throw "$script:Assembly has no valid Authenticode signature: $($signature.Status)."
    }

    $subject = $signature.SignerCertificate.Subject
    if ($subject -notmatch [regex]::Escape($script:Signer)) {
        throw "$script:Assembly is signed by $subject rather than by $script:Signer."
    }
}
