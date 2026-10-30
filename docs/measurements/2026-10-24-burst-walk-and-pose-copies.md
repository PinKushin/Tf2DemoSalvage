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
labels interleaved in one bench with that record's — `a1` (B591 only, the BEFORE) and `m593` (+B592 +B593, commit
`fd33bc3f`) — three rounds, gummo first person and the empty view, `--measure 30`, quiet gate recorded per run.
**Changed from the plan:** B592 and B593 were measured together, not as separate `a2`/`a3` binaries, and are told
apart by their stage timers (`camera` for B592's particles, `advance` for B593's `AdvanceSample`). The whole-run
`camera particles` part is not in the summary lines, so the `camera` phase stands in for it.

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

| view | timer | a1 (3 runs) | m593 (3 runs) | mean change |
|---|---|---|---|---|
| gummo | frame | 8.52, 8.60, 8.55 | 8.69, 8.59, 8.59 | +0.07 (null) |
| gummo | camera | 6.63, 6.67, 6.62 | 6.83, 6.62, 6.66 | +0.06 (null; the 6.83 run alone) |
| gummo | **advance** | 0.72, 0.72, 0.72 | 0.69, 0.69, 0.69 | **−0.03, all three pairs** |
| empty | frame | 3.37, 3.41, 3.35 | 3.41, 3.37, 3.38 | +0.01 (null) |
| empty | camera | 1.83, 1.87, 1.82 | 1.88, 1.84, 1.86 | +0.02 (null) |
| empty | **advance** | 0.57, 0.57, 0.56 | 0.54, 0.54, 0.54 | **−0.03, all three pairs** |

- **B593 is a real but small gain in `advance`:** 0.03 ms in both views, in every pair, at the timer's resolution. That
  is the stage it was predicted to show in. It does not reach the frame.
- **B592 is a null result.** The `camera` phase did not move outside its spread in either view, including the empty view
  where particles are the largest share. The profile's 2.7-5.4% under the enumerator was GC-poll bias (its fault 4), or a
  cost far below a millisecond. The change stays, because it is equivalent by test and it is no larger than what it
  replaced.
- One m593 gummo run (round 3) had a 34% load sample in its quiet gate; it was not the outlier (8.59 ms).

## Verdict

Partly confirmed, partly falsified. Confirmed: both changes are small (the prediction bound was 0.2-0.3 ms each; the
measured change is at most 0.03 ms), B593 shows in `advance` as predicted (0.72 to 0.69 ms gummo, 0.57 to 0.54 ms empty,
all three pairs in both views), and a null result for the other was the plausible outcome the prediction allowed. Falsified:
B592 was predicted to show in `camera particles`, and the `camera` phase did not move outside its spread in either view
(gummo +0.06, empty +0.02 ms), including the empty view where particles are the largest share. B593 was also predicted to
show in `camera pose`; that stage is not tabulated separately, and the whole `camera` phase it sits in did not move. The profile's 2.7-5.4% under the enumerator was
GC-poll bias or a cost far below a millisecond; neither change reaches the frame (gummo +0.07, empty +0.01 ms, null).

Verdict written 2026-10-29 after the fact from the Result above; the run itself was not repeated.
