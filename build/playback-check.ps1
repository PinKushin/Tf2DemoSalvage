<#
.SYNOPSIS
Plays a real demo in the viewer and fails if playback does not finish (B408).

.DESCRIPTION
The UI suite never plays a demo, so a hang in the physics loop passed both gate phases for days (B408): the viewer pinned one
pair event forever 387 seconds into cp_process_f12. This builds the viewer, plays a demo from a seek for -Seconds of PLAYBACK
(--measure counts playback, not wall clock), and requires the viewer to exit on its own within -LimitSeconds having printed
its measurement with at least half a frame-rate report per second played (arithmetic below). A viewer still running at the limit is killed by ITS OWN process id - never by
image name, which would take whatever else is open - and the check fails.

The default demo is lcor (git-ignored). Where it is absent the check FAILS rather than passing unchecked.

Takes the desktop, so run it under the machine-wide lock:
  pwsh C:/Users/pinku/source/repos/PinKushin/run-exclusive.ps1 pwsh -NoProfile -File build/playback-check.ps1
#>
param(
    [string]$Demo = 'tools/corpus/local/demostf-cp_process_f12-2026-08-08-2207.dem',
    [int]$Tick = 26578,
    [int]$Seconds = 20,
    # **At 8x, because fast-forward is what finds lifecycle bugs** (D189): B418's corpse crash was found only once playback
    # covered minutes of match. Passed to the viewer as Valve's own `+demo_timescale`.
    [double]$Speed = 8,
    [int]$LimitSeconds = 420
)

$ErrorActionPreference = 'Stop'
Set-Location (Split-Path $PSScriptRoot -Parent)

if (-not (Test-Path $Demo)) {
    Write-Error "playback-check: $Demo is not here (lcor, git-ignored), so nothing was checked."
    exit 1
}

$env:MSBUILDDISABLENODEREUSE = '1'
dotnet build managed/Tf2DemoSalvage.Viewer3D --nologo -v q

if ($LASTEXITCODE -ne 0) {
    Write-Error 'playback-check: the viewer did not build.'
    exit 1
}

$exe = (Resolve-Path 'managed/Tf2DemoSalvage.Viewer3D/bin/Debug/net10.0-windows/tf2demoview.exe').Path
$out = New-TemporaryFile
# Quoted: Start-Process joins -ArgumentList with spaces and quotes nothing, so a path with a space opened the viewer
# with no demo at all (2026-10-01, `ESEA Season 30`).
$arguments = @("`"$((Resolve-Path $Demo).Path)`"", '--tick', "$Tick", '--autoplay', '--measure', "$Seconds", '+demo_timescale', "$Speed")
$viewer = Start-Process -FilePath $exe -ArgumentList $arguments -RedirectStandardOutput $out.FullName -PassThru

if (-not $viewer.WaitForExit($LimitSeconds * 1000)) {
    Stop-Process -Id $viewer.Id -Force

    # **Wait for the kill to land** before touching its output: the process holds the file until it is gone, and a failed
    # cleanup here once aborted the script with the hung viewer still running.
    if (-not $viewer.WaitForExit(30000)) {
        Write-Error "playback-check: FAILED - the viewer hung, and PID $($viewer.Id) did not die within 30 s of being killed."
        exit 1
    }

    Remove-Item $out
    Write-Error "playback-check: FAILED - the viewer was still running after $LimitSeconds s; playback hung (killed its own PID $($viewer.Id))."
    exit 1
}

$text = Get-Content $out -Raw
Remove-Item $out

if ($text -notmatch 'measured (?<played>[\d.]+) seconds of playback, (?<samples>\d+) samples \((?<rebuilds>\d+) rebuild reports\), ending at tick (?<endTick>\d+)') {
    Write-Error "playback-check: FAILED - the viewer exited (code $($viewer.ExitCode)) without its measurement:`n$text"
    exit 1
}

$played = [double]::Parse($Matches.played, [Globalization.CultureInfo]::InvariantCulture)
$samples = [int]$Matches.samples
$rebuilds = [int]$Matches.rebuilds
$endTick = [int]$Matches.endTick

# **`samples` is frame-rate reports only, carried from `MeasureLog`; rebuild reports are the separate count below.** It once
# summed both, and a run with few rebuilds failed.
# **The floor is half the ideal, derived rather than guessed:** a report needs at least FrameRateLog.IntervalSeconds (1 s) of
# on-screen frame time and overshoots by the frame that crossed it, so reports <= played / 1 and the ideal 20 s run is 19-20;
# measured 17 (cp_process_f12, 420 ms stalls). A mean interval under twice nominal is still continuous sampling:
# $Seconds / (2 * 1 s) = 10 at the default.
$floor = [math]::Ceiling($Seconds / 2)

if ($played -lt $Seconds -or $samples -lt $floor) {
    Write-Error "playback-check: FAILED - $played s of playback in $samples rate reports, asked for $Seconds s and at least $floor reports."
    exit 1
}

# **The samples must be of a demo, not of the loading screen** (D182). The window draws from the moment it opens now, and this
# check once passed on twenty seconds of frames drawn while the demo was still decoding. **The proof is the tick the viewer was
# showing when it stopped**, past the one it started from. It was once "at least one rebuild report", but that line prints once
# per hundred rebuilds and a backgrounded run (the gate's always is) can play twenty seconds without one (2026-10-01).
if ($endTick -le $Tick) {
    Write-Error "playback-check: FAILED - it ended at tick $endTick, not past $Tick; the demo never played:`n$text"
    exit 1
}

Write-Output "playback-check: $Demo from tick $Tick at ${Speed}x played $played s, tick $Tick to $endTick, in $samples rate reports ($rebuilds rebuild reports) and exited on its own."
