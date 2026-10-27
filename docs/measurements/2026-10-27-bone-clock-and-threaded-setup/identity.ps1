# Identity runs on the new base: frame-hash lines (mode 0, fixed step, 1280x720) for NS, NS, NT.
param([string]$Exe = 'D:/measure-f12/bone-clock/tf2demoview.exe')
$ErrorActionPreference = 'Stop'
$out = $PSScriptRoot
$logs = Join-Path $env:LOCALAPPDATA 'Tf2DemoSalvage'
foreach ($run in @(@('h1-NS', '0'), @('h2-NS', '0'), @('h3-NT', '1'))) {
  $env:TF2VIEW_AUTOPLAY = '1'; $env:TF2VIEW_VIEWPORT_SIZE = '1280x720'; $env:TF2VIEW_FRAME_HASH = '1'; $env:TF2VIEW_FIXED_FRAME = '0.015'
  $before = Get-Date
  "$($run[0]) $(Get-Date -Format HH:mm:ss)"
  & pwsh -NoProfile -File C:/Users/pinku/source/repos/PinKushin/run-exclusive.ps1 $Exe D:/measure-f12/f12.dem --tick 30000 `
    --spectate gummo --first-person --measure 6 +fps_max 0 +mat_queue_mode 0 +cl_threaded_bone_setup $run[1] | Set-Content "$out/$($run[0]).txt"
  Remove-Item Env:TF2VIEW_FRAME_HASH, Env:TF2VIEW_FIXED_FRAME -ErrorAction SilentlyContinue
  $log = Get-ChildItem $logs -Filter 'viewer-*.log' | Where-Object { $_.LastWriteTime -gt $before } |
    Sort-Object LastWriteTime -Descending | Select-Object -First 1
  Select-String -Path $log.FullName -Pattern 'frame hash at' | ForEach-Object { $_.Line } | Set-Content "$out/$($run[0])-hash.txt"
}
