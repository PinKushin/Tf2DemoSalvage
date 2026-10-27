# The order run (B597, 2026-10-26). M = c0584951 (whole streams), R = e19d6733 (the flexed range).
# One reading each through the 2026-10-25 run.ps1 (its quiet gate and command). -Verify adds TF2VIEW_QUEUE_VERIFY=1.
param([string]$M = 'D:/b597f/M/managed/Tf2DemoSalvage.Viewer3D/bin/Release/net10.0-windows/tf2demoview.exe',
      [string]$R = 'D:/b597f/R/managed/Tf2DemoSalvage.Viewer3D/bin/Release/net10.0-windows/tf2demoview.exe',
      [string[]]$Order = @('M', 'R', 'R', 'M', 'R', 'M', 'M', 'R'), [int]$Start = 1, [switch]$Verify)
$ErrorActionPreference = 'Stop'
$run = "$PSScriptRoot/../2026-10-25-render-thread-cpu-vs-gpu/run.ps1"
if ($Verify) { $env:TF2VIEW_QUEUE_VERIFY = '1' } else { Remove-Item Env:TF2VIEW_QUEUE_VERIFY -ErrorAction SilentlyContinue }
$n = $Start
foreach ($c in $Order) {
  $name = '{0:00}-{1}{2}' -f $n, $c, $(if ($Verify) { '-verify' } else { '' })
  "$name $(Get-Date -Format HH:mm:ss)"
  & $run -Exe $(if ($c -eq 'M') { $M } else { $R }) -Out $PSScriptRoot -Name $name | ForEach-Object { "  $_" }
  $n++
}
Remove-Item Env:TF2VIEW_QUEUE_VERIFY -ErrorAction SilentlyContinue
