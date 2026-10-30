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
# Null when any processor gives no LoadPercentage: Measure-Object would average a null as 0, which reads as quiet.
function Load {
  $v = @(Get-CimInstance Win32_Processor | ForEach-Object { $_.LoadPercentage })
  if ($v.Count -eq 0 -or @($v | Where-Object { $null -eq $_ }).Count -gt 0) { return $null }
  ($v | Measure-Object -Average).Average
}
$deadline = (Get-Date).AddMinutes($GateMinutes)
while ($true) {
  if ((Get-Date) -gt $deadline) { "gate not met in $GateMinutes min"; exit 2 }
  if (Busy) { Start-Sleep 10; continue }
  $l = 1..3 | ForEach-Object { Load; Start-Sleep 1 }
  # A null sample is not quiet.
  if (@($l | Where-Object { $null -eq $_ -or $_ -ge 25 }).Count -eq 0) { break }
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
# Taking the handle now is what keeps ExitCode readable after WaitForExit; without it ExitCode can come back null.
$null = $p.Handle
$p.WaitForExit()
"exit $($p.ExitCode); load after: $((1..3 | ForEach-Object { Load; Start-Sleep 1 }) -join ' ')"
# No exit code read is a failure, never a zero.
exit ($null -eq $p.ExitCode ? 1 : $p.ExitCode)
