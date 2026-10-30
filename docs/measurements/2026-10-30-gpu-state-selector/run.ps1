# One reading, copied from 2026-10-30-sky3d-gpu-shift/run.ps1 (itself from 2026-10-30-b597-gpu-split/run.ps1, branch
# perf/b597-gpu-split) with condition switches added; the gate and the command are unchanged:
# the quiet gate (no other viewer, dotnet, testhost or MSBuild process; an idle MSBuild /nodemode:1 reuse node and
# VBCSCompiler are not counted; then three LoadPercentage samples under 25%), then the viewer under run-exclusive.
# Conditions (all default to the plain command):
#   -Mode n         +mat_queue_mode n (default 2)
#   -Pos "x,y"      TF2VIEW_WINDOW_POS
#   -Foreground     winsample.ps1 makes the viewer the foreground window and keeps it there
#   -Cover          winsample.ps1 puts a TopMost, never-activated form over the left half of the viewer
#   -ColdSeconds n  after the gate, also require n consecutive seconds with no tf2demoview process before launching
# Every run, whatever the condition: nvidia-smi samples the GPU every 500 ms ($Name.gpu.csv) from just before launch to
# exit, and winsample.ps1 records foreground and occlusion every 500 ms ($Name.win.csv). Stdout to $Name.txt.
# The console line says how long no tf2demoview had been seen before launch. Exit 2 = gate not met.
param([string]$Name, [string]$Exe, [int]$Seconds = 20, [int]$GateMinutes = 90, [string]$Out = $PSScriptRoot,
  [int]$Mode = 2, [string]$Pos = '', [switch]$Foreground, [switch]$Cover, [int]$ColdSeconds = 0)
$ErrorActionPreference = 'Stop'
if (-not (Test-Path $Exe)) { "no exe at $Exe"; exit 3 }
function Busy {
  @(Get-CimInstance Win32_Process | Where-Object {
      ($_.Name -eq 'tf2demoview.exe' -or $_.Name -match '^(dotnet|testhost|MSBuild)') -and
      $_.CommandLine -notmatch 'VBCSCompiler' -and $_.CommandLine -notmatch '/nodemode:1'
    }).Count -gt 0
}
function Viewer { @(Get-Process tf2demoview -ErrorAction SilentlyContinue).Count -gt 0 }
# Null when any processor gives no LoadPercentage: Measure-Object would average a null as 0, which reads as quiet.
function Load {
  $v = @(Get-CimInstance Win32_Processor | ForEach-Object { $_.LoadPercentage })
  if ($v.Count -eq 0 -or @($v | Where-Object { $null -eq $_ }).Count -gt 0) { return $null }
  ($v | Measure-Object -Average).Average
}
$lastExitFile = "$Out/last-exit.txt"
$lastSeen = if (Test-Path $lastExitFile) { [datetime](Get-Content $lastExitFile) } else { $null }
$deadline = (Get-Date).AddMinutes($GateMinutes)
while ($true) {
  if ((Get-Date) -gt $deadline) { "gate not met in $GateMinutes min"; exit 2 }
  if (Viewer) { $lastSeen = Get-Date }
  if (Busy) { Start-Sleep 10; continue }
  if ($ColdSeconds -gt 0 -and $null -ne $lastSeen -and ((Get-Date) - $lastSeen).TotalSeconds -lt $ColdSeconds) {
    Start-Sleep 5; continue
  }
  $l = 1..3 | ForEach-Object { Load; Start-Sleep 1 }
  if (@($l | Where-Object { $null -eq $_ -or $_ -ge 25 }).Count -eq 0) { break }
  Start-Sleep 5
}
$idle = if ($null -eq $lastSeen) { 'unknown' } else { [int]((Get-Date) - $lastSeen).TotalSeconds }
$cond = "mode $Mode; pos '$Pos'; fg $([int][bool]$Foreground); cover $([int][bool]$Cover); cold $ColdSeconds"
"$(Get-Date -Format s) $Name exe $Exe; $cond; no viewer seen for $idle s; load before: $($l -join ' ')"
$env:TF2VIEW_AUTOPLAY = '1'
$env:TF2VIEW_VIEWPORT_SIZE = '1280x720'
Remove-Item Env:TF2VIEW_CAMERA -ErrorAction SilentlyContinue
Remove-Item Env:TF2VIEW_MODEL_VERTS -ErrorAction SilentlyContinue
if ($Pos) { $env:TF2VIEW_WINDOW_POS = $Pos } else { Remove-Item Env:TF2VIEW_WINDOW_POS -ErrorAction SilentlyContinue }
$smi = Start-Process nvidia-smi -PassThru -NoNewWindow -RedirectStandardOutput "$Out/$Name.gpu.csv" -ArgumentList `
  '--query-gpu=timestamp,pstate,clocks.gr,clocks.mem,power.draw,utilization.gpu', '--format=csv', '-lms', '500'
$start = Get-Date
$ws = @('-NoProfile', '-File', "$PSScriptRoot/winsample.ps1", '-Exe', $Exe, '-After', $start.AddSeconds(-1).ToString('o'),
  '-Csv', "$Out/$Name.win.csv", '-WaitSeconds', "$($GateMinutes * 60)")
if ($Foreground) { $ws += '-Foreground' }
if ($Cover) { $ws += '-Cover' }
$side = Start-Process pwsh -ArgumentList $ws -PassThru -NoNewWindow
$a = @('-NoProfile', '-File', 'C:/Users/pinku/source/repos/PinKushin/run-exclusive.ps1', $Exe, 'D:/measure-f12/f12.dem',
  '--tick', '30000', '--spectate', 'gummo', '--first-person', '--measure', "$Seconds", '+fps_max', '0', '+mat_queue_mode', "$Mode")
$p = Start-Process pwsh -ArgumentList $a -PassThru -NoNewWindow -RedirectStandardOutput "$Out/$Name.txt" -RedirectStandardError "$Out/$Name.err"
$null = $p.Handle
$p.WaitForExit()
(Get-Date).ToString('o') | Set-Content $lastExitFile
$null = $side.WaitForExit(15000)
if (-not $side.HasExited) { Stop-Process -Id $side.Id }
Stop-Process -Id $smi.Id -ErrorAction SilentlyContinue
"exit $($p.ExitCode); load after: $((1..3 | ForEach-Object { Load; Start-Sleep 1 }) -join ' ')"
exit ($null -eq $p.ExitCode ? 1 : $p.ExitCode)
