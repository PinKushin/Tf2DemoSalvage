# The nine runs, in the order the record states: N V3 V0, V0 V3 N, N V0 V3. A run that misses the quiet gate or exits
# non-zero stops the sequence, so a gap is visible rather than silently skipped.
$plan = @(
  @('01-N', ''), @('02-V3', '3'), @('03-V0', '0'),
  @('04-V0', '0'), @('05-V3', '3'), @('06-N', ''),
  @('07-N', ''), @('08-V0', '0'), @('09-V3', '3')
)
foreach ($p in $plan) {
  & "$PSScriptRoot/run.ps1" -Name $p[0] -Verts $p[1]
  if ($LASTEXITCODE -ne 0) { "stopped at $($p[0]), exit $LASTEXITCODE"; exit 1 }
}
'all nine done'
