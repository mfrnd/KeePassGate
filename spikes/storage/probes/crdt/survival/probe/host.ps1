# Windows PowerShell 5.1 only. Loads KeePass 2.62 as a library, compiles Crdt.cs against it, then
# reads one tab-separated command per line from stdin and answers each with its output and a line
# "<<END>>". No window.
$ErrorActionPreference = 'Stop'
# Changed on the branch spikes/storage-survival, paths only: KeePass.exe through the branch's tool,
# and the compiled Crdt.dll into the ignored bin\ beside this directory rather than next to its source.
. (Join-Path $PSScriptRoot '..\..\..\..\..\..\tools\KeePass.ps1')
$kpExe = Get-KeePassExe
$dir = $PSScriptRoot
$src = Join-Path $dir 'Crdt.cs'
$bin = [System.IO.Path]::GetFullPath((Join-Path $dir '..\bin'))
if (-not (Test-Path $bin)) { [void](New-Item -ItemType Directory -Path $bin) }
$dll = Join-Path $bin 'Crdt.dll'
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
  if ($r.Errors.HasErrors) { $r.Errors | ForEach-Object { [Console]::Out.WriteLine($_.ToString()) }; exit 1 }
}
[void][System.Reflection.Assembly]::LoadFrom($dll)
[Console]::Out.WriteLine('ready')
[Console]::Out.Flush()
while ($true) {
  $line = [Console]::In.ReadLine()
  if ($line -eq $null -or $line -eq 'quit') { break }
  try { $o = [Crdt]::Run($line) } catch { $x = $_.Exception; while ($x.InnerException) { $x = $x.InnerException }; $o = 'ERROR ' + $x.GetType().Name + ': ' + $x.Message }
  [Console]::Out.WriteLine($o)
  [Console]::Out.WriteLine('<<END>>')
  [Console]::Out.Flush()
}
