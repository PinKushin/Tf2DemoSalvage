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
