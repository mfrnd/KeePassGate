<#
.SYNOPSIS
    Checks one file the way `build.ps1 -Task Lint` checks all of them.

.DESCRIPTION
    A Claude Code hook. Claude Code writes a JSON object on standard input describing the tool call
    that just finished; this reads the path out of it, runs the checks that apply to that one file,
    and exits 2 with the findings on standard error when something is wrong. Exit 2 is the code that
    puts the message in front of the model rather than only in a log, so a finding is fixed where it
    was made instead of at the gate.

    Nothing here is a second set of rules. It runs the same `markdownlint-cli2` and `cspell` against
    the same `.markdownlint-cli2.yaml` and `cspell.yaml`, on the files those include, so a file that
    passes here passes the gate and a file the gate never reads is not read here either. The one
    difference is scope: one file, so that it is fast enough to run after every edit.

    Silent when the tools are not installed. A missing `node_modules` should stop nobody from
    editing a file, and the gate, which a merge into `master` waits for, is what refuses it there.
#>

$ErrorActionPreference = 'Stop'

$root = Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
$payload = [Console]::In.ReadToEnd()
if (-not $payload) { exit 0 }

try {
    $path = ($payload | ConvertFrom-Json).tool_input.file_path
} catch {
    exit 0
}

# Literally, since a path is not a pattern: read as a pattern, a name holding brackets matches
# nothing, or matches some other file, and the edit goes unchecked.
if (-not $path -or -not (Test-Path -LiteralPath $path)) { exit 0 }

# Only files this repository owns. An edit under `node_modules` or a build directory is not ours to
# have an opinion about. Both tests are anchored on the separator: without it a sibling checkout
# whose name merely begins with this one's counts as inside, and a file merely named after one of
# the skipped directories is skipped.
$full = (Resolve-Path -LiteralPath $path).Path
$inside = $root.TrimEnd([IO.Path]::DirectorySeparatorChar) + [IO.Path]::DirectorySeparatorChar
if (-not $full.StartsWith($inside, [System.StringComparison]::OrdinalIgnoreCase)) { exit 0 }
foreach ($skip in '\node_modules\', '\bin\', '\obj\', '\.tmp\', '\scratchpad\') {
    if ($full -like "*$skip*") { exit 0 }
}

if (-not (Test-Path (Join-Path $root 'node_modules'))) { exit 0 }

# From the repository's root, which is where `npx` finds the tools and where the configurations
# are read from: the hook runs in whatever directory the session stands in.
Set-Location -LiteralPath $root

$relative = $full.Substring($root.Length).TrimStart('\')
$findings = @()

if ($full -like '*.md') {
    # `--no-globs`, because markdownlint-cli2 otherwise adds the configuration's own globs to the
    # file named here, lints every markdown file in the tree, and reports what it finds in any of
    # them as findings in this one.
    $out = & npx --no-install markdownlint-cli2 --no-globs -- $full 2>&1
    if ($LASTEXITCODE -ne 0) {
        $findings += ($out | Where-Object { $_ -match ':\d+' })
    }
}

# Through `--file`, which cspell holds to `cspell.yaml`'s own list of files, where a name on the
# command line goes around it: `LICENSE` and `.editorconfig` are files the gate never spells, and
# a check of either reported words the gate has no reason to know. `--dot` as the gate has it, and
# a file the list leaves out is no finding.
$out = & npx --no-install cspell lint --no-progress --no-summary --dot --no-must-find-files --file $full 2>&1
if ($LASTEXITCODE -ne 0) {
    # A word `cspell.yaml` refuses is reported as forbidden rather than unknown, and kept here the
    # same way: filtered out, it would fail nothing and be seen only at the gate.
    $findings += ($out | Where-Object { $_ -match '(Unknown|Forbidden) word' })
}

if ($findings.Count -gt 0) {
    [Console]::Error.WriteLine("$relative does not pass the prose checks:")
    $findings | ForEach-Object { [Console]::Error.WriteLine("  $_") }
    [Console]::Error.WriteLine('Fix the wording, or add the word to `cspell.yaml` if it is one.')
    exit 2
}

exit 0
