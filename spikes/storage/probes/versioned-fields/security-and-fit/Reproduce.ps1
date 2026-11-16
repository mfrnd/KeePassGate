# Added on this branch to run the security and fit critique's probe from the branch root with
# one command, in Windows PowerShell 5.1, which is what `scripts\copies.ps1` names. It measures
# nothing itself: it hands `scripts\copies.ps1` the pinned KeePass 2.62 that `tools\KeePass.ps1`
# provides and checks, and `out\copies.txt` as the file to write. Both are parameters of the
# probe, so no line of it changed. Everything it writes is gitignored: `out\`.
$ErrorActionPreference = 'Stop'
$Here = $PSScriptRoot
. (Join-Path $Here '..\..\..\..\..\tools\KeePass.ps1')
$Out = Join-Path $Here 'out'
New-Item -ItemType Directory -Force -Path $Out | Out-Null
& (Join-Path $Here 'scripts\copies.ps1') -KeePassExe (Get-KeePassExe) -OutFile (Join-Path $Out 'copies.txt')
