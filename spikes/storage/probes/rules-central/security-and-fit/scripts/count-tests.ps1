<#
.SYNOPSIS
    Counts the test attributes in the eleven test files the security critique names, at the
    product's commit.

.DESCRIPTION
    Written on the branch, not by the critique's agent: the critique quotes a count per file from
    "a search of the test tree" and kept neither the command nor its output. This takes the eleven
    files it names out of the product's history with `tools\Product.ps1` and counts three things in
    each: every `[Test` (which the critique's numbers equal, one `[TestFixture]` per file
    included), `[Test]`, and `[TestCase(`. Which files write rules as node custom data was the
    critique's judgment, and is taken from it rather than searched for again.

    Windows PowerShell 5.1, from the branch root:

        powershell.exe -NoProfile -ExecutionPolicy Bypass -File spikes\storage\probes\rules-central\security-and-fit\scripts\count-tests.ps1
#>
$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot '..\..\..\..\..\..\tools\Product.ps1')

$names = 'GrantEditorInRealDialogsTests', 'SkippedOnlyForAReaderTests', 'DecidedAtTheReadTests',
    'TagOfTheCallersViewTests', 'AclInRealDatabaseTests', 'MergeInRealDatabaseTests',
    'SnapshotHazardTests', 'RemovalMergeTests', 'DatabaseNodesTests', 'DecidedAtTheWriteTests',
    'LiveGrantEditorTests'
$paths = foreach ($name in $names) {
    if ($name -like 'Live*') { "tests/KeePassGate.Host.Tests/Live/$name.cs" }
    else { "tests/KeePassGate.Host.Tests/$name.cs" }
}
$files = Export-ProductFile -Path $paths

$lines = New-Object System.Collections.Generic.List[string]
$lines.Add("product commit $(Get-ProductCommit)")
$sum = @{ Any = 0; Test = 0; Case = 0 }
for ($i = 0; $i -lt $names.Count; $i++) {
    $text = [IO.File]::ReadAllText($files[$i])
    $any = [regex]::Matches($text, '\[Test').Count
    $test = [regex]::Matches($text, '\[Test\]').Count
    $case = [regex]::Matches($text, '\[TestCase\(').Count
    $sum.Any += $any; $sum.Test += $test; $sum.Case += $case
    $lines.Add(("{0}: [Test {1}, [Test] {2}, [TestCase( {3}" -f $names[$i], $any, $test, $case))
}
$lines.Add(("all eleven: [Test {0}, [Test] {1}, [TestCase( {2}" -f $sum.Any, $sum.Test, $sum.Case))

$out = Join-Path $PSScriptRoot '..\out'
[void](New-Item -ItemType Directory -Force $out)
[IO.File]::WriteAllLines((Join-Path $out 'count-tests.txt'), $lines)
