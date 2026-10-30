# Per run: frame mean, render thread draw, and every pass's cpu/gpu/draws/maps/primitives, as printed by --measure.
param([string[]]$Runs)
$o = $PSScriptRoot
foreach ($r in $Runs) {
  $t = Get-Content "$o/$r.txt"
  $head = $t | Select-String 'frames, mean ([\d.]+) ms' | Select-Object -First 1
  $mean = [double]$head.Matches[0].Groups[1].Value
  $rtd = ($t | Select-String 'render thread draw ([\d.]+)').Matches[0].Groups[1].Value
  $size = ($t | Select-String 'mean per frame at (\S+):').Matches[0].Groups[1].Value
  "$r size $size mean $mean rtd $rtd"
  $t | Select-String '^\s{4}(\w+): cpu ([\d.]+) ms, gpu ([\d.]+) ms, draws ([\d.]+), maps ([\d.]+)(, primitives (\d+), pixels (\d+))?' | ForEach-Object {
    $m = $_.Matches[0].Groups; "  {0,-18} cpu {1,6} gpu {2,7} draws {3,7} maps {4,7} prims {5,8} px {6,9}" -f $m[1], $m[2], $m[3], $m[4], $m[5], $m[7], $m[8]
  }
}
