# Per run: frame mean, render thread draw, every pass's cpu/gpu/draws/maps, and the nvwgf2umx thread's CPU per frame
# over the sampler's last 15 s (finding 100's method: cumulative cpu_ms delta / (15 s x the run's fps)).
param([string[]]$Runs)
$o = $PSScriptRoot
foreach ($r in $Runs) {
  $t = Get-Content "$o/$r.txt"
  $head = $t | Select-String 'frames, mean ([\d.]+) ms' | Select-Object -First 1
  $mean = [double]$head.Matches[0].Groups[1].Value
  $rtd = ($t | Select-String 'render thread draw ([\d.]+)').Matches[0].Groups[1].Value
  $size = ($t | Select-String 'mean per frame at (\S+):').Matches[0].Groups[1].Value
  $drv = 'n/a'
  if (Test-Path "$o/$r.csv") {
    $rows = Import-Csv "$o/$r.csv" | Where-Object { $_.start -match 'nvwgf2umx' }
    $byTid = $rows | Group-Object tid | Sort-Object { ($_.Group | Measure-Object { [double]$_.cpu_ms } -Maximum).Maximum } -Descending | Select-Object -First 1
    if ($byTid) {
      $g = $byTid.Group | Sort-Object { [double]$_.t }
      $end = [double]$g[-1].t; $from = $g | Where-Object { [double]$_.t -ge $end - 15 } | Select-Object -First 1
      $span = $end - [double]$from.t
      if ($span -gt 0) { $drv = '{0:0.00}' -f (([double]$g[-1].cpu_ms - [double]$from.cpu_ms) / ($span * 1000 / $mean)) }
    }
  }
  "$r size $size mean $mean rtd $rtd driver-ms/frame $drv"
  $t | Select-String '^\s{4}(\w+): cpu ([\d.]+) ms, gpu ([\d.]+) ms, draws ([\d.]+), maps ([\d.]+)(, primitives (\d+), pixels (\d+))?' | ForEach-Object {
    $m = $_.Matches[0].Groups; "  {0,-18} cpu {1,6} gpu {2,7} draws {3,7} maps {4,7} prims {5,8} px {6,9}" -f $m[1], $m[2], $m[3], $m[4], $m[5], $m[7], $m[8]
  }
}
