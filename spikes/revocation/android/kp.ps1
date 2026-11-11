<#
.SYNOPSIS
    Compiles a C# source file against the KeePassLib inside one KeePass and calls its static
    `Run(string[])` with the remaining arguments.

.DESCRIPTION
    How `Kpg.cs` is run: `Add-Type` compiles it against the `KeePass.exe` in the directory given,
    which is loaded as an assembly and runs none of its user interface. `Add-Type` takes a reference
    that does not end in `.dll` for an assembly's name and not for a file, so what it is given is a
    byte copy of that `KeePass.exe` named `KeePass.dll`, made in a directory of its own under the
    temporary directory and named for the file's hash. Windows PowerShell 5.1 only, because KeePass
    is a .NET Framework assembly.

.PARAMETER KeePass
    The directory holding `KeePass.exe`, `.keepass\2.61.1` once `tools\KeePass.ps1` has provided it.

.PARAMETER Source
    The C# file, beside this script unless a full path.

.PARAMETER Type
    The class in it whose `Run` is called.

.EXAMPLE
    powershell.exe -NoProfile -File kp.ps1 -KeePass .keepass\2.61.1 -Source Kpg.cs -Type Kpg make work\android\work\base fast argon2d 2 1024 1
#>
param(
    [Parameter(Mandatory)] [string] $KeePass,
    [Parameter(Mandatory)] [string] $Source,
    [Parameter(Mandatory)] [string] $Type,
    [Parameter(ValueFromRemainingArguments)] [string[]] $Rest
)
$ErrorActionPreference = 'Stop'
if ($PSVersionTable.PSEdition -ne 'Desktop') { throw 'run under Windows PowerShell 5.1 (powershell.exe), not pwsh' }
$me = Split-Path -Parent $MyInvocation.MyCommand.Path
$exe = Join-Path (Resolve-Path $KeePass) 'KeePass.exe'
$copy = Join-Path ([IO.Path]::GetTempPath()) ('KeePassGate-kp-' + (Get-FileHash $exe -Algorithm SHA256).Hash.Substring(0, 16))
$kp = Join-Path $copy 'KeePass.dll'
if (-not (Test-Path $kp)) { [void] (New-Item -ItemType Directory -Force $copy); Copy-Item $exe $kp }
if (-not [IO.Path]::IsPathRooted($Source)) { $Source = Join-Path $me $Source }
$refs = @($kp, 'System.dll', 'System.Core.dll', 'System.Xml.dll', 'System.Windows.Forms.dll', 'System.Drawing.dll')
$kpAsm = [Reflection.Assembly]::LoadFrom($kp)
$resolver = [ResolveEventHandler]{ param($s, $e) if ($e.Name -like 'KeePass,*') { return $kpAsm }; return $null }
[AppDomain]::CurrentDomain.add_AssemblyResolve($resolver)
Add-Type -Path $Source -ReferencedAssemblies $refs -IgnoreWarnings
[Console]::Error.WriteLine("KeePassLib: " + $kpAsm.FullName)
$t = [Type]$Type
$m = $t.GetMethod('Run', [Reflection.BindingFlags]'Public,Static')
if ($null -eq $Rest) { $Rest = @() }
$m.Invoke($null, [object[]] @(, [string[]] $Rest)) | Out-Null
