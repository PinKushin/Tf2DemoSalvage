# Added after the first grid (not in the committed plan): 01, 03 and 04 failed the machine control (`sound` 0.23-0.33
# against 0.16-0.17, `between` 0.41-0.53 against 0.31-0.34), so one more ABBA, same command, to replace them.
$run = "$PSScriptRoot/run.ps1"
$A = 'D:/measure-f12/spl-after/tf2demoview.exe'
$B = 'D:/measure-f12/spl-before/tf2demoview.exe'
foreach ($r in @(@('09-A', $A), @('10-B', $B), @('11-B', $B), @('12-A', $A))) {
  "$($r[0]) $(Get-Date -Format HH:mm:ss): $(& $run -Out $PSScriptRoot -Name $r[0] -Exe $r[1])"

  if ($LASTEXITCODE -eq 2) { "stopped: $($r[0]) never ran"; break }
}
