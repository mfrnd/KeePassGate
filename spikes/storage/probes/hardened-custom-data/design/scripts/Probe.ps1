# Probe for the option "hardened custom data". Windows PowerShell 5.1 only (KeePassLib is .NET Framework).
# Invented content, throwaway password. No window: KeePass.exe is loaded as an assembly, and only
# keepassxc-cli.exe is started.
#
#   powershell.exe -NoProfile -ExecutionPolicy Bypass -File Probe.ps1 -Task make|keepass|xc|xcsave|wipe -Work <dir> -Out <file>
param(
    [Parameter(Mandatory = $true)] [string] $Task,
    [Parameter(Mandatory = $true)] [string] $Work,
    [Parameter(Mandatory = $true)] [string] $Out,
    [string] $Release = '2.8.0',
    [string] $Kdf = 'slow'
)

$ErrorActionPreference = 'Stop'
# Changed to run from the branch: KeePass and the four KeePassXC releases come from the branch's
# tools, which provide and check them and answer full paths. Each tool is dot-sourced in a child
# scope so that its strict mode stays out of the probe's own scope.
$Tools = Join-Path $PSScriptRoot '..\..\..\..\..\..\tools'
$KeePassExe = & { . (Join-Path $Tools 'KeePass.ps1'); Get-KeePassExe }
$Cli = & {
    . (Join-Path $Tools 'KeePassXC.ps1')
    @{
        '2.4.2'  = Get-KeePassXCCli -Version '2.4.2'
        '2.6.6'  = Get-KeePassXCCli -Version '2.6.6'
        '2.7.12' = Get-KeePassXCCli -Version '2.7.12'
        '2.8.0'  = Get-KeePassXCCli -Version '2.8.0'
    }
}
$Password = 'probe-only'

[void][Reflection.Assembly]::LoadFrom($KeePassExe)

$T0 = [DateTime]::new(2026, 1, 1, 10, 0, 0, [DateTimeKind]::Utc)

$flags = [Reflection.BindingFlags]'NonPublic,Instance'
$SetWithTime = [KeePassLib.Collections.StringDictionaryEx].GetMethod(
    'Set', $flags, $null, [Type[]]@([string], [string], [Nullable[DateTime]]), $null)
if ($null -eq $SetWithTime) { throw 'StringDictionaryEx.Set(string, string, DateTime?) is not there' }
$GetTime = [KeePassLib.Collections.StringDictionaryEx].GetMethod(
    'GetLastModificationTime', $flags, $null, [Type[]]@([string]), $null)
if ($null -eq $GetTime) { throw 'StringDictionaryEx.GetLastModificationTime(string) is not there' }

function Say([string] $Line) {
    Add-Content -LiteralPath $Out -Value $Line -Encoding utf8
    Write-Host $Line
}

function New-Key {
    $key = New-Object KeePassLib.Keys.CompositeKey
    $key.AddUserKey((New-Object KeePassLib.Keys.KcpPassword $Password))
    $key
}

function Open-Db([string] $Path) {
    $db = New-Object KeePassLib.PwDatabase
    $ioc = [KeePassLib.Serialization.IOConnectionInfo]::FromPath($Path)
    $db.Open($ioc, (New-Key), $null)
    $db
}

# A key with no modification time, the way a save by a program that drops the times leaves it.
function Set-Timeless($Dictionary, [string] $Key, [string] $Value) {
    [void]$SetWithTime.Invoke($Dictionary, @($Key, $Value, $null))
}

function Set-Both($Db, [string] $Key, [string] $Value, [bool] $Timeless) {
    if ($Timeless) { Set-Timeless $Db.CustomData $Key $Value } else { $Db.CustomData.Set($Key, $Value) }
    $Db.RootGroup.CustomData.Set($Key, $Value)
}

function Remove-Both($Db, [string] $Key) {
    [void]$Db.CustomData.Remove($Key)
    [void]$Db.RootGroup.CustomData.Remove($Key)
}

function Find-Group($Db, [string] $Name) {
    foreach ($g in $Db.RootGroup.GetGroups($true)) { if ($g.Name -eq $Name) { return $g } }
    throw "no group $Name"
}

function Find-Entry($Db, [string] $Title) {
    foreach ($e in $Db.RootGroup.GetEntries($true)) {
        if ($e.Strings.ReadSafe('Title') -eq $Title) { return $e }
    }
    throw "no entry $Title"
}

function Version-Of([string] $Path) {
    $bytes = [IO.File]::ReadAllBytes($Path)
    $minor = [BitConverter]::ToUInt16($bytes, 8)
    $major = [BitConverter]::ToUInt16($bytes, 10)
    "$major.$minor"
}

function Kpg-Keys($Dictionary) {
    $keys = New-Object 'System.Collections.Generic.List[string]'
    foreach ($pair in $Dictionary) { $keys.Add($pair.Key) }
    $keys.Sort([StringComparer]::Ordinal)
    $keys
}

function Dump([string] $Label, [string] $Path) {
    $db = Open-Db $Path
    try {
        Say ("== {0}: kdbx {1}" -f $Label, (Version-Of $Path))
        $dict = Kpg-Keys $db.CustomData
        $mirror = Kpg-Keys $db.RootGroup.CustomData
        Say ("   dictionary ({0}): {1}" -f $dict.Count, ($dict -join ' | '))
        $timed = 0
        foreach ($k in $dict) { if ($null -ne $GetTime.Invoke($db.CustomData, @($k))) { $timed++ } }
        Say ("   dictionary keys carrying a time: {0}" -f $timed)
        Say ("   root mirror ({0}): {1}" -f $mirror.Count, ($mirror -join ' | '))
        Say ("   root time {0:u}" -f $db.RootGroup.LastModificationTime)
        $union = New-Object 'System.Collections.Generic.SortedSet[string]' ([StringComparer]::Ordinal)
        foreach ($k in $dict) { if ($k.StartsWith('KPG.')) { [void]$union.Add($k) } }
        foreach ($k in $mirror) { if ($k.StartsWith('KPG.')) { [void]$union.Add($k) } }
        Say ("   union of KPG. keys ({0}): {1}" -f $union.Count, ($union -join ' | '))
        $differ = 0
        foreach ($k in $union) {
            $a = $db.CustomData.Get($k); $b = $db.RootGroup.CustomData.Get($k)
            if ($null -ne $a -and $null -ne $b -and $a -ne $b) { $differ++ }
        }
        Say ("   keys both hold with different values: {0}" -f $differ)
        $g = Find-Group $db 'G'
        Say ("   G rule [{0}] time {1:u}" -f $g.CustomData.Get('KPG'), $g.LastModificationTime)
        $e = Find-Entry $db 'E1'
        Say ("   E1 rule [{0}] time {1:u} history {2}" -f $e.CustomData.Get('KPG'), $e.LastModificationTime, $e.History.UCount)
        Say ("   root rule [{0}]" -f $db.RootGroup.CustomData.Get('KPG'))
    }
    finally { $db.Close() }
}

function Make([string] $Dir, [string] $KdfName, [bool] $Timeless) {
    if (-not (Test-Path -LiteralPath $Dir)) { New-Item -ItemType Directory -Path $Dir | Out-Null }
    $base = Join-Path $Dir 'base.kdbx'
    $db = New-Object KeePassLib.PwDatabase
    $db.New([KeePassLib.Serialization.IOConnectionInfo]::FromPath($base), (New-Key))
    $kdf = New-Object KeePassLib.Cryptography.KeyDerivation.Argon2Kdf
    $p = $kdf.GetDefaultParameters()
    if ($KdfName -eq 'slow') {
        $p.SetUInt64([KeePassLib.Cryptography.KeyDerivation.Argon2Kdf]::ParamIterations, 200)
        $p.SetUInt64([KeePassLib.Cryptography.KeyDerivation.Argon2Kdf]::ParamMemory, 64 * 1024 * 1024)
        $p.SetUInt32([KeePassLib.Cryptography.KeyDerivation.Argon2Kdf]::ParamParallelism, 2)
    }
    else {
        $p.SetUInt64([KeePassLib.Cryptography.KeyDerivation.Argon2Kdf]::ParamIterations, 2)
        $p.SetUInt64([KeePassLib.Cryptography.KeyDerivation.Argon2Kdf]::ParamMemory, 1024 * 1024)
        $p.SetUInt32([KeePassLib.Cryptography.KeyDerivation.Argon2Kdf]::ParamParallelism, 1)
    }
    $db.KdfParameters = $p
    $root = $db.RootGroup
    $root.LastModificationTime = $T0
    $g = New-Object KeePassLib.PwGroup($true, $true, 'G', [KeePassLib.PwIcon]::Folder)
    $root.AddGroup($g, $true)
    $g.LastModificationTime = $T0
    $e = New-Object KeePassLib.PwEntry($true, $true)
    $e.Strings.Set('Title', (New-Object KeePassLib.Security.ProtectedString($false, 'E1')))
    $g.AddEntry($e, $true)
    $e.LastModificationTime = $T0
    $uuidG = $g.Uuid.ToHexString()
    $uuidE = $e.Uuid.ToHexString()

    # The stale state C: one authority, two clients, two profiles, a wide rule on G, E1 and the root.
    Set-Both $db 'KPG.Authority.a1' 'document-a1' $Timeless
    Set-Both $db 'KPG.Client.c1' 'record-c1' $Timeless
    Set-Both $db 'KPG.Client.c2' 'record-c2' $Timeless
    Set-Both $db 'KPG.Profile.reader.in.1' '1' $Timeless
    Set-Both $db 'KPG.Profile.writer.in.1' '1' $Timeless
    Set-Both $db ("KPG.Rule.$uuidG.1.wide") '{"reader":["read","list"]}' $Timeless
    Set-Both $db ("KPG.Rule.$uuidE.1.wide") '{"reader":["read","list"]}' $Timeless
    $g.CustomData.Set('KPG', '{"reader":["read","list"]}')
    $e.CustomData.Set('KPG', '{"reader":["read","list"]}')
    $root.CustomData.Set('KPG', '{"reader":["list"]}')
    $db.Save($null)
    $db.Close()

    $c = Join-Path $Dir 'C.kdbx'
    $pp = Join-Path $Dir 'P.kdbx'
    Copy-Item -LiteralPath $base -Destination $c
    Copy-Item -LiteralPath $base -Destination $pp

    # P: C after a client was revoked, the authority recreated, a profile removed and both rules
    # narrowed, every node change stamped later than anything in C.
    $db = Open-Db $pp
    $g = Find-Group $db 'G'
    $e = Find-Entry $db 'E1'
    Set-Both $db 'KPG.Revoked.c2' '2027-01-01T00:00:00Z a1' $Timeless
    Remove-Both $db 'KPG.Client.c2'
    Set-Both $db 'KPG.RetiredAuthority.a1' 'retired' $Timeless
    Remove-Both $db 'KPG.Authority.a1'
    Set-Both $db 'KPG.Authority.a2' 'document-a2' $Timeless
    Set-Both $db 'KPG.Client.c3' 'record-c3' $Timeless
    Set-Both $db 'KPG.Profile.writer.out.1' '1' $Timeless
    Set-Both $db ("KPG.Rule.$uuidG.2.narrow") '{"reader":[]}' $Timeless
    Set-Both $db ("KPG.Rule.$uuidE.2.narrow") '{"reader":[]}' $Timeless
    $g.CustomData.Set('KPG', '{"reader":[]}')
    $g.LastModificationTime = $T0.AddDays(31)
    $e.CreateBackup($db)
    $e.CustomData.Set('KPG', '{"reader":[]}')
    $e.LastModificationTime = $T0.AddDays(31)
    $db.Save($null)
    $db.Close()
}

function Invoke-Cli([string] $Version, [string] $Arguments, [string] $Stdin) {
    $info = New-Object System.Diagnostics.ProcessStartInfo
    $info.FileName = $Cli[$Version]
    $info.Arguments = $Arguments
    $info.WorkingDirectory = $Work
    $info.UseShellExecute = $false
    $info.CreateNoWindow = $true
    $info.RedirectStandardInput = $true
    $info.RedirectStandardOutput = $true
    $info.RedirectStandardError = $true
    $start = [DateTime]::UtcNow
    $proc = [System.Diagnostics.Process]::Start($info)
    $o = $proc.StandardOutput.ReadToEndAsync()
    $er = $proc.StandardError.ReadToEndAsync()
    $bytes = [Text.Encoding]::ASCII.GetBytes($Stdin + "`n")
    $proc.StandardInput.BaseStream.Write($bytes, 0, $bytes.Length)
    $proc.StandardInput.BaseStream.Flush()
    $proc.StandardInput.Close()
    $proc.WaitForExit()
    $end = [DateTime]::UtcNow
    Say ("   cli {0} {1}: exit {2}, {3:n1} s" -f $Version, $Arguments, $proc.ExitCode, ($end - $start).TotalSeconds)
    $text = ($o.Result + $er.Result).Trim()
    if ($text.Length -gt 0) { Say ("   cli said: " + ($text -replace '\s+', ' ')) }
}

switch ($Task) {
    'make' {
        foreach ($set in @('slow', 'fast')) {
            Make (Join-Path $Work "timeless-$set") $set $true
            Make (Join-Path $Work "timed-$set") $set $false
        }
        Say ("made at {0:u} with KeePassLib {1}" -f [DateTime]::UtcNow, [KeePassLib.PwDefs]::VersionString)
        foreach ($set in @('timeless-slow', 'timed-slow')) {
            Dump "$set C" (Join-Path $Work "$set\C.kdbx")
            Dump "$set P" (Join-Path $Work "$set\P.kdbx")
        }
    }
    'keepass' {
        # KeePassLib 2.62 Synchronize, both directions, over the timeless set; then again with C's
        # root group stamped later than P's, as a KeePassXC or KeePassDX save leaves it, and with
        # C's root mirror deleted, as a KeePassDroid or KeePassDX 3.2 save leaves it.
        $src = Join-Path $Work 'timeless-fast'
        $dir = Join-Path $Work 'keepass'
        if (Test-Path -LiteralPath $dir) { Remove-Item -LiteralPath $dir -Recurse -Force }
        New-Item -ItemType Directory -Path $dir | Out-Null
        foreach ($case in @('plain', 'rootlater', 'wiped')) {
            foreach ($direction in @('intoP', 'intoC')) {
                $c = Join-Path $dir "$case-$direction-C.kdbx"
                $p = Join-Path $dir "$case-$direction-P.kdbx"
                Copy-Item -LiteralPath (Join-Path $src 'C.kdbx') -Destination $c
                Copy-Item -LiteralPath (Join-Path $src 'P.kdbx') -Destination $p
                if ($case -ne 'plain') {
                    $db = Open-Db $c
                    $db.RootGroup.LastModificationTime = $T0.AddDays(60)
                    if ($case -eq 'wiped') {
                        foreach ($k in @(Kpg-Keys $db.RootGroup.CustomData)) { [void]$db.RootGroup.CustomData.Remove($k) }
                        foreach ($gr in $db.RootGroup.GetGroups($true)) { foreach ($k in @(Kpg-Keys $gr.CustomData)) { [void]$gr.CustomData.Remove($k) } }
                        foreach ($en in $db.RootGroup.GetEntries($true)) { foreach ($k in @(Kpg-Keys $en.CustomData)) { [void]$en.CustomData.Remove($k) } }
                    }
                    $db.Save($null); $db.Close()
                }
                if ($direction -eq 'intoP') { $t = $p; $s = $c } else { $t = $c; $s = $p }
                $target = Open-Db $t
                $source = Open-Db $s
                $target.MergeIn($source, [KeePassLib.PwMergeMethod]::Synchronize, $null)
                $target.Save($null)
                $target.Close(); $source.Close()
                Dump "keepass 2.62 synchronize, $case, $direction" $t
            }
        }
    }
    'xc' {
        $src = Join-Path $Work "timeless-$Kdf"
        $dir = Join-Path $Work "xc-$Release-$Kdf"
        if (Test-Path -LiteralPath $dir) { Remove-Item -LiteralPath $dir -Recurse -Force }
        New-Item -ItemType Directory -Path $dir | Out-Null
        foreach ($direction in @('intoP', 'intoC')) {
            $c = Join-Path $dir "$direction-C.kdbx"
            $p = Join-Path $dir "$direction-P.kdbx"
            Copy-Item -LiteralPath (Join-Path $src 'C.kdbx') -Destination $c
            Copy-Item -LiteralPath (Join-Path $src 'P.kdbx') -Destination $p
            if ($direction -eq 'intoP') { $t = $p; $s = $c } else { $t = $c; $s = $p }
            Say ("-- keepassxc-cli $Release merge -s, $Kdf KDF, $direction")
            Invoke-Cli $Release ('merge -s "{0}" "{1}"' -f $t, $s) $Password
            Dump "keepassxc $Release merge, $Kdf, $direction" $t
        }
    }
    'xcsave' {
        $src = Join-Path $Work 'timeless-fast'
        $dir = Join-Path $Work "xcsave-$Release"
        if (Test-Path -LiteralPath $dir) { Remove-Item -LiteralPath $dir -Recurse -Force }
        New-Item -ItemType Directory -Path $dir | Out-Null
        $p = Join-Path $dir 'P.kdbx'
        Copy-Item -LiteralPath (Join-Path $src 'P.kdbx') -Destination $p
        Say ("-- keepassxc-cli $Release add, a plain save of P")
        Invoke-Cli $Release ('add "{0}" xc-added' -f $p) $Password
        Dump "keepassxc $Release save" $p
    }
    default { throw "unknown task $Task" }
}
