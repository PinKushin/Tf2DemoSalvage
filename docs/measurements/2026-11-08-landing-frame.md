# The frame a seek lands on: where its 2.4 s go (B708)

## Observation

`2026-11-04-skip-across-frames.md`, its corrected run: on `z1800.dem --tick 20000 --autoplay --measure 3`, Release, the
landing frame of the opening seek was 2,367 to 2,401 ms. Its log attributed about 1.7 s to particles (the seek's rebuild,
our addition per D221) and 360 ms to corpses catching up. A second hitch of 1,104 to 1,139 ms falls where the decal replay
begins (`capture`, `BeginReplay`). Both were filed open under B708. Measured by an earlier session, not seen in this one.

## Question

At this commit, how long is the landing frame on that run, and which timed parts make it up: of the particles, which part
names carry the time? Is the decal-replay hitch still there, and how long?

## Method

The viewer's own per-part timers, read from the log's `SLOW FRAME` and `SLOW CAMERA` lines for the frames from the
seek's start to its landing. `--measure` prints only means, so the log is the instrument; it is written at exit, so it is
read after the run.

```bash
MSBUILDDISABLENODEREUSE=1 dotnet build managed/Tf2DemoSalvage.Viewer3D -c Release
TF2VIEW_VIEWPORT_SIZE=1280x720 pwsh run-exclusive.ps1 \
  managed/Tf2DemoSalvage.Viewer3D/bin/Release/net10.0-windows/tf2demoview.exe \
  tools/corpus/demos/z1800.dem --tick 20000 --autoplay --measure 3
```

Two runs, back to back, under the desktop lock. Read the newest `viewer-*.log` in `%LOCALAPPDATA%\Tf2DemoSalvage` after
each, from the `skip` lines to the first frame after `skip landed at 20000`.

## Control

The log must carry a `skip landed at 20000` line and at least one `SLOW FRAME` line with a `camera` part at or after it.
If either is missing, the instrument did not record the landing, and a short landing is not concluded from silence.

## Environment

## Result

## Verdict
