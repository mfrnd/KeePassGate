# Runs a PowerShell command under a basic user token on a desktop of its own, which nobody sees and
# which takes no foreground from the visible desktop, and waits for it.
#
#   private-desktop.ps1 -Dir <directory> -Log <file> -Command <PowerShell command> [-Name <desktop>]
#                       [-SampleSeconds 3]
#
# The command's own output, every stream, goes to <Log> in UTF-8, and its exit code to <Log>.exit,
# which this script also exits with. What the launcher did goes to <Log>.launcher: the desktop's
# name, the child's process identifier, and every few seconds which processes own windows on the
# private desktop and which processes of the child's tree own visible windows on the visible desktop.
#
# Called from an elevated process, it hands itself to `basic-user.ps1 -Headless`, so that the
# private desktop is created by the lowered process and carries that token's default security; the
# child is that token's too. Windows PowerShell 5.1, since `PrivateDesktop.cs` is compiled by its
# `Add-Type`.
param(
    [Parameter(Mandatory)] [string] $Dir,
    [Parameter(Mandatory)] [string] $Log,
    [string] $Command,
    [string] $Name,
    [string] $CommandBase64,
    [int] $SampleSeconds = 3,
    [switch] $Inner)

$here = Split-Path -Parent $PSCommandPath

if ($Inner) {
    $launcherLog = "$Log.launcher"
    function Note([string] $text) { ((Get-Date -Format 'HH:mm:ss.fff') + ' ' + $text) | Add-Content $launcherLog -Encoding UTF8 }
    $elevated = ([Security.Principal.WindowsPrincipal][Security.Principal.WindowsIdentity]::GetCurrent()).IsInRole('Administrators')
    "launcher elevated: $elevated" | Set-Content $launcherLog -Encoding UTF8
    try {
        Add-Type -Path (Join-Path $here 'PrivateDesktop.cs')
        $userCommand = [Text.Encoding]::Unicode.GetString([Convert]::FromBase64String($CommandBase64))
        Note "command: $userCommand"
        # The child writes its own output: Windows PowerShell's redirection is UTF-16 unless told.
        $child = "`$PSDefaultParameterValues['Out-File:Encoding'] = 'utf8'; " +
            "Set-Location -LiteralPath '$($Dir.Replace("'", "''"))'; " +
            "& { $userCommand } *>> '$($Log.Replace("'", "''"))'; exit `$LASTEXITCODE"
        $encoded = [Convert]::ToBase64String([Text.Encoding]::Unicode.GetBytes($child))
        $line = "`"$PSHOME\powershell.exe`" -NoProfile -ExecutionPolicy Bypass -EncodedCommand $encoded"
        $started = Get-Date
        $code = [PrivateDesktop]::Run($Name, $line, $Dir, [Action[string]] { param($t) Note $t }, $SampleSeconds * 1000)
        Note ("duration " + ((Get-Date) - $started).ToString())
    }
    catch {
        Note ("launcher failed: " + $_)
        $code = 255
    }
    $code | Set-Content "$Log.exit" -Encoding ASCII
    exit $code
}

if (-not $Name) { $Name = 'private-' + [Guid]::NewGuid().ToString('n').Substring(0, 8) }
New-Item -ItemType Directory -Force -Path (Split-Path -Parent $Log) | Out-Null
Remove-Item "$Log.exit", "$Log.launcher" -ErrorAction SilentlyContinue
if (Test-Path $Log) { Remove-Item $Log }
$b64 = [Convert]::ToBase64String([Text.Encoding]::Unicode.GetBytes($Command))
# Not `$inner`: PowerShell names ignore case, so that would be the `-Inner` switch.
$handOver = "& '$PSCommandPath' -Dir '$Dir' -Log '$Log' -Name '$Name' -CommandBase64 '$b64' -SampleSeconds $SampleSeconds -Inner"
& (Join-Path $here 'basic-user.ps1') -Dir $Dir -Log "$Log.basic-user" -Command $handOver -Headless | Out-Null
$code = [int](Get-Content "$Log.exit" -Raw).Trim()
"desktop $Name exit $code"
exit $code
