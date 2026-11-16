# Added on this branch from the command the design's agent ran for `design.md` sections 9.1 and 20,
# which it ran inline in PowerShell 7 and did not keep as a file. The body is that command as it
# was run, with the two paths into its working directory made relative to this script and its last
# line, which showed another run's output, left out. Run it in PowerShell 7, as it was measured.
$d = Join-Path $PSScriptRoot '..\work\replace'
if (Test-Path -LiteralPath $d) { Remove-Item -LiteralPath $d -Recurse -Force }
New-Item -ItemType Directory -Path $d | Out-Null
$final = Join-Path $d 'a1b2c3d4e5f60718.kpgw'
$temp = Join-Path $d '~a1b2c3d4e5f60718.tmp'
$lines = @("# File.Replace under a reader, PowerShell $($PSVersionTable.PSVersion) (.NET $([Environment]::Version)), NTFS")
foreach ($share in 'Read', 'ReadWrite', 'Read, Delete', 'ReadWrite, Delete') {
    [System.IO.File]::WriteAllBytes($final, [byte[]](1..100))
    [System.IO.File]::WriteAllBytes($temp, [byte[]](1..120))
    $reader = New-Object System.IO.FileStream($final, [System.IO.FileMode]::Open, [System.IO.FileAccess]::Read, [System.IO.FileShare]$share)
    try {
        [System.IO.File]::Replace($temp, $final, [NullString]::Value)
        $lines += "a reader holding the file with share '$share': replaced, length now $((Get-Item -LiteralPath $final).Length)"
    } catch {
        $e = $_.Exception; if ($e.InnerException) { $e = $e.InnerException }
        $lines += "a reader holding the file with share '$share': refused, $($e.GetType().Name): $($e.Message)"
    } finally { $reader.Dispose() }
    if (Test-Path -LiteralPath $temp) { Remove-Item -LiteralPath $temp -Force }
}
$lines | Tee-Object -FilePath (Join-Path $PSScriptRoot '..\out\replace.txt')
