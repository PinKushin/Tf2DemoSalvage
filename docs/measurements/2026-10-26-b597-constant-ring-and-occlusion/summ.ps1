# Per run: frame mean, render thread draw, the ring status, each pass's cpu/gpu/draws/maps with its spans, and the
# nvwgf2umx thread's CPU per frame over the sampler's last 15 s (the 2026-10-25 summariser, plus spans and status).
param([string[]]$Runs)
$o = $PSScriptRoot
foreach ($r in $Runs) {
  $t = Get-Content "$o/$r.txt"
  $mean = [double]($t | Select-String 'frames, mean ([\d.]+) ms' | Select-Object -First 1).Matches[0].Groups[1].Value
  $rtd = ($t | Select-String 'render thread draw ([\d.]+)').Matches[0].Groups[1].Value
  $size = ($t | Select-String 'mean per frame at (\S+):').Matches[0].Groups[1].Value
  $rings = ($t | Select-String 'constant rings: (.*)$' | Select-Object -First 1)
  $rings = if ($rings) { $rings.Matches[0].Groups[1].Value } else { '(not printed)' }
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
  "$r size $size mean $mean rtd $rtd driver-ms/frame $drv rings: $rings"
  foreach ($line in $t) {
    if ($line -match '^\s{4}(\w+): cpu ([\d.]+) ms, gpu ([\d.]+) ms, draws ([\d.]+), maps ([\d.]+)') {
      "  {0,-18} cpu {1,6} gpu {2,7} draws {3,7} maps {4,7}" -f $Matches[1], $Matches[2], $Matches[3], $Matches[4], $Matches[5]
    }
    elseif ($line -match '^\s{8}span (\w+): cpu ([\d.]+) ms, laps ([\d.]+), bytes (\d+)') {
      "      {0,-16} {1,7} ms laps {2,7} bytes {3,8}" -f $Matches[1], $Matches[2], $Matches[3], $Matches[4]
    }
    elseif ($line -match '^\s{8}unspanned: cpu (-?[\d.]+) ms') { "      {0,-16} {1,7} ms" -f 'unspanned', $Matches[1] }
  }
}
