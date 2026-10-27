# The plan's grid, one viewer at a time behind run.ps1's quiet gate; stops if a gate is not met.
$run = "$PSScriptRoot/run.ps1"
$A = 'D:/measure-f12/spl-after/tf2demoview.exe'
$B = 'D:/measure-f12/spl-before/tf2demoview.exe'
$runs = @(
  @('01-A', $A, ''), @('02-B', $B, ''), @('03-B', $B, ''), @('04-A', $A, ''),
  @('05-B', $B, ''), @('06-A', $A, ''), @('07-A', $A, ''), @('08-B', $B, ''),
  @('E1-A', $A, 'e'), @('E2-B', $B, 'e'), @('E3-B', $B, 'e'), @('E4-A', $A, 'e'),
  @('S1-B', $B, 's'), @('S2-B', $B, 's'), @('S3-A', $A, 's'))
foreach ($r in $runs) {
  $extra = @{}
  if ($r[2] -eq 'e') { $extra.Empty = $true }
  if ($r[2] -eq 's') { $extra.Shot = "$PSScriptRoot/$($r[0]).png"; $extra.Tick = 30600 }
  "$($r[0]) $(Get-Date -Format HH:mm:ss): $(& $run -Out $PSScriptRoot -Name $r[0] -Exe $r[1] @extra)"

  if ($LASTEXITCODE -eq 2) { "stopped: $($r[0]) never ran"; break }
}
