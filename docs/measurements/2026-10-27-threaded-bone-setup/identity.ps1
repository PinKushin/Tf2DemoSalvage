# Identity runs: frame-hash lines (mode 0, fixed step) for S, S, T, and one mode-2 queue-verify run of T.
# Each run's "frame hash at" lines go to $Out/<name>-hash.txt; the verifier's lines to $Out/<name>-verify.txt.
param([string]$Exe = 'D:/measure-f12/threaded-bones/tf2demoview.exe')
$ErrorActionPreference = 'Stop'
$out = $PSScriptRoot
$logs = Join-Path $env:LOCALAPPDATA 'Tf2DemoSalvage'

function Run([string]$Name, [string]$Bones, [string]$Mode, [hashtable]$Env) {
  foreach ($k in $Env.Keys) { Set-Item "Env:$k" $Env[$k] }
  $before = Get-Date
  "$Name $(Get-Date -Format HH:mm:ss)"
  & pwsh -NoProfile -File C:/Users/pinku/source/repos/PinKushin/run-exclusive.ps1 $Exe D:/measure-f12/f12.dem --tick 30000 `
    --spectate gummo --first-person --measure 6 +fps_max 0 +mat_queue_mode $Mode +cl_threaded_bone_setup $Bones | Set-Content "$out/$Name.txt"
  foreach ($k in $Env.Keys) { Remove-Item "Env:$k" -ErrorAction SilentlyContinue }
  $log = Get-ChildItem $logs -Filter 'viewer-*.log' | Where-Object { $_.LastWriteTime -gt $before } |
    Sort-Object LastWriteTime -Descending | Select-Object -First 1
  "  log $($log.Name)"
  return $log
}

$hash = @{ TF2VIEW_AUTOPLAY = '1'; TF2VIEW_FRAME_HASH = '1'; TF2VIEW_FIXED_FRAME = '0.015' }
foreach ($run in @(@('h1-S', '0'), @('h2-S', '0'), @('h3-T', '1'))) {
  $log = Run $run[0] $run[1] '0' $hash
  Select-String -Path $log.FullName -Pattern 'frame hash at' | ForEach-Object { $_.Line } | Set-Content "$out/$($run[0])-hash.txt"
}

$log = Run 'v1-T' '1' '2' @{ TF2VIEW_AUTOPLAY = '1'; TF2VIEW_QUEUE_VERIFY = '1' }
Select-String -Path $log.FullName -Pattern 'queue verify|inputs changed|QueuedThreaded' | ForEach-Object { $_.Line } |
  Set-Content "$out/v1-T-verify.txt"
