param([Parameter(Position=0)][string]$Action, [Parameter(ValueFromRemainingArguments=$true)][string[]]$Rest)
$ErrorActionPreference = 'Stop'
$kpExe = & { . (Join-Path $PSScriptRoot '..\..\..\..\..\tools\KeePass.ps1'); Get-KeePassExe }
$scripts = $PSScriptRoot
New-Item -ItemType Directory -Force (Join-Path $PSScriptRoot '..\bin') | Out-Null
$dll = [System.IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..\bin\KpProbe.dll'))
[void][System.Reflection.Assembly]::LoadFrom($kpExe)
if (-not (Test-Path $dll) -or (Get-Item (Join-Path $scripts 'Kp.cs')).LastWriteTimeUtc -gt (Get-Item $dll).LastWriteTimeUtc) {
  $provider = New-Object Microsoft.CSharp.CSharpCodeProvider
  $cp = New-Object System.CodeDom.Compiler.CompilerParameters
  [void]$cp.ReferencedAssemblies.Add($kpExe)
  [void]$cp.ReferencedAssemblies.Add('System.dll')
  [void]$cp.ReferencedAssemblies.Add('System.Core.dll')
  $cp.OutputAssembly = $dll
  $cp.GenerateInMemory = $false
  $r = $provider.CompileAssemblyFromFile($cp, (Join-Path $scripts 'Kp.cs'))
  if ($r.Errors.HasErrors) { $r.Errors | ForEach-Object { Write-Error $_.ToString() }; exit 1 }
}
[void][System.Reflection.Assembly]::LoadFrom($dll)
switch ($Action) {
  'make'  { [Kp]::Make($Rest[0]) }
  'make40' { [Kp]::Make($Rest[0], $false) }
  'resave' { [Kp]::Resave($Rest[0], $Rest[1]) }
  'merge' { [Kp]::Merge($Rest[0], $Rest[1], $Rest[2], $Rest[3]) }
  'dump'  { foreach ($f in $Rest) { try { [Console]::Out.Write([Kp]::Dump($f)) } catch { $x = $_.Exception; while ($x.InnerException) { $x = $x.InnerException }; [Console]::Out.WriteLine([System.IO.Path]::GetFileName($f) + ": cannot be read by KeePassLib: " + $x.GetType().Name + ": " + $x.Message); [Console]::Out.WriteLine(($x.StackTrace -split "`n" | Select-Object -First 6) -join "`n") } } }
}
