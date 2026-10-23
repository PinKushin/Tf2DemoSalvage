# Measurement 2026-10-23: the viewer's empty-view floor, repeated on a quieter machine (f12)

**Status: written and committed before the run.** Sections above `## Result` are fixed from here; deviations go under Result.

## Observation

`2026-10-22-empty-view-budget-ldr-f12.md` read the viewer's empty view (f12, camera `1613 -2821 154 89 0`, tick 30000) at
7.29 ms/frame, but at 80-100% CPU load with another agent's compiler running; its verdict says repeat on a quiet machine
before quoting it.

## Question

What is the empty-view floor, and its per-phase shape, at low machine load? If it is well under 3 ms, the loaded number
misled and the floor is not the gap.

## Prediction

5-6 ms (the loaded figure divided by ~1.3), same shape: camera largest, then hud and advance.

## Method

Build `origin/main` `273d7c57` + this record, Debug. Two runs back to back, each through
`run-exclusive.ps1` (waiter on `ACQUIRED`):
`TF2VIEW_AUTOPLAY=1 TF2VIEW_VIEWPORT_SIZE=1280x720 TF2VIEW_CAMERA="1613 -2821 154 89 0" tf2demoview.exe D:/measure-f12/f12.dem
--tick 30000 --measure 20 +fps_max 0`, and one with `--spectate gummo --first-person` instead of the camera. Load
`(Get-CimInstance Win32_Processor).LoadPercentage` read before and after each run.

## Control

- Ticks advanced ~1,333 per 20 s window (instrument).
- The two empty runs within 10% of each other, else the spread is stated and no single figure is quoted.
- Load under 30% at both reads, else the run is labelled loaded.

## Result

Raw outputs were kept in the session scratch directory, not committed. Lock queued behind another agent's measured viewer
runs each time; runs were made straight from copied `bin` folders.

| run | build | frames | mean ms | load start/end | stalls >100 ms | gc pause ms/frame | JIT ms in window |
|---|---|---:|---:|---|---:|---:|---:|
| empty 1 | base | 4,010 | 4.99 | 26/16 | 3 | 0.13 | 3,201 |
| gummo | base | 1,391 | 14.38 | 21/39 | 1 | 0.40 | 3,635 |
| empty 2 | base | 2,167 | 9.23 | 18/29 | 4 | 0.40 | 4,729 |
| empty 3 | base | 2,653 | 7.54 | 24/23 | 5 | 0.23 | 4,347 |
| empty 4 | base | 3,657 | 5.47 | 16/12 | 2 | 0.15 | 4,270 |
| empty | probe build (one extra timer) | 3,845 | 5.20 | 18/15 | 0 | 0.14 | 3,385 |
| empty | probe build | 2,806 | 7.13 | 20/21 | 0 | 0.25 | 4,495 |

Ticks advanced ~1,340 in every window (control passes).

**The repeat control failed: the same build's empty view read 4.99-9.23 ms**, and the endpoint load reads (12-29%) did not
flag the slow runs. Every phase scaled together in the slow runs (empty 2: camera 4.73, hud 1.34, advance 1.12 against
2.58, 0.91, 0.73 in empty 1), which is contention on the whole process rather than one stage. The slow runs also spent more
time JIT-compiling inside the window (4.3-4.7 s against 3.2-3.4 s); that is a correlation, not a cause established. **No
single floor figure is quoted from this record.**

**The fastest run's shape** (empty 1, per-second lines, ms): camera 2.0-2.6 = pose 0.71-0.75 + particles 1.23-1.83
(bursts 0.61-0.79 — step 0.18-0.30, muzzles 0.15-0.26, dispatches 0.10-0.17; build 0.26-0.32; decals 0.12-0.51; trails
0-0.08; lights 0.04), hud 0.8 (HudChat 0.34, specgui 0.14, events 0.13), advance 0.7, draw 0.2, sound 0.2, between 0.3.
Rebuild: sample 0.3, drawlist 0.1-0.2, models 0.1, pose 0.7 = simulate 0.5 + collate 0.2; posed 0 of ~1,882 selected,
~1,653 hidden by PVS. The per-second phase sums equal the frame to within 0.1 ms, so there is no untimed residual.

**Stop condition:** not reached. The best run is 4.99 ms with phases summing to it; nothing measured on this machine today
puts the floor under 3 ms.
