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

The owner's desktop, Debug builds, 984×551, window off screen, run 2026-11-12 09:04-09:47 in the order before1, after1,
before2, after2, each under `run-exclusive.ps1`. **The machine was shared** (other agents' builds and tests), and
`Win32_Processor.LoadPercentage` read, before → after each run: before1 52 → 49, after1 74 → 41, before2 45 → 31, after2 68 → 48.
Before: `0f876af1` (worktree `D:/measure-f12/b170-before-src`). After: `fix/model-light-count` @ `1356f556`, its build copied to
`D:/measure-f12/b170-after`.

## Result

`--measure` printed one rate line and one sun line a playback second, 18 and 19 of each a run. Raw output: the run's
background log, `tool-results/b06dl6ykg.txt` in the session's scratch (not committed; 240 lines, 337 KB). Reduced here,
frame-weighted over every sun line of the run, and the median of the rate lines' per-second frame means:

| run | sun calls a frame (light cache) | sun ms a frame | frames on the sun lines | median frame ms (18 lines) | last census tick |
|---|---|---|---|---|---|
| before1 | 0.557 (0.557) | 0.00331 | 3,335 | 5.8 | 7242 |
| after1 | 0.715 (0.714) | 0.00549 | 2,233 | 11.1 | 7253 |
| before2 | 0.671 (0.671) | 0.00558 | 2,448 | 9.9 | 7250 |
| after2 | 0.578 (0.578) | 0.00379 | 3,215 | 5.7 | 7248 |

Per-second sun lines ranged 0.001-0.027 ms in both builds, highest in the first seconds after the seek, when entries are
built. The per-second frame means ran 4.0-454 ms (before1 has one 453.9 ms second) in a loaded machine, so the frame mean is
not resolved between builds.

Controls: ticks advanced 5970 → 7242-7253, about 1,280 of the 1,334 a 20 s window would give, the same in all four runs.
`posed N of M` was 5-47 of 1,847-1,858 in every run (the walk changes it second to second; the ranges agree). The sun line read
calls above zero in every run, so the meter and the map's sun are wired; `static props` 0 and `direct` 0-0.01 in all four.

## Verdict

**Confirmed against the falsifier**: the after builds' sun time is not 0.01 ms a frame above the before builds' in either pair
(after1 − before1 = +0.0022 ms, after2 − before2 = −0.0018 ms); both builds spend at most 0.0056 ms a frame on the sun, about
0.1% of a 5.7 ms frame. The predicted rise in light cache calls was **not seen** (+0.16 and −0.09 calls a frame, inside the
run-to-run spread of 0.11), so a relight's extra trace is below this meter's resolution on f12. Nothing to change: the re-trace
the engine makes stays.
