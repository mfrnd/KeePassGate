# Counts the pixels two pictures of the same size differ in, and where: the rows the differences
# fall in, banded, so a caption can be told from a body. Windows PowerShell 5.1.
param([Parameter(Mandatory)] [string] $A, [Parameter(Mandatory)] [string] $B, [int] $Band = 32)
Add-Type -AssemblyName System.Drawing
$one = [Drawing.Bitmap]::FromFile($A); $two = [Drawing.Bitmap]::FromFile($B)
if ($one.Width -ne $two.Width -or $one.Height -ne $two.Height) { "sizes differ: $($one.Width)x$($one.Height) and $($two.Width)x$($two.Height)"; exit }
$bands = @{}; $total = 0
for ($y = 0; $y -lt $one.Height; $y++) {
    for ($x = 0; $x -lt $one.Width; $x++) {
        if ($one.GetPixel($x, $y).ToArgb() -ne $two.GetPixel($x, $y).ToArgb()) { $total++; $k = [math]::Floor($y / $Band); $bands[$k] = 1 + [int]$bands[$k] }
    }
}
"$($one.Width) by $($one.Height): $total pixels differ"
foreach ($k in ($bands.Keys | Sort-Object)) { "  rows $($k * $Band) to $(($k + 1) * $Band - 1): $($bands[$k])" }
$one.Dispose(); $two.Dispose()
