# grid2's runs (it waited 30 min on idle reused MSBuild nodes this agent did not start, 20:35-21:05), then the empty
# view with a free camera (19/20-F): under --first-person TF2VIEW_CAMERA was ignored (04-E, 11-E drew M's scene).
param([string]$Main = 'D:/measure-f12/b597-main077/tf2demoview.exe')
$o = $PSScriptRoot
$b = "$o/../../../managed/Tf2DemoSalvage.Viewer3D/bin/Release/net10.0-windows/tf2demoview.exe"
foreach ($name in '14-A', '15-M', '16-A', '17-M', '18-F', '19-F') {
  $exe = if ($name -like '*-A') { $Main } else { $b }
  "$name start $(Get-Date -Format HH:mm:ss)"
  if ($name -like '*-F') { & "$o/run.ps1" -Exe $exe -Out $o -Name $name -Camera '1613 -2821 154 89 0' -Free -GateMinutes 20 }
  else { & "$o/run.ps1" -Exe $exe -Out $o -Name $name -GateMinutes 20 }
  if ($LASTEXITCODE -eq 2) { "stopped: gate"; exit 2 }
}
