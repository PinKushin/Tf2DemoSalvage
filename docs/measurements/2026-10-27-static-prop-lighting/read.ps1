# Reads every run file named on the command line (default: 0?-*.txt, E?-*.txt here): moment lines after the first
# (plain mean of `baked`, `pose`, `posed`, `built`), rate lines after the first five (frame-weighted means of the frame
# time and of `sound`, `between`, `camera pose`). One row a run.
param([string[]]$Files = @(Get-ChildItem $PSScriptRoot -Filter '*.txt' | Where-Object Name -match '^(0\d|E\d)-' | ForEach-Object FullName))
$ErrorActionPreference = 'Stop'
$inv = [Globalization.CultureInfo]::InvariantCulture
function Num([string]$s) { [double]::Parse($s, $inv) }
'{0,-6} {1,7} {2,7} {3,7} {4,7} {5,7} {6,7} {7,7} {8,6} {9,6}' -f 'run', 'frame', 'baked', 'pose', 'c.pose', 'sound', 'between', 'posed', 'built', 'lines'
foreach ($file in $Files) {
  $lines = Get-Content $file
  $moments = @($lines | Where-Object { $_ -match 'moment cost, mean over' } | Select-Object -Skip 1)
  $m = @{ baked = 0.0; pose = 0.0; posed = 0.0; built = 0.0 }
  foreach ($l in $moments) {
    if ($l -notmatch ', baked ([\d.]+), place') { throw "no baked in: $l" }
    $m.baked += Num $Matches[1]
    if ($l -match ' pose ([\d.]+), weapons') { $m.pose += Num $Matches[1] }
    if ($l -match 'posed ([\d.]+) of') { $m.posed += Num $Matches[1] }
    if ($l -match 'built ([\d.]+) \(') { $m.built += Num $Matches[1] }
  }
  $rates = @($lines | Where-Object { $_ -match '^\s+frame rate .* mean over (\d+) frames: (.*)$' } | Select-Object -Skip 5)
  $sum = @{}; $frames = 0
  foreach ($l in $rates) {
    $null = $l -match ', ([\d.]+) ms; mean over (\d+) frames: (.*)$'
    $n = [int]$Matches[2]; $frames += $n; $sum.frame += (Num $Matches[1]) * $n
    $body = $Matches[3] -replace '; parts: ', ', ' -replace ' ms;', ','
    foreach ($pair in ($body -split ', ')) {
      if ($pair -match '^(.+) (-?[\d.]+)( ms)?$') { $sum[$Matches[1]] += (Num $Matches[2]) * $n }
    }
  }
  $c = [Math]::Max(1, $moments.Count)
  # The frame is the window's own mean (`measured … frames, mean N ms`), not a mean of the per-second lines.
  $head = $lines | Where-Object { $_ -match '^measured .* frames, mean [\d.]+ ms' } | Select-Object -First 1
  if ($head -notmatch 'frames, mean ([\d.]+) ms') { throw "no window mean in $file" }
  $sum.frame = (Num $Matches[1]) * $frames
  # `sound` and `between` from the window's own phase line, frame-weighted by the viewer.
  $phases = $lines | Where-Object { $_ -match '^\s+phases, mean ms:' } | Select-Object -First 1
  if ($phases -notmatch 'sound ([\d.]+),.* between ([\d.]+)') { throw "no phase line in $file" }
  $sum.sound = (Num $Matches[1]) * $frames; $sum.between = (Num $Matches[2]) * $frames
  '{0,-6} {1,7:0.000} {2,7:0.000} {3,7:0.000} {4,7:0.000} {5,7:0.000} {6,7:0.000} {7,7:0.0} {8,6:0.0} {9,6}' -f `
    ([IO.Path]::GetFileNameWithoutExtension($file)), ($sum.frame / $frames), ($m.baked / $c), ($m.pose / $c),
    ($sum['camera pose'] / $frames), ($sum.sound / $frames), ($sum.between / $frames), ($m.posed / $c), ($m.built / $c), $moments.Count
}
