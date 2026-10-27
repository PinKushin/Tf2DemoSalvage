# One reading: quiet gate (three LoadPercentage samples under 25%, no other viewer/dotnet/MSBuild/testhost), then the viewer
# under run-exclusive. Stdout -> $Out/$Name.txt. Exit 2 = gate not met within $GateMinutes. Extra viewer args after -Extra.
# VBCSCompiler is not in the list: the machine's shared compiler server idles for minutes after any agent's build, and an
# active compile shows in the load samples anyway.
param([string]$Exe = 'D:/measure-f12/offthread-a71/tf2demoview.exe', [string]$Out = $PSScriptRoot, [string]$Name,
  [int]$Seconds = 60, [int]$GateMinutes = 20, [string[]]$Extra = @())
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
$a = @('-NoProfile', '-File', 'C:/Users/pinku/source/repos/PinKushin/run-exclusive.ps1', $Exe, 'D:/measure-f12/f12.dem', '--tick', '30000',
  '--measure', "$Seconds", '+fps_max', '0', '+r_threaded_particles', '1', '--spectate', 'gummo', '--first-person') + $Extra
$p = Start-Process pwsh -ArgumentList $a -PassThru -NoNewWindow -RedirectStandardOutput "$Out/$Name.txt" -RedirectStandardError "$Out/$Name.err"
$p.WaitForExit()
"exit $($p.ExitCode); load after: $((1..3 | ForEach-Object { Load; Start-Sleep 1 }) -join ' ')"
