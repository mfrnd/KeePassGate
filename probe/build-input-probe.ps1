# Compiles `InputProbe.cs` into `InputProbe.exe` beside it, with the compiler Windows PowerShell 5.1
# carries for .NET Framework. Run under Windows PowerShell, not PowerShell 7.
param([string] $Output = (Join-Path (Split-Path -Parent $PSCommandPath) 'InputProbe.exe'))
$source = Join-Path (Split-Path -Parent $PSCommandPath) 'InputProbe.cs'
if (Test-Path $Output) { Remove-Item $Output }
Add-Type -TypeDefinition (Get-Content $source -Raw) -OutputAssembly $Output -OutputType ConsoleApplication `
    -ReferencedAssemblies System.Drawing, System.Core, UIAutomationClient, UIAutomationTypes, WindowsBase, Accessibility
"built $Output"
