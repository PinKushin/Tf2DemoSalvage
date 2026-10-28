# One reading: quiet gate (three LoadPercentage samples under 25%, no other viewer/dotnet/MSBuild/testhost; an idle shared
# VBCSCompiler server is not counted), then the viewer under run-exclusive. Stdout -> $Out/$Name.txt. Exit 2 = gate not met.
# -Mode empty: the tf2-measure empty view (camera override, no --first-person). -Mode fp: f12 gummo first person.
# -Shot <png>: a --shot of the same view instead of a --measure run.
param([ValidateSet('A', 'B')][string]$Build, [ValidateSet('empty', 'fp')][string]$Mode = 'empty', [string]$Name,
  [string]$Shot = '', [int]$Seconds = 20, [int]$GateMinutes = 90, [string]$Out = $PSScriptRoot)
$ErrorActionPreference = 'Stop'
$exe = @{ A = 'D:/measure-f12/evb-before/tf2demoview.exe'; B = 'D:/measure-f12/evb-after/tf2demoview.exe' }[$Build]
function Busy {
  @(Get-CimInstance Win32_Process | Where-Object {
      # An MSBuild worker node (`/nodemode:1`) left by another agent's build idles for minutes and is not load; the build
      # that drives it is a `dotnet build`/`test` process, which still counts, and the load samples catch the rest.
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
# A shot is a PAUSED frame at the tick: under autoplay the first two shots landed 177 ticks apart (33178, 33355).
if ($Shot) { Remove-Item Env:TF2VIEW_AUTOPLAY -ErrorAction SilentlyContinue } else { $env:TF2VIEW_AUTOPLAY = '1' }
$env:TF2VIEW_VIEWPORT_SIZE = '1280x720'
$a = @('-NoProfile', '-File', 'C:/Users/pinku/source/repos/PinKushin/run-exclusive.ps1', $exe, 'D:/measure-f12/f12.dem', '--tick', '30000')
if ($Mode -eq 'empty') { $env:TF2VIEW_CAMERA = '1613 -2821 154 89 0' } else {
  Remove-Item Env:TF2VIEW_CAMERA -ErrorAction SilentlyContinue; $a += @('--spectate', 'gummo', '--first-person') }
if ($Shot) { $a += @('--shot', $Shot) } else { $a += @('--measure', "$Seconds", '+fps_max', '0') }
$p = Start-Process pwsh -ArgumentList $a -PassThru -NoNewWindow -RedirectStandardOutput "$Out/$Name.txt" -RedirectStandardError "$Out/$Name.err"
$p.WaitForExit()
"exit $($p.ExitCode); load after: $((1..3 | ForEach-Object { Load; Start-Sleep 1 }) -join ' ')"
