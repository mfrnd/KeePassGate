# What a process can do on the desktop it runs on: which desktop that is, whether a form gets a
# window and paints, whether it can be activated and focused, and what the calls that reach for the
# input desktop (the pointer, synthesized input, the screen) answer there. Writes `probe.txt` and two
# pictures of the form into <Out>. Windows PowerShell 5.1, STA, as `powershell.exe` starts.
#
#   on a private desktop:  launcher\private-desktop.ps1 -Dir <dir> -Log <log> -Command "& '<this>' -Out '<dir>'"
#   on the visible desktop: launcher\basic-user.ps1 -Dir <dir> -Log <log> -Command "& '<this>' -Out '<dir>'"
#
# On the visible desktop it shows a form for about two seconds, moves the pointer 37 by 23 pixels
# and back, and synthesizes a zero mouse move and one key that no layout assigns (0x97).
param([Parameter(Mandatory)] [string] $Out)
$env:PROBE_OUT = $Out
New-Item -ItemType Directory -Force -Path $Out | Out-Null
$report = Join-Path $Out 'probe.txt'
Set-Content $report '' -Encoding UTF8
function Note([string] $t) { Add-Content $report $t -Encoding UTF8; Write-Host $t }
Add-Type -AssemblyName System.Windows.Forms, System.Drawing, UIAutomationClient, UIAutomationTypes, WindowsBase
Add-Type -Path (Join-Path (Split-Path -Parent $PSCommandPath) 'DesktopProbe.cs') -ReferencedAssemblies System.Windows.Forms, System.Drawing, UIAutomationClient, UIAutomationTypes, WindowsBase
$elevated = ([Security.Principal.WindowsPrincipal][Security.Principal.WindowsIdentity]::GetCurrent()).IsInRole('Administrators')
Note "elevated: $elevated"
Note ("apartment: " + [Threading.Thread]::CurrentThread.GetApartmentState())
[Probe]::Run([Action[string]] { param($t) Note $t })
