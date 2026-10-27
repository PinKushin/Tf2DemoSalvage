# The timing grid in the plan, one viewer at a time behind the last file's quiet gate; stops if a gate is not met.
$run = "$PSScriptRoot/../2026-10-27-threaded-bone-setup/run.ps1"
$O = 'D:/measure-f12/threaded-bones/tf2demoview.exe'
$N = 'D:/measure-f12/bone-clock/tf2demoview.exe'
$runs = @(
  @('01-NS', $N, '0'), @('02-NT', $N, '1'), @('03-O', $O, '0'), @('04-O', $O, '0'),
  @('05-NT', $N, '1'), @('06-NS', $N, '0'), @('07-NS', $N, '0'), @('08-NT', $N, '1'))
foreach ($r in $runs) {
  "$($r[0]) $(Get-Date -Format HH:mm:ss): $(& $run -Out $PSScriptRoot -Name $r[0] -Exe $r[1] -Bones $r[2])"

  if ($LASTEXITCODE -eq 2) { "stopped: $($r[0]) never ran"; break }
}
