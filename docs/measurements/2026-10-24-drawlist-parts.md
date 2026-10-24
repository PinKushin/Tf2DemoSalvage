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

## Second split, and the fix (same day)

A nested `first person` part was added inside `filters` (`DrawListParts.FirstPerson`, a stamp after the first-person
block, carried like the others). One gummo run on that build (load 11/5/5), 15 report lines: `filters` 0.30-0.40,
`first person` 0.27-0.37. The block is the whole cost.

**Cause:** `HashSet<SceneProp> kept = [.. seen]`. `SceneProp` is a `sealed record`, so the default comparer hashed and
compared every member of every prop on each add and each `Contains`. **Fix:** `ReferenceEqualityComparer`. The output
cannot change: the drawn list is `seen` itself, which no set touches, and `_undrawn` could differ only for two distinct
value-equal props, which get the same verdict from `FirstPersonVisibility`, so both are in `seen` or neither is.

**Interleaved before/after** (`D:/measure-f12/b578-before`, `b578-after`, same commands; whole-window means over every
report line, n = 14-18; ticks end at 31,277-31,288 in every run):

| run | load | drawlist | filters | first person | gc paused ms/frame |
|---|---|---|---|---|---|
| before gummo | 25/3/43 (one sample over the gate) | 0.557 | 0.346 | 0.323 | 0.36 |
| before empty | 7/9/10 | 0.172 | 0.025 | 0 | 0.13 |
| after gummo | 2/5/15 | 0.219 | 0.059 | 0.039 | 0.33 |
| after empty | 9/6/5 | 0.156 | 0.022 | 0 | 0.12 |
| before gummo | 5/8/7 | 0.456 | 0.300 | 0.279 | 0.34 |
| before empty | 15/2/16 | 0.178 | 0.024 | 0.001 | 0.14 |
| after gummo | 6/5/4 | 0.212 | 0.059 | 0.041 | 0.32 |
| after empty | 3/4/5 | 0.144 | 0.020 | 0 | 0.11 |

`first person` 0.28-0.32 → 0.04 ms and `drawlist` 0.46-0.56 → 0.21-0.22 on gummo; the empty view does not run the block
and does not move. Every part is now under 0.2 ms, so the change-driven per-entity rewrite is not warranted by cost.

**Reading picked:** the first-person verdict depends on the followed target and the first-person toggle, view state no
entity event carries, so re-judging it each moment was not what cost time; the record hash was.
