# Per thread of a dotnet-trace speedscope file (evented profiles): total sampled time, and the frames with the most
# inclusive time, deepest first among those holding at least -Share of the thread. Names what a thread runs.
param([string]$In, [int[]]$Tid, [double]$Share = 0.3, [int]$Top = 12)
$j = Get-Content $In -Raw | ConvertFrom-Json
$f = $j.shared.frames
foreach ($pr in $j.profiles) {
  if ($Tid -and -not ($Tid | Where-Object { $pr.name -match "\($_\)" })) { continue }
  $incl = @{}; $depth = @{}; $open = @{}; $stack = 0
  foreach ($e in $pr.events) {
    if ($e.type -eq 'O') { $open["$($e.frame)"] = $e.at; $stack++; $depth["$($e.frame)"] = [math]::Max($depth["$($e.frame)"], $stack) }
    else { $incl["$($e.frame)"] += $e.at - $open["$($e.frame)"]; $stack-- }
  }
  $total = $pr.endValue - $pr.startValue
  "== $($pr.name): $([math]::Round($total)) $($pr.unit) sampled"
  $incl.GetEnumerator() | Where-Object { $_.Value -ge $Share * $total } | Sort-Object { $depth[$_.Key] } -Descending | Select-Object -First $Top |
    ForEach-Object { '  {0,5:P0} d{1,-3} {2}' -f ($_.Value / $total), $depth[$_.Key], $f[[int]$_.Key].name }
}
