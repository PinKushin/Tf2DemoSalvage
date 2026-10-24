# 2026-10-24 — the four parts of the moment's `drawlist` (B578)

## Observation

`drawlist` measured 0.4-0.5 ms per rebuild on f12 (B578, finding 97), an upper bound that also held `PlayerProps.Add`.
`main` (7611bf9f) now splits it into `prepare`, `players`, `place`, `filters` (`MomentScene.Build`, carried into
`MomentCostLog` through `MomentPhases.DrawListParts`).

## Question

Which part holds the time, and is `filters` (the per-moment first-person, weapon, disguise, respawn-room and vision
judgments) at or above ~0.2 ms per rebuild?

## Prediction

Written before the runs: `players` is the largest part (per-player prop assembly); `filters` is 0.05-0.15 ms, under the
0.2 ms threshold, on both scenes.

## Method

Build `01faad62` (origin/main, beta.50), Debug, through `run-exclusive.ps1 -Shared`. Quiet gate before each run (three
`(Get-CimInstance Win32_Processor).LoadPercentage` samples under 25%, no build running). Two runs per scene, alternating,
each through `run-exclusive.ps1`:

- empty: `TF2VIEW_AUTOPLAY=1 TF2VIEW_VIEWPORT_SIZE=1280x720 TF2VIEW_CAMERA="1613 -2821 154 89 0" tf2demoview.exe
  D:/measure-f12/f12.dem --tick 30000 --measure 20 +fps_max 0`
- gummo: `TF2VIEW_AUTOPLAY=1 TF2VIEW_VIEWPORT_SIZE=1280x720 tf2demoview.exe D:/measure-f12/f12.dem --tick 30000
  --spectate gummo --first-person --measure 20 +fps_max 0`

Figures read: the `moment cost, mean over N rebuilds` line's `drawlist (prepare, players, place, filters)`.

## Control

- Ticks advanced ~1,330 per 20 s window.
- The four parts should sum to `drawlist` within rounding; a gap means an untimed span.
- Two runs per scene give the spread; a difference inside it is not claimed.

## Results

Run order empty1, gummo1, empty2, gummo2; quiet gate loads 23/12/13, 9/8/3, 23/9/14, 13/24/14 %. Parts in ms per rebuild:

| run | drawlist | prepare | players | place | filters |
|---|---|---|---|---|---|
| empty1 | 0.1 | 0.05 | 0.06 | 0.01 | 0.02 |
| empty2 | 0.2 | 0.08 | 0.10 | 0.01 | 0.02 |
| gummo1 | 0.5 | 0.08 | 0.09 | 0.01 | 0.29 |
| gummo2 | 0.5 | 0.06 | 0.07 | 0.01 | 0.32 |

Controls: the parts sum to `drawlist` within its one-decimal rounding (0.14 vs 0.1-0.2, 0.47 vs 0.5). Final census at
ticks 31,216-31,288, so about 1,280 ticks per window (not the ~1,330 written in the control). **Capture caveat:** the
pipeline kept only each run's LAST `mean over 100 rebuilds` line, about 1.5 s of the 20 s window, not the window mean;
the 15x gap between scenes is far outside that.

**The prediction missed.** `filters` is the largest part on gummo (0.29-0.32 ms), not `players`, and over the 0.2 ms
threshold. It is 0.02 ms in the empty view, which runs every filter except the first-person block, so about 0.27 ms is
inside `if (info.FirstPerson && info.Followed …)` in `MomentScene.Build`. Split next (B578).
