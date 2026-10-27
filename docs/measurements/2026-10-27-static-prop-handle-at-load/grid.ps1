# The plan's grid, one viewer at a time behind the quiet gate of ../2026-10-27-static-prop-lighting/run.ps1 (reused, not
# copied); stops if a gate is not met. Outputs land beside this script.
$run = "$PSScriptRoot/../2026-10-27-static-prop-lighting/run.ps1"
$A = 'D:/measure-f12/hal-after/tf2demoview.exe'
$B = 'D:/measure-f12/hal-before/tf2demoview.exe'
$runs = @(
  @('01-A', $A, ''), @('02-B', $B, ''), @('03-B', $B, ''), @('04-A', $A, ''),
  @('05-B', $B, ''), @('06-A', $A, ''), @('07-A', $A, ''), @('08-B', $B, ''),
  @('S1-B', $B, 's'), @('S2-B', $B, 's'), @('S3-A', $A, 's'))
foreach ($r in $runs) {
  $extra = @{}
  if ($r[2] -eq 's') { $extra.Shot = "$PSScriptRoot/$($r[0]).png"; $extra.Tick = 30600 }
  "$($r[0]) $(Get-Date -Format HH:mm:ss): $(& $run -Out $PSScriptRoot -Name $r[0] -Exe $r[1] @extra)"

  if ($LASTEXITCODE -eq 2) { "stopped: $($r[0]) never ran"; break }
}
