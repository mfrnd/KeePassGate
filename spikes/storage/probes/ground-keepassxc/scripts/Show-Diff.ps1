param([string] $Left, [string] $Right, [switch] $Raw)
# Normalizes away what every KeePassXC save adds (empty standard fields, the protected flag on
# every attachment, the date text of its marker) and prints a line diff of what is left.
function Norm([string] $Path) {
    $lines = [System.IO.File]::ReadAllLines($Path)
    $out = foreach ($l in $lines) {
        if ($l.StartsWith('# ')) { $l = $l -replace '^# \S+', '# db' }
        if (-not $Raw) {
            $l = $l -replace ' (Notes|Password\[P\]|Password|URL|UserName)=(?= |\}|$)', ''
            $l = $l -replace '(bin:[^\[=]+)\[P\]', '$1'
            $l = $l -replace '_LAST_MODIFIED=[^@]*@none', '_LAST_MODIFIED=<date>@none'
        }
        $l
    }
    , $out
}
$a = Norm $Left
$b = Norm $Right
$tmp = Join-Path (Split-Path -Parent $PSScriptRoot) 'work\tmp'
New-Item -ItemType Directory -Force -Path $tmp | Out-Null
$fa = Join-Path $tmp ('xcp-l-' + [guid]::NewGuid().ToString('n') + '.txt')
$fb = Join-Path $tmp ('xcp-r-' + [guid]::NewGuid().ToString('n') + '.txt')
[System.IO.File]::WriteAllLines($fa, [string[]] $a)
[System.IO.File]::WriteAllLines($fb, [string[]] $b)
git diff --no-index --no-color -U0 $fa $fb | Select-Object -Skip 4
Remove-Item $fa, $fb
