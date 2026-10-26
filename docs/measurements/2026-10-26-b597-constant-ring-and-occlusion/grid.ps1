# The order actually run (B597, 2026-10-26). A = c0584951 (spans, old constants, old occlusion), B = this branch,
# R = B with TF2VIEW_CONSTANT_RING=0 (old constants, new occlusion). One reading each through the 2026-10-25 run.ps1.
param([string]$A = 'D:/b597r/A/managed/Tf2DemoSalvage.Viewer3D/bin/Release/net10.0-windows/tf2demoview.exe',
      [string]$B, [string[]]$Order = @('A', 'B', 'R', 'A', 'B', 'R', 'A', 'B', 'R'), [int]$Start = 1)
$ErrorActionPreference = 'Stop'
$out = $PSScriptRoot
$run = "$PSScriptRoot/../2026-10-25-render-thread-cpu-vs-gpu/run.ps1"
$n = $Start
foreach ($c in $Order) {
  $name = '{0:00}-{1}' -f $n, $c
  if ($c -eq 'R') { $env:TF2VIEW_CONSTANT_RING = '0' } else { Remove-Item Env:TF2VIEW_CONSTANT_RING -ErrorAction SilentlyContinue }
  $exe = if ($c -eq 'A') { $A } else { $B }
  "$name $(Get-Date -Format HH:mm:ss)"
  & $run -Exe $exe -Out $out -Name $name | ForEach-Object { "  $_" }
  $n++
}
Remove-Item Env:TF2VIEW_CONSTANT_RING -ErrorAction SilentlyContinue
