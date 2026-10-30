# Runs queued fixes back to back once an earlier queue has gone: waits while any `all.ps1` process of this folder is alive,
# then runs each "A,B,Tag" in -Pairs (separated by ';') through all.ps1. Meant for pmux, which outlives the session that
# started it.
param([Parameter(Mandatory)][string]$Pairs)
$all = Join-Path $PSScriptRoot 'all.ps1'
while (@(Get-CimInstance Win32_Process -Filter "Name = 'pwsh.exe'" | Where-Object { $_.CommandLine -match 'alloc-sites-2[\\/]all\.ps1' -and $_.ProcessId -ne $PID }).Count -gt 0) {
  Start-Sleep 30
}
"$(Get-Date -Format s) chain starting: $Pairs" | Tee-Object -Append -FilePath "D:/measure-f12/alloc2/runs/chain.log"
foreach ($pair in $Pairs -split ';') {
  ($a, $b, $tag) = $pair -split ','
  "$(Get-Date -Format s) chain: $tag ($a against $b)"
  & $all -A $a -B $b -Tag $tag *>&1 | Tee-Object -Append -FilePath "D:/measure-f12/alloc2/runs/chain.log"
}
"chain done $(Get-Date -Format s)" | Tee-Object -Append -FilePath "D:/measure-f12/alloc2/runs/chain.log"
