# Compares two runs' frame-hash lines part by part, matched by tick label (the first line at each label).
param([Parameter(Mandatory)][string]$A, [Parameter(Mandatory)][string]$B)
$ErrorActionPreference = 'Stop'
function Load([string]$path) {
  $map = @{}
  foreach ($line in [IO.File]::ReadLines($path)) {
    if ($line -match 'frame hash at ([\d.]+): pose (\w+) particles (\w+) rest (\w+) cull (\w+) image (\w+)') {
      if (-not $map.ContainsKey($Matches[1])) {
        $map[$Matches[1]] = @{ pose = $Matches[2]; particles = $Matches[3]; rest = $Matches[4]; cull = $Matches[5]; image = $Matches[6] }
      }
    }
  }
  $map
}
$first = Load $A; $second = Load $B
$common = @($first.Keys | Where-Object { $second.ContainsKey($_) })
"labels: $($first.Count) and $($second.Count), common $($common.Count)"
foreach ($part in 'pose', 'cull', 'particles', 'rest', 'image') {
  $differ = @($common | Where-Object { $first[$_][$part] -ne $second[$_][$part] }).Count
  '  {0,-10} differ on {1} of {2}' -f $part, $differ, $common.Count
}
"  pose differs at: $((@($common | Where-Object { $first[$_].pose -ne $second[$_].pose } | Sort-Object { [double]$_ })) -join ' ')"
