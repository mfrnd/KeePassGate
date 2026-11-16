# Written on this branch, not by the critic. The critique's sections 5.1 and 5.2 quote line counts
# of 32 files of the product "[M here, wc -l]" and two counts of files that "mention `CustomData`"
# "[M here, grep]", and the critic recorded neither the counts nor the commands. This counts them
# again at the product's commit, through `tools\Product.ps1`: each file exported into `.product\`
# and its lines counted as newline characters, which is how `wc -l` counts them; and the files under
# `src/KeePassGate` and under `tests` whose text holds `CustomData`, as `git grep -l` lists them at
# that commit. It prints into the file it is given, which has no recorded twin. Windows
# PowerShell 5.1.
param([Parameter(Mandatory = $true)][string] $OutFile)
$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot '..\..\..\..\..\..\tools\Product.ps1')
$commit = Get-ProductCommit

# The names the critique gives, in its order, at their paths in the product.
$paths = @(
    'src/KeePassGate/Bridge/CustomDataStore.cs', 'src/KeePassGate/Authority/AuthorityKeys.cs',
    'src/KeePassGate/Authority/AuthorityStore.cs', 'src/KeePassGate/Authority/AdmissionStore.cs',
    'src/KeePassGate/Acl/ProfileNames.cs', 'src/KeePassGate/Authority/RenewalRefusal.cs',
    'src/KeePassGate/Ui/PendingChanges.cs', 'src/KeePassGate/Bridge/NodeDialog.cs',
    'src/KeePassGate/Bridge/NodeDialogs.cs', 'src/KeePassGate/Ui/GrantPanelFactory.cs',
    'src/KeePassGate/Bridge/KdbxNodes.cs', 'src/KeePassGate/Bridge/DatabaseNodes.cs',
    'src/KeePassGate/Acl/AclDocument.cs', 'src/KeePassGate/Acl/AclResolver.cs',
    'src/KeePassGate/Bridge/AuthorityChooser.cs', 'src/KeePassGate/Gate/UndecidedTarget.cs',
    'src/KeePassGate/Bridge/DatabaseWriter.cs', 'src/KeePassGate/Bridge/OpenDatabaseTargets.cs',
    'src/KeePassGate/Bridge/GroupDialogs.cs', 'src/KeePassGate/Bridge/KdbxVault.cs',
    'src/KeePassGate/Diagnostics/Expected.cs', 'src/KeePassGate/Ui/AuthorityPanel.cs',
    'src/KeePassGate/Ui/DatabaseProfiles.cs', 'src/KeePassGate/Ui/ClientReaches.cs',
    'src/KeePassGate/Pairing/ApprovalBook.cs', 'src/KeePassGate/Authority/AuthorityService.cs',
    'src/KeePassGate/Bridge/MainWindowDatabases.cs',
    'tests/KeePassGate.Host.Tests/CustomDataMergeRuleTests.cs',
    'tests/KeePassGate.Host.Tests/MergeInRealDatabaseTests.cs',
    'tests/KeePassGate.Host.Tests/AuthorityMergeTests.cs',
    'tests/KeePassGate.Host.Tests/SnapshotHazardTests.cs',
    'tests/KeePassGate.Tests/PendingChangesTests.cs'
)

$report = New-Object System.Collections.Generic.List[string]
$report.Add("product commit $commit")
$report.Add('')
$report.Add('## lines, counted as newline characters')
$files = @(Export-ProductFile -Path $paths)
for ($i = 0; $i -lt $paths.Count; $i++) {
    $text = [System.IO.File]::ReadAllText($files[$i])
    $name = [System.IO.Path]::GetFileNameWithoutExtension($paths[$i])
    $report.Add("$name $(([regex]::Matches($text, "`n")).Count)")
}

$report.Add('')
$report.Add('## files whose text holds CustomData')
foreach ($tree in 'src/KeePassGate', 'tests') {
    $listed = Invoke-ProductGit -Arguments @('grep', '-l', '-e', 'CustomData', $commit, '--', $tree)
    if ($listed.ExitCode -ne 0) { throw "git grep failed: $($listed.Errors)" }
    $names = @([System.Text.Encoding]::UTF8.GetString($listed.Output) -split "`n" | Where-Object { $_ })
    $report.Add("$tree $($names.Count)")
}
[System.IO.File]::WriteAllLines($OutFile, $report)
