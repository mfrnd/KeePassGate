$c = [char]0x034F
$names = @("KPG", ("KP" + $c + "G"), ("KPG" + $c), ("K" + $c + "PG.Group"), "KPG ", ("KPG" + [char]0x00AD))
foreach ($culture in @("en-US", "de-DE", "tr-TR", "lt-LT")) {
  $ci = [System.Globalization.CultureInfo]::GetCultureInfo($culture)
  foreach ($n in $names) {
    $cmp = $ci.CompareInfo.Compare("KPG", $n, [System.Globalization.CompareOptions]::None)
    $pre = $ci.CompareInfo.IsPrefix($n, "KPG.", [System.Globalization.CompareOptions]::None)
    $cat = [System.Globalization.CharUnicodeInfo]::GetUnicodeCategory($c)
    "{0} name={1} cmpToKPG={2} prefixKPGdot={3}" -f $culture, (($n.ToCharArray() | ForEach-Object { '{0:X4}' -f [int]$_ }) -join ' '), $cmp, $pre
  }
}
"category of U+034F: " + [System.Globalization.CharUnicodeInfo]::GetUnicodeCategory([char]0x034F)
"runtime: " + [System.Environment]::Version
