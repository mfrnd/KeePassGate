# Changed to run from the branch: KeePass comes from the branch's tool, which provides and checks it
# and answers a full path, dot-sourced in a child scope so that its strict mode stays out of this
# script's scope.
[void][Reflection.Assembly]::LoadFrom((& { . (Join-Path $PSScriptRoot '..\..\..\..\..\..\tools\KeePass.ps1'); Get-KeePassExe }))
$flags = [Reflection.BindingFlags]'Public,NonPublic,Instance,DeclaredOnly'
foreach ($m in [KeePassLib.Collections.StringDictionaryEx].GetMembers($flags)) {
    $access = ''
    if ($m -is [Reflection.MethodBase]) { if ($m.IsPublic) { $access = 'public' } elseif ($m.IsAssembly) { $access = 'internal' } else { $access = 'private' } }
    '{0,-9} {1,-12} {2}' -f $access, $m.MemberType, $m.ToString()
}
