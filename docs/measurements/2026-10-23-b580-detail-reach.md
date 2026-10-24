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

## Result

## Verdict
