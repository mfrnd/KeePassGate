<#
.SYNOPSIS
    Runs each Live fixture of a KeePassGate checkout on its own and records whether the run ended
    with the NUnit engine failing to unload the test domain.

.DESCRIPTION
    Runs against a KeePassGate checkout on a mainline branch, whose host tests must already be
    built in Debug and whose pinned KeePass must already be provided under `.keepass\<version>\`.
    Each fixture gets a `dotnet test` of its own, filtered to the `Live` category less
    `Screenshots`, after any KeePass and test host left over from the one before is ended.

    **Run it unelevated.** The Live fixtures drive a KeePass they start, and the desktop tier is
    meant to run as an ordinary user; the summary's first line says which this run was.

    Writes `summary.txt` and one log per fixture into the output directory. The desktop is in use
    for the whole run, which is several minutes.

.EXAMPLE
    .\spikes\unload\each-fixture.ps1 -Repo C:\path\to\KeePassGate -Out work\fixtures
#>
param(
    [Parameter(Mandatory)] [string] $Repo,
    [Parameter(Mandatory)] [string] $Out,
    [string[]] $Fixtures = @(
        'LiveApiTests', 'LiveAuthorityChooserTests', 'LiveAuthorityTests', 'LiveCertificateTests',
        'LiveGateTests', 'LiveGrantEditorTests', 'LiveKeePassTests', 'LiveLockTests',
        'LiveLockTimerTests', 'LivePairingTests', 'LiveReferenceClientTests', 'LiveRenewalTests'),
    [string] $Extra = ''
)

$ErrorActionPreference = 'Continue'
New-Item -ItemType Directory -Force -Path $Out | Out-Null
$Out = (Resolve-Path $Out).Path
Get-ChildItem env: | Where-Object { $_.Name -like 'KEEPASSGATE_*' } | ForEach-Object { Remove-Item "env:$($_.Name)" }
$summary = Join-Path $Out 'summary.txt'
$elevated = ([Security.Principal.WindowsPrincipal][Security.Principal.WindowsIdentity]::GetCurrent()).IsInRole('Administrators')
"elevated: $elevated" | Set-Content $summary -Encoding UTF8

Set-Location $Repo
$project = Join-Path $Repo 'tests\KeePassGate.Host.Tests\KeePassGate.Host.Tests.csproj'
$version = (Get-Content (Join-Path $Repo 'keepass.version') -Raw).Trim()
$keePass = Join-Path $Repo ".keepass\$version"

foreach ($fixture in $Fixtures) {
    Get-Process KeePass -ErrorAction SilentlyContinue | Stop-Process -Force
    Get-Process testhost* -ErrorAction SilentlyContinue | Stop-Process -Force
    $log = Join-Path $Out "$fixture.log"
    $filter = "TestCategory=Live&TestCategory!=Screenshots&FullyQualifiedName~.$fixture."
    $arguments = @('test', $project, '-c', 'Debug', "-p:KeePassDevDir=$keePass", '--no-build', '--nologo', '--filter', $filter)
    if ($Extra) { $arguments += @('--', $Extra) }
    $started = Get-Date
    & dotnet @arguments *> $log
    $code = $LASTEXITCODE
    $text = Get-Content $log -Raw
    $unload = $text -match 'NUnitEngineUnloadException'
    $counts = ([regex]::Match($text, 'Failed:\s+\d+, Passed:\s+\d+, Skipped:\s+\d+, Total:\s+\d+')).Value
    "{0,-28} exit {1}  unload {2}  {3}  {4:n0} s" -f $fixture, $code, $unload, $counts, ((Get-Date) - $started).TotalSeconds |
        Add-Content $summary -Encoding UTF8
}

'done' | Add-Content $summary -Encoding UTF8
