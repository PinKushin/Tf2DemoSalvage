# Measurement 2026-10-27: detail sprite build, transport bar repaint, B577 one-shots

**Status: written and committed before the run.** Sections above `## Result` are fixed from here; deviations go under Result.

## Observation

From the read-only ranking:

1. `camera setcamera detail` averages 0.44 ms on f12 gummo first person (5% of the frame), but it reads 0.00-0.01 ms for
   about 20 s from tick 30000 and then 0.6-1.0 ms. B580's record (`2026-10-23-b580-detail-reach.md`) measured 20 s windows
   from tick 30000, so it never saw gummo inside detail range.
2. `between` holds 0.32-0.36 ms of transport bar repaint on every run (B572). `ShowTick` wrote the tick label's text and
   accessible name twice per frame, and once even when the tick had not moved.
3. B577's remainder: `dispatches` plus `muzzles` cost 0.30 ms, the same on the empty view.

## Changes measured (branch `perf/detail-transport-b577`)

- **D** (detail): `5faf3cfa` HeapSorts each leaf lane separately and reads through spans. `ae9791ca` makes
  `DetailSpriteLeaves.Add` count per lane.
- **T** (transport): `38e543e8`, which writes the label only when the tick moved, and only once.
- **B** (B577): `73f02aad`, which lets played frames leave out the one-shots `ParticleEffects` has let go of.

**Identity, shown before any timing run** (no viewer was needed):

- D: the `detail-hash` probe on cp_process_f12, 2000 views, 5,437,195 quads. The per-lane set hash is
  `747bd2e36488e1b3` before and after. Order-including hash: `293530ac9f0717a4` → `9be0ad88d4b0ef3c`. With ties broken by
  lump index in both builds the two order-including hashes match (`dc27b769f0b8dc78`), so the only reorder is among the
  79 exact ties, which now take Valve's HeapSort order.
- T: unit tests on the label.
- B: a per-frame state-equivalence test, comparing drawn batch bytes and whizzes (not offers).

## Question

How much of each named part do the three changes remove, in ms and as a % of the frame, on f12 gummo first person and on
the empty view?

## Prediction

- `setcamera detail` falls on the second half of a 60 s window, where gummo is in detail range, from 0.6-1.0 ms to under
  half that. Falsified if the change is smaller than the same-build spread.
- `between` falls by about 0.1-0.3 ms on both subjects. Most frames repeat a tick, so the label write is skipped
  entirely. The TrackBar repaint on a moved tick stays.
- `camera particles/bursts/dispatches` plus `/muzzles` falls from about 0.30 ms by 0.05-0.2 ms on both subjects.
  Followed points are still read every frame, as the engine reads them.

## Method

Two Release builds of the viewer, copied out so both exist at once:

- **A (before)**: `d75ab179` (origin/main at branch point). `D:/measure-f12/dtb-before/`.
- **B (after)**: the branch head with D, T and B. `D:/measure-f12/dtb-after/`.

**Timing.** Each run is one viewer through `run-exclusive.ps1` (exclusive), and only one timed run happens at a time.
ABBA order, then repeated reversed (BAAB), per subject, which gives 4 runs per build per subject:

- gummo: `TF2VIEW_AUTOPLAY=1 TF2VIEW_VIEWPORT_SIZE=1280x720 <exe> D:/measure-f12/f12.dem --tick 30000 --spectate gummo
  --first-person --measure 60 +fps_max 0`. **Only the per-second lines of the second 30 s count** for `detail`, because
  gummo is inside detail range there. The first half is reported separately as the out-of-range control.
- empty view: `TF2VIEW_AUTOPLAY=1 TF2VIEW_VIEWPORT_SIZE=1280x720 TF2VIEW_CAMERA="1613 -2821 154 89 0" <exe>
  D:/measure-f12/f12.dem --tick 30000 --measure 20 +fps_max 0`.

**Quiet gate before each run:** three consecutive `(Get-CimInstance Win32_Processor).LoadPercentage` samples, all under 25%,
and no build running; otherwise wait and repeat. Loads before and after each run are recorded. A run whose after-load
exceeds 25% is reported and marked, not silently dropped. Discarded runs are listed with their reason.

**Reported per run:** frames, frame mean, `camera`, `setcamera detail` (unweighted mean of the per-second lines in the window
named above), `between`, and `camera particles/bursts/dispatches` and `/muzzles`. Each figure is given in ms and as a % of
that run's frame mean, with the range across a build's runs.

## Control

- **Instrument:** ticks advance about 4,000 per 60 s window. The tick reached at the end is stated for every run, and runs
  of one build must agree within the spread.
- **In range:** the gummo `detail` reading in the second 30 s must be well above zero on A (0.6-1.0 ms is expected). If it
  is not, the window did not reach detail range and the detail result is void.
- **Spread:** same-build spread is stated beside every A-B difference. A difference inside the spread is reported as no
  measured change.

## Environment

This machine, 1280x720 windowed, shared with other agents. One timed agent (bones/HUD, finding 105) is running when this is
written, so **no timed run starts before the lead's GO**.

## Result

(not run yet)
