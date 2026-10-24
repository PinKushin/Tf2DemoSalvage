# The burst walk by reference (B592) and `ScenePropTrack.At` without whole-pose copies (B593), 2026-10-24

## Observation

The main-thread profile (`docs/measurements/2026-10-24-main-thread-cpu-profile.md`) charged `BulkMoveWithWriteBarrier`
to two callers that copy large reference-holding structs:

- `List<ParticleBurst>.Enumerator.MoveNext`, from `ParticleEffects.Bursts`'s `foreach` by value: 2.7% of samples in
  gummo first person (G), 5.4% in the empty view (E);
- `ScenePropTrack.At`: 2.5% G, 4.9% E. Read: `At` builds a new `ScenePose`, and on the way made about six whole-pose
  copies (`shape`, then `moving`, `toward` and `previous` by `with`, then three more by `with` in `Animated`) for readers
  that take three or two numbers each.

Both shares are upper bounds (the profile's fault 4: exclusive time is biased toward GC-poll sites).

## Question

Do the two copy removals change the frame, and where?

## Prediction

Each is small: the shares above are of a ~2x-perturbed frame, so at most ~0.2-0.3 ms each and likely less.
- B592 should show in `camera particles` (the stage timer covering `ParticleEffects.Bursts`, via
  `camera particles/bursts/step`), most visibly in the empty view where particles are 40% of the phases.
- B593 should show in `advance` (`DemoTimeline.AdvanceSample` runs inside `ViewerSession.Simulate`) and in `camera pose`
  (`MomentPresenter.Show`).
- A null result inside run-to-run spread is a plausible outcome for both.

**Why not `ref readonly` for `At`, as asked:** `At` returns a pose it has just built from history, which is a local; a
ref to it cannot be returned, and every named caller (`DeriveSample`/`AdvanceSample`'s `BuildProp`, `MomentPresenter`,
`BulletStrikes.End`, `SoundPresenter.Stepper`) reads the full pose. The copies worth removing were inside `At`, so the
signature and every caller are unchanged.

## Method

As `2026-10-24-muzzle-flash-log-line.md`: Release builds through `run-exclusive.ps1 -Shared`, copied to the scratchpad;
three labels interleaved in one bench — `a1` (B591 only, the BEFORE for both), `a2` (+B592), `a3` (+B592 +B593) — three
rounds, gummo first person and the empty view, `--measure 30`, quiet gate recorded per run.

## Control

- **Equivalence, B592:** the B577 playback test (`BurstEffectsStepTests.StepOver_LiveOwnedDispatchesOverAPlayback_…`)
  now also folds every frame's `ParticleEffects.Census()` and its build/replay/step/sleep counters into one FNV-1a
  fingerprint, recorded from the by-value walk BEFORE the change (`PlaybackFingerprint`); the by-reference walk must
  reproduce it exactly.
- **Equivalence, B593:** the existing `At` conformance suites in `Core.Tests` (Hermite window, cycle reset, flush newer,
  interpolation neighbours, pose parameters, restated-pose spline, applied time, pose completeness), which predict exact
  values. Sabotage: swap the older and newer cycle in `AnimationNeighbours`, and record which test reddens.
- The empty view is the control for B593's `camera pose` share (few posed props).

## Result

(filled in after the runs)
