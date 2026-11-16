<#
.SYNOPSIS
    Lists the KDBX version of every database the design's probe left in `work\`, after `run.ps1`.

.DESCRIPTION
    Written on the branch, not by the design's agent. The probe prints only the set of versions it
    met ("4.0, 4.1"), and the design names the scenario the 4.1 came from. This reads the version
    out of each file's header, as the probe's own `KdbxVersion` does, and writes one line per file
    that is not 4.0, then the count of files read, into `out\kdbx-versions.txt`.

    Windows PowerShell 5.1, from the branch root, after `run.ps1`:

        powershell.exe -NoProfile -ExecutionPolicy Bypass -File spikes\storage\probes\rules-central\design\scripts\kdbx-versions.ps1
#>
$ErrorActionPreference = 'Stop'

$design = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
$work = Join-Path $design 'work'
if (-not (Test-Path $work)) { throw "No $work. Run run.ps1 first." }

$lines = New-Object System.Collections.Generic.List[string]
$files = @(Get-ChildItem $work -Recurse -Filter '*.kdbx' | Sort-Object FullName)
foreach ($file in $files) {
    $header = New-Object byte[] 12
    $stream = [IO.File]::OpenRead($file.FullName)
    try { [void]$stream.Read($header, 0, 12) } finally { $stream.Dispose() }
    $version = '{0}.{1}' -f [BitConverter]::ToUInt16($header, 10), [BitConverter]::ToUInt16($header, 8)
    if ($version -ne '4.0') { $lines.Add("$version  $($file.FullName.Substring($work.Length + 1))") }
}
$lines.Add("$($files.Count) databases read; every one not listed is 4.0")

$out = Join-Path $design 'out'
[void](New-Item -ItemType Directory -Force $out)
[IO.File]::WriteAllLines((Join-Path $out 'kdbx-versions.txt'), $lines)
