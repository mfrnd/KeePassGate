param([string] $Package, [string] $Version)

$dir = Join-Path $env:TEMP ("kpg-json-" + [Guid]::NewGuid().ToString('n'))
New-Item -ItemType Directory -Path $dir | Out-Null
try {
    Set-Content -Path (Join-Path $dir 'p.csproj') -Encoding UTF8 -Value @"
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net48</TargetFramework>
    <EnableDefaultCompileItems>false</EnableDefaultCompileItems>
  </PropertyGroup>
  <ItemGroup>
    <PackageReference Include="$Package" Version="$Version" />
  </ItemGroup>
</Project>
"@
    Push-Location $dir
    $null = dotnet build -c Release -v q --nologo 2>&1
    $code = $LASTEXITCODE
    Pop-Location

    if ($code -ne 0) { "$Package $Version -> build failed"; return }

    $out = Join-Path $dir 'bin\Release\net48'
    $dlls = Get-ChildItem $out -Filter *.dll -ErrorAction SilentlyContinue |
        Where-Object { $_.Name -ne 'p.dll' } | Sort-Object Name
    "$Package $Version -> $($dlls.Count) assemblies"
    $dlls | ForEach-Object { "    " + $_.Name }
}
finally {
    Remove-Item $dir -Recurse -Force -ErrorAction SilentlyContinue
}
