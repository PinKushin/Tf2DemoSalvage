# Reads one run's stdout. Per-second rate lines: the first $Warm (first use, the miss path) and the rest (steady, the hit
# path), each frame-weighted per named phase or part. Moment lines: plain mean after the first. Prints the run's own
# `measured ... mean` and every requested key in ms and as % of that mean.
param([Parameter(Mandatory)][string]$File, [int]$Warm = 5,
  [string[]]$Keys = @('sound', 'camera', 'project', 'advance', 'capture', 'hud', 'draw', 'unaccounted',
    'camera pose', 'camera particles', 'camera particles/decals', 'camera particles/bursts', 'camera particles/bursts/step',
    'camera particles/bursts/muzzles', 'camera particles/bursts/dispatches', 'camera particles/build', 'camera wait',
    'hud events', 'hud frame', 'hud paint', 'hud layout', 'HudChat', 'specgui', 'hud tools'))
$text = Get-Content $File
$rates = @($text | Where-Object { $_ -match '^\s*frame rate .* mean over (\d+) frames: (.*)$' })
function Parse([string]$line) {
  $null = $line -match 'mean over (\d+) frames: (.*)$'
  $h = @{ '#frames' = [double]$Matches[1] }
  foreach ($kv in ($Matches[2] -replace '; parts:', ',' -replace ' ms;', ',' -replace ' ms$', '') -split ', ') {
    if ($kv -match '^(.+?) (-?[\d.]+)( ms)?$') { $h[$Matches[1].Trim()] = [double]$Matches[2] }
  }
  $h
}
function Weighted($lines) {
  $sum = @{}; $n = 0
  foreach ($l in $lines) { $h = Parse $l; $f = $h['#frames']; $n += $f
    foreach ($k in $h.Keys) { $sum[$k] = $sum[$k] + ($h[$k] * $f) } }
  $out = @{ '#frames' = $n }; foreach ($k in $sum.Keys) { if ($k -ne '#frames') { $out[$k] = $sum[$k] / [math]::Max(1, $n) } }; $out
}
$first = Weighted ($rates | Select-Object -First $Warm)
$steady = Weighted ($rates | Select-Object -Skip $Warm)
$mean = [double](($text | Select-String 'measured .* frames, mean ([\d.]+) ms').Matches[0].Groups[1].Value)
$moments = @($text | Where-Object { $_ -match 'moment cost, mean over' } | Select-Object -Skip 1)
$ticks = ($text | Select-String 'ending at tick (\d+)').Matches[0].Groups[1].Value
$posed = ($moments | ForEach-Object { if ($_ -match 'posed ([\d.]+) of ([\d.]+) selected') { "$($Matches[1])/$($Matches[2])" } }) | Select-Object -Unique
"file $(Split-Path $File -Leaf): mean $mean ms; ending tick $ticks; steady frames $($steady['#frames']), warm frames $($first['#frames']); posed $($posed -join ',')"
$m = @{}
foreach ($l in $moments) { if ($l -match '= (.*)$') { foreach ($kv in $Matches[1] -split '[,;] ') { if ($kv -match '^(\w+) ([\d.]+)') { $m[$Matches[1]] += [double]$Matches[2] } } } }
"moment (mean of $($moments.Count)): " + (($('sample', 'drawlist', 'models', 'pose', 'simulate', 'collate') | ForEach-Object { "$_ {0:0.00}" -f ($m[$_] / [math]::Max(1, $moments.Count)) }) -join ', ')
foreach ($k in $Keys) {
  "{0,-38} steady {1,6:0.000} ms {2,5:0.0}%   warm {3,6:0.000}" -f $k, $steady[$k], (100 * $steady[$k] / $mean), $first[$k]
}
