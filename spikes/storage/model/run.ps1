<#
.SYNOPSIS
    Runs the executable models of option `crdt` from the branch, writing what each prints into
    `out\` beside it, under the name its recorded copy has in `recorded\`.

.DESCRIPTION
    Added on the branch: the papers give the one line that starts each model, and the models print
    to standard output, which their agents redirected into the files now under `recorded\`. This
    does the same, byte for byte, through a process of its own rather than PowerShell's pipeline,
    which would decode and re-encode the text.

      full       crdt_model.py full                     out\results-full.txt
      quick      crdt_model.py quick                    out\results-quick.txt
      scenarios  crdt_model.py scenarios                out\results-scenarios.txt
      critic     critic\crdt_formal.py all 1500         critic\out\results-critic.txt
                 critic\probe_liveness.py 1500          critic\out\results-liveness.txt
                 critic\probe_restore.py                critic\out\results-restore.txt
                 critic\probe_fixed.py                  critic\out\results-fixed.txt

    Python through `uv run --no-project`, with no package. Nothing shows a window.

.EXAMPLE
    powershell.exe -NoProfile -ExecutionPolicy Bypass -File spikes\storage\model\run.ps1 full
#>
param([Parameter(Mandatory)] [ValidateSet('full', 'quick', 'scenarios', 'critic')] [string] $Run)

$ErrorActionPreference = 'Stop'
$here = $PSScriptRoot

function Invoke-Model([string] $Script, [string[]] $Arguments, [string] $Output) {
    $directory = Split-Path -Parent $Output
    if (-not (Test-Path $directory)) { [void](New-Item -ItemType Directory -Path $directory) }

    $info = New-Object System.Diagnostics.ProcessStartInfo
    $info.FileName = 'uv'
    $info.Arguments = (@('run', '--no-project', 'python', '-I', '-X', 'utf8', $Script) + $Arguments |
        ForEach-Object { '"' + $_ + '"' }) -join ' '
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
    if ($process.ExitCode -ne 0) { throw "$Script exited with $($process.ExitCode)" }
    Write-Host ("== {0} {1}: {2:0} s" -f (Split-Path -Leaf $Script), ($Arguments -join ' '), $clock.Elapsed.TotalSeconds)
}

$model = Join-Path $here 'crdt_model.py'
$critic = Join-Path $here 'critic'
switch ($Run) {
    'full'      { Invoke-Model $model @('full') (Join-Path $here 'out\results-full.txt') }
    'quick'     { Invoke-Model $model @('quick') (Join-Path $here 'out\results-quick.txt') }
    'scenarios' { Invoke-Model $model @('scenarios') (Join-Path $here 'out\results-scenarios.txt') }
    'critic' {
        Invoke-Model (Join-Path $critic 'crdt_formal.py') @('all', '1500') (Join-Path $critic 'out\results-critic.txt')
        Invoke-Model (Join-Path $critic 'probe_liveness.py') @('1500') (Join-Path $critic 'out\results-liveness.txt')
        Invoke-Model (Join-Path $critic 'probe_restore.py') @() (Join-Path $critic 'out\results-restore.txt')
        Invoke-Model (Join-Path $critic 'probe_fixed.py') @() (Join-Path $critic 'out\results-fixed.txt')
    }
}
