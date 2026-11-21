# Sums test durations per class in two trx files and prints the classes whose sum grew most, then
# the totals for the classes carrying `[Desktop]` against the rest. The classes carrying it are read
# from the unit tests' sources.
#
#   compare-classes.ps1 -Alone <trx> -Beside <trx> -Tests <worktree>\tests\KeePassGate.Tests [-Top 15]
param([Parameter(Mandatory)] [string] $Alone, [Parameter(Mandatory)] [string] $Beside, [Parameter(Mandatory)] [string] $Tests, [int] $Top = 15)
$desktopClasses = @(Get-ChildItem $Tests -Recurse -Filter '*.cs' | Select-String -Pattern '\[Desktop\]|Desktop\]|, Desktop' |
    ForEach-Object { [IO.Path]::GetFileNameWithoutExtension($_.Path) } | Sort-Object -Unique)
function Sums([string] $trx) {
    $run = ([xml] (Get-Content -LiteralPath $trx -Raw)).DocumentElement
    $classOf = @{}
    foreach ($d in $run.SelectNodes("*[local-name()='TestDefinitions']/*[local-name()='UnitTest']")) {
        $classOf[$d.GetAttribute('id')] = $d.SelectSingleNode("*[local-name()='TestMethod']").GetAttribute('className')
    }
    $sums = @{}
    foreach ($r in $run.SelectNodes("*[local-name()='Results']/*[local-name()='UnitTestResult']")) {
        $c = $classOf[$r.GetAttribute('testId')]
        $c = $c.Substring($c.LastIndexOf('.') + 1)
        $d = $r.GetAttribute('duration')
        if ($d) { $sums[$c] = [double] $sums[$c] + ([TimeSpan] $d).TotalSeconds }
    }
    $sums
}
$a = Sums $Alone; $b = Sums $Beside
$rows = foreach ($k in $b.Keys) { [pscustomobject] @{ Class = $k; Alone = [math]::Round([double] $a[$k], 1); Beside = [math]::Round($b[$k], 1); Grew = [math]::Round($b[$k] - [double] $a[$k], 1); Desktop = $desktopClasses -contains $k } }
$rows | Sort-Object Grew -Descending | Select-Object -First $Top | Format-Table -AutoSize | Out-String -Width 200
foreach ($flag in $true, $false) {
    $set = @($rows | Where-Object { $_.Desktop -eq $flag })
    "Desktop=$flag classes $($set.Count): alone $([math]::Round(($set | Measure-Object Alone -Sum).Sum, 1)) s, beside $([math]::Round(($set | Measure-Object Beside -Sum).Sum, 1)) s"
}
