# Per run, one line: frame mean, render thread draw, frames read on the GPU / skipped / disjoint, and cpu/gpu of the passes
# this record reads (Clear, Sky2D, Sky3D, World, OpaqueModels, Present), as printed by --measure. Copied in spirit from
# 2026-10-30-b597-gpu-split/summ.ps1 (same regex for a pass line); the header regex tolerates the old build's line, which
# has no "abandoned".
param([string[]]$Runs)
$o = $PSScriptRoot
$want = 'Clear', 'Sky2D', 'Sky3D', 'World', 'OpaqueModels', 'Present'
foreach ($r in $Runs) {
  $t = Get-Content "$o/$r.txt"
  $mean = ($t | Select-String 'frames, mean ([\d.]+) ms' | Select-Object -First 1).Matches[0].Groups[1].Value
  $rtd = ($t | Select-String 'render thread draw ([\d.]+)').Matches[0].Groups[1].Value
  $h = ($t | Select-String 'mean per frame at (\S+): (\d+) frames drawn, (\d+) read on the GPU, (\d+) skipped, (\d+) disjoint').Matches[0].Groups
  $line = "{0,-14} {1} frame {2} rtd {3} drawn {4} gpu {5} skip {6} disj {7} |" -f $r, $h[1], $mean, $rtd, $h[2], $h[3], $h[4], $h[5]
  foreach ($w in $want) {
    $m = ($t | Select-String "^\s{4}${w}: cpu ([\d.]+) ms, gpu ([\d.]+) ms, draws ([\d.]+)").Matches
    if ($m.Count -eq 0) { $line += " $w -"; continue }
    $line += " {0} {1}/{2}" -f $w, $m[0].Groups[1].Value, $m[0].Groups[2].Value
  }
  $line
}
