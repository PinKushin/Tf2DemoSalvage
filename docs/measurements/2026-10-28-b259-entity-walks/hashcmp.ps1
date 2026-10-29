# Compares two TF2VIEW_FRAME_HASH logs tick by tick: each tick's FIRST frame line in each log, on the named parts
# (`pose`, `particles`, `rest`, `cull`, `image`). Prints, per part, how many shared ticks agree.
param([Parameter(Mandatory)][string]$LogA, [Parameter(Mandatory)][string]$LogB,
  [string[]]$Parts = @('pose', 'particles', 'rest', 'cull', 'image'))
function Read([string]$path) {
  $first = @{}
  foreach ($line in Get-Content $path) {
    if ($line -match 'frame hash at ([\d.]+): pose (\w+) particles (\w+) rest (\w+) cull (\w+) image (\w+)') {
      if (-not $first.ContainsKey($Matches[1])) {
        $first[$Matches[1]] = @{ pose = $Matches[2]; particles = $Matches[3]; rest = $Matches[4]; cull = $Matches[5]; image = $Matches[6] }
      }
    }
  }
  $first
}
$a = Read $LogA; $b = Read $LogB
$shared = @($a.Keys | Where-Object { $b.ContainsKey($_) } | Sort-Object { [double]$_ })
"$(Split-Path $LogA -Leaf) vs $(Split-Path $LogB -Leaf): $($a.Count) / $($b.Count) ticks, $($shared.Count) shared"
foreach ($p in $Parts) {
  $same = @($shared | Where-Object { $a[$_][$p] -eq $b[$_][$p] }).Count
  $firstDiff = $shared | Where-Object { $a[$_][$p] -ne $b[$_][$p] } | Select-Object -First 1
  "  $p equal at $same/$($shared.Count)" + $(if ($firstDiff) { "; first difference at tick $firstDiff" } else { '' })
}
