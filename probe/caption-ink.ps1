# The darkest pixel in a picture's top rows, which is the caption's text: black on a window in the
# foreground, gray on a window that is not. Windows PowerShell 5.1.
param([Parameter(Mandatory)] [string] $Path, [int] $Rows = 30, [int] $FromX = 30, [int] $ToX = 200)
Add-Type -AssemblyName System.Drawing
$picture = [Drawing.Bitmap]::FromFile($Path)
$darkest = 765; $at = ''
for ($y = 2; $y -lt $Rows; $y++) { for ($x = $FromX; $x -lt [math]::Min($ToX, $picture.Width); $x++) {
    $c = $picture.GetPixel($x, $y); $sum = $c.R + $c.G + $c.B
    if ($sum -lt $darkest) { $darkest = $sum; $at = "$x,$y = $($c.R),$($c.G),$($c.B)" } } }
"$(Split-Path -Leaf $Path): darkest caption pixel $at"
$picture.Dispose()
