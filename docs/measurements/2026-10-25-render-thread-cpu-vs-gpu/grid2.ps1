# Repeats after grid.ps1: 05-A and 09-A failed to launch (exe under a long scratch path; copied to D:), 12-M's after-load
# was 26 97 93 (a build this agent did not start), 13-M never passed the gate. Order: A M A M.
param([string]$Main = 'D:/measure-f12/b597-main077/tf2demoview.exe')
$o = $PSScriptRoot
$b = "$o/../../../managed/Tf2DemoSalvage.Viewer3D/bin/Release/net10.0-windows/tf2demoview.exe"
foreach ($name in '14-A', '15-M', '16-A', '17-M') {
  $exe = if ($name -like '*-A') { $Main } else { $b }
  "$name start $(Get-Date -Format HH:mm:ss)"
  & "$o/run.ps1" -Exe $exe -Out $o -Name $name -GateMinutes 30
  if ($LASTEXITCODE -eq 2) { "stopped: gate"; exit 2 }
}
