param([Parameter(Position=0)][string]$Action, [Parameter(ValueFromRemainingArguments=$true)][string[]]$Rest)
# Windows PowerShell 5.1 only. Loads KeePass 2.62 as a library and compiles Surv.cs against it.
$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot '..\..\..\..\..\..\tools\KeePass.ps1'); $kpExe = Get-KeePassExe
$scripts = $PSScriptRoot
$bin = Join-Path (Split-Path -Parent $PSScriptRoot) 'bin'
New-Item -ItemType Directory -Force $bin | Out-Null
$src = Join-Path $scripts 'Surv.cs'
$dll = Join-Path $bin 'Surv.dll'
[void][System.Reflection.Assembly]::LoadFrom($kpExe)
if (-not (Test-Path $dll) -or (Get-Item $src).LastWriteTimeUtc -gt (Get-Item $dll).LastWriteTimeUtc) {
  $provider = New-Object Microsoft.CSharp.CSharpCodeProvider
  $cp = New-Object System.CodeDom.Compiler.CompilerParameters
  [void]$cp.ReferencedAssemblies.Add($kpExe)
  [void]$cp.ReferencedAssemblies.Add('System.dll')
  [void]$cp.ReferencedAssemblies.Add('System.Core.dll')
  $cp.OutputAssembly = $dll
  $cp.GenerateInMemory = $false
  $r = $provider.CompileAssemblyFromFile($cp, $src)
  if ($r.Errors.HasErrors) { $r.Errors | ForEach-Object { Write-Output $_.ToString() }; exit 1 }
}
[void][System.Reflection.Assembly]::LoadFrom($dll)
switch ($Action) {
  'make'  { [Surv]::Make($Rest[0]) }
  'merge' { [Surv]::Merge($Rest[0], $Rest[1], $Rest[2], $Rest[3]) }
  'dump'  { foreach ($f in $Rest) { try { [Console]::Out.Write([Surv]::Dump($f)) } catch { $x = $_.Exception; while ($x.InnerException) { $x = $x.InnerException }; [Console]::Out.WriteLine([System.IO.Path]::GetFileName($f) + ": cannot be read: " + $x.GetType().Name + ": " + $x.Message) } } }
}
