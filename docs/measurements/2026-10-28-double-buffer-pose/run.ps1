# One reading, as 2026-10-28-pvs-node-prune/run.ps1 with this record's two builds: the quiet gate (no other viewer,
# dotnet, testhost or MSBuild process; an idle MSBuild /nodemode:1 reuse node and VBCSCompiler are not counted; then three
# LoadPercentage samples under 25%), then the viewer under run-exclusive. Stdout -> $Out/$Name.txt. Exit 2 = gate not met.
# -Mode empty: the tf2-measure empty view (camera override, no --first-person). -Mode fp: f12 gummo first person.
# -Shot <png>: a paused --shot of the same view instead. TF2 (tf_win64) may be running as a shared fixture; it is not
# excluded by name, only by the load samples.
param([ValidateSet('A', 'B')][string]$Build, [ValidateSet('empty', 'fp')][string]$Mode = 'empty', [string]$Name,
  [string]$Shot = '', [int]$Seconds = 20, [int]$GateMinutes = 90, [string]$Out = $PSScriptRoot)
$ErrorActionPreference = 'Stop'
$exe = @{ A = 'D:/measure-f12/dbp-before/tf2demoview.exe'; B = 'D:/measure-f12/dbp-after/tf2demoview.exe' }[$Build]
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
Remove-Item Env:TF2VIEW_QUEUE_VERIFY, Env:TF2VIEW_FIXED_FRAME, Env:TF2VIEW_FRAME_HASH -ErrorAction SilentlyContinue
$a = @('-NoProfile', '-File', 'C:/Users/pinku/source/repos/PinKushin/run-exclusive.ps1', $exe, 'D:/measure-f12/f12.dem', '--tick', '30000')
if ($Mode -eq 'empty') { $env:TF2VIEW_CAMERA = '1613 -2821 154 89 0' } else {
  Remove-Item Env:TF2VIEW_CAMERA -ErrorAction SilentlyContinue; $a += @('--spectate', 'gummo', '--first-person') }
if ($Shot) { $a += @('--shot', $Shot) } else { $a += @('--measure', "$Seconds", '+fps_max', '0') }
$p = Start-Process pwsh -ArgumentList $a -PassThru -NoNewWindow -RedirectStandardOutput "$Out/$Name.txt" -RedirectStandardError "$Out/$Name.err"
$p.WaitForExit()
"exit $($p.ExitCode); load after: $((1..3 | ForEach-Object { Load; Start-Sleep 1 }) -join ' ')"
