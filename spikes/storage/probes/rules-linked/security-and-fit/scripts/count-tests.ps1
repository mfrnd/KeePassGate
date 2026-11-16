<#
.SYNOPSIS
    Counts, at the product's commit, the test files that name the rule's custom data key or the
    dialog's copy, and the `KPG.` names in the vault: two searches the security critique quotes.

.DESCRIPTION
    Written on the branch, not by the critique's agent: the critique quotes "27 host-test files"
    with four counts per file "[M here, a search of the test tree]", and "zero matches in
    `KdbxVault.cs`" for a `KPG.` guard, and kept neither command nor output. This repeats both
    searches over the repository's own history at the commit `tools\Product.ps1` names, with
    `git grep`, which reads that commit and changes nothing. Every occurrence is counted, not
    every line.

    The tests are searched twice. First with a reconstruction of the critique's search:
    `CustomDataKey`, `NodeFormCustomDataField` (the name under which the tests reach the dialog's
    copy) and `m_sdCustomData` (that copy's own name). It is the one tried here that gives all four
    per-file counts the critique quotes, and it also matches `ProfileNames.CustomDataKey`, the
    profile list's key. Then with the rule's key alone, `AclDocument.CustomDataKey`, beside the
    other two.

    Windows PowerShell 5.1, from the branch root:

        powershell.exe -NoProfile -ExecutionPolicy Bypass -File spikes\storage\probes\rules-linked\security-and-fit\scripts\count-tests.ps1
#>
$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot '..\..\..\..\..\..\tools\Product.ps1')

$commit = Get-ProductCommit

function Search([string[]] $Arguments) {
    $result = Invoke-ProductGit -Arguments $Arguments
    # git grep answers 1 when nothing matched, which is an answer here, not a failure.
    if ($result.ExitCode -gt 1) { throw "git $($Arguments -join ' ') failed: $($result.Errors)" }
    $text = [Text.Encoding]::UTF8.GetString($result.Output)
    @($text -split "`n" | Where-Object { $_ -ne '' })
}

$lines = New-Object System.Collections.Generic.List[string]
$lines.Add("product commit $commit")

foreach ($pattern in 'CustomDataKey|NodeFormCustomDataField|m_sdCustomData',
    'AclDocument\.CustomDataKey|NodeFormCustomDataField|m_sdCustomData') {
    $lines.Add('')
    $lines.Add("tests: occurrences of $pattern, per file")
    # Each line of `git grep -o` is <commit>:<path>:<the match>.
    $counts = @{}
    foreach ($line in Search @('grep', '-o', '-E', $pattern, $commit, '--', 'tests')) {
        $path = $line.Substring($commit.Length + 1)
        $path = $path.Substring(0, $path.LastIndexOf(':'))
        if ($counts.ContainsKey($path)) { $counts[$path]++ } else { $counts[$path] = 1 }
    }
    $tiers = @{}
    foreach ($path in ($counts.Keys | Sort-Object)) {
        $tier = ($path -split '/')[1]
        if ($tiers.ContainsKey($tier)) { $tiers[$tier]++ } else { $tiers[$tier] = 1 }
        $lines.Add(("  {0} {1}" -f $counts[$path], $path))
    }
    foreach ($tier in ($tiers.Keys | Sort-Object)) { $lines.Add(("files in {0}: {1}" -f $tier, $tiers[$tier])) }
    $lines.Add(("files in every tier: {0}" -f $counts.Count))
}

$lines.Add('')
$vault = 'src/KeePassGate/Bridge/KdbxVault.cs'
foreach ($literal in 'KPG.', 'KPG') {
    $found = @(Search @('grep', '-o', '-F', $literal, $commit, '--', $vault))
    $lines.Add(("{0}: occurrences of '{1}': {2}" -f $vault, $literal, $found.Count))
}

$out = Join-Path $PSScriptRoot '..\out'
[void](New-Item -ItemType Directory -Force $out)
[IO.File]::WriteAllLines((Join-Path $out 'count-tests.txt'), $lines)
$lines
