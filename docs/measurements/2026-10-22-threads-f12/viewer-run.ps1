# One viewer reading: launches the viewer (the caller holds the machine lock), optionally restricts its
# affinity the moment it exists, samples its threads until it exits. Stdout -> $Out/$Name.txt, CSV -> $Out/$Name.csv.
param([string]$Exe, [string]$Out, [string]$Name, [long]$Mask = 0, [int]$MaskAfter = 180)
$env:TF2VIEW_AUTOPLAY = '1'; $env:TF2VIEW_VIEWPORT_SIZE = '1280x720'
"load before: $((Get-CimInstance Win32_Processor).LoadPercentage)%"
$p = Start-Process $Exe -ArgumentList 'D:/measure-f12/f12.dem', '--tick', '30000', '--spectate', 'gummo', '--first-person', '--measure', '20', '+fps_max', '0' `
  -PassThru -RedirectStandardOutput "$Out/$Name.txt" -RedirectStandardError "$Out/$Name.err"
# The affinity is set -MaskAfter seconds in (still inside asset loading, which takes ~210 s on all cores), so
# loading does not run on 2 cores; only playback is the reading.
if ($Mask) { $null = Start-ThreadJob { param($id, $m, $s) Start-Sleep $s; (Get-Process -Id $id).ProcessorAffinity = [IntPtr]$m } -ArgumentList $p.Id, $Mask, $MaskAfter
  "affinity 0x{0:X} to be set at {1} s" -f $Mask, $MaskAfter }
pwsh -NoProfile -File "$PSScriptRoot/sample-threads.ps1" -ProcessId $p.Id -Seconds 600 -Out "$Out/$Name.csv"
$p.WaitForExit()
if ($Mask) { Get-Job | Receive-Job -Wait -AutoRemoveJob }  # surfaces a failed affinity set
"exit $($p.ExitCode); load after: $((Get-CimInstance Win32_Processor).LoadPercentage)%"
