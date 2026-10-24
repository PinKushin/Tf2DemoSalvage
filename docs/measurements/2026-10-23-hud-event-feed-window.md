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

## Verdict
