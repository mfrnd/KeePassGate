<#
.SYNOPSIS
    Runs the probes of the ground report `others` from the branch, as `RUN.md` describes, into
    `out\`.

.DESCRIPTION
    Added on the branch: the agent typed these steps as console commands and kept two scripts of
    them, `scripts\run-kdbxweb.ps1` and `scripts\run-keepass.ps1`. The steps are the agent's, in
    the agent's order and with the agent's arguments, rebuilt from the session transcript:

      bases       `kp.ps1 make40` into `work\db40`, `kp.ps1 make` into `work\db`, and the dump of
                  both bases.
      build-xml    `scripts\build-xml.js` over `work\db\A.kdbx`: the XML document kdbxweb builds for
                  a binary save, the database custom data item and the first `Times` element.
      kdbxweb40   `scripts\run-kdbxweb.ps1 db40`: kdbxweb saves A again and merges six pairs of the
                  KDBX 4.0 set, and KeePassLib dumps what it wrote.
      kdbxweb41   `scripts\run-kdbxweb.ps1 db`: the same over the KDBX 4.1 set.
      xml         `kw.js xml` over kdbxweb's resave of the 4.1 set: the database as kdbxweb exports
                  it to XML, which shows the history limits it writes.
      pykeepass   `pk.py` resave, inject and count, with a KeePassLib resave and dump between.
      keepass     `scripts\run-keepass.ps1 db`: KeePass 2.62's Synchronize of the six pairs.
      group       `kp.ps1 make40` into `work\db40g`, kdbxweb's and KeePass's merges of the copy
                  that changed only the group's custom data, and the group lines of their dump.

    Each writes `out\<name>`, the name its recorded copy has under `recorded\`. One more step is
    added on the branch, with no record beside it:

      inputs      the dump of every input database the merges read, which the report's appendix
                  summarizes and no recorded output shows.

    `registries` is not run unless asked for: it asks npm, crates.io and pkg.go.dev, over the
    network, the questions the agent asked them, and their answers change with time.

    The databases go to `work\`, which is emptied first; `bin\` takes the compiled `Kp.cs`. kdbxweb
    2.1.1 is installed into `kdbxweb\node_modules` from `kdbxweb\package-lock.json` when it is
    absent. Windows PowerShell 5.1, from the branch root:

        powershell.exe -NoProfile -ExecutionPolicy Bypass -File spikes\storage\probes\ground-others\run.ps1

.PARAMETER Only
    Runs the named steps alone. `build-xml`, `kdbxweb40`, `kdbxweb41`, `pykeepass` and `keepass`
    read the databases `bases` makes, `xml` and `pykeepass` read what `kdbxweb41` writes, so
    `work\` is emptied only when `bases` runs. Several are separated by commas, `-Only xml,group`,
    since `-File` hands a list over as one string.
#>
param([string] $Only)
$ErrorActionPreference = 'Stop'
if ($PSVersionTable.PSEdition -ne 'Desktop') { throw 'Windows PowerShell 5.1 only (powershell.exe).' }
$known = 'bases', 'build-xml', 'kdbxweb40', 'kdbxweb41', 'xml', 'pykeepass', 'keepass', 'group', 'inputs', 'registries'
$picked = @($Only -split '[,\s]+' | ForEach-Object { $_.Trim() } | Where-Object { $_ })
foreach ($name in $picked) { if ($known -notcontains $name) { throw "No step '$name'; the steps are $($known -join ', ')." } }

$here = $PSScriptRoot
$root = (Resolve-Path (Join-Path $here '..\..\..\..')).Path
$out = Join-Path $here 'out'
$work = Join-Path $here 'work'
$scripts = Join-Path $here 'scripts'
$kp = Join-Path $scripts 'kp.ps1'
$kw = Join-Path $scripts 'kw.js'
$pk = Join-Path $scripts 'pk.py'
$M = Join-Path $here 'kdbxweb\node_modules\kdbxweb'
$ps = Join-Path $env:SystemRoot 'System32\WindowsPowerShell\v1.0\powershell.exe'
$steps = if ($picked) { $picked } else { 'bases', 'build-xml', 'kdbxweb40', 'kdbxweb41', 'xml', 'pykeepass', 'keepass', 'group', 'inputs' }
$utf8 = New-Object System.Text.UTF8Encoding($false)
[void](New-Item -ItemType Directory -Force $out)

# Provides and checks the pinned KeePass before any step loads it, so that a missing one stops here.
& { . (Join-Path $root 'tools\KeePass.ps1'); $null = Get-KeePassExe }

# A native command's standard output and error, each line as text, with the exit code kept.
# Windows PowerShell turns a line on standard error into an error record, which `Stop` would throw.
function Invoke-Native([scriptblock] $Command) {
    $saved = $ErrorActionPreference
    $ErrorActionPreference = 'Continue'
    try { & $Command 2>&1 | ForEach-Object { "$_" } }
    finally { $ErrorActionPreference = $saved }
}

function Write-Out([string] $Name, [string[]] $Lines) {
    [IO.File]::WriteAllLines((Join-Path $out $Name), [string[]] $Lines, $utf8)
}

function Step([string] $Name, [scriptblock] $Run) {
    if ($steps -notcontains $Name) { return }
    $clock = [Diagnostics.Stopwatch]::StartNew()
    & $Run
    Write-Host ([string]::Format([Globalization.CultureInfo]::InvariantCulture, '{0}: {1:0.0} s', $Name, $clock.Elapsed.TotalSeconds))
}

function Kp([string[]] $Arguments) { Invoke-Native { & $ps -NoProfile -ExecutionPolicy Bypass -File $kp @Arguments } }

# kdbxweb 2.1.1 and its two dependencies, as the lock file holds them, with their integrity hashes.
$installed = Join-Path $M 'package.json'
if ($steps | Where-Object { $_ -in 'build-xml', 'kdbxweb40', 'kdbxweb41', 'xml', 'pykeepass', 'group' }) {
    if (-not (Test-Path $installed) -or ((Get-Content -Raw $installed | ConvertFrom-Json).version -ne '2.1.1')) {
        Push-Location (Join-Path $here 'kdbxweb')
        try { Invoke-Native { npm ci --ignore-scripts --no-audit --no-fund } | Write-Host }
        finally { Pop-Location }
    }
    if ((Get-Content -Raw $installed | ConvertFrom-Json).version -ne '2.1.1') { throw 'kdbxweb 2.1.1 is not installed.' }
}

Step 'bases' {
    if (Test-Path $work) { Remove-Item $work -Recurse -Force }
    $db40 = Join-Path $work 'db40'
    $db = Join-Path $work 'db'
    [void](New-Item -ItemType Directory -Force $db40, $db)
    $lines = @()
    $lines += @(Kp @('make40', $db40))
    $lines += @(Kp @('make', $db))
    $lines += @(Kp @('dump', (Join-Path $db40 'base.kdbx'), (Join-Path $db 'base.kdbx')))
    Write-Out 'bases.txt' $lines
}

Step 'build-xml' {
    $code = [IO.File]::ReadAllText((Join-Path $scripts 'build-xml.js'))
    Write-Out 'build-xml.txt' (Invoke-Native { node -e $code $M (Join-Path $work 'db\A.kdbx') })
}

Step 'kdbxweb40' {
    Write-Out 'kdbxweb-db40.txt' (Invoke-Native { & $ps -NoProfile -ExecutionPolicy Bypass -File (Join-Path $scripts 'run-kdbxweb.ps1') db40 })
}

Step 'kdbxweb41' {
    Write-Out 'kdbxweb-db41.txt' (Invoke-Native { & $ps -NoProfile -ExecutionPolicy Bypass -File (Join-Path $scripts 'run-kdbxweb.ps1') db })
}

Step 'xml' {
    Write-Out 'A-resaved.xml' (Invoke-Native { node $kw $M xml (Join-Path $work 'out-kdbxweb-db\A-resaved.kdbx') })
}

Step 'pykeepass' {
    $O = Join-Path $work 'out-pykeepass'
    [void](New-Item -ItemType Directory -Force $O)
    $with = 'pykeepass==4.2.0'
    function Pk([string[]] $Arguments) { Invoke-Native { uv run --no-project --with $with python -I -X utf8 $pk @Arguments } }
    $lines = @()
    Push-Location $O
    try {
        # The agent's first command line, step for step, with the lines it kept of each.
        $lines += @(Pk @('resave', (Join-Path $work 'db\A.kdbx'), (Join-Path $O 'A-resaved.kdbx')) | Select-Object -Last 3)
        $lines += @(Pk @('inject', (Join-Path $work 'db\A.kdbx'), (Join-Path $O 'A-unknown.kdbx')) | Select-Object -Last 2)
        $lines += @(Kp @('resave', (Join-Path $O 'A-unknown.kdbx'), (Join-Path $O 'A-unknown-keepass.kdbx')))
        $lines += @(Pk @('count', (Join-Path $O 'A-unknown.kdbx')) | Select-Object -Last 1)
        $lines += @(Pk @('count', (Join-Path $O 'A-unknown-keepass.kdbx')) | Select-Object -Last 1)
        $lines += @(Kp @('dump', (Join-Path $O 'A-resaved.kdbx'), (Join-Path $O 'A-unknown.kdbx')))
        # Its second: the 4.0 copy with the three elements, resaved by kdbxweb, then counted, and
        # kdbxweb's resave of the 4.1 set opened by pykeepass.
        $lines += @(Pk @('inject', (Join-Path $work 'db40\A.kdbx'), (Join-Path $O 'A40-unknown.kdbx')) | Select-Object -Last 1)
        $null = Invoke-Native { node $kw $M resave (Join-Path $O 'A40-unknown.kdbx') (Join-Path $O 'A40-unknown-kdbxweb.kdbx') }
        $lines += @(Pk @('count', (Join-Path $O 'A40-unknown-kdbxweb.kdbx')) | Select-Object -Last 1)
        $lines += @(Pk @('count', (Join-Path $work 'out-kdbxweb-db\A-resaved.kdbx')) | Select-Object -Last 1)
    }
    finally { Pop-Location }
    Write-Out 'pykeepass.txt' $lines
    # Added on the branch: the versions the environment resolved, which the agent did not print.
    Write-Out 'pykeepass-env.txt' (Invoke-Native { uv run --no-project --with $with python -I -X utf8 -c 'import importlib.metadata as m; print(*sorted(d.metadata[''Name''] + '' '' + d.version for d in m.distributions()), sep=''\n'')' })
}

Step 'keepass' {
    Write-Out 'keepass-db41.txt' (Invoke-Native { & $ps -NoProfile -ExecutionPolicy Bypass -File (Join-Path $scripts 'run-keepass.ps1') db })
}

Step 'group' {
    $W = Join-Path $work 'db40g'
    $O = Join-Path $work 'out-group'
    [void](New-Item -ItemType Directory -Force $W, $O)
    $null = Kp @('make40', $W)
    $null = Invoke-Native { node $kw $M merge (Join-Path $W 'A.kdbx') (Join-Path $W 'Bg.kdbx') (Join-Path $O 'kdbxweb-A+Bg.kdbx') }
    $null = Invoke-Native { node $kw $M merge (Join-Path $W 'Bg.kdbx') (Join-Path $W 'A.kdbx') (Join-Path $O 'kdbxweb-Bg+A.kdbx') }
    $null = Kp @('merge', (Join-Path $W 'A.kdbx'), (Join-Path $W 'Bg.kdbx'), (Join-Path $O 'keepass-A+Bg.kdbx'), 'Synchronize')
    $null = Kp @('merge', (Join-Path $W 'Bg.kdbx'), (Join-Path $W 'A.kdbx'), (Join-Path $O 'keepass-Bg+A.kdbx'), 'Synchronize')
    $dump = Kp @('dump', (Join-Path $W 'Bg.kdbx'), (Join-Path $O 'kdbxweb-A+Bg.kdbx'), (Join-Path $O 'kdbxweb-Bg+A.kdbx'),
        (Join-Path $O 'keepass-A+Bg.kdbx'), (Join-Path $O 'keepass-Bg+A.kdbx'))
    # `| Select-String -Pattern 'kdbx:|  G ' | Tee-Object -FilePath`, as the agent ran it in
    # PowerShell 7: the matching lines, with the empty line before and after that the formatting of
    # match objects writes to a file.
    Write-Out 'group-merge.txt' (@('') + @($dump | Where-Object { $_ -match 'kdbx:|  G ' }) + @(''))
}

Step 'inputs' {
    $files = @()
    foreach ($set in 'db40', 'db', 'db40g') {
        foreach ($name in 'base', 'A', 'B', 'Btie', 'Bcd', 'Bg') {
            $f = Join-Path $work "$set\$name.kdbx"
            if (Test-Path $f) { $files += $f }
        }
    }
    $lines = @()
    foreach ($f in $files) {
        $lines += "== $(Split-Path -Leaf (Split-Path -Parent $f))"
        $lines += @(Kp @('dump', $f))
    }
    Write-Out 'inputs.txt' $lines
}

Step 'registries' {
    # The agent's `npm view` line, and its two `curl` lines to crates.io, with the parts it kept.
    $view = Invoke-Native { npm view kdbxweb versions time.modified dist-tags --json }
    $time = Invoke-Native { npm view kdbxweb time --json } | Where-Object { $_ -match '"(2\.1\.1|created|modified)"' }
    Write-Out 'npm-view.txt' (@($view | Select-Object -Last 20) + @('-- added on the branch: npm view kdbxweb time') + @($time))
    $agent = 'storage-survey (research)'
    $page = Invoke-Native { curl.exe -s -A $agent 'https://crates.io/api/v1/crates/keepass/reverse_dependencies?per_page=60' }
    $crates = [regex]::Matches(($page -join "`n"), '"crate":"[^"]+"') | ForEach-Object { $_.Value } | Sort-Object -Unique
    Write-Out 'crates.txt' $crates
    # Added on the branch: the agent read this page through a fetch tool, not a command.
    $go = Invoke-Native { curl.exe -s -L -A $agent 'https://pkg.go.dev/github.com/tobischo/gokeepasslib/v3?tab=importedby' }
    Write-Out 'pkg-go-dev.txt' ([regex]::Matches(($go -join "`n"), 'Known importing (modules|packages)[^<]*<[^>]*>[^<]*|\d+\s+known importing (modules|packages)') | ForEach-Object { $_.Value })
}
