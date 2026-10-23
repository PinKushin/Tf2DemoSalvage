# Runs control.ps1 under the sampler and an 8 s sample-profiler trace, then summarizes. Trace to -TraceDir.
param([string]$TraceDir)
$c = Start-Process pwsh -ArgumentList '-NoProfile', '-File', "$PSScriptRoot/control.ps1", '-Seconds', '30' -PassThru -NoNewWindow
Start-Sleep 3
$s = Start-Process pwsh -ArgumentList '-NoProfile', '-File', "$PSScriptRoot/sample-threads.ps1", '-ProcessId', $c.Id, '-Seconds', '20', '-Out', "$PSScriptRoot/control.csv" -PassThru -NoNewWindow
Start-Sleep 6
dotnet-trace collect -p $c.Id --providers Microsoft-DotNETCore-SampleProfiler --duration 00:00:00:08 -o "$TraceDir/control.nettrace" --format Speedscope 2>&1 | Select-Object -Last 3
$c.WaitForExit(); $s.WaitForExit()
pwsh -NoProfile -File "$PSScriptRoot/../2026-10-22-threads-f12/summarize.ps1" -In "$PSScriptRoot/control.csv" -From 2 -To 18
