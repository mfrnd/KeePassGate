<#
.SYNOPSIS
    Counts again, at the product's commit, the lines and occurrences the security critique of option
    `crdt` quotes as measured, and writes them to `out\counts.txt` in the shape of the recorded
    `recorded\counts.txt`.

.DESCRIPTION
    Written on the branch, not by the critique's agent: the agent kept its output, `counts.txt`,
    whose first line names how the lines were counted, `[System.IO.File]::ReadAllLines(...).Count`,
    and kept no command. This repeats each count over the repository's own history at the commit
    `tools\Product.ps1` names:

    - the lines of the thirteen files the critique's lens (f) names, each exported by
      `Export-ProductFile` into `.product\` and counted as the agent counted them;
    - the files under `src/KeePassGate` that mention `CustomData`, with `git grep -l`;
    - the lines matching `RecycleBin|InRecycleBin|IsInBin` in each file, with `git grep -c`, which
      counts as `rg -c` does (lines, not occurrences), and the occurrences beside them;
    - the four lines of the design's model that the last line of the record cites.

    Windows PowerShell 5.1, from the branch root:

        powershell.exe -NoProfile -ExecutionPolicy Bypass -File spikes\storage\probes\crdt\security-and-fit\scripts\counts.ps1
#>
$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot '..\..\..\..\..\..\tools\Product.ps1')

$commit = Get-ProductCommit

function Invoke-Git([string[]] $Arguments) {
    $result = Invoke-ProductGit -Arguments $Arguments
    # git grep answers 1 when nothing matched, which is an answer here, not a failure.
    if ($result.ExitCode -gt 1) { throw "git $($Arguments -join ' ') failed: $($result.Errors)" }
    $text = [Text.Encoding]::UTF8.GetString($result.Output)
    @($text -split "`n" | Where-Object { $_ -ne '' })
}

$files = 'Bridge\CustomDataStore.cs', 'Authority\AuthorityKeys.cs', 'Authority\AuthorityStore.cs',
    'Authority\AdmissionStore.cs', 'Authority\RenewalRefusal.cs', 'Acl\ProfileNames.cs',
    'Ui\PendingChanges.cs', 'Bridge\NodeDialog.cs', 'Acl\AclResolver.cs', 'Bridge\DatabaseWriter.cs',
    'Bridge\OpenDatabaseTargets.cs', 'Bridge\GroupDialogs.cs', 'Bridge\KdbxVault.cs'

$lines = New-Object System.Collections.Generic.List[string]
$lines.Add("Measured $((Get-Date).ToString('yyyy-MM-dd')) in the product at $($commit.Substring(0, 8)), by PowerShell, [System.IO.File]::ReadAllLines(...).Count")
$lines.Add('')
$lines.Add('lines  file (under src\KeePassGate)')
$exported = Export-ProductFile -Path ($files | ForEach-Object { 'src/KeePassGate/' + ($_ -replace '\\', '/') })
for ($k = 0; $k -lt $files.Count; $k++) {
    $lines.Add(('{0,6}  {1}' -f [System.IO.File]::ReadAllLines($exported[$k]).Count, $files[$k]))
}

$lines.Add('')
$mentioning = Invoke-Git @('grep', '-l', '-F', 'CustomData', $commit, '--', 'src/KeePassGate')
$sources = @($mentioning | Where-Object { $_ -like '*.cs' })
$lines.Add(('Source files under src\KeePassGate mentioning "CustomData": {0}' -f $mentioning.Count))
$lines.Add(('  of them .cs files: {0}' -f $sources.Count))

$lines.Add('')
$pattern = 'RecycleBin|InRecycleBin|IsInBin'
$prefix = 'src/KeePassGate/'
$matching = @()
$total = 0
foreach ($line in (Invoke-Git @('grep', '-c', '-E', $pattern, $commit, '--', 'src/KeePassGate') | Sort-Object)) {
    # Each line of `git grep -c` is <commit>:<path>:<count of lines that match>.
    $rest = $line.Substring($commit.Length + 1)
    $cut = $rest.LastIndexOf(':')
    $path = $rest.Substring(0, $cut).Substring($prefix.Length) -replace '/', '\'
    $count = [int]$rest.Substring($cut + 1)
    $matching += ('{0} {1}' -f $path, $count)
    $total += $count
}
$lines.Add(('Lines matching {0}, per file (git grep -c, as rg -c counts):' -f $pattern))
$lines.Add(('  {0} ({1} total)' -f ($matching -join ', '), $total))
$occurrences = @(Invoke-Git @('grep', '-o', '-E', $pattern, $commit, '--', 'src/KeePassGate'))
$lines.Add(('  occurrences of the pattern, not lines: {0}' -f $occurrences.Count))

$lines.Add('')
$model = [System.IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..\..\..\..\model\crdt_model.py'))
$text = [System.IO.File]::ReadAllLines($model)
$lines.Add('Model: crdt_model.py lines 206 to 209:')
for ($k = 206; $k -le 209; $k++) { $lines.Add(('  {0}: {1}' -f $k, $text[$k - 1])) }

$out = Join-Path $PSScriptRoot '..\out'
[void](New-Item -ItemType Directory -Force $out)
[IO.File]::WriteAllLines([System.IO.Path]::GetFullPath((Join-Path $out 'counts.txt')), $lines)
$lines
