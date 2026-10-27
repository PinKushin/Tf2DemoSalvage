# The timing grid in the plan, one viewer at a time, each behind run.ps1's quiet gate. S = off, P = on and serial, T = forked.
$runs = @(
  @('01-S', '0', ''), @('02-P', '1', '1000'), @('03-T', '1', ''), @('04-T', '1', ''),
  @('05-P', '1', '1000'), @('06-S', '0', ''), @('07-S', '0', ''), @('08-T', '1', ''))
foreach ($r in $runs) {
  "$($r[0]) $(Get-Date -Format HH:mm:ss): $(& "$PSScriptRoot/run.ps1" -Name $r[0] -Bones $r[1] -Threshold $r[2])"

  # A run whose gate was not met breaks the ABBA order; stop rather than run the rest out of balance (added after 01 and 02).
  if ($LASTEXITCODE -eq 2) { "stopped: $($r[0]) never ran"; break }
}
