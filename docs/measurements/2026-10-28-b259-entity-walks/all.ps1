# The committed order: a shot per build per view plus an A/A control (identity), mode-0 frame-hash runs A B A B, one
# mode-2 verifier run on B, ABBA+BAAB first person, ABBA+BAAB empty view, then a second shot pair. Shots and hash logs go
# to the scratch folder given. -From: the first measured run (1-16); -SkipIdentity skips the shots, hashes and verifier.
param([Parameter(Mandatory)][string]$ShotDir, [int]$From = 1, [switch]$SkipIdentity)
$run = Join-Path $PSScriptRoot 'run.ps1'
if (-not $SkipIdentity) {
  foreach ($m in 'empty', 'fp') { foreach ($b in 'A', 'B') { & $run -Build $b -Mode $m -Name "shot-$m-$b" -Shot "$ShotDir/shot-$m-$b.png" } }
  foreach ($m in 'empty', 'fp') { & $run -Build 'A' -Mode $m -Name "shot-$m-A2" -Shot "$ShotDir/shot-$m-A2.png" }
  # Frame hashes, mode 0, fixed frame time: the viewer logs one line per presented frame (`frame hash at <tick>: ...`).
  $env:TF2VIEW_FRAME_HASH = '1'; $env:TF2VIEW_FIXED_FRAME = '0.015'
  $i = 0
  foreach ($b in 'A', 'B', 'A', 'B') {
    $i++
    & $run -Build $b -Mode 'fp' -Name "hash-$i-$b" -Seconds 6 -Out $ShotDir -Extra @('+mat_queue_mode', '0') -Diagnostics
  }
  Remove-Item Env:TF2VIEW_FRAME_HASH
  $env:TF2VIEW_QUEUE_VERIFY = '1'
  & $run -Build 'B' -Mode 'fp' -Name 'verify-B' -Out $ShotDir -Diagnostics
  Remove-Item Env:TF2VIEW_QUEUE_VERIFY, Env:TF2VIEW_FIXED_FRAME
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
