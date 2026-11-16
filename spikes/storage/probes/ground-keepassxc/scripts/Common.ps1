$ErrorActionPreference = 'Stop'
$script:Root = Split-Path -Parent $PSScriptRoot
$script:Probe = Join-Path $script:Root 'scripts\probe\out\Xcp.exe'
$script:Cli = & {
    . (Join-Path $PSScriptRoot '..\..\..\..\..\tools\KeePassXC.ps1')
    @{
        '2.4.2'  = Get-KeePassXCCli -Version 2.4.2
        '2.6.6'  = Get-KeePassXCCli -Version 2.6.6
        '2.7.12' = Get-KeePassXCCli -Version 2.7.12
        '2.8.0'  = Get-KeePassXCCli -Version 2.8.0
    }
}
$script:Releases = @('2.4.2', '2.6.6', '2.7.12', '2.8.0')
$script:Password = 'probe-only'

function Reset-Dir([string] $Path) {
    if (Test-Path -LiteralPath $Path) { Remove-Item -LiteralPath $Path -Recurse -Force }
    New-Item -ItemType Directory -Path $Path | Out-Null
}

# Runs keepassxc-cli with no window, the password on standard input, and logs what it printed.
function Invoke-Cli([string] $Version, [string[]] $Arguments, [string] $Log, [string] $WorkDir, [string] $Stdin = $script:Password) {
    $info = New-Object System.Diagnostics.ProcessStartInfo
    $info.FileName = $script:Cli[$Version]
    $info.Arguments = ($Arguments | ForEach-Object { '"' + $_ + '"' }) -join ' '
    $info.WorkingDirectory = $WorkDir
    $info.UseShellExecute = $false
    $info.CreateNoWindow = $true
    $info.RedirectStandardInput = $true
    $info.RedirectStandardOutput = $true
    $info.RedirectStandardError = $true
    $info.StandardInputEncoding = New-Object System.Text.UTF8Encoding $false
    $start = [DateTime]::UtcNow
    $p = [System.Diagnostics.Process]::Start($info)
    $o = $p.StandardOutput.ReadToEndAsync()
    $e = $p.StandardError.ReadToEndAsync()
    foreach ($line in ($Stdin -split "`n")) { $p.StandardInput.WriteLine($line) }
    $p.StandardInput.Close()
    $p.WaitForExit()
    $end = [DateTime]::UtcNow
    $text = $o.Result + $e.Result
    Add-Content -LiteralPath $Log -Value ("### {0} {1}`r`n### exit {2} {3:o}/{4:o}`r`n{5}" -f $Version, $info.Arguments, $p.ExitCode, $start, $end, $text) -Encoding utf8
    [pscustomobject]@{ Exit = $p.ExitCode; Text = $text; Start = $start; End = $end }
}

function Invoke-Dump([string[]] $Files) {
    $out = & $script:Probe dump @Files
    if ($LASTEXITCODE -ne 0) { throw "dump failed: $Files" }
    $out
}
