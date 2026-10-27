# Reads one run as the plan's Method says: moment lines after the first (plain mean; slowest's peak is the max), the
# per-second rate lines after the first five (frame-weighted means of the parts and phases).
param([Parameter(Mandatory)][string]$File)
$ErrorActionPreference = 'Stop'
$inv = [Globalization.CultureInfo]::InvariantCulture
$lines = Get-Content $File
function Num([string]$s) { [double]::Parse($s, $inv) }

$moments = @($lines | Where-Object { $_ -match 'moment cost, mean over' } | Select-Object -Skip 1)
$m = @{}
$peak = 0
foreach ($l in $moments) {
  if ($l -notmatch 'setup ([\d.]+) \(roots ([\d.]+) over ([\d.]+) built, eligible ([\d.]+) over ([\d.]+), slowest ([\d.]+) peak ([\d.]+); children ([\d.]+) over ([\d.]+) built\)') { throw "no split in: $l" }
  $v = $Matches
  foreach ($k in @{ setup = 1; roots = 2; rootBuilds = 3; eligible = 4; eligibleBuilds = 5; slowest = 6; children = 8; childBuilds = 9 }.GetEnumerator()) {
    $m[$k.Key] += Num $v[$k.Value]
  }
  $peak = [Math]::Max($peak, (Num $v[7]))
  if ($l -notmatch 'built ([\d.]+) \(simulate [\d.]+, corpses [\d.]+, draw ([\d.]+), place') { throw "no built in: $l" }
  $m.built += Num $Matches[1]; $m.draw += Num $Matches[2]
  if ($l -match 'pose = lighting [\d.]+, simulate [\d.]+, wornlight [\d.]+, setup [^;]+; children [^)]+\), skin [\d.]+, anim ([\d.]+)') { $m.anim += Num $Matches[1] }
}
"moment lines read: $($moments.Count)"
foreach ($k in 'setup', 'roots', 'rootBuilds', 'eligible', 'eligibleBuilds', 'slowest', 'children', 'childBuilds', 'built', 'draw', 'anim') {
  '  {0,-15} {1:0.000}' -f $k, ($m[$k] / $moments.Count)
}
"  slowest peak    $peak"

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
foreach ($name in $order) { if ($name -match '^(sound|camera|advance|hud|draw|between|unaccounted|hud .*|HudChat|specgui|HudDeathNotice|KillStreakNotice|card\..*|camera pose|camera setcamera|camera particles)$') { '  {0,-24} {1:0.000}' -f $name, ($sum[$name] / $frames) } }
