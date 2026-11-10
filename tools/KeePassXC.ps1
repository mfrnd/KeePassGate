<#
.SYNOPSIS
    Provides the exact KeePassXC releases this branch measured, each as the Windows archive its
    release publishes, so that nobody had to put them there by hand.

.DESCRIPTION
    The same shape as `tools/KeePass.ps1`, for the other program. Which releases were measured is a
    fact about this branch rather than about somebody's machine: `keepassxc.sha256` pins each
    release by the archive it publishes, and this fetches a release into `.keepassxc\<version>\`,
    gitignored and named after the version so that two of them cannot be confused.

    **What it verifies, in order, before anything is put in place.** The archive against the
    SHA-256 pinned for it. Then, after extracting, that `KeePassXC.exe` reports itself as KeePassXC
    of the version asked for, and that both programs this branch runs, `KeePassXC.exe` and
    `keepassxc-cli.exe`, carry a valid Authenticode signature from KeePassXC's publisher. The last of
    those is the check that does not depend on where the bytes came from, which is why it is made
    again every time a provided release is asked for.

    **Nothing in the archive is run here.** Extracting and reading signatures executes none of it;
    the measurement starts the programs later, with a working directory of its own.

    **A release that is not pinned is refused before anything is fetched.** The archive's name is
    part of the pin, and it changed between releases, so there is no name to fetch until somebody
    has written it down. Pinning a release is an edit to `keepassxc.sha256`, taking the hash from
    the `.DIGEST` file the release publishes beside its archive.

.EXAMPLE
    . .\tools\KeePassXC.ps1
    Install-KeePassXC -Version 2.7.12
#>

Set-StrictMode -Version Latest

$script:KeePassXCRoot = Split-Path -Parent $PSScriptRoot
$script:KeePassXCSigner = 'DroidMonkey Apps, LLC'

function Get-KeePassXCPin {
    <#
    .SYNOPSIS
        Every release `keepassxc.sha256` pins, with its archive and hash, oldest first.
    #>
    [CmdletBinding()]
    param()

    $hashFile = Join-Path $script:KeePassXCRoot 'keepassxc.sha256'
    if (-not (Test-Path $hashFile)) {
        throw "No keepassxc.sha256 at $hashFile. That file names the KeePassXC releases this branch measures."
    }

    $pins = foreach ($line in Get-Content $hashFile) {
        if ($line -match '^([0-9a-fA-F]{64})\s+\*?(KeePassXC-(\d+\.\d+\.\d+)-[A-Za-z0-9-]+\.zip)\s*$') {
            [pscustomobject]@{
                Version = $Matches[3]
                Sha256  = $Matches[1].ToUpperInvariant()
                Archive = $Matches[2]
            }
        }
    }

    @($pins | Sort-Object { [version] $_.Version })
}

function Get-KeePassXCDirectory {
    <#
    .SYNOPSIS
        Where a given release lives. Named after the version, so two of them cannot be confused.
    #>
    [CmdletBinding()]
    param([Parameter(Mandatory)] [string] $Version)

    Join-Path (Join-Path $script:KeePassXCRoot '.keepassxc') $Version
}

function Install-KeePassXC {
    <#
    .SYNOPSIS
        Provides one pinned KeePassXC release and returns the directory holding it.

    .DESCRIPTION
        Does nothing but check it again when it is already there. Everything it fetches is verified
        before it is put in place, and a failure anywhere leaves no half-populated directory behind.

    .PARAMETER Version
        The release, which `keepassxc.sha256` must pin.

    .PARAMETER Archive
        The release's archive, already on disk, taken in place of a download. It is hashed and its
        contents checked exactly as a download's would be.
    #>
    [CmdletBinding()]
    param([Parameter(Mandatory)] [string] $Version, [string] $Archive, [switch] $Force)

    $pin = Get-KeePassXCPin | Where-Object { $_.Version -eq $Version }
    if (-not $pin) {
        throw "KeePassXC $Version is not pinned in keepassxc.sha256, so there is nothing to fetch and nothing to check it against. Add the line from that release's own .DIGEST file if it is the release intended."
    }

    $directory = Get-KeePassXCDirectory -Version $Version

    if (-not $Force -and (Test-Path (Join-Path $directory 'KeePassXC.exe'))) {
        Assert-KeePassXCAuthentic -Directory $directory -Version $Version
        return $directory
    }

    $url = "https://github.com/keepassxreboot/keepassxc/releases/download/$Version/$($pin.Archive)"

    $staging = Join-Path ([System.IO.Path]::GetTempPath()) ("kpg-keepassxc-" + [System.Guid]::NewGuid().ToString('n'))
    New-Item -ItemType Directory -Path $staging | Out-Null

    try {
        $file = Join-Path $staging $pin.Archive

        if ($Archive) {
            Write-Host "  taking KeePassXC $Version from $Archive" -ForegroundColor DarkGray
            Copy-Item -LiteralPath $Archive -Destination $file
        }
        else {
            Write-Host "  fetching KeePassXC $Version" -ForegroundColor DarkGray
            Invoke-WebRequest -Uri $url -OutFile $file -UseBasicParsing -TimeoutSec 600 `
                -Headers @{ 'User-Agent' = 'KeePassGate-build' }
        }

        $actual = (Get-FileHash $file -Algorithm SHA256).Hash.ToUpperInvariant()
        if ($actual -ne $pin.Sha256) {
            throw "KeePassXC $Version hashed to $actual, and keepassxc.sha256 pins $($pin.Sha256). Nothing was installed."
        }

        # `Expand-Archive` takes minutes over an archive this size under Windows PowerShell 5.1; the
        # class it wraps takes seconds and extracts the same files.
        Add-Type -AssemblyName System.IO.Compression.FileSystem
        $unpacked = Join-Path $staging 'unpacked'
        [System.IO.Compression.ZipFile]::ExtractToDirectory($file, $unpacked)

        # Every archive measured holds one directory with the release inside it. Anything else is a
        # layout nobody has looked at, and is refused rather than guessed at.
        $top = @(Get-ChildItem -LiteralPath $unpacked -Force)
        if ($top.Count -ne 1 -or -not $top[0].PSIsContainer) {
            throw "The KeePassXC $Version archive does not hold exactly one directory. Nothing was installed."
        }

        Assert-KeePassXCAuthentic -Directory $top[0].FullName -Version $Version

        # Only now, and as one move, so a failure above never leaves a directory that looks
        # provided.
        if (Test-Path $directory) {
            Remove-Item $directory -Recurse -Force
        }

        New-Item -ItemType Directory -Path (Split-Path -Parent $directory) -Force | Out-Null
        Move-Item -LiteralPath $top[0].FullName -Destination $directory

        Write-Host "  KeePassXC $Version is in $directory" -ForegroundColor DarkGray
        return $directory
    }
    finally {
        Remove-Item $staging -Recurse -Force -ErrorAction SilentlyContinue
    }
}

function Assert-KeePassXCAuthentic {
    <#
    .SYNOPSIS
        Refuses anything that is not a signed KeePassXC of the version asked for.

    .DESCRIPTION
        `KeePassXC.exe` carries the product name and version; `keepassxc-cli.exe` carries neither,
        so for that program the signature is the whole of the check.
    #>
    [CmdletBinding()]
    param([string] $Directory, [string] $Version)

    $gui = Join-Path $Directory 'KeePassXC.exe'
    $cli = Join-Path $Directory 'keepassxc-cli.exe'
    foreach ($exe in $gui, $cli) {
        if (-not (Test-Path $exe)) {
            throw "The KeePassXC $Version release holds no $(Split-Path -Leaf $exe)."
        }
    }

    $info = (Get-Item $gui).VersionInfo
    if ($info.ProductName -ne 'KeePassXC') {
        throw "That KeePassXC.exe reports the product name '$($info.ProductName)'."
    }

    # Exact rather than a prefix: 2.7.1 is a prefix of 2.7.12.
    if ($info.FileVersion -ne $Version) {
        throw "That KeePassXC.exe is version $($info.FileVersion), not $Version."
    }

    foreach ($exe in $gui, $cli) {
        $signature = Get-AuthenticodeSignature $exe
        if ($signature.Status -ne 'Valid') {
            throw "$exe is not validly signed: $($signature.Status)."
        }

        $subject = $signature.SignerCertificate.Subject
        if ($subject -notlike "*$script:KeePassXCSigner*") {
            throw "$exe is signed by '$subject' rather than by KeePassXC's publisher."
        }
    }
}
