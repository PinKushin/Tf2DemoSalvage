# One reading: quiet gate, the viewer under run-exclusive, finding 100's thread sampler beside it.
# Stdout -> $Out/$Name.txt, sampler -> $Out/$Name.csv. Exit 2 = gate not met within $GateMinutes.
param([string]$Exe, [string]$Out, [string]$Name, [string]$Size = '1280x720', [string]$Camera, [int]$GateMinutes = 20)
$ErrorActionPreference = 'Stop'
function Busy { @(Get-CimInstance Win32_Process | Where-Object { $_.Name -eq 'tf2demoview.exe' -or $_.CommandLine -match 'run-one|--measure ' }).Count -gt 0 }
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
$env:TF2VIEW_AUTOPLAY = '1'; $env:TF2VIEW_VIEWPORT_SIZE = $Size
if ($Camera) { $env:TF2VIEW_CAMERA = $Camera } else { Remove-Item Env:TF2VIEW_CAMERA -ErrorAction SilentlyContinue }
$args2 = @('-NoProfile', '-File', 'C:/Users/pinku/source/repos/PinKushin/run-exclusive.ps1', $Exe, 'D:/measure-f12/f12.dem', '--tick', '30000',
  '--spectate', 'gummo', '--first-person', '--measure', '20', '+fps_max', '0', '+mat_queue_mode', '2')
$p = Start-Process pwsh -ArgumentList $args2 -PassThru -NoNewWindow -RedirectStandardOutput "$Out/$Name.txt" -RedirectStandardError "$Out/$Name.err"
$full = (Resolve-Path $Exe).Path
$v = $null
while (-not $p.HasExited -and -not $v) { $v = Get-CimInstance Win32_Process | Where-Object { $_.ExecutablePath -eq $full } | Select-Object -First 1; Start-Sleep 1 }
if ($v) { $s = Start-Process pwsh -ArgumentList '-NoProfile', '-File', "$PSScriptRoot/../2026-10-22-viewer-helper-threads/sample-threads.ps1", '-ProcessId', $v.ProcessId, '-Out', "$Out/$Name.csv" -PassThru -NoNewWindow }
$p.WaitForExit(); if ($s) { $s.WaitForExit() }
"exit $($p.ExitCode); load after: $((1..3 | ForEach-Object { Load; Start-Sleep 1 }) -join ' ')"
