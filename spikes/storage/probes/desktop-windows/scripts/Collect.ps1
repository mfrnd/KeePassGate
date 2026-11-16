<#
.SYNOPSIS
    Copies what the runs left in the work directory into an evidence directory: every log, dump,
    diff, verdict and picture, and never a database (no .kdbx, no .kdb). Machine paths in text are
    written <work>.
#>
param(
    [string] $Work = 'W:\',
    [Parameter(Mandatory)] [string] $To,
    [string] $LongWork,
    # Further machine paths and what each is written as, as path=name pairs.
    [string[]] $Replace = @()
)

$ErrorActionPreference = 'Stop'
New-Item -ItemType Directory -Force $To | Out-Null
$utf8 = New-Object System.Text.UTF8Encoding $false
# The drive the work directory is mapped as, W: unless the run chose another.
$drive = $Work.TrimEnd('\')
foreach ($sub in 'runs', 'logs', 'base') {
    $from = Join-Path $Work $sub
    if (-not (Test-Path $from)) { continue }
    Get-ChildItem $from -Recurse -File | Where-Object { $_.Extension -notin '.kdbx', '.kdb' -and $_.Name -notlike '*.cmd' -and $_.Name -ne 'pw.txt' } | ForEach-Object {
        $rel = $_.FullName.Substring($Work.Length)
        $dest = Join-Path $To $rel
        New-Item -ItemType Directory -Force (Split-Path -Parent $dest) | Out-Null
        if ($_.Extension -in '.png', '.url') {
            Copy-Item $_.FullName $dest -Force
        } else {
            $t = [IO.File]::ReadAllText($_.FullName)
            $t = $t.Replace("`0", '')
            if ($LongWork) { $t = $t.Replace($LongWork, '<work>') }
            foreach ($pair in $Replace) { $p = $pair.Split('='); $t = $t.Replace($p[0], $p[1]) }
            $t = $t.Replace("$drive\", '<work>\').Replace("$drive/", '<work>/')
            [IO.File]::WriteAllText($dest, $t, $utf8)
        }
    }
}
Get-ChildItem $To -Recurse -File | Measure-Object | ForEach-Object { "copied $($_.Count) files to $To" }
