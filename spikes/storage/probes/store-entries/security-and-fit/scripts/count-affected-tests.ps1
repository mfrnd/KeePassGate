# Written on this branch, not by the critic. The critique's section M8 quotes "49 test files ...
# about 26,000 lines and 734 test methods" from `affected-tests.txt`, which records the list of
# files and neither count nor the command that chose them. This counts the files, their lines and
# the lines holding an NUnit `[Test]` attribute over that list, at the product's commit, as
# `tools\Product.ps1` exports it into `.product\`. Lines are counted as newline characters, which is
# how `wc -l` counts them. Windows PowerShell 5.1.
param(
    [Parameter(Mandatory = $true)][string]$List,
    [Parameter(Mandatory = $true)][string]$OutFile
)
$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot '..\..\..\..\..\..\tools\Product.ps1')

$paths = @(Get-Content -LiteralPath $List | ForEach-Object { $_.Trim() } | Where-Object { $_ })
$files = @(Export-ProductFile -Path $paths)
$lines = 0
$tests = 0
foreach ($file in $files) {
    $text = [System.IO.File]::ReadAllText($file)
    $lines += ([regex]::Matches($text, "`n")).Count
    $tests += ([regex]::Matches($text, '(?m)^.*\[Test\].*$')).Count
}
$report = @(
    "product commit $(Get-ProductCommit)",
    "files listed $($paths.Count), exported $($files.Count)",
    "lines $lines",
    "lines holding [Test] $tests"
)
[System.IO.File]::WriteAllLines($OutFile, [string[]] $report)
