# Survival probe for the critique of option "hardened custom data". Windows PowerShell 5.1 only.
# Invented content, throwaway password. No window: KeePass.exe 2.62 is loaded as an assembly, and only
# keepassxc-cli.exe 2.8.0 is started, with no window.
#
#   powershell.exe -NoProfile -ExecutionPolicy Bypass -File Chain.ps1 -Task make|chain|branch|xctimes -Work <dir> -Out <file>
param(
    [Parameter(Mandatory = $true)] [string] $Task,
    [Parameter(Mandatory = $true)] [string] $Work,
    [Parameter(Mandatory = $true)] [string] $Out
)

$ErrorActionPreference = 'Stop'
# Changed to run from the branch: KeePass and KeePassXC 2.8.0 come from the branch's tools, which
# provide and check them and answer full paths. Each tool is dot-sourced in a child scope so that
# its strict mode stays out of the probe's own scope.
$Tools = Join-Path $PSScriptRoot '..\..\..\..\..\..\tools'
$KeePassExe = & { . (Join-Path $Tools 'KeePass.ps1'); Get-KeePassExe }
$Cli = & { . (Join-Path $Tools 'KeePassXC.ps1'); Get-KeePassXCCli -Version '2.8.0' }
$Password = 'probe-only'

[void][Reflection.Assembly]::LoadFrom($KeePassExe)
$T0 = [DateTime]::new(2026, 1, 1, 10, 0, 0, [DateTimeKind]::Utc)
$flags = [Reflection.BindingFlags]'NonPublic,Instance'
$SetWithTime = [KeePassLib.Collections.StringDictionaryEx].GetMethod(
    'Set', $flags, $null, [Type[]]@([string], [string], [Nullable[DateTime]]), $null)
$GetTime = [KeePassLib.Collections.StringDictionaryEx].GetMethod(
    'GetLastModificationTime', $flags, $null, [Type[]]@([string]), $null)
if ($null -eq $SetWithTime -or $null -eq $GetTime) { throw 'StringDictionaryEx internals not found' }

function Say([string] $Line) { Add-Content -LiteralPath $Out -Value $Line -Encoding utf8; Write-Host $Line }

function New-Key {
    $key = New-Object KeePassLib.Keys.CompositeKey
    $key.AddUserKey((New-Object KeePassLib.Keys.KcpPassword $Password))
    $key
}

function Open-Db([string] $Path) {
    $db = New-Object KeePassLib.PwDatabase
    $db.Open([KeePassLib.Serialization.IOConnectionInfo]::FromPath($Path), (New-Key), $null)
    $db
}

function Set-Timeless($Dictionary, [string] $Key, [string] $Value) { [void]$SetWithTime.Invoke($Dictionary, @($Key, $Value, $null)) }

function Set-Both($Db, [string] $Key, [string] $Value) {
    Set-Timeless $Db.CustomData $Key $Value
    $Db.RootGroup.CustomData.Set($Key, $Value)
}

function Remove-Both($Db, [string] $Key) { [void]$Db.CustomData.Remove($Key); [void]$Db.RootGroup.CustomData.Remove($Key) }

function Find-Group($Db, [string] $Name) { foreach ($g in $Db.RootGroup.GetGroups($true)) { if ($g.Name -eq $Name) { return $g } }; throw "no group $Name" }
function Find-Entry($Db, [string] $Title) { foreach ($e in $Db.RootGroup.GetEntries($true)) { if ($e.Strings.ReadSafe('Title') -eq $Title) { return $e } }; throw "no entry $Title" }

function Version-Of([string] $Path) {
    $bytes = [IO.File]::ReadAllBytes($Path)
    '{0}.{1}' -f [BitConverter]::ToUInt16($bytes, 10), [BitConverter]::ToUInt16($bytes, 8)
}

function Kpg-Keys($Dictionary) {
    $keys = New-Object 'System.Collections.Generic.List[string]'
    foreach ($pair in $Dictionary) { if ($pair.Key.StartsWith('KPG.')) { $keys.Add($pair.Key) } }
    $keys.Sort([StringComparer]::Ordinal)
    , $keys
}

function Short([string] $Key) { $Key -replace '^KPG\.', '' -replace '([0-9A-F]{8})[0-9A-F]{24}', '$1' }

# What the gate would read: the union of the two places, and the facts that decide a client.
function Dump([string] $Label, [string] $Path) {
    $db = Open-Db $Path
    try {
        $dict = Kpg-Keys $db.CustomData
        $mirror = Kpg-Keys $db.RootGroup.CustomData
        $timed = 0
        foreach ($k in $dict) { if ($null -ne $GetTime.Invoke($db.CustomData, @($k))) { $timed++ } }
        $union = New-Object 'System.Collections.Generic.SortedSet[string]' ([StringComparer]::Ordinal)
        foreach ($k in $dict) { [void]$union.Add($k) }
        foreach ($k in $mirror) { [void]$union.Add($k) }
        Say ("== {0}: kdbx {1}, root time {2:u}" -f $Label, (Version-Of $Path), $db.RootGroup.LastModificationTime)
        Say ("   dictionary {0} keys ({1} timed): {2}" -f $dict.Count, $timed, (($dict | ForEach-Object { Short $_ }) -join ' '))
        Say ("   root mirror {0} keys: {1}" -f $mirror.Count, (($mirror | ForEach-Object { Short $_ }) -join ' '))
        Say ("   union {0} keys" -f $union.Count)
        $has = { param($k) $union.Contains($k) }
        $c2 = if (& $has 'KPG.Client.c2') { if (& $has 'KPG.Revoked.c2') { 'record beside its revocation: refused' } else { 'RECORD WITH NO REVOCATION: ADMITTED' } } else { 'no record: refused' }
        $a1 = if (& $has 'KPG.RetiredAuthority.a1') { 'retired' } elseif (& $has 'KPG.Authority.a1') { 'A1 UNRETIRED AND PRESENT' } else { 'gone' }
        $writer = if (& $has 'KPG.Profile.writer.out.1') { 'removed' } else { 'WRITER IN FORCE' }
        Say ("   gate: client c2 {0}; authority a1 {1}; a2 {2}; client c3 {3}; profile writer {4}" -f $c2, $a1, ($(if (& $has 'KPG.Authority.a2') { 'present' } else { 'gone' })), ($(if (& $has 'KPG.Client.c3') { 'present' } else { 'gone' })), $writer)
        $gen2 = @($union | Where-Object { $_ -like 'KPG.Rule.*.2.*' }).Count
        $g = Find-Group $db 'G'; $e = Find-Entry $db 'E1'
        Say ("   G rule {0} @{1:u}; E1 rule {2} @{3:u}; ledger generation-2 keys in union: {4}" -f $g.CustomData.Get('KPG'), $g.LastModificationTime, $e.CustomData.Get('KPG'), $e.LastModificationTime, $gen2)
    }
    finally { $db.Close() }
}

function Make([string] $Dir) {
    New-Item -ItemType Directory -Force -Path $Dir | Out-Null
    $base = Join-Path $Dir 'base.kdbx'
    $db = New-Object KeePassLib.PwDatabase
    $db.New([KeePassLib.Serialization.IOConnectionInfo]::FromPath($base), (New-Key))
    $kdf = New-Object KeePassLib.Cryptography.KeyDerivation.Argon2Kdf
    $p = $kdf.GetDefaultParameters()
    # Slow, so that keepassxc-cli loads the two files in different seconds.
    $p.SetUInt64([KeePassLib.Cryptography.KeyDerivation.Argon2Kdf]::ParamIterations, 200)
    $p.SetUInt64([KeePassLib.Cryptography.KeyDerivation.Argon2Kdf]::ParamMemory, 64 * 1024 * 1024)
    $p.SetUInt32([KeePassLib.Cryptography.KeyDerivation.Argon2Kdf]::ParamParallelism, 2)
    $db.KdfParameters = $p
    $root = $db.RootGroup
    $root.LastModificationTime = $T0
    $g = New-Object KeePassLib.PwGroup($true, $true, 'G', [KeePassLib.PwIcon]::Folder)
    $root.AddGroup($g, $true); $g.LastModificationTime = $T0
    $e = New-Object KeePassLib.PwEntry($true, $true)
    $e.Strings.Set('Title', (New-Object KeePassLib.Security.ProtectedString($false, 'E1')))
    $g.AddEntry($e, $true); $e.LastModificationTime = $T0
    $uG = $g.Uuid.ToHexString(); $uE = $e.Uuid.ToHexString()
    Set-Both $db 'KPG.Authority.a1' 'document-a1'
    Set-Both $db 'KPG.Client.c1' 'record-c1'
    Set-Both $db 'KPG.Client.c2' 'record-c2'
    Set-Both $db 'KPG.Profile.reader.in.1' '1'
    Set-Both $db 'KPG.Profile.writer.in.1' '1'
    Set-Both $db "KPG.Rule.$uG.1.wide" '{"#":"1","reader":["read","list"]}'
    Set-Both $db "KPG.Rule.$uE.1.wide" '{"#":"1","reader":["read","list"]}'
    $g.CustomData.Set('KPG', '{"#":"1","reader":["read","list"]}')
    $e.CustomData.Set('KPG', '{"#":"1","reader":["read","list"]}')
    $db.Save($null); $db.Close()
    Copy-Item -LiteralPath $base -Destination (Join-Path $Dir 'C.kdbx')
    Copy-Item -LiteralPath $base -Destination (Join-Path $Dir 'P.kdbx')
    # P: client c2 revoked, authority a1 retired and a2 made, client c3 paired, profile writer removed,
    # G and E1 narrowed a month later. Nothing moves the root group's time: the plugin never does.
    $db = Open-Db (Join-Path $Dir 'P.kdbx')
    $g = Find-Group $db 'G'; $e = Find-Entry $db 'E1'
    Set-Both $db 'KPG.Revoked.c2' '2027-01-01T00:00:00Z a1'
    Remove-Both $db 'KPG.Client.c2'
    Set-Both $db 'KPG.RetiredAuthority.a1' 'retired'
    Remove-Both $db 'KPG.Authority.a1'
    Set-Both $db 'KPG.Authority.a2' 'document-a2'
    Set-Both $db 'KPG.Client.c3' 'record-c3'
    Set-Both $db 'KPG.Profile.writer.out.1' '1'
    Set-Both $db "KPG.Rule.$uG.2.narrow" '{"#":"2","reader":[]}'
    Set-Both $db "KPG.Rule.$uE.2.narrow" '{"#":"2","reader":[]}'
    $g.CustomData.Set('KPG', '{"#":"2","reader":[]}'); $g.LastModificationTime = $T0.AddDays(31)
    $e.CreateBackup($db); $e.CustomData.Set('KPG', '{"#":"2","reader":[]}'); $e.LastModificationTime = $T0.AddDays(31)
    $db.Save($null); $db.Close()
}

function Invoke-Cli([string] $Arguments) {
    $info = New-Object System.Diagnostics.ProcessStartInfo
    $info.FileName = $Cli; $info.Arguments = $Arguments; $info.WorkingDirectory = $Work
    $info.UseShellExecute = $false; $info.CreateNoWindow = $true
    $info.RedirectStandardInput = $true; $info.RedirectStandardOutput = $true; $info.RedirectStandardError = $true
    $start = [DateTime]::UtcNow
    $proc = [System.Diagnostics.Process]::Start($info)
    $o = $proc.StandardOutput.ReadToEndAsync(); $er = $proc.StandardError.ReadToEndAsync()
    $bytes = [Text.Encoding]::ASCII.GetBytes($Password + "`n")
    $proc.StandardInput.BaseStream.Write($bytes, 0, $bytes.Length); $proc.StandardInput.BaseStream.Flush(); $proc.StandardInput.Close()
    $proc.WaitForExit()
    Say ("   keepassxc-cli 2.8.0 {0}: exit {1}, {2:n1} s" -f ($Arguments -replace [regex]::Escape($Work), '<work>'), $proc.ExitCode, ([DateTime]::UtcNow - $start).TotalSeconds)
}

function Sync([string] $Target, [string] $Source, [string] $Result, [bool] $ReconcileFirst) {
    Copy-Item -LiteralPath $Target -Destination $Result -Force
    $t = Open-Db $Result; $s = Open-Db $Source
    if ($ReconcileFirst) {
        # What the design's reconcile would do, run on the open copy before the merge instead of after it.
        foreach ($k in (Kpg-Keys $t.RootGroup.CustomData)) { if (-not $t.CustomData.Exists($k)) { Set-Timeless $t.CustomData $k $t.RootGroup.CustomData.Get($k) } }
        foreach ($k in (Kpg-Keys $t.CustomData)) { if (-not $t.RootGroup.CustomData.Exists($k)) { $t.RootGroup.CustomData.Set($k, $t.CustomData.Get($k)) } }
    }
    $t.MergeIn($s, [KeePassLib.PwMergeMethod]::Synchronize, $null)
    $t.Save($null); $t.Close(); $s.Close()
}

switch ($Task) {
    'make' {
        Make (Join-Path $Work 'bases')
        Say ("made at {0:u} with KeePassLib {1}" -f [DateTime]::UtcNow, [KeePassLib.PwDefs]::VersionString)
        Dump 'C, the stale copy' (Join-Path $Work 'bases\C.kdbx')
        Dump 'P, the fresh copy' (Join-Path $Work 'bases\P.kdbx')
    }
    'chain' {
        $b = Join-Path $Work 'bases'; $d = Join-Path $Work 'chain'
        if (Test-Path -LiteralPath $d) { Remove-Item -LiteralPath $d -Recurse -Force }
        New-Item -ItemType Directory -Path $d | Out-Null
        # Step 1, KeePassXC: the fresh copy open, the stale one merged into it (target P, source C).
        $R = Join-Path $d 'R.kdbx'; Copy-Item -LiteralPath (Join-Path $b 'P.kdbx') -Destination $R
        Say '-- step 1: keepassxc-cli merge, stale C into fresh P'
        Invoke-Cli ('merge -s "{0}" "{1}"' -f $R, (Join-Path $b 'C.kdbx'))
        Dump 'R = P after KeePassXC merged C into it' $R
        # The other direction, for comparison: the stale copy open, the fresh one merged into it.
        $R2 = Join-Path $d 'R2.kdbx'; Copy-Item -LiteralPath (Join-Path $b 'C.kdbx') -Destination $R2
        Say '-- step 1, other direction: keepassxc-cli merge, fresh P into stale C'
        Invoke-Cli ('merge -s "{0}" "{1}"' -f $R2, (Join-Path $b 'P.kdbx'))
        Dump 'R2 = C after KeePassXC merged P into it' $R2
        # Step 2, a stale copy saved by a program that stamps the root group (here a KeePassXC save).
        $Q = Join-Path $d 'Q.kdbx'; Copy-Item -LiteralPath (Join-Path $b 'C.kdbx') -Destination $Q
        Say '-- step 2: keepassxc-cli add, a plain save of the stale copy C'
        Invoke-Cli ('add "{0}" phone-added' -f $Q)
        Dump 'Q = C after a KeePassXC save' $Q
        $Qeq = Join-Path $b 'C.kdbx'
        # Step 3, KeePassLib 2.62 Synchronize: what the plugin's reconcile at FileSavingPre would see.
        Say '-- step 3: KeePassLib 2.62 Synchronize'
        Sync $R $Q (Join-Path $d 's-R+Q.kdbx') $false; Dump 'R open, Q arriving (root newer in Q)' (Join-Path $d 's-R+Q.kdbx')
        Sync $Q $R (Join-Path $d 's-Q+R.kdbx') $false; Dump 'Q open, R arriving (root newer in Q)' (Join-Path $d 's-Q+R.kdbx')
        Sync $R $Qeq (Join-Path $d 's-R+C.kdbx') $false; Dump 'R open, C arriving (equal root times)' (Join-Path $d 's-R+C.kdbx')
        Sync $Qeq $R (Join-Path $d 's-C+R.kdbx') $false; Dump 'C open, R arriving (equal root times)' (Join-Path $d 's-C+R.kdbx')
        Sync $R2 $Q (Join-Path $d 's-R2+Q.kdbx') $false; Dump 'R2 open, Q arriving' (Join-Path $d 's-R2+Q.kdbx')
        Sync $R $Q (Join-Path $d 's-R+Q-reconciled-first.kdbx') $true; Dump 'R open and reconciled in memory first, Q arriving' (Join-Path $d 's-R+Q-reconciled-first.kdbx')
        Sync $Q $R (Join-Path $d 's-Q+R-reconciled-first.kdbx') $true; Dump 'Q open and reconciled first, R arriving' (Join-Path $d 's-Q+R-reconciled-first.kdbx')
    }
    'branch' {
        # Two copies with the plugin edit one entry's rule from generation 1: A once, B twice, A last in time.
        $d = Join-Path $Work 'branch'
        if (Test-Path -LiteralPath $d) { Remove-Item -LiteralPath $d -Recurse -Force }
        New-Item -ItemType Directory -Path $d | Out-Null
        $base = Join-Path $d 'base.kdbx'
        $db = New-Object KeePassLib.PwDatabase
        $db.New([KeePassLib.Serialization.IOConnectionInfo]::FromPath($base), (New-Key))
        $kdf = New-Object KeePassLib.Cryptography.KeyDerivation.AesKdf
        $p = $kdf.GetDefaultParameters(); $p.SetUInt64([KeePassLib.Cryptography.KeyDerivation.AesKdf]::ParamRounds, 1); $db.KdfParameters = $p
        $e = New-Object KeePassLib.PwEntry($true, $true)
        $e.Strings.Set('Title', (New-Object KeePassLib.Security.ProtectedString($false, 'E1')))
        $db.RootGroup.AddEntry($e, $true); $e.LastModificationTime = $T0
        $u = $e.Uuid.ToHexString()
        $X = '{"#":"1","reader":["read","list"]}'
        Set-Both $db "KPG.Rule.$u.1.x" $X; $e.CustomData.Set('KPG', $X)
        $db.Save($null); $db.Close()
        $A = Join-Path $d 'A.kdbx'; $B = Join-Path $d 'B.kdbx'
        Copy-Item -LiteralPath $base -Destination $A; Copy-Item -LiteralPath $base -Destination $B
        $Y = '{"#":"2","reader":[]}'
        $W = '{"#":"2","reader":["read","list"],"writer":["read"]}'
        $V = '{"#":"3","reader":["read","list"],"writer":["read","update"]}'
        $db = Open-Db $A; $e = Find-Entry $db 'E1'; $e.CreateBackup($db)
        $e.CustomData.Set('KPG', $Y); $e.LastModificationTime = $T0.AddHours(3); Set-Both $db "KPG.Rule.$u.2.y" $Y; $db.Save($null); $db.Close()
        $db = Open-Db $B; $e = Find-Entry $db 'E1'; $e.CreateBackup($db)
        $e.CustomData.Set('KPG', $W); $e.LastModificationTime = $T0.AddHours(1); Set-Both $db "KPG.Rule.$u.2.w" $W
        $e.CreateBackup($db); $e.CustomData.Set('KPG', $V); $e.LastModificationTime = $T0.AddHours(2); Set-Both $db "KPG.Rule.$u.3.v" $V
        $db.Save($null); $db.Close()
        foreach ($pair in @(@($A, $B, 'A open, B arriving'), @($B, $A, 'B open, A arriving'))) {
            $res = Join-Path $d ('r-' + [IO.Path]::GetFileNameWithoutExtension($pair[0]) + '.kdbx')
            Sync $pair[0] $pair[1] $res $false
            $db = Open-Db $res
            $e = Find-Entry $db 'E1'
            $ledger = ((Kpg-Keys $db.CustomData) | ForEach-Object { ($_ -split '\.')[3..4] -join '.' }) -join ' '
            Say ("== branch, {0}: E1 rule {1} @{2:u}; ledger generations in the dictionary: {3}" -f $pair[2], $e.CustomData.Get('KPG'), $e.LastModificationTime, $ledger)
            $db.Close()
        }
    }
    'xctimes' {
        # A KDBX 4.1 file (a group tag) whose KPG keys carry no time, saved once by KeePassXC 2.8.0.
        $d = Join-Path $Work 'xctimes'
        if (Test-Path -LiteralPath $d) { Remove-Item -LiteralPath $d -Recurse -Force }
        New-Item -ItemType Directory -Path $d | Out-Null
        $f = Join-Path $d 'T.kdbx'
        $db = New-Object KeePassLib.PwDatabase
        $db.New([KeePassLib.Serialization.IOConnectionInfo]::FromPath($f), (New-Key))
        $kdf = New-Object KeePassLib.Cryptography.KeyDerivation.AesKdf
        $p = $kdf.GetDefaultParameters(); $p.SetUInt64([KeePassLib.Cryptography.KeyDerivation.AesKdf]::ParamRounds, 1); $db.KdfParameters = $p
        $g = New-Object KeePassLib.PwGroup($true, $true, 'Tagged', [KeePassLib.PwIcon]::Folder)
        $db.RootGroup.AddGroup($g, $true); $g.Tags.Add('user-tag')
        $e = New-Object KeePassLib.PwEntry($true, $true)
        $e.Strings.Set('Title', (New-Object KeePassLib.Security.ProtectedString($false, 'E1')))
        $g.AddEntry($e, $true)
        Set-Both $db 'KPG.Revoked.c2' '2027-01-01T00:00:00Z a1'
        Set-Both $db 'KPG.Client.c1' 'record-c1'
        $db.Save($null); $db.Close()
        $db = Open-Db $f
        $timed = 0; foreach ($k in (Kpg-Keys $db.CustomData)) { if ($null -ne $GetTime.Invoke($db.CustomData, @($k))) { $timed++ } }
        Say ("== before: kdbx {0}, KPG keys carrying a time: {1}" -f (Version-Of $f), $timed); $db.Close()
        Invoke-Cli ('add "{0}" xc-added' -f $f)
        $db = Open-Db $f
        $times = foreach ($k in (Kpg-Keys $db.CustomData)) { '{0}@{1}' -f (Short $k), $GetTime.Invoke($db.CustomData, @($k)) }
        Say ("== after a KeePassXC 2.8.0 save: kdbx {0}, KPG keys and times: {1}" -f (Version-Of $f), ($times -join ' ')); $db.Close()
    }
    default { throw "unknown task $Task" }
}
