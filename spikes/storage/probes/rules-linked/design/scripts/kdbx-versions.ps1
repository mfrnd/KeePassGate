<#
.SYNOPSIS
    Reads the KDBX version out of the header of every database the design's probe left in `work\`.

.DESCRIPTION
    A check written on the branch, not a probe of the papers: the design's probe prints a KDBX
    version only once per KeePassXC release, after its plain save, and the design's section 6 says
    KDBX 4.0 for what KeePass writes. This reads the version from each file's header, as the
    probe's own `KdbxVersion` does (the major at byte 10, the minor at byte 8), and counts them by
    the part of the run that wrote them. Run it after `run.ps1`.

    Windows PowerShell 5.1, from the branch root:

        powershell.exe -NoProfile -ExecutionPolicy Bypass -File spikes\storage\probes\rules-linked\design\scripts\kdbx-versions.ps1
#>
$ErrorActionPreference = 'Stop'

$design = Split-Path -Parent $PSScriptRoot
$work = Join-Path $design 'work'
$lines = New-Object System.Collections.Generic.List[string]
foreach ($part in 'kp', 'xc') {
    $root = Join-Path $work $part
    $versions = @{}
    $odd = New-Object System.Collections.Generic.List[string]
    foreach ($file in Get-ChildItem -LiteralPath $root -Recurse -File -Filter '*.kdbx') {
        $header = New-Object byte[] 12
        $stream = [IO.File]::OpenRead($file.FullName)
        try { [void]$stream.Read($header, 0, 12) } finally { $stream.Dispose() }
        $version = '{0}.{1}' -f [BitConverter]::ToUInt16($header, 10), [BitConverter]::ToUInt16($header, 8)
        if ($versions.ContainsKey($version)) { $versions[$version]++ } else { $versions[$version] = 1 }
        if ($version -ne '4.0') { $odd.Add("  $version $($file.FullName.Substring($work.Length + 1))") }
    }
    $counts = ($versions.Keys | Sort-Object | ForEach-Object { "$_ x$($versions[$_])" }) -join ', '
    $lines.Add("work\$part : $counts")
    $lines.AddRange($odd)
}

$out = Join-Path $design 'out'
[void](New-Item -ItemType Directory -Force $out)
[IO.File]::WriteAllLines((Join-Path $out 'kdbx-versions.txt'), $lines)
$lines
