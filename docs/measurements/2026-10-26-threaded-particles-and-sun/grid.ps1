# The grid in the plan, one viewer at a time, each behind run.ps1's quiet gate.
$A = 'D:/measure-f12/threaded-sun/tf2demoview.exe'
$C = 'D:/measure-f12/threaded-sun-bound/tf2demoview.exe'
$runs = @(
  @('01-C-S', $C, '0', '', $false), @('02-C-T', $C, '1', '', $false), @('03-A-T', $A, '1', '', $false),
  @('04-C-T', $C, '1', '', $false), @('05-C-S', $C, '0', '', $false), @('06-A-T', $A, '1', '', $false),
  @('07-C-S', $C, '0', '', $false), @('08-C-T', $C, '1', '', $false), @('09-A-T', $A, '1', '', $false),
  @('10-C-T0', $C, '1', '0', $false), @('11-C-T16', $C, '1', '16', $false),
  @('12-C-E', $C, '1', '', $true), @('13-A-E', $A, '1', '', $true), @('14-A-E', $A, '1', '', $true), @('15-C-E', $C, '1', '', $true))
foreach ($r in $runs) {
  $args3 = @{ Name = $r[0]; Exe = $r[1]; Threaded = $r[2]; Threshold = $r[3] }
  if ($r[4]) { $args3.Empty = $true }
  "$($r[0]) $(Get-Date -Format HH:mm:ss): $(& "$PSScriptRoot/run.ps1" @args3)"
}
