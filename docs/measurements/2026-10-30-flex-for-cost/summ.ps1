# Per run: the measured window's frame mean and camera phase (the `measured ...` block), then the mean over the run's
# `moment cost` lines of `place`, `flex` and every `flex parts` figure. The first moment cost line is left out: it is
# printed in the opening seek, before the window (its frame-rate line reads ~77 fps against ~150-200 after).
param([string]$Runs)
$o = $PSScriptRoot
$names = 'driving', 'scenes', 'voices', 'step', 'local', 'rules', 'delayed', 'probe', 'corners', 'clear', 'deltas', 'slots',
  'resting', 'calls', 'faces', 'own', 'worn', 'corpse', 'hits', 'misses', 'vertices'
foreach ($r in ($Runs -split ';')) {
  $t = Get-Content "$o/$r.txt"
  $mean = ($t | Select-String 'frames, mean ([\d.]+) ms').Matches[0].Groups[1].Value
  $camera = ($t | Select-String 'phases, mean ms: .*camera ([\d.]+)').Matches[0].Groups[1].Value
  $lines = @($t | Select-String 'moment cost' | Select-Object -Skip 1 | ForEach-Object { $_.Line })
  $sum = @{}
  foreach ($l in $lines) {
    $place = [regex]::Match($l, ', place ([\d.]+) \(').Groups[1].Value
    $flex = [regex]::Match($l, ', flex ([\d.]+)\), flex parts').Groups[1].Value
    $parts = [regex]::Match($l, 'flex parts \(([^)]*)\)').Groups[1].Value
    $sum['place'] += [double]$place
    $sum['flex'] += [double]$flex
    # `resting` appears twice: the time first, then the count.
    $seen = @{}
    foreach ($m in [regex]::Matches($parts, '([a-z]+) ([\d.]+)')) {
      $k = $m.Groups[1].Value
      if ($seen.ContainsKey($k)) { $k = "$k#" }
      $seen[$k] = 1
      $sum[$k] += [double]$m.Groups[2].Value
    }
  }
  $n = $lines.Count
  $f = { param($k) '{0:0.###}' -f ($sum[$k] / $n) }
  "$r frame $mean camera $camera; over $n lines: place $(& $f 'place') flex $(& $f 'flex')"
  "  " + ((($names + 'resting#') | ForEach-Object { "$_ $(& $f $_)" }) -join ', ')
}
