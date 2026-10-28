# The committed order: a shot per build per view (identity), ABBA+BAAB first person (the measured view), ABBA+BAAB in
# the empty view (the control: nothing posed, so nothing should move), then a second shot pair. Shots go to the scratch
# folder given. -From: the first measured run (1-16).
param([Parameter(Mandatory)][string]$ShotDir, [int]$From = 1)
$run = Join-Path $PSScriptRoot 'run.ps1'
if ($From -le 1) {
  foreach ($m in 'empty', 'fp') { foreach ($b in 'A', 'B') { & $run -Build $b -Mode $m -Name "shot-$m-$b" -Shot "$ShotDir/shot-$m-$b.png" } }
  foreach ($m in 'empty', 'fp') { & $run -Build 'A' -Mode $m -Name "shot-$m-A2" -Shot "$ShotDir/shot-$m-A2.png" }
}
$i = 1
foreach ($m in 'fp', 'empty') {
  foreach ($b in 'A', 'B', 'B', 'A', 'B', 'A', 'A', 'B') {
    if ($i -ge $From) { & $run -Build $b -Mode $m -Name ("{0:00}-{1}-{2}" -f $i, $m, $b) }
    $i++
  }
}
foreach ($m in 'empty', 'fp') { foreach ($b in 'A', 'B') { & $run -Build $b -Mode $m -Name "shot2-$m-$b" -Shot "$ShotDir/shot2-$m-$b.png" } }
"done $(Get-Date -Format s)"
