# Measurement 2026-10-23: detail props reached through the leaf runs near the eye (B580)

**Status: written and committed before the run.** Sections above `## Result` are fixed from here; deviations go under Result.

## Observation

`2026-10-22-f12-camera-breakdown.md` read `camera setcamera detail` at 0.26-0.28 ms a frame on f12 first person, and finding
97 traced it to `DetailSprites.Build` / `DetailModels.Build` walking every detail prop of the map on every move, where the
engine visits only the leaves in a `cl_detaildist` sphere (`detailobjectsystem.cpp:2839-2841`). Commit `4b0d9040` makes the
builds visit only the leaf runs whose box is within the fade maximum (`DetailReach`).

## Question

(1) Are the detail-sprite corners the view hands the draw identical before and after, on f12, tick for tick? (2) How much of
`setcamera detail` does the reach remove, on the empty view and on gummo first person?

## Prediction

`detail` falls from 0.26-0.28 to 0.05-0.15 ms first person (finding 97's estimate, 0.1-0.2 removed); the frame mean moves
by less than its run-to-run spread. Falsified if `detail` does not fall by more than the same-build spread.

## Method

Two Release builds of the viewer from this branch, copied out so both exist at once:

- **A (before)**: `4b0d9040` with the two `Device3D` call sites passing a null reach (the whole-list walk; the reach is still
  built once at load, unused). `D:/measure-f12/b580-before/`.
- **B (after)**: `4b0d9040` as committed. `D:/measure-f12/b580-after/`.

**Equivalence.** Each build twice, mode 0: `TF2VIEW_FRAME_HASH=1 TF2VIEW_FIXED_FRAME=0.015 TF2VIEW_AUTOPLAY=1
TF2VIEW_VIEWPORT_SIZE=1280x720 <exe> D:/measure-f12/f12.dem --tick 30000 --spectate gummo --first-person --measure 6 +fps_max
0 +mat_queue_mode 0`, order A B A B. Each tick ≥ 30000 compared once, by its first frame, on the `cull` hash (every
detail-sprite corner, plus the world/sky/overlay runs). Then one B run in mode 2 with `TF2VIEW_QUEUE_VERIFY=1`: the
`queue verify:` line must read 0 changed.

**Timing.** Interleaved A B B A A B, each one viewer through `run-exclusive.ps1` (exclusive), two subjects:

- empty view: `TF2VIEW_AUTOPLAY=1 TF2VIEW_VIEWPORT_SIZE=1280x720 TF2VIEW_CAMERA="1613 -2821 154 89 0" <exe>
  D:/measure-f12/f12.dem --tick 30000 --measure 20 +fps_max 0`
- gummo: `TF2VIEW_AUTOPLAY=1 TF2VIEW_VIEWPORT_SIZE=1280x720 <exe> D:/measure-f12/f12.dem --tick 30000 --spectate gummo
  --first-person --measure 20 +fps_max 0`

Quiet gate before each run: three consecutive `(Get-CimInstance Win32_Processor).LoadPercentage` samples all under 25% and
no build running, else wait and repeat. Reported: frames, frame mean, `camera`, and the `setcamera detail` part (per-second
lines, unweighted mean).

## Control

- **Hash floor**: A vs A and B vs B must agree on `cull` for every compared tick; if a build disagrees with itself, the
  cull hash is not reproducible and A vs B proves nothing.
- **The hash covers detail corners**: the detail-sprite corner count on the gummo view is non-zero (the `detail sprites:`
  log line), so the cull hash has corners in it.
- **Verifier**: its checked-frame count is in the hundreds.
- **Timing**: ticks advance ~1,333 per 20 s window; same-build spread of `detail` and frame mean is stated beside every
  difference.

## Environment

This machine, 1280x720 windowed, shared with other agents (one was measuring `b577` builds when the gate first ran, so the
first queued run waited). Builds A and B as in Method, both 0 warnings; the two `Tf2DemoSalvage.Render.dll` differ, the
rest are identical. Loads per run are in the tables; none of mine built during the timing runs.

## Result

Raw stdout of every run was kept in the session scratch directory and not committed. The run script and the summariser are
scratch too; the table is their output.

**Deviations, stated.** (1) The first attempt passed the run list as one string, so the script launched A with no
arguments. That viewer played free with no `--measure`; I stopped it (my own PID) and fixed the script. Nothing from it is
used. (2) For h4, the script took the next run's log. h4's own log was found by its start time (23:00:00) and holds 36,335
hash lines. (3) The timing order ran longer than planned: A B B A, then gummo A B B A, then empty A B, then gummo A B. That
gives 3 empty and 3 gummo runs per build, not 2.

**Equivalence: the `cull` hash, mode 0, gummo, first frame per tick ≥ 30000.**

| pair | ticks compared | `cull` equal |
|---|---:|---:|
| A1 vs A2 (floor) | 170 | 170 |
| B1 vs B2 (floor) | 172 | 172 |
| A1 vs B1 | 170 | 170 |
| A1 vs B2 | 170 | 170 |
| A2 vs B1 | 191 | 191 |
| A2 vs B2 | 172 | 172 |

The floor holds, and A equals B on every compared tick.

**Verifier**, B in mode 2 with `TF2VIEW_QUEUE_VERIFY=1`: the last line reads `764000 frames checked, 0 changed`, and the log
has no `inputs changed while` line. The count runs from before the demo loaded (500 at 23:08:19), so it is not all playback
frames. It is still well past "hundreds".

**Timing** (frame-weighted `phases` line for the mean and `camera`; `detail` and `setcamera` are unweighted means of 18
per-second lines; every run ended at tick 31328-31347, so the instrument control passes):

| run | build | subject | load before / after | frames | mean ms | camera | setcamera | detail |
|---|---|---|---|---:|---:|---:|---:|---:|
| t01 | A | empty | 11 10 17 / 19 11 11 | 3906 | 5.12 | 2.50 | 0.01 | 0.000 |
| t02 | B | empty | 12 18 24 / 27 11 28 | 4025 | 4.97 | 2.55 | 0.00 | 0.000 |
| t03 | B | empty | 21 24 23 / 12 11 11 | 4202 | 4.76 | 2.41 | 0.00 | 0.000 |
| t04 | A | empty | 14 14 6 / 8 18 31 | 4417 | 4.53 | 2.24 | 0.00 | 0.000 |
| t05 | A | gummo | 7 14 4 / 10 8 15 | 1503 | 13.31 | 8.40 | 1.58 | 0.551 |
| t06 | B | gummo | 15 5 19 / 11 7 10 | 1668 | 11.99 | 7.39 | 0.99 | 0.002 |
| t07 | B | gummo | 8 7 6 / 15 6 10 | 1586 | 12.61 | 7.79 | 1.04 | 0.002 |
| t08 | A | gummo | 6 10 9 / 10 9 13 | 1510 | 13.25 | 8.53 | 1.54 | 0.551 |
| t09 | A | empty | 16 13 13 / 18 9 3 | 4149 | 4.82 | 2.43 | 0.01 | 0.000 |
| t10 | B | empty | 11 9 14 / 7 8 9 | 4457 | 4.49 | 2.27 | 0.00 | 0.000 |
| t11 | A | gummo | 6 9 0 / 5 5 4 | 1543 | 12.96 | 8.23 | 1.54 | 0.540 |
| t12 | B | gummo | 17 3 5 / 6 9 1 | 1600 | 12.51 | 7.78 | 1.02 | 0.002 |

Some after-loads are 27-31%. They are reported but the runs are not discarded: the method gated only the before-load.

- **Gummo**: `detail` A 0.540-0.551 → B 0.002 (spread within a build 0.011 and 0.000). `setcamera` A 1.54-1.58 → B
  0.99-1.04. Frame mean A 12.96-13.31 (spread 0.35) → B 11.99-12.61 (spread 0.62). The intervals do not overlap; the
  difference of means is 0.80 ms. `camera` falls 0.6-0.8 ms, more than `detail`'s 0.55. That gap is not explained here.
- **Empty view**: `detail` is 0 in both builds. The camera does not move, and `BuildDetailSprites` is gated on the eye, so
  nothing rebuilds. The frame means (A 4.53-5.12, B 4.49-4.97) overlap, which is the null the gate predicts.
- **Control, corners present in the hash:** **not shown.** The `detail sprites:` count line is logged at debug level and was
  not in these logs. The map holds 18,841 detail props (the asset line), and A spends 0.55 ms building them for gummo's view,
  but no logged number says how many corners went into the hash.

## Verdict

**Equivalent and faster. The prediction is falsified on the low side.** The cull hash (every detail corner, plus the world
runs) is identical before and after on every compared tick, 170-191 per pair, with the floor holding. The verifier saw 0
changes. In first person, `setcamera detail` falls from 0.54-0.55 to 0.002 ms. That baseline is twice the 0.26-0.28 the
earlier record read, and the drop is far below the 0.05-0.15 predicted. The frame mean moves 0.8 ms, outside both builds'
spreads, where the prediction said it would stay inside. The empty view does not rebuild detail at all. Still open: the
corner-count control, and why `camera` falls by more than `detail`.
