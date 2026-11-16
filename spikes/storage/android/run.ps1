<#
.SYNOPSIS
    Runs this family's console part from the branch: the database builder and reader, the MD5 pair,
    and the checks that hold the papers to the record.

.DESCRIPTION
    Added on the branch. The emulator runs need Linux, KVM and a device, and are started by hand as
    RUN.md says; everything else runs here, under Windows PowerShell 5.1, with no window:

      1. builds the probe, `probe\Probe.csproj`, in Release, against the pinned KeePass;
      2. `StoreProbe make work\base` and `StoreProbe dumpdir work\base`: the six bases the phones
         were given, and their dumps;
      3. the MD5 pair of ground-android.md 5.1, into `md5pair\out\`;
      4. `checks\tables.py`, every table of measured-android.md against `recorded\`, into
         `out\tables.txt`, and its base claims against the fresh bases, into `out\tables-bases.txt`;
      5. for the two later measurements, the later state of the probe, `probe\overwrite\Probe.csproj`,
         built the same way; `makeow work\base`, the four overwrite bases beside the six;
         `makesync` and `newsync` into `sync\base`, SOLD and SNEW-fixed; their dumps; and
         `checks\overwrite.py` and `checks\k2a_synchronize.py` against `recorded\`, into
         `out\overwrite.txt` and `out\k2a-synchronize.txt`;
      6. `compare.py`, the fresh output against the record.

    Python through `uv run --no-project`, with no package. What a program prints is written byte
    for byte through a process of its own rather than PowerShell's pipeline.

.EXAMPLE
    powershell.exe -NoProfile -ExecutionPolicy Bypass -File spikes\storage\android\run.ps1
#>
$ErrorActionPreference = 'Stop'
$here = $PSScriptRoot
$root = [System.IO.Path]::GetFullPath((Join-Path $here '..\..\..'))

function Invoke-Captured([string] $FileName, [string[]] $Arguments, [string] $Output) {
    $directory = Split-Path -Parent $Output
    if (-not (Test-Path $directory)) { [void](New-Item -ItemType Directory -Path $directory) }
    $info = New-Object System.Diagnostics.ProcessStartInfo
    $info.FileName = $FileName
    $info.Arguments = ($Arguments | ForEach-Object { '"' + $_ + '"' }) -join ' '
    $info.UseShellExecute = $false
    $info.CreateNoWindow = $true
    $info.RedirectStandardOutput = $true
    $clock = [System.Diagnostics.Stopwatch]::StartNew()
    $process = [System.Diagnostics.Process]::Start($info)
    $file = [System.IO.File]::Create($Output)
    try {
        $process.StandardOutput.BaseStream.CopyTo($file)
        $process.WaitForExit()
    }
    finally {
        $file.Dispose()
    }
    Write-Host ("== {0}: exit {1}, {2:0.0} s" -f (Split-Path -Leaf $Output), $process.ExitCode, $clock.Elapsed.TotalSeconds)
    return $process.ExitCode
}

function Invoke-Python([string] $Script, [string[]] $Arguments, [string] $Output) {
    return Invoke-Captured 'uv' (@('run', '--no-project', 'python', '-I', '-X', 'utf8', $Script) + $Arguments) $Output
}

. (Join-Path $root 'tools\KeePass.ps1')
Write-Host ('== KeePass: ' + (Get-KeePassExe))

$clock = [System.Diagnostics.Stopwatch]::StartNew()
& dotnet build (Join-Path $here 'probe\Probe.csproj') -c Release -nologo -v quiet
if ($LASTEXITCODE -ne 0) { throw "the probe did not build" }
Write-Host ("== build: {0:0.0} s" -f $clock.Elapsed.TotalSeconds)

$probe = Join-Path $here 'probe\bin\Release\net48\StoreProbe.exe'
$base = Join-Path $here 'work\base'
$clock = [System.Diagnostics.Stopwatch]::StartNew()
& $probe make $base
if ($LASTEXITCODE -ne 0) { throw "StoreProbe make failed" }
& $probe dumpdir $base
if ($LASTEXITCODE -ne 0) { throw "StoreProbe dumpdir failed" }
Write-Host ("== bases: {0:0.0} s" -f $clock.Elapsed.TotalSeconds)

$pair = Join-Path $here 'md5pair\out'
[void](Invoke-Python (Join-Path $here 'md5pair\scripts\md5prefix_pair.py') @((Join-Path $pair 'md5pair')) (Join-Path $pair 'md5pair.log'))

$checks = Join-Path $here 'checks\tables.py'
$tables = Invoke-Python $checks @() (Join-Path $here 'out\tables.txt')
$tablesBases = Invoke-Python $checks @('--bases', $base) (Join-Path $here 'out\tables-bases.txt')

$clock = [System.Diagnostics.Stopwatch]::StartNew()
& dotnet build (Join-Path $here 'probe\overwrite\Probe.csproj') -c Release -nologo -v quiet
if ($LASTEXITCODE -ne 0) { throw "the later probe did not build" }
$later = Join-Path $here 'probe\overwrite\bin\Release\net48\StoreProbe.exe'
& $later makeow $base
if ($LASTEXITCODE -ne 0) { throw "StoreProbe makeow failed" }
$syncBase = Join-Path $here 'sync\base'
if (-not (Test-Path $syncBase)) { [void](New-Item -ItemType Directory -Path $syncBase) }
& $later makesync (Join-Path $syncBase 'SOLD.kdbx')
if ($LASTEXITCODE -ne 0) { throw "StoreProbe makesync failed" }
& $later newsync (Join-Path $syncBase 'SOLD.kdbx') (Join-Path $syncBase 'SNEW-fixed.kdbx')
if ($LASTEXITCODE -ne 0) { throw "StoreProbe newsync failed" }
& $later dumpdir $base
& $later dumpdir $syncBase
Write-Host ("== later bases: {0:0.0} s" -f $clock.Elapsed.TotalSeconds)

$overwrite = Invoke-Python (Join-Path $here 'checks\overwrite.py') @() (Join-Path $here 'out\overwrite.txt')
$synchronize = Invoke-Python (Join-Path $here 'checks\k2a_synchronize.py') @() (Join-Path $here 'out\k2a-synchronize.txt')

& uv run --no-project python -I -X utf8 (Join-Path $here 'compare.py')
$compare = $LASTEXITCODE
if ($tables -ne 0 -or $tablesBases -ne 0 -or $overwrite -ne 0 -or $synchronize -ne 0 -or $compare -ne 0) {
    throw "a check differs: tables $tables, tables against the fresh bases $tablesBases, overwrite $overwrite, " +
        "synchronize $synchronize, compare $compare"
}
