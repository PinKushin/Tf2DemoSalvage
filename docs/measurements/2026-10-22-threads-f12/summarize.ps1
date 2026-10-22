# Summarize a sample-threads.ps1 CSV over [From, To] seconds: per thread, CPU ms per second (and per
# frame when -Fps is given), sorted by cost. Threads under 1% of one core are folded into one line.
param([string]$In, [double]$From, [double]$To, [double]$Fps = 0)
$r = Import-Csv $In | ForEach-Object { $_.t = [double]$_.t; $_.cpu_ms = [double]$_.cpu_ms; $_ }
$ts = $r.t | Sort-Object -Unique
$a = ($ts | Where-Object { $_ -ge $From } | Select-Object -First 1); $b = ($ts | Where-Object { $_ -le $To } | Select-Object -Last 1)
$span = $b - $a
$s = @{}; foreach ($x in $r) { if ($x.t -eq $a) { $s[$x.tid] = $x } }
$out = foreach ($x in $r | Where-Object { $_.t -eq $b }) {
  if (-not $s.ContainsKey($x.tid)) { continue }
  $d = $x.cpu_ms - $s[$x.tid].cpu_ms
  [pscustomobject]@{ tid = $x.tid; start = $x.start; core_pct = [math]::Round(100 * $d / ($span * 1000), 1)
    ms_per_frame = if ($Fps) { [math]::Round($d / ($span * $Fps), 3) } else { '' } }
}
"window $a..$b s ($span s), threads alive both ends: $($out.Count)"
$big = $out | Where-Object core_pct -ge 1 | Sort-Object core_pct -Descending
$small = $out | Where-Object core_pct -lt 1
$big | Format-Table -AutoSize | Out-String
"other $($small.Count) threads: {0:F1}% of a core total" -f (($small | Measure-Object core_pct -Sum).Sum)
"process total: {0:F1}% of a core" -f (($out | Measure-Object core_pct -Sum).Sum)
