# Added after h3-T differed from both S runs at 30021-30022: h4 T and h5 S, as identity.ps1 ran h1-h3.
param([string]$Exe = 'D:/measure-f12/threaded-bones/tf2demoview.exe')
$ErrorActionPreference = 'Stop'
$out = $PSScriptRoot
$logs = Join-Path $env:LOCALAPPDATA 'Tf2DemoSalvage'
foreach ($run in @(@('h4-T', '1'), @('h5-S', '0'))) {
  $env:TF2VIEW_AUTOPLAY = '1'; $env:TF2VIEW_FRAME_HASH = '1'; $env:TF2VIEW_FIXED_FRAME = '0.015'
  $before = Get-Date
  "$($run[0]) $(Get-Date -Format HH:mm:ss)"
  & pwsh -NoProfile -File C:/Users/pinku/source/repos/PinKushin/run-exclusive.ps1 $Exe D:/measure-f12/f12.dem --tick 30000 `
    --spectate gummo --first-person --measure 6 +fps_max 0 +mat_queue_mode 0 +cl_threaded_bone_setup $run[1] | Set-Content "$out/$($run[0]).txt"
  Remove-Item Env:TF2VIEW_FRAME_HASH, Env:TF2VIEW_FIXED_FRAME, Env:TF2VIEW_AUTOPLAY -ErrorAction SilentlyContinue
  $log = Get-ChildItem $logs -Filter 'viewer-*.log' | Where-Object { $_.LastWriteTime -gt $before } |
    Sort-Object LastWriteTime -Descending | Select-Object -First 1
  Select-String -Path $log.FullName -Pattern 'frame hash at' | ForEach-Object { $_.Line } | Set-Content "$out/$($run[0])-hash.txt"
}
