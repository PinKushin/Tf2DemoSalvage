# One reading of the B597 vertex control: the quiet gate (no other viewer, dotnet, testhost or MSBuild process; an idle
# MSBuild /nodemode:1 reuse node and VBCSCompiler are not counted; then three LoadPercentage samples under 25%), then the
# viewer under run-exclusive. Same exe every time; only TF2VIEW_MODEL_VERTS differs. Stdout -> $Out/$Name.txt.
# -Verts '' = N (normal), '3' = V3, '0' = V0. Exit 2 = gate not met.
param([string]$Name, [string]$Verts = '', [int]$Seconds = 20, [int]$GateMinutes = 90, [string]$Out = $PSScriptRoot)
$ErrorActionPreference = 'Stop'
$exe = 'D:/measure-f12/b597-split/tf2demoview.exe'
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
"$(Get-Date -Format s) $Name verts '$Verts'; load before: $($l -join ' ')"
$env:TF2VIEW_AUTOPLAY = '1'
$env:TF2VIEW_VIEWPORT_SIZE = '1280x720'
Remove-Item Env:TF2VIEW_CAMERA -ErrorAction SilentlyContinue
if ($Verts -ne '') { $env:TF2VIEW_MODEL_VERTS = $Verts } else { Remove-Item Env:TF2VIEW_MODEL_VERTS -ErrorAction SilentlyContinue }
$a = @('-NoProfile', '-File', 'C:/Users/pinku/source/repos/PinKushin/run-exclusive.ps1', $exe, 'D:/measure-f12/f12.dem',
  '--tick', '30000', '--spectate', 'gummo', '--first-person', '--measure', "$Seconds", '+fps_max', '0', '+mat_queue_mode', '2')
$p = Start-Process pwsh -ArgumentList $a -PassThru -NoNewWindow -RedirectStandardOutput "$Out/$Name.txt" -RedirectStandardError "$Out/$Name.err"
$p.WaitForExit()
"exit $($p.ExitCode); load after: $((1..3 | ForEach-Object { Load; Start-Sleep 1 }) -join ' ')"
