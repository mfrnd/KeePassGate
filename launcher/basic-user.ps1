# Runs a PowerShell command in a directory under a basic user token, for a machine whose logon is
# elevated and whose tests must not run elevated. Writes every stream of the command to <Log> and
# its exit code to <Log>.exit, and waits for it.
#
#   basic-user.ps1 -Dir <directory> -Log <file> -Command <PowerShell command> [-Headless]
#
# -Headless runs it under a console with no window (`conhost --headless`), so that it takes no
# foreground: for a build, or for `private-desktop.ps1`, whose child shows its windows elsewhere.
#
# `runas /trustlevel:0x20000` is what lowers the token, and it names no desktop: the command runs on
# the desktop of the process that called this, which is why `private-desktop.ps1` creates the private
# desktop from inside the lowered process rather than before it.
param(
    [Parameter(Mandatory)] [string] $Dir,
    [Parameter(Mandatory)] [string] $Log,
    [Parameter(Mandatory)] [string] $Command,
    [switch] $Headless,
    [switch] $Inner)

if ($Inner) {
    $elevated = ([Security.Principal.WindowsPrincipal][Security.Principal.WindowsIdentity]::GetCurrent()).IsInRole('Administrators')
    "elevated: $elevated" | Set-Content $Log -Encoding UTF8
    "command: $Command" | Add-Content $Log -Encoding UTF8
    Set-Location $Dir
    & powershell.exe -NoProfile -ExecutionPolicy Bypass -Command ($Command + '; exit $LASTEXITCODE') *>> $Log
    $LASTEXITCODE | Set-Content "$Log.exit" -Encoding ASCII
    exit
}

Remove-Item "$Log.exit" -ErrorAction SilentlyContinue
New-Item -ItemType Directory -Force -Path (Split-Path -Parent $Log) | Out-Null
$encoded = [Convert]::ToBase64String([Text.Encoding]::Unicode.GetBytes(
    "& '$PSCommandPath' -Dir '$Dir' -Log '$Log' -Command '" + $Command.Replace("'", "''") + "' -Inner"))
$console = if ($Headless) { 'conhost.exe --headless ' } else { '' }
& runas.exe /trustlevel:0x20000 "${console}powershell.exe -NoProfile -ExecutionPolicy Bypass -EncodedCommand $encoded"
# Looked for every ten seconds, as in every run `RESULTS.md` reports: up to eleven seconds of each
# wall clock there is this wait rather than the command.
while (-not (Test-Path "$Log.exit")) { Start-Sleep -Seconds 10 }
Start-Sleep -Seconds 1
$code = [int](Get-Content "$Log.exit" -Raw).Trim()
"exit $code"
exit $code
