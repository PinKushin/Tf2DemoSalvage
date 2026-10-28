# Added after the grid, not in the plan: one more ABBA to replace runs 01-06, which failed the machine control.
$run = "$PSScriptRoot/../2026-10-27-static-prop-lighting/run.ps1"
$A = 'D:/measure-f12/hal-after/tf2demoview.exe'
$B = 'D:/measure-f12/hal-before/tf2demoview.exe'
foreach ($r in @(@('09-A', $A), @('10-B', $B), @('11-B', $B), @('12-A', $A))) {
  "$($r[0]) $(Get-Date -Format HH:mm:ss): $(& $run -Out $PSScriptRoot -Name $r[0] -Exe $r[1])"

  if ($LASTEXITCODE -eq 2) { "stopped: $($r[0]) never ran"; break }
}
