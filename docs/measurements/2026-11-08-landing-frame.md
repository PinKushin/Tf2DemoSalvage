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

The owner's desktop machine, Windows 11, viewport 1280x720, Release build of `c53a4709`, under the desktop lock. No other
agent or viewer was running, and TF2 was closed. Load before and after was not recorded.

## Result

The control holds in both runs: `skip landed at 20000`, then a `SLOW FRAME` with a `camera` part.

| | run 1 (`viewer-20261108-163022-16352.log`) | run 2 (`viewer-20261108-163147-34524.log`) |
|---|---|---|
| landing frame | 1,388 ms | 1,159 ms |
| camera, of it | 1,006 (device 172.5, particles 833.4) | 796.3 (device 196.8, particles 599.5) |
| advance, of it | 334.6 | 322.7 |
| sound, of it | 46.4 | 39.4 |
| GC in it | 52/10/0 collections, paused 126 ms | 68/12/0, paused 156 ms |
| longest frame before the landing | 297 ms (advance 292.8) | 334 ms (advance 329.5) |
| decal replay (`model decals: a seek to 20000 …`) | 979 ms over the walk's batches | 931 ms |
| walk | 202 batches over 133 frames | 202 batches over 125 frames |

Raw lines, run 1:

```
16:31:40.111 WARN [render] SLOW FRAME 297 ms: sound 0, camera 2.9, project 0, advance 292.8, capture 0, hud 1.2, draw 0; unaccounted 0 ms; gc 23/4/0 paused 47 ms
16:31:40.445      [render] model decals: a seek to 20000 replayed ticks 18379-19999, posing 829 of them, in 979 ms (lookback 96, skipped ticks 6, building 463, posing 175 of which simulate 89, bones 0, skin 0; the rest placing decals); 72 held
16:31:40.445      [render] skip landed at 20000 after 202 batches over 133 frames, at most 6 in a frame; corpses stepped 0 ms on the way
16:31:41.451 WARN [render] SLOW CAMERA 1006 ms: fly 0, view 0, device 172.5, particles 833.4
16:31:41.499 WARN [render] SLOW FRAME 1388 ms: sound 46.4, camera 1006, project 0, advance 334.6, capture 0, hud 1.3, draw 0; unaccounted 0 ms; gc 52/10/0 paused 126 ms
```

Raw lines, run 2:

```
16:32:45.982 WARN [render] SLOW FRAME 334 ms: sound 0, camera 4, project 0, advance 329.5, capture 0, hud 0.6, draw 0; unaccounted 0 ms; gc 22/7/0 paused 36 ms
16:32:46.251 WARN [render] SLOW FRAME 268 ms: sound 0, camera 3, project 0, advance 264.5, capture 0, hud 0.9, draw 0; unaccounted 0 ms; gc 16/3/0 paused 24 ms
16:32:46.572      [render] model decals: a seek to 20000 replayed ticks 18379-19999, posing 829 of them, in 931 ms (lookback 100, skipped ticks 5, building 445, posing 166 of which simulate 72, bones 0, skin 0; the rest placing decals); 72 held
16:32:46.573      [render] skip landed at 20000 after 202 batches over 125 frames, at most 6 in a frame; corpses stepped 0 ms on the way
16:32:47.370 WARN [render] SLOW CAMERA 796 ms: fly 0, view 0, device 196.8, particles 599.5
16:32:47.410 WARN [render] SLOW FRAME 1159 ms: sound 39.4, camera 796.3, project 0, advance 322.7, capture 0, hud 0.9, draw 0; unaccounted 0 ms; gc 68/12/0 paused 156 ms
```

## Verdict

The landing frame is now 1.16 to 1.39 s, down from 2.37 to 2.40 s. The particle rebuild (0.60 to 0.83 s) is still the
largest part, then `advance` (0.32 to 0.33 s), which the 2026-11-04 run named as the corpses catching up (kept, D250). The
separate 1.1 s decal-replay hitch is gone: the replay now runs across the walk's batches, and no frame before the landing
passes 334 ms. *Not attributed:* which change since 2026-11-04 halved the landing. The next measurement is inside the
particle rebuild, to find which effects and which steps carry the time.
