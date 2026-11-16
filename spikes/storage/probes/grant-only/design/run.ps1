<#
.SYNOPSIS
    Builds the design's probe and runs it from the branch, as `RUN.md` describes.

.DESCRIPTION
    Added on the branch: the agent ran these steps by hand from Bash and kept no script. What it
    runs is unchanged, in the agent's order. `Go.exe` is called four ways, as the probe's `Main`
    takes them: `model` (400 generated trees, seed 20261115), `keepass` (the 150 KeePassLib cells,
    the copies, the cost of the fold), `xc` once for each KeePassXC release (a plain save and the
    34 `keepassxc-cli` cells of 17 scenarios), and `state`, which reads databases and prints what
    `agent` may do in each.

    Two steps the agent typed as a Bash loop and a Bash line follow, rebuilt here from the session
    transcript that holds them:

      touched   `xc-touched.txt`: for KeePassXC 2.4.2 and 2.8.0 and scenarios 16 and 06, the copy
                B is saved once by `keepassxc-cli add`, then merged into A and A into it, and the
                three files are read with `Go.exe state`.
      versions  `ver.txt`: `Go.exe state` over seven of the KeePassLib results, and
                `kdbx-versions.txt`, how many files of each KDBX version each scenario's directory
                holds, which the agent printed with `od` and did not save.

    Each writes the file of the same name that `recorded\` holds, into `out\`; `ver-all.txt`, the
    same read over every KeePassLib result, is added on the branch and has no recorded copy. The
    databases go to `work\`, which is emptied first.

    Windows PowerShell 5.1, from the branch root:

        powershell.exe -NoProfile -ExecutionPolicy Bypass -File spikes\storage\probes\grant-only\design\run.ps1

.PARAMETER Only
    Runs the named steps alone, over the databases a whole run left in `work\`, which is then not
    emptied: `touched` and `versions` read what `xc` and `keepass` wrote. Used to run `touched`
    again, whose one cell depends on timing (`RUN.md` section 1).
#>
param([ValidateSet('model', 'keepass', 'xc', 'touched', 'versions')] [string[]] $Only)
$ErrorActionPreference = 'Stop'

$root = (Resolve-Path (Join-Path $PSScriptRoot '..\..\..\..\..')).Path
. (Join-Path $root 'tools\KeePass.ps1')
. (Join-Path $root 'tools\KeePassXC.ps1')
. (Join-Path $root 'tools\Product.ps1')

$here = $PSScriptRoot
$out = Join-Path $here 'out'
$work = Join-Path $here 'work'
$bin = Join-Path $here 'bin'
$steps = if ($Only) { $Only } else { 'model', 'keepass', 'xc', 'touched', 'versions' }
if (-not $Only -and (Test-Path $work)) { Remove-Item $work -Recurse -Force }
[void](New-Item -ItemType Directory -Force $out, $work)

# The eight files of the product the agent copied, from the product's commit.
$null = Export-ProductFile -Path @(
    'src/KeePassGate/Acl/AclDocument.cs', 'src/KeePassGate/Acl/AclDocuments.cs',
    'src/KeePassGate/Acl/AclGrant.cs', 'src/KeePassGate/Acl/AclInheritance.cs',
    'src/KeePassGate/Acl/AclResolver.cs', 'src/KeePassGate/Acl/AclVerb.cs',
    'src/KeePassGate/Acl/VerbMask.cs', 'src/KeePassGate/Transport/JsonBody.cs')

# Provides and checks the pinned KeePass the project references.
$null = Get-KeePassExe
& dotnet build (Join-Path $here 'probe\Go.csproj') -c Release -o $bin -v q -nologo
if ($LASTEXITCODE -ne 0) { throw "The build failed." }
$go = Join-Path $bin 'Go.exe'

function Step([string] $Name, [scriptblock] $Run) {
    $clock = [Diagnostics.Stopwatch]::StartNew()
    & $Run
    if ($LASTEXITCODE -ne 0) { throw "$Name exited $LASTEXITCODE." }
    Write-Host ([string]::Format([Globalization.CultureInfo]::InvariantCulture, '{0}: {1:0.0} s', $Name, $clock.Elapsed.TotalSeconds))
}

# `printf 'probe-only\nprobe-only\n' | keepassxc-cli ...`, as the agent's loop ran it: those bytes
# on standard input, the output discarded, the exit code kept.
function Invoke-Cli([string] $Exe, [string[]] $Arguments) {
    $info = New-Object System.Diagnostics.ProcessStartInfo
    $info.FileName = $Exe
    $info.Arguments = ($Arguments | ForEach-Object { '"' + $_ + '"' }) -join ' '
    $info.UseShellExecute = $false
    $info.CreateNoWindow = $true
    $info.RedirectStandardInput = $true
    $info.RedirectStandardOutput = $true
    $info.RedirectStandardError = $true
    $process = [System.Diagnostics.Process]::Start($info)
    try {
        $o = $process.StandardOutput.ReadToEndAsync()
        $e = $process.StandardError.ReadToEndAsync()
        $bytes = (New-Object System.Text.UTF8Encoding($false)).GetBytes("probe-only`nprobe-only`n")
        $process.StandardInput.BaseStream.Write($bytes, 0, $bytes.Length)
        $process.StandardInput.Close()
        $process.WaitForExit()
        [void]$o.Result; [void]$e.Result
        $process.ExitCode
    }
    finally { $process.Dispose() }
}

if ($steps -contains 'model') { Step 'model' { & $go $work model (Join-Path $out 'model.txt') 400 30 20261115 } }
if ($steps -contains 'keepass') { Step 'keepass' { & $go (Join-Path $work 'kp') keepass (Join-Path $out 'keepass.txt') } }
if ($steps -contains 'xc') {
    foreach ($version in '2.8.0', '2.4.2', '2.6.6', '2.7.12') {
        $cli = Get-KeePassXCCli -Version $version
        Step "xc $version" { & $go (Join-Path $work 'xc') xc (Join-Path $out "xc-$version.txt") $version $cli }
    }
}

# The agent's loop, step for step.
$touched = Join-Path $out 'xc-touched.txt'
$state = Join-Path $work 'touched\state.txt'
$utf8 = New-Object System.Text.UTF8Encoding($false)
if ($steps -contains 'touched') {
[IO.File]::WriteAllText($touched, '')
$clock = [Diagnostics.Stopwatch]::StartNew()
foreach ($version in '2.4.2', '2.8.0') {
    $cli = Get-KeePassXCCli -Version $version
    foreach ($scenario in '16', '06') {
        $d = Join-Path $work "touched\$version-$scenario"
        [void](New-Item -ItemType Directory -Force $d)
        Copy-Item (Join-Path $work "xc\xc$version-$scenario\A.kdbx") (Join-Path $d 'A.kdbx')
        Copy-Item (Join-Path $work "xc\xc$version-$scenario\B.kdbx") (Join-Path $d 'B.kdbx')
        # The stale copy B is saved once by KeePassXC itself before the merge.
        Write-Host "add exit $(Invoke-Cli $cli @('add', (Join-Path $d 'B.kdbx'), 'H/xc-touch'))"
        Copy-Item (Join-Path $d 'A.kdbx') (Join-Path $d 'intoA.kdbx')
        Copy-Item (Join-Path $d 'B.kdbx') (Join-Path $d 'intoB.kdbx')
        Write-Host "merge into A exit $(Invoke-Cli $cli @('merge', '-s', (Join-Path $d 'intoA.kdbx'), (Join-Path $d 'B.kdbx')))"
        Write-Host "merge into B exit $(Invoke-Cli $cli @('merge', '-s', (Join-Path $d 'intoB.kdbx'), (Join-Path $d 'A.kdbx')))"
        & $go $work state $state (Join-Path $d 'B.kdbx') (Join-Path $d 'intoA.kdbx') (Join-Path $d 'intoB.kdbx')
        if ($LASTEXITCODE -ne 0) { throw "state exited $LASTEXITCODE." }
        # `echo "== ..." >> out/xc-touched.txt; tail -n +2 work/touched/state.txt >> out/xc-touched.txt`:
        # a line ending in LF, then the state file's bytes after its first line, which keep the
        # CRLF the probe writes and the bare LF inside a line that holds an ENTRIES ALONE line.
        $header = $utf8.GetBytes("== KeePassXC $version, scenario $scenario, B saved once by keepassxc-cli before the merge`n")
        $bytes = [IO.File]::ReadAllBytes($state)
        $first = [Array]::IndexOf($bytes, [byte]10)
        $stream = New-Object System.IO.FileStream($touched, [IO.FileMode]::Append)
        try {
            $stream.Write($header, 0, $header.Length)
            $stream.Write($bytes, $first + 1, $bytes.Length - $first - 1)
        }
        finally { $stream.Dispose() }
    }
}
Write-Host ([string]::Format([Globalization.CultureInfo]::InvariantCulture, 'touched: {0:0.0} s', $clock.Elapsed.TotalSeconds))
}

# The agent's version line: `Go.exe state` over seven results into `ver.txt`, then for each
# scenario's directory `od -An -tu2 -j8 -N4` of every database, sorted and counted.
$kp = Join-Path $work 'kp'
if ($steps -contains 'versions') {
Step 'versions' {
    & $go $work state (Join-Path $out 'ver.txt') (Join-Path $kp 'kp01\A.kdbx') (Join-Path $kp 'kp01\Synchronize-intoA.kdbx') `
        (Join-Path $kp 'kp02\A.kdbx') (Join-Path $kp 'kp02\Synchronize-intoB.kdbx') (Join-Path $kp 'kp09\A.kdbx') `
        (Join-Path $kp 'kp09\Synchronize-intoB.kdbx') (Join-Path $kp 'kp07\Synchronize-intoA.kdbx')
}
# Added on the branch, with nothing recorded beside it: the same `state` over every database the
# KeePassLib cells left, so that "4.0 without a key, 4.1 with one" is read off every file rather
# than seven. `RUN.md` has what it showed.
$all = @(Get-ChildItem $kp -Recurse -Filter *.kdbx | Sort-Object FullName | ForEach-Object { $_.FullName })
Step 'versions, every file' { & $go $work state (Join-Path $out 'ver-all.txt') @all }
$tally = New-Object System.Collections.Generic.List[string]
foreach ($n in 1..15) {
    $name = 'kp{0:00}' -f $n
    $seen = @{}
    foreach ($file in Get-ChildItem (Join-Path $kp $name) -Filter *.kdbx) {
        $b = [IO.File]::ReadAllBytes($file.FullName)
        $key = '{0} {1}' -f [BitConverter]::ToUInt16($b, 8), [BitConverter]::ToUInt16($b, 10)
        $seen[$key] = 1 + [int]$seen[$key]
    }
    # `uniq -c` writes the count right-aligned in seven places.
    $parts = $seen.Keys | Sort-Object | ForEach-Object { '{0,7} {1};' -f $seen[$_], $_ }
    $tally.Add("${name}: " + ($parts -join ''))
}
[IO.File]::WriteAllText((Join-Path $out 'kdbx-versions.txt'), (($tally -join "`n") + "`n"))
}
