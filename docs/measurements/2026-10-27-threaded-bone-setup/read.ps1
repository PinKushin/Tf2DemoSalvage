# Reads one run as the plan's Method says: moment lines after the first (plain mean; slowest's peak is the max), the
# per-second rate lines after the first five (frame-weighted means), and the last census note's bone and particle forks.
param([Parameter(Mandatory)][string]$File)
$ErrorActionPreference = 'Stop'
$inv = [Globalization.CultureInfo]::InvariantCulture
$lines = Get-Content $File
function Num([string]$s) { [double]::Parse($s, $inv) }

$moments = @($lines | Where-Object { $_ -match 'moment cost, mean over' } | Select-Object -Skip 1)
$m = @{}
foreach ($l in $moments) {
  if ($l -notmatch 'setup ([\d.]+) \(roots ([\d.]+) over ([\d.]+) built, eligible ([\d.]+) over ([\d.]+), slowest ([\d.]+) peak [\d.]+; children ([\d.]+) over ([\d.]+) built; prepose ([\d.]+) over ([\d.]+), blend ([\d.]+)\)') { throw "no split in: $l" }
  $v = $Matches
  $names = 'setup', 'roots', 'rootBuilds', 'eligible', 'eligibleBuilds', 'slowest', 'children', 'childBuilds', 'prepose', 'prePosed', 'blend'
  for ($i = 0; $i -lt $names.Count; $i++) { $m[$names[$i]] += Num $v[$i + 1] }
  if ($l -notmatch 'built ([\d.]+) \(simulate [\d.]+, corpses [\d.]+, draw ([\d.]+), place') { throw "no built in: $l" }
  $m.built += Num $Matches[1]; $m.draw += Num $Matches[2]
  if ($l -match ' pose ([\d.]+), weapons') { $m.pose += Num $Matches[1] }
}
"moment lines read: $($moments.Count)"
foreach ($k in 'pose', 'setup', 'roots', 'rootBuilds', 'eligible', 'eligibleBuilds', 'slowest', 'children', 'childBuilds', 'prepose', 'prePosed', 'blend', 'built', 'draw') {
  '  {0,-15} {1:0.000}' -f $k, ($m[$k] / $moments.Count)
}

$rates = @($lines | Where-Object { $_ -match '^\s+frame rate .* mean over (\d+) frames: (.*)$' } | Select-Object -Skip 5)
$sum = @{}; $order = [Collections.Generic.List[string]]::new(); $frames = 0
foreach ($l in $rates) {
  $null = $l -match 'mean over (\d+) frames: (.*)$'
  $n = [int]$Matches[1]; $frames += $n
  $body = $Matches[2] -replace '; parts: ', ', ' -replace ' ms;', ','
  foreach ($pair in ($body -split ', ')) {
    if ($pair -match '^(.+) (-?[\d.]+)( ms)?$') {
      $name = $Matches[1]; if (-not $sum.ContainsKey($name)) { $sum[$name] = 0.0; $order.Add($name) }
      $sum[$name] += (Num $Matches[2]) * $n
    }
  }
}
"rate lines read: $($rates.Count), frames $frames"
foreach ($name in $order) { if ($name -match '^(camera|camera pose|camera particles|camera particles/bursts/step|hud)$') { '  {0,-30} {1:0.000}' -f $name, ($sum[$name] / $frames) } }
$census = @($lines | Where-Object { $_ -match 'bone forks so far' })
if ($census.Count -gt 0) {
  $null = $census[-1] -match 'particle forks so far (\d+); bone forks so far (\d+)'
  "  census notes $($census.Count); last: particle forks $($Matches[1]), bone forks $($Matches[2])"
  $null = $census[0] -match 'bone forks so far (\d+)'
  "  first note's bone forks $($Matches[1])"
}
