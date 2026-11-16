<#
  Builds the KeePassDX harness: KeePassDX 4.5.8's `database` module and the Kotlin and Java sources
  of its `crypto` module, compiled for a desktop JVM by kotlinc 2.0.0 (the version the project pins)
  and javac 17, against the stand-ins in `standin\`, and then the driver `Driver.kt`, all written for
  this harness. Writes `bin\module\`, `bin\driver\` and the launcher `bin\harness.cmd`.

  Windows PowerShell 5.1:  powershell -NoProfile -ExecutionPolicy Bypass -File kpdx\build.ps1 [-Clean]

  It downloads, into `..\work\tools\` (not committed), what it lacks, and refuses any file whose
  SHA-256 is not the one pinned below. The source is the fetch's `..\work\src\kpdx-4.5.8\`, checked
  against its commit. No file of KeePassDX is changed or copied: two of its files are left out of
  the compilation and replaced by stand-ins of the same name (the list below). The parcelize
  compiler plugin the project applies is applied here too, from the compiler's own distribution.
  The module is compiled again only with -Clean or when `bin\module\` is empty; the driver always.
#>
param([string]$Source = "", [switch]$Clean)

$ErrorActionPreference = 'Stop'
$ProgressPreference = 'SilentlyContinue'
[Net.ServicePointManager]::SecurityProtocol = [Net.SecurityProtocolType]::Tls12

$here = $PSScriptRoot
$family = Split-Path $here -Parent
$tools = Join-Path $family 'work\tools'
if ($Source -eq '') { $Source = Join-Path $family 'work\src\kpdx-4.5.8' }
$commit = '77ecfa8654a5bd0d79d512f8fa71c0af8a29b554'

# Every download, with the SHA-256 it must have. The JDK's is the one Adoptium publishes for it,
# the compiler's the one JetBrains publishes beside it, and the jars' those Maven Central served on
# 2026-11-16.
$pins = @(
  @{ Url = 'https://github.com/adoptium/temurin17-binaries/releases/download/jdk-17.0.21%2B9/OpenJDK17U-jdk_x64_windows_hotspot_17.0.21_9.zip'
     File = 'jdk\jdk.zip'; Sha = '98518ecf554465057ee5509462631deeee374bc368c12a411d225cd29381f84b'; Unzip = 'jdk' },
  @{ Url = 'https://github.com/JetBrains/kotlin/releases/download/v2.0.0/kotlin-compiler-2.0.0.zip'
     File = 'kotlinc\kotlinc.zip'; Sha = 'ef578730976154fd2c5968d75af8c2703b3de84a78dffe913f670326e149da3b'; Unzip = 'kotlinc' },
  @{ Url = 'https://repo1.maven.org/maven2/joda-time/joda-time/2.14.1/joda-time-2.14.1.jar'
     File = 'm2\joda-time-2.14.1.jar'; Sha = '656721d9456d5ec7e7bfcf02665202ea5a375ff22136b8f23473cf7eae8fa0c6' },
  @{ Url = 'https://repo1.maven.org/maven2/commons-io/commons-io/2.21.0/commons-io-2.21.0.jar'
     File = 'm2\commons-io-2.21.0.jar'; Sha = '7d643a2afea8b058b762aa6fb90e5b256f6c729739f8b3784c3370ddc609e88d' },
  @{ Url = 'https://repo1.maven.org/maven2/org/bouncycastle/bcprov-jdk18on/1.84/bcprov-jdk18on-1.84.jar'
     File = 'm2\bcprov-jdk18on-1.84.jar'; Sha = '64d6c5a6121fcd927152dd182cbed39afe0fda641a970d9bcc0c9cb1858b2731' },
  @{ Url = 'https://repo1.maven.org/maven2/org/bouncycastle/bcpkix-jdk18on/1.84/bcpkix-jdk18on-1.84.jar'
     File = 'm2\bcpkix-jdk18on-1.84.jar'; Sha = 'c87f16ed9e5ec61bc94151e9f3646ac44e50cd448121ce84367fa4b7ec7ec1bb' },
  @{ Url = 'https://repo1.maven.org/maven2/org/bouncycastle/bcutil-jdk18on/1.84/bcutil-jdk18on-1.84.jar'
     File = 'm2\bcutil-jdk18on-1.84.jar'; Sha = 'b374e16963421fb9cfb01cc20d7ad8fd2f8b8188e3eef0ec0a8965e245f7619a' },
  @{ Url = 'https://repo1.maven.org/maven2/net/sf/kxml/kxml2/2.3.0/kxml2-2.3.0.jar'
     File = 'm2\kxml2-2.3.0.jar'; Sha = 'f264dd9f79a1fde10ce5ecc53221eff24be4c9331c830b7d52f2f08a7b633de2' }
)

function Sha256([string]$path) { (Get-FileHash -Algorithm SHA256 -LiteralPath $path).Hash.ToLowerInvariant() }

foreach ($p in $pins) {
  $f = Join-Path $tools $p.File
  if (-not (Test-Path -LiteralPath $f)) {
    New-Item -ItemType Directory -Force (Split-Path $f -Parent) | Out-Null
    Write-Host "fetching $($p.Url)"
    Invoke-WebRequest -UseBasicParsing -Uri $p.Url -OutFile $f
  }
  $got = Sha256 $f
  if ($got -ne $p.Sha) { throw "$f has SHA-256 $got, pinned $($p.Sha)" }
  if ($p.Unzip) {
    $dir = Join-Path $tools $p.Unzip
    if (-not (Get-ChildItem -Directory -LiteralPath $dir -ErrorAction SilentlyContinue)) { Expand-Archive -LiteralPath $f -DestinationPath $dir -Force }
  }
}

$jdk = (Get-ChildItem -Directory (Join-Path $tools 'jdk') | Where-Object { $_.Name -like 'jdk-17*' } | Select-Object -First 1).FullName
$kotlinHome = Join-Path $tools 'kotlinc\kotlinc'
$env:JAVA_HOME = $jdk
$env:JAVA_OPTS = '-Xmx4g -Xss16m'
$javac = Join-Path $jdk 'bin\javac.exe'

# The source, at the commit the tag names.
$head = (& git -C $Source rev-parse HEAD).Trim()
if ($head -ne $commit) { throw "$Source is at $head, expected $commit (KeePassDX 4.5.8)" }

# Left out of the compilation, each with its reason:
#   crypto\...\argon2\NativeArgon2KeyTransformer.java  JNI into the app's native argon2; replaced by
#                                                      the stand-in of the same name
#   crypto\...\aes\NativeAESKeyTransformer.java        JNI into the app's native AES; replaced by the
#                                                      stand-in of the same name
$excluded = @(
  'crypto\src\main\java\com\kunzisoft\encrypt\argon2\NativeArgon2KeyTransformer.java',
  'crypto\src\main\java\com\kunzisoft\encrypt\aes\NativeAESKeyTransformer.java'
) | ForEach-Object { (Join-Path $Source $_).ToLowerInvariant() }

$theirs = @(Get-ChildItem -Recurse -File -Include *.kt, *.java -LiteralPath (Join-Path $Source 'database\src\main\java'), (Join-Path $Source 'crypto\src\main\java')) |
  Where-Object { $excluded -notcontains $_.FullName.ToLowerInvariant() } | ForEach-Object { $_.FullName }
$ours = @(Get-ChildItem -Recurse -File -Include *.kt, *.java -LiteralPath (Join-Path $here 'standin')) | ForEach-Object { $_.FullName }
$module = $theirs + $ours

$jars = @(Get-ChildItem -File (Join-Path $tools 'm2') -Filter *.jar | ForEach-Object { $_.FullName })
$parcelizeRuntime = Join-Path $kotlinHome 'lib\parcelize-runtime.jar'
$parcelizePlugin = Join-Path $kotlinHome 'lib\parcelize-compiler.jar'
$stdlib = Join-Path $kotlinHome 'lib\kotlin-stdlib.jar'
$jars += $parcelizeRuntime
$cp = ($jars -join ';')
$bin = Join-Path $here 'bin'
$moduleClasses = Join-Path $bin 'module'
$driverClasses = Join-Path $bin 'driver'
New-Item -ItemType Directory -Force $bin | Out-Null

# An argument file takes a backslash as an escape, so every path in it is written with slashes.
function Fwd([string]$x) { $x.Replace('\', '/') }

function Fresh([string]$dir) {
  if (Test-Path -LiteralPath $dir) { [IO.Directory]::Delete($dir, $true) }
  New-Item -ItemType Directory -Force $dir | Out-Null
}

# Stage 1, the module with the stand-ins.
if ($Clean -or -not (Test-Path (Join-Path $moduleClasses 'com'))) {
  Fresh $moduleClasses
  $argfile = Join-Path $bin 'kotlinc-module.args'
  $lines = @('-jvm-target', '17', '-no-reflect', '-nowarn', "-Xplugin=$(Fwd $parcelizePlugin)", '-cp', "`"$(Fwd $cp)`"", '-d', "`"$(Fwd $moduleClasses)`"") + ($module | ForEach-Object { "`"$(Fwd $_)`"" })
  [IO.File]::WriteAllLines($argfile, $lines)
  Write-Host "kotlinc: $($theirs.Count) of KeePassDX's files, $($ours.Count) stand-ins"
  & (Join-Path $kotlinHome 'bin\kotlinc.bat') "@$argfile"
  if ($LASTEXITCODE -ne 0) { throw "kotlinc failed on the module" }
  $jargs = Join-Path $bin 'javac-module.args'
  $javaSources = $module | Where-Object { $_ -like '*.java' }
  $lines = @('--release', '17', '-nowarn', '-encoding', 'UTF-8', '-cp', "`"$(Fwd "$moduleClasses;$cp;$stdlib")`"", '-d', "`"$(Fwd $moduleClasses)`"") + ($javaSources | ForEach-Object { "`"$(Fwd $_)`"" })
  [IO.File]::WriteAllLines($jargs, $lines)
  & $javac "@$jargs"
  if ($LASTEXITCODE -ne 0) { throw "javac failed on the module" }
}

# Stage 2, the driver.
Fresh $driverClasses
$argfile = Join-Path $bin 'kotlinc-driver.args'
$lines = @('-jvm-target', '17', '-no-reflect', '-nowarn', '-cp', "`"$(Fwd "$moduleClasses;$cp")`"", '-d', "`"$(Fwd $driverClasses)`"", "`"$(Fwd (Join-Path $here 'Driver.kt'))`"")
[IO.File]::WriteAllLines($argfile, $lines)
& (Join-Path $kotlinHome 'bin\kotlinc.bat') "@$argfile"
if ($LASTEXITCODE -ne 0) { throw "kotlinc failed on the driver" }

# The launcher, with every path relative to its own place.
$rel = { param($p) '%~dp0..\..\' + $p.Substring($family.Length + 1) }
$cpRun = @('%~dp0driver', '%~dp0module', (& $rel $stdlib)) + ($jars | ForEach-Object { & $rel $_ })
$jdkRel = & $rel $jdk
$cmd = @(
  '@echo off',
  'rem Written by kpdx\build.ps1: runs the KeePassDX harness driver.',
  "`"$jdkRel\bin\java.exe`" -Xss8m -cp `"$($cpRun -join ';')`" sourceharness.kpdx.DriverKt %*"
)
[IO.File]::WriteAllLines((Join-Path $bin 'harness.cmd'), $cmd)
Write-Host "built: $bin\harness.cmd"
