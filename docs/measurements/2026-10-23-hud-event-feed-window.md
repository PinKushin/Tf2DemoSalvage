# Measurement 2026-10-23: the HUD event feed's window search against its full walk (B579, f12)

**Status: written and committed before the run.** Sections above `## Result` are fixed from here; deviations go under Result.

## Observation

`2026-10-22-f12-camera-breakdown.md` read the `hud events` stage at 0.27-0.28 ms a frame on f12; finding 97 traced it to
`HudEventFeed.Advance` walking every game event and user message of the match each frame (B579). The fix searches the
`(last, tick]` window of each tick-ordered list instead (`LandedWindow`), and its unit tests count the items examined.

## Question

Does the `hud events` stage fall when the walk becomes a window search, by more than the run-to-run spread of the same build?

## Prediction

`hud events` falls by 0.1-0.25 ms on both scenes (the walk is linear in the match and `Resolve` of a crossed event stays).
Falsified if the after build's figure is inside the before build's own spread.

## Method

Two Debug builds: `before` = `origin/main` `74d23fdd`, `after` = this commit. Each copied to its own folder in the session
scratch directory. Interleaved runs, before/after/before/after, each through `run-exclusive.ps1` (waiter on `ACQUIRED`), the
machine quiet gate before each (three `(Get-CimInstance Win32_Processor).LoadPercentage` samples under 25%, no build running):

- empty: `TF2VIEW_AUTOPLAY=1 TF2VIEW_VIEWPORT_SIZE=1280x720 TF2VIEW_CAMERA="1613 -2821 154 89 0" tf2demoview.exe
  D:/measure-f12/f12.dem --tick 30000 --measure 20 +fps_max 0`
- gummo: the same with `--spectate gummo --first-person` instead of the camera.

The figure read is the `hud events` stage's per-second lines, and the frame mean beside it.

## Control

- Ticks advanced ~1,333 per 20 s window in every run (the instrument ran playback).
- The two runs of one build on one scene give that build's spread; a difference inside it is not claimed.
- The `hud events` stage must be present and non-zero in the before runs (the instrument can see the subject).

## Result

Builds were copied to `D:/measure-f12/b579-before` and `b579-after` (the scratch path was too long for `Start-Process`).
Raw outputs were kept in the session scratch directory and not committed. Order: b1, a1, b2, a2.

| run | frames | mean ms | load before / after | `hud events` per-second lines, ms |
|---|---:|---:|---|---|
| empty b1 | 3,857 | 5.19 | 18,21,20 / 9 | 0.13-0.17, median 0.14 |
| empty a1 | 3,897 | 5.13 | 19,23,7 / 5 | 0-0.01, median 0 |
| empty b2 | 3,619 | 5.53 | 15,15,10 / 25 | 0.13-0.20, median 0.15 |
| empty a2 | 2,371 | 8.45 | 7,17,11 / **90** | 0-0.02, median 0.01 |

Every run ended near tick 31,345, about 1,330 ticks of playback (the control passes). `hud events` was present and
non-zero in both before runs (control passes). **Gummo first person: not measured.** The quiet gate refused all four
runs (load 20-100%: another workload started), so no figure was taken.

## Verdict

**Confirmed on the empty view:** `hud events` falls from 0.13-0.20 ms to 0-0.02 ms, about 0.13 ms. That is far outside
either build's own spread (0.07 ms before, 0.02 ms after), and the drop holds even in a2, which was loaded by the end.
The saving is below the prediction's 0.1-0.25 band midpoint, but inside the band. The frame mean is not claimed: it is
inside the run-to-run spread (5.13-8.45 ms). The first-person scene is still open.
