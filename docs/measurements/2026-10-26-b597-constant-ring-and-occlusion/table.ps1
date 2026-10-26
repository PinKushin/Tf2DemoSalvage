# One row per run: frame, render thread draw, driver, the passes this change touches, and OpaqueModels' spans.
param([string[]]$Runs)
$o = $PSScriptRoot
$spans = 'InstanceSetup', 'PipelineBinds', 'Flex', 'ModelConstants', 'Bones', 'Cubemap', 'BatchState', 'MaterialBinds',
  'Proxies', 'MaterialMap', 'DrawCall', 'Bookkeeping', 'Renderable', 'TimerOverhead', 'unspanned'
foreach ($r in $Runs) {
  $t = Get-Content "$o/$r.txt"
  $mean = ($t | Select-String 'frames, mean ([\d.]+) ms' | Select-Object -First 1).Matches[0].Groups[1].Value
  $rtd = ($t | Select-String 'render thread draw ([\d.]+)').Matches[0].Groups[1].Value
  $pass = @{}; $span = @{}; $in = $null
  foreach ($line in $t) {
    if ($line -match '^\s{4}(\w+): cpu ([\d.]+) ms') { $in = $Matches[1]; $pass[$in] = $Matches[2] }
    elseif ($in -eq 'OpaqueModels' -and $line -match '^\s{8}span (\w+): cpu ([\d.]+) ms') { $span[$Matches[1]] = $Matches[2] }
    elseif ($in -eq 'OpaqueModels' -and $line -match '^\s{8}unspanned: cpu (-?[\d.]+) ms') { $span['unspanned'] = $Matches[1] }
  }
  "$r frame $mean rtd $rtd sum $($pass['all']) world $($pass['World']) models $($pass['OpaqueModels']) occlusion $($pass['Occlusion'])"
  "    " + (($spans | ForEach-Object { "$_ $($span[$_])" }) -join ', ')
}
