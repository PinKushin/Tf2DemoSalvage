# The record's order: M S L E A M M L A S E M M (A = origin/main 077d0175 at 1280x720, the overhead control).
param([string]$Main)
$o = $PSScriptRoot
$b = "$o/../../../managed/Tf2DemoSalvage.Viewer3D/bin/Release/net10.0-windows/tf2demoview.exe"
$empty = '1613 -2821 154 89 0'
$plan = 'M', 'S', 'L', 'E', 'A', 'M', 'M', 'L', 'A', 'S', 'E', 'M', 'M'
$n = 0
foreach ($c in $plan) {
  $n++; $name = '{0:00}-{1}' -f $n, $c
  $exe = if ($c -eq 'A') { $Main } else { $b }
  $size = switch ($c) { 'S' { '640x360' } 'L' { '1920x1080' } default { '1280x720' } }
  $cam = if ($c -eq 'E') { $empty } else { '' }
  "$name start $(Get-Date -Format HH:mm:ss)"
  & "$o/run.ps1" -Exe $exe -Out $o -Name $name -Size $size -Camera $cam
  if ($LASTEXITCODE -eq 2) { "stopped: gate"; exit 2 }
}
