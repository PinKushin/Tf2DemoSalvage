# One viewer reading (the caller holds the machine lock): launch, sample threads until exit, and with -Trace take a
# 15 s sample-profiler trace starting 10 s after the `measure heartbeat` thread appears (= --measure starts counting).
# Stdout -> $Out/$Name.txt, CSV -> $Out/$Name.csv, trace -> $Out/$Name.nettrace (not committed: size).
param([string]$Exe, [string]$Out, [string]$Name, [switch]$Trace)
$env:TF2VIEW_AUTOPLAY = '1'; $env:TF2VIEW_VIEWPORT_SIZE = '1280x720'
"load before: $((Get-CimInstance Win32_Processor).LoadPercentage)%"
$p = Start-Process $Exe -ArgumentList 'D:/measure-f12/f12.dem', '--tick', '30000', '--spectate', 'gummo', '--first-person', '--measure', '40', '+fps_max', '0' `
  -PassThru -RedirectStandardOutput "$Out/$Name.txt" -RedirectStandardError "$Out/$Name.err"
$csv = "$Out/$Name.csv"
$s = Start-Process pwsh -ArgumentList '-NoProfile', '-File', "$PSScriptRoot/sample-threads.ps1", '-ProcessId', $p.Id, '-Out', $csv -PassThru -NoNewWindow
if ($Trace) {
  # Wait on the condition (the heartbeat thread's name in the sampler's CSV), not a fixed delay.
  while (-not $p.HasExited -and -not ((Test-Path $csv) -and (Select-String -Path $csv -Pattern 'measure heartbeat' -Quiet))) { Start-Sleep 1 }
  "heartbeat seen at $(Get-Date -Format HH:mm:ss.fff)"
  Start-Sleep 10
  "trace start $(Get-Date -Format HH:mm:ss.fff); load $((Get-CimInstance Win32_Processor).LoadPercentage)%"
  dotnet-trace collect -p $p.Id --providers Microsoft-DotNETCore-SampleProfiler --duration 00:00:00:15 -o "$Out/$Name.nettrace" --format Speedscope
}
$p.WaitForExit(); $s.WaitForExit()
"exit $($p.ExitCode); load after: $((Get-CimInstance Win32_Processor).LoadPercentage)%"
