<#
.SYNOPSIS
    Checks that the plugin's own line coverage has not fallen below the floor.

.DESCRIPTION
    **The collector is the one the test SDK already ships.** `Microsoft.NET.Test.Sdk` carries
    `Microsoft.CodeCoverage`, which instruments .NET Framework and writes cobertura when asked, so
    the answer costs no new dependency. `coverlet.collector` was tried first and wrote an empty
    report on `net48`, which is the whole reason to prefer what is already here.

    **The two tiers are combined rather than compared.** A line the host tests reach and the unit
    tests do not is covered, so the reports are combined line by line. Read on its own each tier
    says far less than the two say together, and only the combined number is about the plugin
    rather than about one tier's share of it.

    **The test assemblies are not counted.** Including them would mostly measure the unit tests
    covering themselves. Only the `KeePassGate` package is measured, which is the code that ships.

    **The number reads the same wherever it ran.** A percentage rendered in the machine's own
    culture says 84,8 on one and 84.8 on the next, and a gate whose output depends on where it was
    run is a gate two people cannot compare notes about.

.PARAMETER Reports
    Where the collector wrote its cobertura files.

.PARAMETER Floor
    The percentage the plugin may not fall below.
#>

[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)] [string] $Reports,
    [Parameter(Mandatory = $true)] [int] $Floor
)

$ErrorActionPreference = 'Stop'

$files = @(Get-ChildItem -Path $Reports -Recurse -Filter '*.cobertura.xml' -ErrorAction SilentlyContinue)
if ($files.Count -eq 0) {
    throw "No coverage report under $Reports. The tests run with --collect before this is asked."
}

# **Each report once.** A tier that writes a result file has its logger keep a copy of every
# attachment beside that file, so the same report is found twice. Read twice it would change
# nothing but the time this takes and the count printed below, which is the number of reports
# measured, so a copy is known by its contents and read only once.
$distinct = @{}
foreach ($file in $files) {
    $distinct[(Get-FileHash -LiteralPath $file.FullName -Algorithm SHA256).Hash] = $file
}

$files = @($distinct.Values)

# One entry per source line, true once anything has reached it. A hashtable rather than a count,
# because what is wanted is whether the line was reached at all, by either tier.
$reached = @{}
foreach ($file in $files) {
    $xml = [xml] (Get-Content -Path $file.FullName -Raw)
    foreach ($package in $xml.coverage.packages.package) {
        if ($package.name -ne 'KeePassGate') {
            continue
        }

        foreach ($class in $package.classes.class) {
            foreach ($line in $class.lines.line) {
                $key = $class.filename + ':' + $line.number
                if (-not $reached.ContainsKey($key)) {
                    $reached[$key] = $false
                }

                if ([int] $line.hits -gt 0) {
                    $reached[$key] = $true
                }
            }
        }
    }
}

if ($reached.Count -eq 0) {
    throw "The reports under $Reports carry no KeePassGate package. Nothing was measured."
}

$covered = @($reached.Values | Where-Object { $_ }).Count
$percent = [math]::Round(100.0 * $covered / $reached.Count, 1)

# Compared as a number and printed as an invariant string, so the floor is never a text comparison
# and the number never arrives with whatever separator this machine prefers.
$shown = $percent.ToString([System.Globalization.CultureInfo]::InvariantCulture)

Write-Host ("  {0} of {1} lines, {2}%, from {3} report(s)." -f
    $covered, $reached.Count, $shown, $files.Count)

if ($percent -lt $Floor) {
    throw "Plugin coverage is $shown%, below the floor of $Floor%."
}
