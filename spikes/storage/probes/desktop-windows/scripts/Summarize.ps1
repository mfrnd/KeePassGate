<#
.SYNOPSIS
    One line per run of a measurement: the windows KeePass or KeePassXC showed (title and first
    text), the buttons pressed, and the verdict line of the file, read from each run's run.log.
#>
param([string] $Work = 'W:\', [string] $Measurement = 'm1', [string] $Out)

$lines = foreach ($d in (Get-ChildItem (Join-Path $Work "runs\$Measurement") -Directory | Sort-Object Name)) {
    $log = Join-Path $d.FullName 'run.log'
    if (-not (Test-Path $log)) { continue }
    $t = Get-Content $log -Encoding UTF8
    $shown = New-Object System.Collections.Generic.List[string]
    $cur = $null
    foreach ($l in $t) {
        if ($l -match '\] WINDOW title=<<(.*?)>>') { $cur = $Matches[1]; continue }
        if ($cur -and $l -match '\]   Text name=<<(.*?)(>>|$)') { $shown.Add("[$cur] $($Matches[1])"); $cur = $null; continue }
        if ($l -match '\] pressed <<(.*?)>>') { $shown.Add("pressed $($Matches[1])") }
    }
    $verdict = ($t | Where-Object { $_ -match 'verdict: db\.kdbx' } | Select-Object -First 1) -replace '^.*verdict: ', ''
    $failed = ($t | Where-Object { $_ -match 'RUN FAILED' }) -join ' '
    "$($d.Name) | $($shown -join ' / ') | $(if ($failed) { $failed } else { $verdict })"
}
if ($Out) { [IO.File]::WriteAllLines($Out, [string[]]$lines) } else { $lines }
