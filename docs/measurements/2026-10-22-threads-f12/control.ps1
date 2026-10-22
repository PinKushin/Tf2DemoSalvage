# Instrument and topology controls. Spawns throwaway pwsh spinners, samples them; they exit on their own.
param([string]$Dir, [string]$Out)
$spin = { param($n, $mask, $secs)
  $code = "`$j = 1..$n | % { Start-ThreadJob { `$i=0L; `$e=[DateTime]::UtcNow.AddSeconds($secs); while([DateTime]::UtcNow -lt `$e){`$i++}; `$i } }; (`$j | Wait-Job | Receive-Job | Measure-Object -Sum).Sum"
  $p = Start-Process pwsh -ArgumentList '-NoProfile', '-Command', $code -PassThru -RedirectStandardOutput "$Out/spin-$n-$mask.out" -WindowStyle Hidden
  if ($mask) { Start-Sleep -Milliseconds 300; $p.ProcessorAffinity = [IntPtr]$mask }
  $p }
foreach ($n in 1, 4) {
  $p = & $spin $n 0 8; Start-Sleep 1
  pwsh -NoProfile -File "$Dir/sample-threads.ps1" -ProcessId $p.Id -Seconds 6 -Out "$Out/control-spin$n.csv"; $p.WaitForExit()
  "== spin $n threads"; pwsh -NoProfile -File "$Dir/summarize.ps1" -In "$Out/control-spin$n.csv" -From 1 -To 5 }
# Topology: the spinners sleep 3 s first (pwsh startup and the affinity set land outside the count), then spin 4 s.
$spin2 = { param($mask)
  $code = "Start-Sleep 3; `$j = 1..2 | % { Start-ThreadJob { `$i=0L; `$e=[DateTime]::UtcNow.AddSeconds(4); while([DateTime]::UtcNow -lt `$e){`$i++}; `$i } }; (`$j | Wait-Job | Receive-Job | Measure-Object -Sum).Sum"
  $p = Start-Process pwsh -ArgumentList '-NoProfile', '-Command', $code -PassThru -RedirectStandardOutput "$Out/topo-$mask.out" -WindowStyle Hidden
  Start-Sleep -Milliseconds 200; $p.ProcessorAffinity = [IntPtr]$mask; $p }
foreach ($m in 0x3, 0xC, 0x30, 0x5, 0xA, 0x14, 0xFFF) { $p = & $spin2 $m; $p.WaitForExit(); "== topo 2 spinners mask 0x{0:X} iterations: {1}" -f $m, (Get-Content "$Out/topo-$m.out") }
foreach ($m in 3, 5) { $p = & $spin 2 $m 6; $p.WaitForExit(); "== 2 spinners mask 0x$m iterations: $(Get-Content "$Out/spin-2-$m.out")" }
