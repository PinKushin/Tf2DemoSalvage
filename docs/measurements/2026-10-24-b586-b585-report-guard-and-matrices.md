# Measurement 2026-10-24: one Debug read guards the reports, and an unchanged prop keeps its matrix (B586, B585)

**Status: written and committed before the run.** Sections above `## Result` are fixed from here; deviations go under Result.

## Observation

A code read (RISKS B586, B585, 2026-10-24) found two per-prop per-frame costs. B586: `ModelReports.BrushMoved`, `Lit` and
`Animating` key a dictionary or set for every prop before any `IsEnabled` check (estimate 0.15-0.3 ms). B585: the instance
build allocates per prop (estimate 0.2-0.4 ms, "~560 KB a frame").

**Corrected on reading before this record** (read from code): `ModelInstance` is a `readonly record struct`
(`EntityModels.cs:112`), so `new ModelInstance` allocates nothing on the heap. The only per-instance allocation in scope is
`PropTransform.ToMatrix()`'s `float[16]` (~88 B), once per prop and once more per econ attachment. The B585 byte figure was
inflated by the struct.

## Question

How much do the two changes move frame time, `reports` and `place`, and allocated MB a frame, on f12 gummo first person and
the empty view?

## Prediction

- **B586**: `reports` falls to ~0.00 ms (Debug is off in a measured run) from whatever it is in A; frame mean moves by no more
  than that. Falsified if `reports` does not fall, or the frame falls by more than 0.3 ms with no stage carrying it.
- **B585**: allocation falls by ~0.05-0.15 MB a frame (≈ 600-1,600 instances × 88 B, most of them static props whose
  transform does not change). Frame time: **a null result is the likely outcome** (gc pause is 0.2-0.3 ms total; this is a
  small share). Falsified if allocation does not fall at all, or falls by more than 0.3 MB a frame.

## Method

Three Release builds of the viewer, copied out so all exist at once:

- **A**: `origin/main` `8fa05fb4`. `D:/measure-f12/b586-before/`.
- **M**: `6ea1d5b6`, B586 alone. `D:/measure-f12/b586-mid/`.
- **B**: this branch's B585 head. `D:/measure-f12/b586-after/`.

Timing interleaved A M B B M A A M B (three of each) per subject, each one viewer through `run-exclusive.ps1`:

- empty view: `TF2VIEW_AUTOPLAY=1 TF2VIEW_VIEWPORT_SIZE=1280x720 TF2VIEW_CAMERA="1613 -2821 154 89 0" <exe>
  D:/measure-f12/f12.dem --tick 30000 --measure 20 +fps_max 0`
- gummo: `TF2VIEW_AUTOPLAY=1 TF2VIEW_VIEWPORT_SIZE=1280x720 <exe> D:/measure-f12/f12.dem --tick 30000 --spectate gummo
  --first-person --measure 20 +fps_max 0`

Quiet gate before each run: CPU load samples under 25%, no build of mine running. Reported: frames, frame mean, gc alloc
MB/frame, `moment`, `pose`, `reports`, `place` from the moment-cost line.

**B585 safety, not timing:** one mode-2 run (`+mat_queue_mode 2`, `TF2VIEW_QUEUE_VERIFY=1`) of build B, gummo first person.

**No new timer.** Both changes sit inside stages already timed where the work runs (`reports`, `place`).

## Control

- Same-build spread of every reported column beside each difference.
- Ticks advanced ~1,333 per 20 s window.
- Equivalence is held by unit tests (Scene.Tests: reports still print when Debug is on, and keyed nothing while it was off;
  an unchanged prop reuses an array whose contents equal `ToMatrix()`, a moved prop gets a new array and the old one is
  untouched), not by this record.

## Environment

This machine, 1280x720 windowed, shared with other agents.

## Result

(after the runs)
