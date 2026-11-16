<#
.SYNOPSIS
    Exports named files of KeePassGate, at the commit the evaluation on this branch was made
    against, for the few probes that compile part of the product.

.DESCRIPTION
    A probe that asks what the plugin's own code answers, the access control resolver for one,
    compiles that code rather than a copy of it. The copy would be a second version of the product
    on a branch that is never merged, and nobody could tell from the branch whether it still holds
    what the commit held. So this branch carries none of the product's code: it names the commit,
    and this takes each file out of the repository's own history with
    `git show <commit>:<path>`, into `.product\`, which is gitignored.

    **The commit is written here and nowhere else.** A probe's project reads the files from
    `$(ProductDir)`, which `spikes\storage\Directory.Build.props` points at `.product\`, and a
    PowerShell probe asks `Export-ProductFile` for their full paths. `.product\COMMIT` records which
    commit the files there came from, and a directory that came from another commit is refused
    rather than mixed with this one.

    **The bytes are the blob's, unchanged.** Each file is written exactly as the repository stores
    it, so with the line endings the repository normalizes to rather than the ones a checkout of
    the product would have, and is then hashed as a blob and compared with the commit's own blob
    identifier. A file whose identifier differs is removed again and refused.

    **The commit has to be in this repository's history.** It is on `master`, so a clone has it; a
    clone of this branch alone does not, and is told the command that fetches it rather than having
    something fetched without being asked.

.EXAMPLE
    . .\tools\Product.ps1
    Export-ProductFile -Path src/KeePassGate/Acl/AclResolver.cs
#>

Set-StrictMode -Version Latest

$script:ProductBranchRoot = Split-Path -Parent $PSScriptRoot

# KeePassGate's `master` at the time the evaluation was made, which every report on this branch
# cites by its first eight digits.
$script:ProductCommit = 'a42e38aa019d572c5a10b489e6aadeea91ef91c1'

function Get-ProductCommit {
    <#
    .SYNOPSIS
        The full hash of the commit the product's files are exported from.
    #>
    [CmdletBinding()]
    param()

    $script:ProductCommit
}

function Get-ProductDirectory {
    <#
    .SYNOPSIS
        Where the exported files are, each at its path in the product.
    #>
    [CmdletBinding()]
    param()

    Join-Path $script:ProductBranchRoot '.product'
}

function Invoke-ProductGit {
    <#
    .SYNOPSIS
        Runs git over this branch's repository and returns its standard output as bytes, unchanged.

    .DESCRIPTION
        Through a process of its own rather than PowerShell's pipeline, which decodes what a
        native program writes as text in the console's code page and splits it into lines: a blob
        would arrive with its encoding and its line endings changed.
    #>
    [CmdletBinding()]
    param([Parameter(Mandatory)] [string[]] $Arguments)

    $info = New-Object System.Diagnostics.ProcessStartInfo
    $info.FileName = 'git'
    # Quoted the way the C runtime reads a command line back: a quotation mark is escaped, and the
    # backslashes before it, or before the closing mark, are doubled.
    $info.Arguments = (@('-C', $script:ProductBranchRoot) + $Arguments | ForEach-Object {
            $escaped = [regex]::Replace($_, '(\\*)"', '$1$1\"')
            '"' + [regex]::Replace($escaped, '(\\+)$', '$1$1') + '"'
        }) -join ' '
    $info.UseShellExecute = $false
    $info.CreateNoWindow = $true
    $info.RedirectStandardOutput = $true
    $info.RedirectStandardError = $true

    $process = [System.Diagnostics.Process]::Start($info)
    try {
        $errors = $process.StandardError.ReadToEndAsync()
        $buffer = New-Object System.IO.MemoryStream
        $process.StandardOutput.BaseStream.CopyTo($buffer)
        $process.WaitForExit()

        [pscustomobject]@{
            ExitCode = $process.ExitCode
            Output   = $buffer.ToArray()
            Errors   = $errors.Result.Trim()
        }
    }
    finally {
        $process.Dispose()
    }
}

function Export-ProductFile {
    <#
    .SYNOPSIS
        Writes each named file of the product, at the commit this tool names, under `.product\`, and
        returns the full path of each.

    .PARAMETER Path
        The file's path in the product, as git names it: relative to the product's root, with
        forward slashes, for example `src/KeePassGate/Acl/AclResolver.cs`.

    .PARAMETER Force
        Clears a `.product\` that holds files from another commit, rather than refusing it.
    #>
    [CmdletBinding()]
    param([Parameter(Mandatory)] [string[]] $Path, [switch] $Force)

    $commit = $script:ProductCommit
    $directory = Get-ProductDirectory

    $present = Invoke-ProductGit -Arguments @('cat-file', '-e', "$commit^{commit}")
    if ($present.ExitCode -ne 0) {
        throw "The product's commit $commit is not in this repository's history, so nothing can be exported from it. It is on master: git fetch origin master"
    }

    $stamp = Join-Path $directory 'COMMIT'
    if (Test-Path -LiteralPath $stamp) {
        $previous = (Get-Content -LiteralPath $stamp -Raw).Trim()
        if ($previous -ne $commit) {
            if (-not $Force) {
                throw "$directory holds files exported from $previous, not from $commit. Run again with -Force to clear it."
            }

            Remove-Item -LiteralPath $directory -Recurse -Force
        }
    }

    New-Item -ItemType Directory -Path $directory -Force | Out-Null
    [System.IO.File]::WriteAllText($stamp, "$commit`n")

    foreach ($name in $Path) {
        # A path git would read as something other than one file of the product is refused before
        # it reaches git: an absolute path, a backslash, and a step out of the tree.
        if ($name -match '^[/\\]|^[A-Za-z]:|\\' -or ($name -split '/') -contains '..' -or ($name -split '/') -contains '.') {
            throw "'$name' is not a path in the product as git names it: relative, with forward slashes."
        }

        $expected = Invoke-ProductGit -Arguments @('rev-parse', '--verify', '--quiet', "${commit}:$name")
        if ($expected -eq $null -or $expected.ExitCode -ne 0) {
            throw "The product at $commit has no file $name."
        }

        $blob = [System.Text.Encoding]::ASCII.GetString($expected.Output).Trim()
        $shown = Invoke-ProductGit -Arguments @('show', "${commit}:$name")
        if ($shown.ExitCode -ne 0) {
            throw "git show ${commit}:$name failed: $($shown.Errors)"
        }

        $target = Join-Path $directory ($name -replace '/', '\')
        New-Item -ItemType Directory -Path (Split-Path -Parent $target) -Force | Out-Null
        [System.IO.File]::WriteAllBytes($target, $shown.Output)

        # The check that does not depend on how the bytes traveled: the file as it now lies on the
        # disk, hashed as git hashes a blob with no filter in between, has to give the identifier
        # the commit holds. Not over standard input, where the writer .NET puts in front of a
        # process's input adds a byte order mark of its own when it is closed.
        $hashed = Invoke-ProductGit -Arguments @('hash-object', '--no-filters', '--', $target)
        $actual = [System.Text.Encoding]::ASCII.GetString($hashed.Output).Trim()
        if ($actual -ne $blob) {
            Remove-Item -LiteralPath $target -Force
            throw "$name hashed to the blob $actual, and the product at $commit holds $blob. Nothing was kept."
        }

        Write-Host "  $name at $($commit.Substring(0, 8)), blob $blob, $($shown.Output.Length) bytes" -ForegroundColor DarkGray
        $target
    }
}
