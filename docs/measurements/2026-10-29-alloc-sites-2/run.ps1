# One reading, as 2026-10-28-b259-entity-walks/run.ps1 with the build named by label: the quiet gate, then the viewer under
# run-exclusive. The gate is by LOAD, as the brief for this branch asks ("sample CPU load, not process names"): no other
# viewer, then three LoadPercentage samples under 25%. Changed from the 10-28 script, which also refused any dotnet,
# testhost or MSBuild process: another agent's single-threaded corpus test held that gate shut for 30 minutes at 17% load. Stdout -> $Out/$Name.txt. Exit 2 = gate not met.
# -Build: a label; the exe is D:/measure-f12/as2-<label>-src/managed/Tf2DemoSalvage.Viewer3D/bin/Release/net10.0-windows.
# -Mode empty: the tf2-measure empty view (camera override, no --first-person). -Mode fp: f12 gummo first person.
# -Shot <png>: a paused --shot of the same view instead. -Trace <nettrace>: also attach dotnet-trace (GC verbose, keyword
# 0x1 level 5, which carries GCAllocationTick with stacks) for the whole process; the analyser keeps only its tail.
# -Diagnostics keeps TF2VIEW_FRAME_HASH / TF2VIEW_QUEUE_VERIFY / TF2VIEW_FIXED_FRAME as the caller set them.
param([Parameter(Mandatory)][string]$Build, [ValidateSet('empty', 'fp')][string]$Mode = 'empty', [Parameter(Mandatory)][string]$Name,
  [string]$Shot = '', [int]$Seconds = 20, [int]$GateMinutes = 90, [string]$Out = 'D:/measure-f12/alloc2/runs',
  [string[]]$Extra = @(), [string]$Trace = '', [switch]$Diagnostics)
$ErrorActionPreference = 'Stop'
$exe = "D:/measure-f12/as2-$Build-src/managed/Tf2DemoSalvage.Viewer3D/bin/Release/net10.0-windows/tf2demoview.exe"
if (-not (Test-Path $exe)) { "no build at $exe"; exit 3 }
New-Item -ItemType Directory -Force $Out | Out-Null
function Busy {
  @(Get-CimInstance Win32_Process -Filter "Name = 'tf2demoview.exe'").Count -gt 0
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
$others = @(Get-CimInstance Win32_Process | Where-Object { $_.Name -match '^(dotnet|testhost|MSBuild)' -and $_.CommandLine -notmatch 'VBCSCompiler|/nodemode:1' }).Count
"$(Get-Date -Format s) $Name build $Build mode $Mode; load before: $($l -join ' '); other dotnet/testhost/MSBuild processes: $others"
if ($Shot) { Remove-Item Env:TF2VIEW_AUTOPLAY -ErrorAction SilentlyContinue } else { $env:TF2VIEW_AUTOPLAY = '1' }
$env:TF2VIEW_VIEWPORT_SIZE = '1280x720'
if (-not $Diagnostics) { Remove-Item Env:TF2VIEW_QUEUE_VERIFY, Env:TF2VIEW_FIXED_FRAME, Env:TF2VIEW_FRAME_HASH -ErrorAction SilentlyContinue }
$a = @('-NoProfile', '-File', 'C:/Users/pinku/source/repos/PinKushin/run-exclusive.ps1', $exe, 'D:/measure-f12/f12.dem', '--tick', '30000')
if ($Mode -eq 'empty') { $env:TF2VIEW_CAMERA = '1613 -2821 154 89 0' } else {
  Remove-Item Env:TF2VIEW_CAMERA -ErrorAction SilentlyContinue; $a += @('--spectate', 'gummo', '--first-person') }
if ($Shot) { $a += @('--shot', $Shot) } else { $a += @('--measure', "$Seconds", '+fps_max', '0') }
$a += $Extra
$p = Start-Process pwsh -ArgumentList $a -PassThru -NoNewWindow -RedirectStandardOutput "$Out/$Name.txt" -RedirectStandardError "$Out/$Name.err"
$tracer = $null
if ($Trace) {
  $viewer = $null
  while (-not $viewer -and -not $p.HasExited) {
    $viewer = Get-CimInstance Win32_Process -Filter "Name = 'tf2demoview.exe'" | Where-Object { $_.ExecutablePath -replace '\\', '/' -eq $exe } | Select-Object -First 1
    if (-not $viewer) { Start-Sleep -Milliseconds 200 }
  }
  if ($viewer) {
    $tracer = Start-Process dotnet-trace -ArgumentList @('collect', '-p', "$($viewer.ProcessId)", '--providers',
      'Microsoft-Windows-DotNETRuntime:0x1:5', '-o', $Trace) -PassThru -NoNewWindow -RedirectStandardOutput "$Out/$Name.trace.txt" -RedirectStandardError "$Out/$Name.trace.err"
  }
}
$p.WaitForExit()
if ($tracer) { $tracer.WaitForExit() }
if ($Diagnostics) {
  Get-ChildItem "$env:LOCALAPPDATA/Tf2DemoSalvage" -Filter 'viewer-*.log' | Sort-Object LastWriteTime -Descending |
    Select-Object -First 1 | Copy-Item -Destination "$Out/$Name.log"
}
"exit $($p.ExitCode); load after: $((1..3 | ForEach-Object { Load; Start-Sleep 1 }) -join ' ')"
