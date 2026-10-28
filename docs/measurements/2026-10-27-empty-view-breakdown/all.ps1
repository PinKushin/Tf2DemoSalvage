# The whole committed plan, in order: a shot per build (identity, emptiness), ABBA then BAAB empty-view runs, then one
# first-person run per build. Shots go to the scratch folder given; their hashes are what is kept.
# -From: the first measured run to make (1-8), to resume after a stop; shots are taken only from 1.
param([Parameter(Mandatory)][string]$ShotDir, [int]$From = 1)
$run = Join-Path $PSScriptRoot 'run.ps1'
if ($From -le 1) {
  & $run -Build A -Name shot-A -Shot "$ShotDir/shot-A.png"
  & $run -Build B -Name shot-B -Shot "$ShotDir/shot-B.png"
}
$i = 1
foreach ($b in 'A', 'B', 'B', 'A', 'B', 'A', 'A', 'B') {
  if ($i -ge $From) { & $run -Build $b -Name ("{0:00}-{1}" -f $i, $b) }
  $i++
}
& $run -Build A -Name shot-A2 -Shot "$ShotDir/shot-A2.png"
& $run -Build B -Name shot-B2 -Shot "$ShotDir/shot-B2.png"
& $run -Build A -Name shot-A3 -Shot "$ShotDir/shot-A3.png"
& $run -Build A -Mode fp -Name fp-A
& $run -Build B -Mode fp -Name fp-B
"done $(Get-Date -Format s)"
