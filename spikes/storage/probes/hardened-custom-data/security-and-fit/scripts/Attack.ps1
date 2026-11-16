# Attack probe for the critique "hardened custom data, security and fit".
# Windows PowerShell 5.1 only (KeePassLib is .NET Framework). Invented content, throwaway password.
# No window: KeePass.exe is loaded as an assembly, and only keepassxc-cli.exe is started.
#
#   powershell.exe -NoProfile -ExecutionPolicy Bypass -File Attack.ps1 -Task fork|prune|import|profiles -Work <dir> -Out <file>
#
# It models the design's store (dictionary plus root mirror, timeless keys, ledger keys) and the
# design's three states (section 2 of option-hardened-custom-data.md) as written. It is not the
# plugin's code.
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
if ($null -eq $SetWithTime) { throw 'internal Set is not there' }

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
    $db.Open([KeePassLib.Serialization.IOConnectionInfo]::FromPath($Path), (New-Key), $null)
    $db
}

function Set-Both($Db, [string] $Key, [string] $Value) {
    [void]$SetWithTime.Invoke($Db.CustomData, @($Key, $Value, $null))
    $Db.RootGroup.CustomData.Set($Key, $Value)
}

function Remove-Both($Db, [string] $Key) {
    [void]$Db.CustomData.Remove($Key)
    [void]$Db.RootGroup.CustomData.Remove($Key)
}

function Digest([string] $Text) {
    $sha = [Security.Cryptography.SHA256]::Create()
    $bytes = $sha.ComputeHash([Text.Encoding]::UTF8.GetBytes($Text))
    (-join ($bytes | ForEach-Object { $_.ToString('x2') })).Substring(0, 16)
}

# A ledger version, in both replicas, as RuleLedger.Record would add it.
function Record($Db, [string] $Uuid, [int] $Gen, [string] $Text) {
    if ($null -eq $Text) { Set-Both $Db ("KPG.Rule.$Uuid.$Gen.none") 'none' }
    else { Set-Both $Db ("KPG.Rule.$Uuid.$Gen." + (Digest $Text)) $Text }
}

function Find-Entry($Db, [string] $Title) {
    foreach ($e in $Db.RootGroup.GetEntries($true)) {
        if ($e.Strings.ReadSafe('Title') -eq $Title) { return $e }
    }
    $null
}

function Version-Of([string] $Path) {
    $bytes = [IO.File]::ReadAllBytes($Path)
    "{0}.{1}" -f [BitConverter]::ToUInt16($bytes, 10), [BitConverter]::ToUInt16($bytes, 8)
}

function Union($Db) {
    $u = New-Object 'System.Collections.Generic.SortedDictionary[string,string]' ([StringComparer]::Ordinal)
    foreach ($p in $Db.CustomData) { if ($p.Key.StartsWith('KPG.')) { $u[$p.Key] = $p.Value } }
    foreach ($p in $Db.RootGroup.CustomData) { if ($p.Key.StartsWith('KPG.')) { if (-not $u.ContainsKey($p.Key)) { $u[$p.Key] = $p.Value } } }
    $u
}

# The design's three states for one node, as section 2 defines them.
function Classify($Union, [string] $Uuid, $NodeText) {
    $versions = @()
    foreach ($k in $Union.Keys) {
        if ($k.StartsWith("KPG.Rule.$Uuid.")) {
            $rest = $k.Substring(("KPG.Rule.$Uuid.").Length).Split('.')
            $text = $null
            if ($rest[1] -ne 'none') { $text = $Union[$k] }
            $versions += New-Object psobject -Property @{ Gen = [int]$rest[0]; Text = $text; Tomb = ($rest[1] -eq 'none') }
        }
    }
    $recorded = ($versions | Sort-Object Gen | ForEach-Object { if ($_.Tomb) { "g$($_.Gen)=none" } else { "g$($_.Gen)=$($_.Text)" } }) -join '  '
    if ($versions.Count -eq 0) {
        if ($null -eq $NodeText) { return "agrees (no rule, nothing recorded) | in force: none | recorded: -" }
        return "disagrees (a rule with nothing recorded) | witnesses: node [$NodeText] only | recorded: -"
    }
    $top = ($versions | Measure-Object -Property Gen -Maximum).Maximum
    $head = @($versions | Where-Object { $_.Gen -eq $top })
    if ($head.Count -eq 1) {
        $h = $head[0]
        if ((-not $h.Tomb) -and ($NodeText -eq $h.Text)) { return "agrees | in force: [$NodeText] | recorded: $recorded" }
        if ($h.Tomb -and ($null -eq $NodeText)) { return "agrees (head is a tombstone) | in force: none | recorded: $recorded" }
        if ($null -ne $NodeText) {
            $older = @($versions | Where-Object { ($_.Gen -lt $top) -and (-not $_.Tomb) -and ($_.Text -eq $NodeText) })
            if ($older.Count -gt 0) {
                $ht = if ($h.Tomb) { 'none' } else { $h.Text }
                return "BEHIND (node holds g$($older[0].Gen)) | in force: head g$top [$ht] | recorded: $recorded"
            }
        }
    }
    $w = @(($head | ForEach-Object { if ($_.Tomb) { 'none' } else { $_.Text } }))
    $nt = if ($null -eq $NodeText) { 'absent' } else { $NodeText }
    "disagrees | witnesses: node [$nt], head [$($w -join '] [')] | recorded: $recorded"
}

function New-Base([string] $Path, [bool] $Slow) {
    $db = New-Object KeePassLib.PwDatabase
    $db.New([KeePassLib.Serialization.IOConnectionInfo]::FromPath($Path), (New-Key))
    $kdf = New-Object KeePassLib.Cryptography.KeyDerivation.Argon2Kdf
    $p = $kdf.GetDefaultParameters()
    if ($Slow) {
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
    $db.RootGroup.Name = 'Probe'
    $db.RootGroup.LastModificationTime = $T0
    $g = New-Object KeePassLib.PwGroup($true, $true, 'G', [KeePassLib.PwIcon]::Folder)
    $db.RootGroup.AddGroup($g, $true)
    $g.LastModificationTime = $T0
    $e = New-Object KeePassLib.PwEntry($true, $true)
    $e.Strings.Set('Title', (New-Object KeePassLib.Security.ProtectedString($false, 'N')))
    $e.Strings.Set('Password', (New-Object KeePassLib.Security.ProtectedString($true, 'invented-secret')))
    $g.AddEntry($e, $true)
    $e.LastModificationTime = $T0
    $db
}

function Set-Rule($Db, $Entry, [string] $Text, [DateTime] $When) {
    $Entry.CreateBackup($Db)
    if ($null -eq $Text) { [void]$Entry.CustomData.Remove('KPG') } else { $Entry.CustomData.Set('KPG', $Text) }
    $Entry.LastModificationTime = $When
}

function Invoke-Cli([string] $Arguments) {
    $info = New-Object System.Diagnostics.ProcessStartInfo
    $info.FileName = $Cli
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
    $bytes = [Text.Encoding]::ASCII.GetBytes($Password + "`n")
    $proc.StandardInput.BaseStream.Write($bytes, 0, $bytes.Length)
    $proc.StandardInput.BaseStream.Flush()
    $proc.StandardInput.Close()
    $proc.WaitForExit()
    Say ("   keepassxc-cli 2.8.0 {0}: exit {1}, {2:n1} s" -f ($Arguments -replace [regex]::Escape($Work), '<work>'), $proc.ExitCode, ([DateTime]::UtcNow - $start).TotalSeconds)
}

# Rule texts, generation in "#" as the design writes it.
$R1  = '{"#":"1","default":"inherit","agent":["read","list"]}'
$R2a = '{"#":"2","default":"inherit","agent":[]}'
$R3  = '{"#":"3","default":"inherit","agent":["read","list"]}'
$R2b = '{"#":"2","default":"inherit","agent":["list"]}'

switch ($Task) {
    'fork' {
        # S1. Two copies, both KeePass with the plugin. A narrows N (g2) and later widens it again
        # (g3); B, not synchronized, narrows N later than both (g2 of its own). KeePass 2.62
        # Synchronize, both directions.
        $dir = Join-Path $Work 'fork'
        if (Test-Path -LiteralPath $dir) { Remove-Item -LiteralPath $dir -Recurse -Force }
        New-Item -ItemType Directory -Path $dir | Out-Null
        $base = Join-Path $dir 'base.kdbx'
        $db = New-Base $base $false
        $n = Find-Entry $db 'N'
        $uuid = $n.Uuid.ToHexString()
        $n.CustomData.Set('KPG', $R1)
        Record $db $uuid 1 $R1
        $db.Save($null); $db.Close()
        $a = Join-Path $dir 'A.kdbx'; $b = Join-Path $dir 'B.kdbx'
        Copy-Item -LiteralPath $base -Destination $a; Copy-Item -LiteralPath $base -Destination $b
        $db = Open-Db $a; $n = Find-Entry $db 'N'
        Set-Rule $db $n $R2a $T0.AddDays(1); Record $db $uuid 2 $R2a
        Set-Rule $db $n $R3 $T0.AddDays(2); Record $db $uuid 3 $R3
        $db.Save($null); $db.Close()
        $db = Open-Db $b; $n = Find-Entry $db 'N'
        Set-Rule $db $n $R2b $T0.AddDays(3); Record $db $uuid 2 $R2b
        $db.Save($null); $db.Close()
        Say "== S1 fork: A g2 [$R2a] at +1d then g3 [$R3] at +2d; B g2 [$R2b] at +3d"
        foreach ($direction in @('intoA', 'intoB')) {
            $ta = Join-Path $dir "$direction-A.kdbx"; $tb = Join-Path $dir "$direction-B.kdbx"
            Copy-Item -LiteralPath $a -Destination $ta; Copy-Item -LiteralPath $b -Destination $tb
            if ($direction -eq 'intoA') { $t = $ta; $s = $tb } else { $t = $tb; $s = $ta }
            $target = Open-Db $t; $source = Open-Db $s
            $target.MergeIn($source, [KeePassLib.PwMergeMethod]::Synchronize, $null)
            $target.Save($null); $target.Close(); $source.Close()
            $db = Open-Db $t; $n = Find-Entry $db 'N'
            $nt = $n.CustomData.Get('KPG')
            Say ("   KeePass 2.62 Synchronize {0}: kdbx {1}; N time {2:u}; N rule [{3}]; history {4}" -f $direction, (Version-Of $t), $n.LastModificationTime, $nt, $n.History.UCount)
            Say ("   state: " + (Classify (Union $db) $uuid $nt))
            $db.Close()
        }
    }
    'prune' {
        # S2. A narrows N (g2), then deletes N for good; with -prune the plugin's next write drops
        # N's ledger "once the node is no longer in the tree" (design section 3, Pruning). C is the
        # base, kept elsewhere. keepassxc-cli 2.8.0 merge -s, slow KDF, both directions.
        $dir = Join-Path $Work 'prune'
        if (Test-Path -LiteralPath $dir) { Remove-Item -LiteralPath $dir -Recurse -Force }
        New-Item -ItemType Directory -Path $dir | Out-Null
        $base = Join-Path $dir 'base.kdbx'
        $db = New-Base $base $true
        $n = Find-Entry $db 'N'
        $uuid = $n.Uuid.ToHexString()
        $n.CustomData.Set('KPG', $R1)
        Record $db $uuid 1 $R1
        $db.Save($null); $db.Close()
        foreach ($prune in @($true, $false)) {
            $label = if ($prune) { 'pruned' } else { 'kept' }
            $a = Join-Path $dir "A-$label.kdbx"
            Copy-Item -LiteralPath $base -Destination $a
            $db = Open-Db $a; $n = Find-Entry $db 'N'
            Set-Rule $db $n $R2a $T0.AddDays(1); Record $db $uuid 2 $R2a
            $parent = $n.ParentGroup
            [void]$parent.Entries.Remove($n)
            $db.DeletedObjects.Add((New-Object KeePassLib.PwDeletedObject($n.Uuid, $T0.AddDays(2))))
            if ($prune) {
                foreach ($k in @((Union $db).Keys)) { if ($k.StartsWith("KPG.Rule.$uuid.")) { Remove-Both $db $k } }
            }
            $db.Save($null); $db.Close()
            foreach ($direction in @('intoA', 'intoC')) {
                $ta = Join-Path $dir "$label-$direction-A.kdbx"; $tc = Join-Path $dir "$label-$direction-C.kdbx"
                Copy-Item -LiteralPath $a -Destination $ta; Copy-Item -LiteralPath $base -Destination $tc
                if ($direction -eq 'intoA') { $t = $ta; $s = $tc } else { $t = $tc; $s = $ta }
                Say ("-- S2 ledger {0}, merge {1}" -f $label, $direction)
                Invoke-Cli ('merge -s "{0}" "{1}"' -f $t, $s)
                $db = Open-Db $t; $n = Find-Entry $db 'N'
                if ($null -eq $n) { Say "   N is not in the result" }
                else {
                    $nt = $n.CustomData.Get('KPG')
                    Say ("   N is back; rule [{0}]; kdbx {1}" -f $nt, (Version-Of $t))
                    Say ("   state: " + (Classify (Union $db) $uuid $nt))
                }
                $db.Close()
            }
            # Control: KeePass 2.62 Synchronize of the same pair, A open.
            $ta = Join-Path $dir "$label-kp-A.kdbx"; $tc = Join-Path $dir "$label-kp-C.kdbx"
            Copy-Item -LiteralPath $a -Destination $ta; Copy-Item -LiteralPath $base -Destination $tc
            $target = Open-Db $ta; $source = Open-Db $tc
            $target.MergeIn($source, [KeePassLib.PwMergeMethod]::Synchronize, $null)
            $target.Save($null); $target.Close(); $source.Close()
            $db = Open-Db $ta; $n = Find-Entry $db 'N'
            Say ("   control, KeePass 2.62 Synchronize into A ({0}): N {1}" -f $label, $(if ($null -eq $n) { 'not in the result' } else { 'present' }))
            $db.Close()
        }
    }
    'import' {
        # S3. File then Import of a copy of the same database with KeePass's preselected method,
        # Create new IDs; then the plugin's Delete authority removes the authority from both
        # replicas. Where is the authority document afterwards?
        $dir = Join-Path $Work 'import'
        if (Test-Path -LiteralPath $dir) { Remove-Item -LiteralPath $dir -Recurse -Force }
        New-Item -ItemType Directory -Path $dir | Out-Null
        $base = Join-Path $dir 'base.kdbx'
        $db = New-Base $base $false
        Set-Both $db 'KPG.Authority.a1' 'document-a1-with-private-keys'
        Set-Both $db 'KPG.Client.c1' 'record-c1'
        $db.RootGroup.CustomData.Set('KPG', '{"#":"1","default":"inherit","agent":["list"]}')
        $db.Save($null); $db.Close()
        $copy = Join-Path $dir 'backup.kdbx'
        Copy-Item -LiteralPath $base -Destination $copy
        $target = Open-Db $base; $source = Open-Db $copy
        $target.MergeIn($source, [KeePassLib.PwMergeMethod]::CreateNewUuids, $null)
        $target.Save($null); $source.Close()
        Say "== S3 import with Create new IDs of a copy of the same database"
        foreach ($g in @($target.RootGroup) + @($target.RootGroup.GetGroups($true))) {
            $keys = @(); foreach ($p in $g.CustomData) { $keys += $p.Key }
            $isRoot = [object]::ReferenceEquals($g, $target.RootGroup)
            Say ("   group [{0}] root={1} parent=[{2}] custom data: {3}" -f $g.Name, $isRoot, $(if ($g.ParentGroup) { $g.ParentGroup.Name } else { '-' }), ($keys -join ' | '))
        }
        Remove-Both $target 'KPG.Authority.a1'
        Set-Both $target 'KPG.RetiredAuthority.a1' 'retired'
        $target.Save($null); $target.Close()
        $db = Open-Db $base
        $where = @()
        foreach ($g in @($db.RootGroup) + @($db.RootGroup.GetGroups($true))) {
            if ($null -ne $g.CustomData.Get('KPG.Authority.a1')) { $where += $g.Name }
        }
        Say ("   after Delete authority: database dictionary holds it: {0}; groups holding it: [{1}]" -f ($null -ne $db.CustomData.Get('KPG.Authority.a1')), ($where -join ', '))
        $db.Close()
    }
    'profiles' {
        # S4. The profile list as generations. A removes "writer" (out.1). B, not synchronized,
        # had removed it and added it back (out.1, in.2). KeePass 2.62 Synchronize.
        $dir = Join-Path $Work 'profiles'
        if (Test-Path -LiteralPath $dir) { Remove-Item -LiteralPath $dir -Recurse -Force }
        New-Item -ItemType Directory -Path $dir | Out-Null
        $base = Join-Path $dir 'base.kdbx'
        $db = New-Base $base $false
        Set-Both $db 'KPG.Profile.writer.in.1' '1'
        $db.Save($null); $db.Close()
        $a = Join-Path $dir 'A.kdbx'; $b = Join-Path $dir 'B.kdbx'
        Copy-Item -LiteralPath $base -Destination $a; Copy-Item -LiteralPath $base -Destination $b
        $db = Open-Db $b; Set-Both $db 'KPG.Profile.writer.out.1' '1'; Set-Both $db 'KPG.Profile.writer.in.2' '1'; $db.Save($null); $db.Close()
        $db = Open-Db $a; Set-Both $db 'KPG.Profile.writer.out.1' '1'; $db.Save($null); $db.Close()
        $target = Open-Db $a; $source = Open-Db $b
        $target.MergeIn($source, [KeePassLib.PwMergeMethod]::Synchronize, $null)
        $target.Save($null); $source.Close()
        $u = Union $target
        $maxIn = 0; $maxOut = 0
        foreach ($k in $u.Keys) {
            if ($k -match '^KPG\.Profile\.writer\.in\.(\d+)$') { $maxIn = [Math]::Max($maxIn, [int]$Matches[1]) }
            if ($k -match '^KPG\.Profile\.writer\.out\.(\d+)$') { $maxOut = [Math]::Max($maxOut, [int]$Matches[1]) }
        }
        Say ("== S4 profiles: union {0}; writer in force: {1}" -f (($u.Keys) -join ' | '), ($maxIn -gt $maxOut))
        $target.Close()
    }
    'xc41' {
        # S5. The design's keys carry no time and keep the file at 4.0. A group tag is a 4.1 feature
        # of KeePass's own; with one in the file, does a KeePassXC 2.8.0 merge put times on the
        # plugin's keys it brings in? Slow KDF, both directions.
        $GetTime = [KeePassLib.Collections.StringDictionaryEx].GetMethod(
            'GetLastModificationTime', $flags, $null, [Type[]]@([string]), $null)
        $dir = Join-Path $Work 'xc41'
        if (Test-Path -LiteralPath $dir) { Remove-Item -LiteralPath $dir -Recurse -Force }
        New-Item -ItemType Directory -Path $dir | Out-Null
        $base = Join-Path $dir 'base.kdbx'
        $db = New-Base $base $true
        foreach ($g in $db.RootGroup.GetGroups($true)) { if ($g.Name -eq 'G') { $l = New-Object 'System.Collections.Generic.List[string]'; $l.Add('tagged'); $g.Tags = $l } }
        Set-Both $db 'KPG.Authority.a1' 'document-a1'
        Set-Both $db 'KPG.Client.c1' 'record-c1'
        $db.Save($null); $db.Close()
        $c = Join-Path $dir 'C.kdbx'; $p = Join-Path $dir 'P.kdbx'
        Copy-Item -LiteralPath $base -Destination $c; Copy-Item -LiteralPath $base -Destination $p
        $db = Open-Db $p
        Set-Both $db 'KPG.Revoked.c1' '2027-01-01T00:00:00Z a1'
        Set-Both $db 'KPG.Client.c2' 'record-c2'
        $db.Save($null); $db.Close()
        Say ("== S5 base kdbx {0}, P kdbx {1}" -f (Version-Of $base), (Version-Of $p))
        foreach ($direction in @('intoP', 'intoC')) {
            $tp = Join-Path $dir "$direction-P.kdbx"; $tc = Join-Path $dir "$direction-C.kdbx"
            Copy-Item -LiteralPath $p -Destination $tp; Copy-Item -LiteralPath $c -Destination $tc
            if ($direction -eq 'intoP') { $t = $tp; $s = $tc } else { $t = $tc; $s = $tp }
            Invoke-Cli ('merge -s "{0}" "{1}"' -f $t, $s)
            $db = Open-Db $t
            $timed = @(); $all = @()
            foreach ($pair in $db.CustomData) {
                if ($pair.Key.StartsWith('KPG.')) {
                    $all += $pair.Key
                    if ($null -ne $GetTime.Invoke($db.CustomData, @($pair.Key))) { $timed += $pair.Key }
                }
            }
            Say ("   {0}: kdbx {1}; dictionary KPG keys [{2}]; carrying a time [{3}]" -f $direction, (Version-Of $t), ($all -join ', '), ($timed -join ', '))
            $db.Close()
        }
    }
    'xc41save' {
        # S5b. A plain keepassxc-cli 2.8.0 save (add an entry) of the 4.1 base made by 'xc41'.
        $GetTime = [KeePassLib.Collections.StringDictionaryEx].GetMethod(
            'GetLastModificationTime', $flags, $null, [Type[]]@([string]), $null)
        $dir = Join-Path $Work 'xc41'
        $t = Join-Path $dir 'save-P.kdbx'
        Copy-Item -LiteralPath (Join-Path $dir 'P.kdbx') -Destination $t -Force
        Invoke-Cli ('add "{0}" xc-added' -f $t)
        $db = Open-Db $t
        $timed = @(); $all = @()
        foreach ($pair in $db.CustomData) {
            if ($pair.Key.StartsWith('KPG.')) {
                $all += $pair.Key
                if ($null -ne $GetTime.Invoke($db.CustomData, @($pair.Key))) { $timed += $pair.Key }
            }
        }
        Say ("== S5b plain save of the 4.1 P: kdbx {0}; dictionary KPG keys [{1}]; carrying a time [{2}]" -f (Version-Of $t), ($all -join ', '), ($timed -join ', '))
        $db.Close()
    }
    default { throw "unknown task $Task" }
}
