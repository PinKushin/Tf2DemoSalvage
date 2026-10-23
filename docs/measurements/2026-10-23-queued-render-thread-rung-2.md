# Measurement 2026-10-23: `mat_queue_mode 2`, rung 2 against rung 1 and mode 0 (B581, D228)

## Record (written and committed before the first run)

## Observation

Rung 1 (`2026-10-23-queued-render-thread-rung-1.md`) waited for the previous draw before `PlaceCamera` and measured mode 0 at
13.34-13.69 ms and mode 2 at 11.79-12.00 ms on f12 first person, a saving of 1.34-1.90 ms (mean 1.62). Rung 2 (merged as
`ed520251`, review fixes to `e45c937e`) moved the wait to the pose's entry, so the camera cull overlaps the previous draw
(finding 98 section 9). Nobody has timed rung 2. The wait is now reported as `camera wait` under `camera`, not under `draw`.

## Question

On f12 from tick 30000, first person on gummo, how many ms per frame does rung 2 (mode 2, `e45c937e`) save over rung 1
(mode 2, `fd6ed0a6`) and over mode 0 (`e45c937e`)? And how much `camera wait` remains for rung 3 (double-buffering pose,
particles and HUD so the wait can move to the hand-off) to win?

## Prediction (arithmetic, before the runs)

The camera stage is about 8 ms and the draw about 2.2 ms. Rung 1 left about 0.6-0.7 ms of game-thread wait inside `draw`. If
the cull now overlaps the draw, that wait should mostly vanish: rung 2 saves 0.3-0.7 ms over rung 1, so mode 2 runs about
11.1-11.6 ms, with `camera wait` near 0.1 ms. Falsified if R2 is not faster than R1 by more than the same-condition spread.

## Method

Binaries, each a Release build through `run-exclusive.ps1 -Shared dotnet build`, `MSBUILDDISABLENODEREUSE=1`:

- `e45c937e` (origin/main) in this worktree: conditions **M0** (`+mat_queue_mode 0`) and **R2** (`+mat_queue_mode 2`).
- `fd6ed0a6` (the rung-1 merge) in a separate worktree `D:/r1meas`: condition **R1** (`+mat_queue_mode 2`).

Each run, one viewer, exclusive:

`TF2VIEW_AUTOPLAY=1 TF2VIEW_VIEWPORT_SIZE=1280x720 pwsh C:/Users/pinku/source/repos/PinKushin/run-exclusive.ps1 <exe>
D:/measure-f12/f12.dem --tick 30000 --spectate gummo --first-person --measure 20 +fps_max 0 +mat_queue_mode <0|2>`

Order, interleaved: M0 R2 R1 R1 R2 M0 M0 R2 R1 (three of each). Quiet gate: immediately before each run three consecutive
`Get-CimInstance Win32_Processor` `LoadPercentage` samples all under 25% and no build running; load sampled again after.
A run whose gate fails, or whose after-load is not quiet, is discarded and repeated. Raw stdout of each run is kept in
`2026-10-23-queued-render-thread-rung-2/`.

Reported per run: frames, frame mean (ms), every phase column including `camera wait`, `draw` (game thread), `render thread
draw`, and `frame - phases`. Savings are compared on the TOTAL frame mean, because the wait moved between columns. Each
condition's spread (max - min across its repeats) is reported; a difference inside the spread is called that.

**Deviation from the task's wording, stated:** the task named "f12 under tools/corpus/local"; lcor has no file named f12, and
the f12 every earlier frame measurement (rung 1 included) used is `D:/measure-f12/f12.dem`. That file holds the subject constant.

## Control

(a) Mode 0 prints no `queue` part and a `frame - phases` of 0.00. (b) In mode 2 `render thread draw` is about the size of
M0's `draw` (2-3 ms); near zero means the draw did not go to the thread. (c) In mode 2 the queue verifier line must read
0 changed; if the line does not print without `TF2VIEW_QUEUE_VERIFY=1`, one extra R2 validation run with it set is made and
its checked-frame count must be in the hundreds or more.

## Environment

This machine, 1280x720 windowed, Release builds of `e45c937e` (this tree) and `fd6ed0a6` (`D:/r1meas`, clean), both 0
warnings. Loads per run are in the table. The quiet gate ran BEFORE `run-exclusive.ps1` acquired the lock, so a lock wait
would make "before" describe the queue; no run waited visibly (start and end times in each raw file).

## Result

Raw stdout of every run, discarded ones included: `2026-10-23-queued-render-thread-rung-2/NN-<cond>.txt`; the summariser
that produced the table is `summ.sh` beside them. Order run: M0 R2 R1 R1 R2 M0 M0 R2 R1, then repeats.

| # | cond | load before % | load after % | frames | mean ms | camera | `camera wait`* | draw (game) | render thread draw | advance | hud | between | frame - phases |
|---|---|---|---|---|---|---|---|---|---|---|---|---|---|
| 01 | M0 | 13 15 12 | 22 16 17 | 1541 | 12.98 | 8.13 | 0 | 2.15 | 0.00 | 1.10 | 0.88 | 0.47 | 0.01 |
| 02 | R2 | 5 3 4 | 19 **28** 15 | 1794 | 11.15 | 8.33 | 0.03 | 0.01 | 2.29 | 1.18 | 0.94 | 0.42 | -0.01 | **discarded** (after) |
| 03 | R1 | 8 16 9 | 23 23 19 | 1689 | 11.84 | 8.17 | - | 0.75 | 2.31 | 1.23 | 0.96 | 0.47 | 0.01 |
| 04 | R1 | 19 19 15 | **30** 12 12 | 1790 | 11.17 | 7.75 | - | 0.81 | 2.25 | 1.15 | 0.85 | 0.40 | 0.00 | **discarded** (after) |
| 05 | R2 | 10 13 6 | 9 7 7 | 1770 | 11.30 | 8.53 | 0.09 | 0.01 | 2.33 | 1.15 | 0.97 | 0.42 | 0.00 |
| 06 | M0 | 5 3 7 | 11 10 9 | 1530 | 13.07 | 8.13 | 0 | 2.18 | 0.00 | 1.09 | 0.89 | 0.53 | 0.01 |
| 07 | M0 | 13 19 15 | 10 5 9 | 1531 | 13.07 | 8.26 | 0 | 2.14 | 0.00 | 1.08 | 0.88 | 0.46 | 0.01 |
| 08 | R2 | 7 7 6 | 5 9 5 | 1804 | 11.09 | 8.34 | 0.06 | 0.01 | 2.28 | 1.16 | 0.94 | 0.42 | 0.01 |
| 09 | R1 | 7 8 5 | 11 13 13 | 1764 | 11.34 | 7.78 | - | 0.78 | 2.24 | 1.16 | 0.94 | 0.46 | 0.00 |
| 10 | R2 | 9 17 10 | 11 14 7 | 1761 | 11.36 | 8.57 | 0.05 | 0.01 | 2.35 | 1.20 | 0.93 | 0.42 | 0.00 |
| 11 | R1 | 15 3 8 | 10 2 8 | 1762 | 11.35 | 7.79 | - | 0.79 | 2.30 | 1.20 | 0.93 | 0.42 | 0.00 |

\* `camera wait` is not in the frame-weighted phases line; it appears only in the per-second parts, each a mean over the last
50 frames. The column is the UNWEIGHTED mean of those 18 samples. R1 has no such part (its wait is inside `draw`). Sound
(0.21-0.28) is in the raw files; capture and project are 0.00 throughout.

**Controls.** (a) Mode 0 prints no queue part; every run's phases sum to its mean within 0.01 ms. (b) `render thread draw`
2.24-2.35 ms in every mode-2 run, against M0's `draw` 2.14-2.18: the draw went to the thread. (c) Validation run 12-V (R2,
`TF2VIEW_QUEUE_VERIFY=1`, load 8 5 22 / 12 15 15): the verifier line is NOT on stdout; it is in the viewer log
`%LOCALAPPDATA%/Tf2DemoSalvage/viewer-20261023-160514-46100.log`, ending `queue verify: 878000 frames checked, 0 changed`,
with 0 `inputs changed` lines. The count includes loading frames, as in rung 1. The run is slow (22.01 ms) because of the
hashing, so it is not a timing run.

**Per condition, quiet runs only** (M0: 01 06 07; R2: 05 08 10; R1: 03 09 11):

| cond | frame mean range | mean | spread | fps (mean) | camera | draw (game) |
|---|---|---|---|---|---|---|
| M0 | 12.98-13.07 | 13.04 | 0.09 | 76.7 | 8.13-8.26 | 2.14-2.18 |
| R1 | 11.34-11.84 | 11.51 | 0.50 | 86.9 | 7.78-8.17 | 0.75-0.79 |
| R2 | 11.09-11.36 | 11.25 | 0.27 | 88.9 | 8.34-8.57 | 0.01 |

- **R2 vs M0:** saves 1.62-1.98 ms, mean 1.79 (about 14%, 76.7 to 88.9 fps). Far outside both spreads.
- **R1 vs M0:** saves 1.14-1.73 ms, mean 1.53, consistent with the rung-1 record (1.34-1.94).
- **R2 vs R1:** mean 0.26 ms (86.9 to 88.9 fps), but the ranges overlap: R2 11.09-11.36, R1 11.34-11.84. R1's spread
  (0.50) comes from run 03 alone; its other two runs read 11.34-11.35, against which R2 is -0.26 to +0.02. **The
  difference is inside the spread; rung 2's saving over rung 1 is not established by this set.** The prediction of 0.3-0.7
  ms is not met.
- **Where the wait went.** The game thread's `draw` falls from 0.75-0.79 (R1) to 0.01 (R2), as designed, but `camera`
  rises from 7.78-8.17 to 8.34-8.57 (means 7.91 to 8.48, +0.57), while `camera wait` is only 0.03-0.09. So about 0.5 ms
  of the camera's growth is NOT waiting. That is observed, not explained. Candidates, untested: the cull now runs while the
  render thread draws, and contends with it for cache and memory bandwidth; or the camera's lower R1 figure was the
  post-draw effect the rung-1 record noted (R1's camera is below M0's here too).

## Verdict

Rung 2 runs mode 2 at 11.25 ms (88.9 fps) against mode 0's 13.04, a 1.79 ms saving, but against rung 1 it gains at most
about 0.26 ms, inside the run-to-run spread, because the camera stage got about 0.5 ms slower once it overlapped the draw.
The remaining `camera wait` is 0.03-0.09 ms (unweighted), so **rung 3's ceiling for removing the wait itself is about 0.1 ms
a frame**; anything more from rung 3 would have to come from undoing the camera's slowdown, which this measurement does not
attribute. Before rung 3, measure why `camera` grows in R2 (its parts, R1 vs R2, frame-weighted).
