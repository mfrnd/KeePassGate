<#
.SYNOPSIS
    Provides the KeePass 2.62 source the ground report and many papers read, checks it against the
    hash and size the report quotes, and extracts it into `src\`, where the papers cite it.

.DESCRIPTION
    Added on this branch. The report read `KeePass-2.62-Source.zip` from SourceForge and quotes its
    SHA-256 and size as measured, and as what `https://keepass.info/integrity.html` publishes for
    it. The archive is somebody else's code, so it is fetched rather than committed: `src\` is
    ignored by the `.gitignore` beside this script, and nothing here keeps a copy anywhere else.

    What it writes to `out\source.txt`: the archive's SHA-256 and length, and whether each equals
    the value the report quotes. With -Integrity, also whether the integrity page lists that hash
    and that size for that file, the page fetched into `out\integrity.html` or taken from
    -IntegrityPage.

    -Archive takes an archive already on disk in place of the download, checked the same way.

    Run from the branch root in Windows PowerShell 5.1:

        powershell.exe -NoProfile -ExecutionPolicy Bypass -File spikes\storage\probes\ground-keepass\source.ps1 -Integrity
#>
param([string] $Archive, [switch] $Integrity, [string] $IntegrityPage)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

# As `ground-keepass.md` quotes them, under "What was run".
$name = 'KeePass-2.62-Source.zip'
$quotedHash = '9C07B0539A013EE5E22B26FD806E472C1858BC2F3F7B3B4406F4404774263AC0'
$quotedLength = 5339759
$url = "https://sourceforge.net/projects/keepass/files/KeePass%202.x/2.62/$name/download"

$out = Join-Path $PSScriptRoot 'out'
$src = Join-Path $PSScriptRoot 'src'
New-Item -ItemType Directory -Force $out | Out-Null

$staging = Join-Path ([System.IO.Path]::GetTempPath()) ('kpg-source-' + [System.Guid]::NewGuid().ToString('n'))
New-Item -ItemType Directory -Path $staging | Out-Null
try {
    $file = Join-Path $staging $name
    if ($Archive) {
        Copy-Item -LiteralPath $Archive -Destination $file
    }
    else {
        # The user agent the scaffold's `tools\KeePass.ps1` sends, for the reason it gives: a
        # browser's gets SourceForge's interstitial page rather than the file.
        Invoke-WebRequest -Uri $url -OutFile $file -UseBasicParsing -TimeoutSec 300 `
            -Headers @{ 'User-Agent' = 'KeePassGate-build' }
    }

    $hash = (Get-FileHash $file -Algorithm SHA256).Hash.ToUpperInvariant()
    $length = (Get-Item $file).Length
    $lines = New-Object System.Collections.Generic.List[string]
    $lines.Add("$name SHA-256 $hash, equal to the report's: $($hash -eq $quotedHash)")
    $lines.Add("$name length $length bytes, equal to the report's: $($length -eq $quotedLength)")

    if ($Integrity) {
        $page = Join-Path $out 'integrity.html'
        if ($IntegrityPage) {
            Copy-Item -LiteralPath $IntegrityPage -Destination $page -Force
        }
        else {
            Invoke-WebRequest -Uri 'https://keepass.info/integrity.html' -OutFile $page -UseBasicParsing -TimeoutSec 120
        }

        # The page prints a hash in groups of eight digits; the text between the file's name and
        # the next file's is what it gives for this one.
        $text = [System.IO.File]::ReadAllText($page) -replace '<[^>]+>', ' ' -replace '&[a-z]+;', ' ' -replace '\s+', ' '
        $at = $text.IndexOf("$name :")
        $about = if ($at -lt 0) { '' } else { $text.Substring($at, [Math]::Min(400, $text.Length - $at)) }
        $grouped = ($quotedHash -split '(.{8})' | Where-Object { $_ }) -join ' '
        $lines.Add("integrity page names ${name}: $($at -ge 0)")
        $lines.Add("integrity page gives it SHA-256 $grouped`: $($about.Contains("SHA-256: $grouped"))")
        $lines.Add("integrity page gives it Size: $quotedLength B: $($about.Contains("Size: $quotedLength B"))")
    }

    [System.IO.File]::WriteAllLines((Join-Path $out 'source.txt'), $lines)
    $lines

    if ($hash -ne $quotedHash -or $length -ne $quotedLength) {
        throw "$name is not the archive the report read, so it was not extracted."
    }

    $unpacked = Join-Path $staging 'unpacked'
    Expand-Archive -Path $file -DestinationPath $unpacked -Force
    if (Test-Path $src) { Remove-Item $src -Recurse -Force }
    Move-Item -Path $unpacked -Destination $src
    "extracted into $src"
}
finally {
    Remove-Item $staging -Recurse -Force -ErrorAction SilentlyContinue
}
