# One reading: quiet gate (three LoadPercentage samples under 25%, no other viewer/dotnet/MSBuild/testhost), then the viewer
# under run-exclusive. Stdout -> $Out/$Name.txt. Exit 2 = gate not met within $GateMinutes.
# -Bones is cl_threaded_bone_setup; -Threshold, when given, is TF2VIEW_BONE_THRESHOLD.
param([string]$Exe = 'D:/measure-f12/threaded-bones/tf2demoview.exe', [string]$Out = $PSScriptRoot, [string]$Name,
  [string]$Bones = '0', [string]$Threshold = '', [int]$Seconds = 60, [int]$GateMinutes = 20)
$ErrorActionPreference = 'Stop'
function Busy { @(Get-CimInstance Win32_Process | Where-Object { $_.Name -eq 'tf2demoview.exe' -or $_.Name -match '^(dotnet|testhost|MSBuild)' }).Count -gt 0 }
function Load { (Get-CimInstance Win32_Processor | Measure-Object LoadPercentage -Average).Average }
$deadline = (Get-Date).AddMinutes($GateMinutes)
while ($true) {
  if ((Get-Date) -gt $deadline) { "gate not met in $GateMinutes min"; exit 2 }
  if (Busy) { Start-Sleep 10; continue }
  $l = 1..3 | ForEach-Object { Load; Start-Sleep 1 }
  if (($l | Where-Object { $_ -ge 25 }).Count -eq 0) { break }
  Start-Sleep 5
}
"load before: $($l -join ' ')"
$env:TF2VIEW_AUTOPLAY = '1'; $env:TF2VIEW_VIEWPORT_SIZE = '1280x720'
Remove-Item Env:TF2VIEW_CAMERA -ErrorAction SilentlyContinue
if ($Threshold) { $env:TF2VIEW_BONE_THRESHOLD = $Threshold } else { Remove-Item Env:TF2VIEW_BONE_THRESHOLD -ErrorAction SilentlyContinue }
$a = @('-NoProfile', '-File', 'C:/Users/pinku/source/repos/PinKushin/run-exclusive.ps1', $Exe, 'D:/measure-f12/f12.dem', '--tick', '30000',
  '--measure', "$Seconds", '+fps_max', '0', '+r_threaded_particles', '1', '+cl_threaded_bone_setup', $Bones,
  '--spectate', 'gummo', '--first-person')
$p = Start-Process pwsh -ArgumentList $a -PassThru -NoNewWindow -RedirectStandardOutput "$Out/$Name.txt" -RedirectStandardError "$Out/$Name.err"
$p.WaitForExit()
Remove-Item Env:TF2VIEW_BONE_THRESHOLD -ErrorAction SilentlyContinue
"exit $($p.ExitCode); load after: $((1..3 | ForEach-Object { Load; Start-Sleep 1 }) -join ' ')"
