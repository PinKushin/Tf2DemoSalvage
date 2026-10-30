# Measurement 2026-10-30: the face-flex vertex work after the rewrite, three runs against three

## Record (written and committed before the first run)

## Observation

`2026-10-30-flex-for-cost.md` split `flex` (0.527-0.546 ms a rebuild on f12 tick 30000, gummo first person) into its
parts: the accumulation (`deltas`) 0.203-0.211, the write into the flexed slots (`slots`) 0.156-0.159, the global
weights (`driving`) 0.081-0.092 and the scratch clear (`clear`) 0.029-0.032, at 5.8 faces a rebuild, nearly all misses.
Commit `3b7d98a1` rewrote the three vertex parts without changing an output bit (`FaceFlexIdentityTests`): the
accumulation indexed instead of enumerated through the interface, the clear limited to the vertices the model's flexes
name, and the slot write range-checked once.

## Question

On the same scene and instrument, how much do `deltas`, `clear`, `slots`, `flex`, `place`, the `camera` phase and the
frame mean move between the build before the rewrite (A, `ffd51e49`, the instrument alone) and after (B, `3b7d98a1`),
with the runs interleaved?

## Hypothesis

The accumulation's cost was the interface enumeration, not the arithmetic; the clear's was the whole scratch; the slot
write's was partly its per-slot checks.

## Prediction (before the runs)

Per run, the mean over its `moment cost` lines (the first left out, as before):

1. B's `deltas` at most 0.10 ms in every run (A about 0.20).
2. B's `clear` at most 0.010 ms in every run (A about 0.03).
3. B's `slots` at most 0.13 ms in every run (A about 0.16).
4. B's `flex` at most 0.42 ms in every run, and every B run's `flex` below every A run's.
5. `driving`, `rules` and the counts (faces, misses, vertices) unchanged within the A runs' own spread plus 10%.
6. `camera` and the frame mean: no prediction beyond "B not above A by more than the A runs' spread"; a 0.1-0.15 ms
   change is inside their run-to-run spread (0.47 ms in the baseline), so they are reported, not judged.

## Falsified if

1. Any B run's `deltas` above 0.10, or `clear` above 0.010, or `slots` above 0.13.
2. Any B run's `flex` above 0.42, or the B and A `flex` ranges overlap.
3. B's faces, misses or vertices outside A's range by more than 10% (the scene differs; the comparison is void).

## Instrument

As `2026-10-30-flex-for-cost.md` (`FlexCounters`, the `flex parts` print); both builds carry it unchanged.

## Method

A: `D:/measure-f12/flex-a/tf2demoview.exe` (built for the baseline from a `git archive` of `ffd51e49`). B: a `git archive`
of `3b7d98a1` extracted no-clobber into a new `D:/measure-f12/flex-b-src`, `MSBUILDDISABLENODEREUSE=1 dotnet build
managed/Tf2DemoSalvage.Viewer3D -c Release`, the output copied no-clobber to `D:/measure-f12/flex-b/`. Six runs in the order
A B B A A B, named `04-A 05-B 06-B 07-A 08-A 09-B`, by `2026-10-30-flex-for-cost/all.ps1` with the same `run.ps1` (quiet
gate, then under `run-exclusive.ps1`): `TF2VIEW_AUTOPLAY=1 TF2VIEW_VIEWPORT_SIZE=1280x720 tf2demoview.exe
D:/measure-f12/f12.dem --tick 30000 --spectate gummo --first-person --measure 20 +fps_max 0 +mat_queue_mode 2`. Read with
`summ.ps1`.

## Control

- Each run passes the baseline record's controls (parts within 15% of `flex`, counts consistent, 67 ticks a second).
- The A runs here reproduce the baseline's A runs: `flex` within 0.05 ms of 0.527-0.546.
- Identity at the output is separate from this record: the byte tests, and a paused `--shot` pair per build at tick 30000
  and at a talking-face tick, compared pixel for pixel, with a same-build pair as the comparison's control.

## Environment

Same machine and settings as the baseline; load before and after each run in `runlog.txt`.

## Result

## Verdict
