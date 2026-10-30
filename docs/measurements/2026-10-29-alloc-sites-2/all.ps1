# One fix's queue, A against B (build labels, see run.ps1): shots per view for A, B and a second A (identity and its
# control), one mode-2 verifier run on B, ABBA BAAB first person, ABBA BAAB empty, then one traced B run per view for the
# attribution (as the census). Output and shots go to D:/measure-f12/alloc2/runs/<Tag>/.
param([Parameter(Mandatory)][string]$A, [Parameter(Mandatory)][string]$B, [Parameter(Mandatory)][string]$Tag,
  [switch]$SkipIdentity, [switch]$SkipTrace, [string[]]$Modes = @('fp', 'empty'))
$run = Join-Path $PSScriptRoot 'run.ps1'
$out = "D:/measure-f12/alloc2/runs/$Tag"
New-Item -ItemType Directory -Force $out | Out-Null
if (-not $SkipIdentity) {
  foreach ($m in 'empty', 'fp') {
    foreach ($pair in @(@($A, 'A'), @($B, 'B'), @($A, 'A2'))) {
      & $run -Build $pair[0] -Mode $m -Name "shot-$m-$($pair[1])" -Shot "$out/shot-$m-$($pair[1]).png" -Out $out
    }
  }
  $env:TF2VIEW_QUEUE_VERIFY = '1'
  & $run -Build $B -Mode 'fp' -Name 'verify-B' -Out $out -Diagnostics
  Remove-Item Env:TF2VIEW_QUEUE_VERIFY
}
$i = 1
# NOTE: PowerShell names are case-insensitive — a loop variable named `$b` IS the `-B` parameter. It was, until
# 2026-10-30 02:05, which sent every B run to the label "B" (as2-B-src, the same folder as as2-b-src on Windows): right
# for item 1 by luck, wrong for any later pair. Hence `$side`.
foreach ($m in $Modes) {
  foreach ($side in 'A', 'B', 'B', 'A', 'B', 'A', 'A', 'B') {
    & $run -Build ($side -eq 'A' ? $A : $B) -Mode $m -Name ("{0:00}-{1}-{2}" -f $i, $m, $side) -Out $out
    $i++
  }
}
if (-not $SkipTrace) {
  foreach ($m in 'fp', 'empty') {
    & $run -Build $B -Mode $m -Name "trace-$m-B" -Seconds 120 -Out $out -Trace "$out/trace-$m-B.nettrace"
  }
}
"done $(Get-Date -Format s)"
