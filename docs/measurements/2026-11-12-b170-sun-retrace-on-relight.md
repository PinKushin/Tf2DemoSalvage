# 2026-11-12 — the sun's trace on a light cache relight (B170)

## Observation

Reading the code after B170's fix (`fix/model-light-count` @ `ec535168`): the sun is now ranked inside `LevelLighting.StateAt`,
so its sky trace (`SunFrom`, a 57,016-unit world trace) runs every time an entry is built AND every time an entry is relit on
a light-style change (`Entry`'s `StyleVersion` check calls `StateAt` again). Before the fix the entry's sun was traced once,
when the entry was built, and a relight did not trace it. Read from code, not measured; the coordinator asked for the cost.

## Question

On f12's fight, first person, how many sun traces a frame does the light cache make, and how many milliseconds a frame do they
take, before and after B170 (and its two-pass follow-up)? What share of the frame is the difference?

## Prediction

- The light cache's sun calls a frame rise after, by the entries relit a frame (styles change on cp_process: its flickering
  and switchable lamps), at most a few a frame.
- The sun's time a frame stays under 0.01 ms in both, so the difference is below what this project calls a win or a loss
  (`a-win-above-0-01-ms-counts`).
- The frame mean moves by less than run-to-run noise (about 0.2 ms).

Falsified if the after-runs' sun time a frame exceeds the before-runs' by 0.01 ms or more in both pairs.

## Method

Two builds, interleaved before, after, before, after, the same command, demo and tick, window off screen:

```
TF2VIEW_WINDOW_POS=-3000,100 TF2VIEW_AUTOPLAY=1 pwsh C:/Users/pinku/source/repos/PinKushin/run-exclusive.ps1 \
  <build>/tf2demoview.exe D:/measure-f12/f12.dem --tick 5970 --first-person --measure 20 +fps_max 0
```

Before: `origin/main` @ `0f876af1` (sun traced once per entry build, apart from the slots), built in a worktree at
`D:/measure-f12/b170-before-src`. After: `fix/model-light-count` with the two-pass ranking and `ComputeLighting`'s re-trace,
its Debug build copied to `D:/measure-f12/b170-after`. Each build by `MSBUILDDISABLENODEREUSE=1 dotnet build
managed/Tf2DemoSalvage.Viewer3D`, the summary read.

Read from each report: the frame mean; the `sun per frame` line (calls by caller, ms a frame — `SunMeter`, timed inside
`SunFrom` under `--measure`, `TimeSun`); the pose line's `lighting`; `posed N of M`.

## Control

- Ticks advanced against 66.7 × 20 s, so a run that measured load rather than playback is caught.
- `posed N of M` must agree between before and after: the same workload.
- The sun line's own control: the `direct` and `static props` callers do not change between the builds except by the
  static states built in the window, so a light cache difference is not a meter that moved.
- The sun line must read above zero calls in the before runs (f12 is outdoors in part); zero would mean the meter or the
  map's sun is not wired, not that nothing is traced.

## Environment

## Result

## Verdict
