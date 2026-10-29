# One reading, as 2026-10-28-double-buffer-pose/run.ps1 with this record's builds: the quiet gate (no other viewer,
# dotnet, testhost or MSBuild process; an idle MSBuild /nodemode:1 reuse node and VBCSCompiler are not counted; then three
# LoadPercentage samples under 25%), then the viewer under run-exclusive. Stdout -> $Out/$Name.txt. Exit 2 = gate not met.
# -Build A (before) / B (after) / P (the throwaway split probe). -Mode empty: the tf2-measure empty view (camera
# override, no --first-person). -Mode fp: f12 gummo first person. -Shot <png>: a paused --shot of the same view instead.
# -Diagnostics keeps TF2VIEW_FRAME_HASH / TF2VIEW_QUEUE_VERIFY / TF2VIEW_FIXED_FRAME as the caller set them (the identity
# runs); without it they are cleared, so a timed run never carries one. -Extra: more viewer arguments.
param([ValidateSet('A', 'B', 'P')][string]$Build, [ValidateSet('empty', 'fp')][string]$Mode = 'empty', [string]$Name,
  [string]$Shot = '', [int]$Seconds = 20, [int]$GateMinutes = 90, [string]$Out = $PSScriptRoot, [string[]]$Extra = @(),
  [switch]$Diagnostics)
$ErrorActionPreference = 'Stop'
$exe = @{ A = 'D:/measure-f12/b259-before/tf2demoview.exe'; B = 'D:/measure-f12/b259-after/tf2demoview.exe';
  P = 'D:/measure-f12/b259-probe/tf2demoview.exe' }[$Build]
function Busy {
  @(Get-CimInstance Win32_Process | Where-Object {
      ($_.Name -eq 'tf2demoview.exe' -or $_.Name -match '^(dotnet|testhost|MSBuild)') -and
      $_.CommandLine -notmatch 'VBCSCompiler' -and $_.CommandLine -notmatch '/nodemode:1'
    }).Count -gt 0
}
function Load { (Get-CimInstance Win32_Processor | Measure-Object LoadPercentage -Average).Average }
$deadline = (Get-Date).AddMinutes($GateMinutes)
while ($true) {
  if ((Get-Date) -gt $deadline) { "gate not met in $GateMinutes min"; exit 2 }
  if (Busy) { Start-Sleep 10; continue }
  $l = 1..3 | ForEach-Object { Load; Start-Sleep 1 }
  if (($l | Where-Object { $_ -ge 25 }).Count -eq 0) { break }
  Start-Sleep 5
}
"$(Get-Date -Format s) $Name build $Build mode $Mode; load before: $($l -join ' ')"
if ($Shot) { Remove-Item Env:TF2VIEW_AUTOPLAY -ErrorAction SilentlyContinue } else { $env:TF2VIEW_AUTOPLAY = '1' }
$env:TF2VIEW_VIEWPORT_SIZE = '1280x720'
if (-not $Diagnostics) { Remove-Item Env:TF2VIEW_QUEUE_VERIFY, Env:TF2VIEW_FIXED_FRAME, Env:TF2VIEW_FRAME_HASH -ErrorAction SilentlyContinue }
$a = @('-NoProfile', '-File', 'C:/Users/pinku/source/repos/PinKushin/run-exclusive.ps1', $exe, 'D:/measure-f12/f12.dem', '--tick', '30000')
if ($Mode -eq 'empty') { $env:TF2VIEW_CAMERA = '1613 -2821 154 89 0' } else {
  Remove-Item Env:TF2VIEW_CAMERA -ErrorAction SilentlyContinue; $a += @('--spectate', 'gummo', '--first-person') }
if ($Shot) { $a += @('--shot', $Shot) } else { $a += @('--measure', "$Seconds", '+fps_max', '0') }
$a += $Extra
$p = Start-Process pwsh -ArgumentList $a -PassThru -NoNewWindow -RedirectStandardOutput "$Out/$Name.txt" -RedirectStandardError "$Out/$Name.err"
$p.WaitForExit()
# The hash and verifier lines go to the viewer's own log, one file per process: keep the newest beside the stdout.
if ($Diagnostics) {
  Get-ChildItem "$env:LOCALAPPDATA/Tf2DemoSalvage" -Filter 'viewer-*.log' | Sort-Object LastWriteTime -Descending |
    Select-Object -First 1 | Copy-Item -Destination "$Out/$Name.log"
}
"exit $($p.ExitCode); load after: $((1..3 | ForEach-Object { Load; Start-Sleep 1 }) -join ' ')"
