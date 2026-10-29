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

Raw stdout kept in the session scratch directory, not committed. A `8fa05fb4`, M `6ea1d5b6`, B `94217782`; all Release,
0 warnings. **Deviations.** (1) As in the B583 record, stdout went to the one task log rather than the per-run files; the
eighteen runs were split on the eighteen `measured 20 seconds` lines, and each block's `census at tick` lines run
30080 → 31286 in order, so a block is its own run. (2) The quiet gate's samples went to the per-run files (all under 25%).
(3) `moment`/`place`/`reports` are unweighted means of each run's 14-19 `moment cost, mean over 100 rebuilds` lines.

| run | build | subject | end tick | frames | mean ms | alloc MB/frame | gc pause | moment | pose | reports | place |
|---|---|---|---:|---:|---:|---:|---:|---:|---:|---:|---:|
| t01 | A | gummo | 31347 | 2090 | 9.57 | 1.30 | 0.23 | 4.57 | 3.91 | 0.006 | 0.82 |
| t02 | M | gummo | 31345 | 2083 | 9.60 | 1.36 | 0.24 | 4.69 | 4.05 | 0 | 0.85 |
| t03 | B | gummo | 31343 | 2099 | 9.53 | 1.29 | 0.21 | 4.69 | 4.04 | 0 | 0.82 |
| t04 | B | gummo | 31327 | 2054 | 9.74 | 1.36 | 0.23 | 4.76 | 4.11 | 0 | 0.86 |
| t05 | M | gummo | 31347 | 2134 | 9.37 | 1.35 | 0.25 | 4.51 | 3.86 | 0 | 0.81 |
| t06 | A | gummo | 31342 | 2034 | 9.83 | 1.39 | 0.25 | 4.71 | 4.07 | 0 | 0.84 |
| t07 | A | gummo | 31345 | 2084 | 9.60 | 1.36 | 0.24 | 4.61 | 3.99 | 0.006 | 0.82 |
| t08 | M | gummo | 31344 | 2095 | 9.55 | 1.36 | 0.25 | 4.60 | 3.96 | 0 | 0.82 |
| t09 | B | gummo | 31327 | 1674 | 11.95 | 1.40 | 0.33 | 5.27 | 4.55 | 0 | 0.89 |
| t10 | A | empty | 31347 | 5295 | 3.78 | 0.66 | 0.11 | 1.09 | 0.53 | 0 | 0 |
| t11 | M | empty | 31347 | 5282 | 3.79 | 0.66 | 0.11 | 1.08 | 0.52 | 0 | 0 |
| t12 | B | empty | 31346 | 5237 | 3.83 | 0.65 | 0.10 | 1.08 | 0.52 | 0 | 0 |
| t13 | B | empty | 31339 | 5156 | 3.88 | 0.66 | 0.11 | 1.11 | 0.54 | 0 | 0 |
| t14 | M | empty | 31346 | 5279 | 3.79 | 0.66 | 0.11 | 1.12 | 0.54 | 0 | 0 |
| t15 | A | empty | 31341 | 5209 | 3.84 | 0.65 | 0.11 | 1.10 | 0.53 | 0 | 0 |
| t16 | A | empty | 31347 | 5305 | 3.77 | 0.66 | 0.11 | 1.08 | 0.53 | 0 | 0 |
| t17 | M | empty | 31346 | 5248 | 3.81 | 0.64 | 0.10 | 1.09 | 0.53 | 0 | 0 |
| t18 | B | empty | 31347 | 4735 | 4.22 | 0.67 | 0.12 | 1.24 | 0.61 | 0 | 0 |

Ticks advanced 1,327-1,347 per window: the instrument control passes. **t09 and t18 are disturbed** (frames 20% and 10%
below every other run of their subject, every stage up together, t09 with 15 rebuild reports of 19 samples): both are
build B's last run of a subject; read as machine load, and excluded below rather than counted as B's cost.

- **Gummo, frame mean**: A 9.57-9.83, M 9.37-9.60, B 9.53-9.74 (t09 out). Intervals overlap; no build separates.
- **Empty view**: A 3.77-3.84, M 3.79-3.81, B 3.83-3.88 (t18 out). Overlap.
- **`reports`** reads 0 or 0.1 per line (one decimal) in every build; A's mean is 0.006 ms. **The column is below its own
  resolution in the before build**, so B586 has nothing to remove that this instrument can see.
- **Allocation**: gummo A 1.30-1.39, M 1.35-1.36, B 1.29-1.36 MB a frame; empty 0.64-0.66 throughout. No fall.
- ~~Why B585 cannot show: `posed` ~30 props~~ **Withdrawn, see "Added after" below**: `posed` counts props that reached
  bone setup, not instances built.
- **Queue safety (B)**: `+mat_queue_mode 2`, `TF2VIEW_QUEUE_VERIFY=1`, gummo first person, log
  `viewer-20261024-121558-19948.log`: mode confirmed `QueuedThreaded`, final `queue verify: 839000 frames checked, 0
  changed`, no `inputs changed while it was drawn` line. The verifier hashes `instance.Matrix` among the inputs
  (`Device3D.cs` `hash.Floats(instance.Matrix)`); **no positive control was run** (a build that writes a matrix in place).

**Added after the table above (deviations, after a review).**

1. **The instance count was first read from the wrong counter.** `posed` is props that reached bone setup; static props
   never do, yet they reach the reports and the matrix. The carried figure is the moment line's own `collated unparented`,
   246.1-247.0 in every run, both subjects (read where the cull produced it). So the matrices were about 246 × 88 B ≈
   22 KB a frame (*arithmetic*), 1.6% of 1.35 MB and under the same-build allocation spread (0.09 MB).
2. **B585's own timer, `place … lookups = matrix`, was not parsed at first.** Read now, per run (ms): A 0.0118, 0.0113,
   0.0118; M 0.0112, 0.0094, 0.0118; B 0.0218, 0.0235, 0.0229 (t09), 0.0200 (verify run, mode 2). **The change DOUBLED
   its own stage**: a dictionary probe plus a seven-float record compare costs more than allocating 64 bytes. +0.01 ms, under
   any frame resolution, but the sign is wrong.
3. **Control rerun for the two excluded B runs** (both were B's last run of a subject, a pattern), same script and gate,
   order B A B per subject:

| run | build | subject | end tick | frames | mean ms | alloc MB/frame | moment | place | matrix |
|---|---|---|---:|---:|---:|---:|---:|---:|---:|
| t21 | B | gummo | 31347 | 2117 | 9.45 | 1.28 | 4.58 | 0.81 | 0.0212 |
| t22 | A | gummo | 31347 | 2127 | 9.40 | 1.30 | 4.46 | 0.78 | 0.0106 |
| t23 | B | gummo | 31347 | 2135 | 9.37 | 1.29 | 4.44 | 0.79 | 0.0212 |
| t24 | B | empty | 31347 | 5402 | 3.70 | 0.65 | 1.06 | 0 | 0 |
| t25 | A | empty | 31347 | 5244 | 3.81 | 0.66 | 1.10 | 0 | 0 |
| t26 | B | empty | 31342 | 5188 | 3.86 | 0.66 | 1.09 | 0 | 0 |

   B is clean and inside A's range on both subjects, so t09/t18 were load, and B has four clean runs a subject. The
   `matrix` doubling reproduces (0.0212 against 0.0106).
4. **What the verifier covers**: `AddInstances` (`Device3D.cs:3365-3395`) hashes `Matrix` and every bone array, and the
   pose waits for the previous draw (`queue wait FinishDrawBeforePose` on every rate line), which is why the in-place
   `_skinning` bones are safe and why an in-place matrix pool would have been too.

## Verdict

**Null result on the frame for both, and both code-read estimates were wrong.** B586 (estimate 0.15-0.3 ms): `reports`
is below its own resolution (one decimal, 0 or 0.1 per line) in the BEFORE build; ~246 instances × three probes is a few
microseconds (*arithmetic*). B585 (estimate 0.2-0.4 ms, 560 KB): `ModelInstance` is a struct and the matrices were ~22 KB
a frame; allocation did not move, falsifying the prediction, and **its own `matrix` timer doubled, 0.011 → 0.021 ms.**
B586 is kept as the B191 rule. **B585 is a measured regression on its own stage and gains nothing; the recommendation is to
drop commit `94217782`** (left on the branch for the owner's call, not reverted).

Against the Prediction. **B586:** inconclusive. `reports` read 0.006 ms in A and 0 in M and B, so it fell to ~0.00 as
predicted and the frame did not move, but the column's one-decimal resolution cannot show a fall of that size, so the
prediction was never tested at an effect size this instrument resolves. **B585:** the allocation prediction is falsified
(a fall of 0.05-0.15 MB a frame was predicted; allocation did not fall, 1.29-1.36 MB in B against 1.30-1.39 in A, because
the matrices were only ~22 KB), while the prediction that the frame would be a null result is confirmed (the intervals
overlap for every build). The measurement also found what the prediction did not look for: B585's own `matrix` timer doubled.

Verdict extended 2026-10-29 after the fact from the Result above; the run itself was not repeated.
