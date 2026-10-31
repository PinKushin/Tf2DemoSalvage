# Dense pose through accumulate: before and after (2026-10-31)

**Question.** The branch `perf/dense-pose-accumulate` stops re-expanding a pose that is already dense: one entry per bone, in
bone order. The expansions it removes are in four places:

- each grid `StudioPoseBlend.BlendInto`
- `SkeletonPose.Accumulate`'s rest seed plus overlay
- `Adjust`'s seed plus overlay
- the `_overrideOf` index

Valve keeps `pos`/`q` dense from `InitPose` to `BuildTransformations` (`bone_setup.cpp:1782`, `BlendBones` :1531, `SlerpBones`
:1373, `AccumulatePose` :2393, `CalcBoneAdj` :2462 in place). Does removing the expansions lower the `roots split` parts it touches,
with output unchanged?

Output identity is held by two differential tests, not by this record:

- `DensePoseBlendDifferentialTests` compares 188,256 real grid blends plus every synthetic input shape, bitwise.
- `DensePoseAccumulateDifferentialTests` compares 504 real builds (four players and the sentry) plus every synthetic shape,
  matrix by matrix, bitwise.

Why each removal changes no bit, and where each one lands in the timers: `2026-10-31-setup-bones-read.md` §5.

**Before.** The task's before-numbers:

| part | before | from |
|---|---|---|
| accumulate remainder (accumulate − layer sampling) | 0.077 | `2026-10-31-setup-parts.md`, run means 0.070-0.088 |
| roots total | 0.495 | `2026-10-31-frame-pair-one-pass.md`, second after-set |
| base + layer sampling | 0.274 | `2026-10-31-frame-pair-one-pass.md`, second after-set |
| adjust | 0.004-0.005 | `2026-10-31-setup-parts.md` |

The 0.077 is older than the frame-pair change. The second after-set's own accumulate remainder can be read from
`2026-10-31-frame-pair-one-pass/after-review/01.txt`..`03.txt` with the same reader, and it is the fairer before for
prediction 2 if it differs.

## Command (not yet run)

The command is copied from `2026-10-31-frame-pair-one-pass/after-review/all.sh`. Two lines differ:

- `out` points at this record's directory.
- `repo` must be a checkout of this branch's commit, not the owner's main worktree.

Read the results with `2026-10-31-setup-parts/summ.ps1 -Dir`.

```bash
#!/usr/bin/env bash
# Three runs of the command in ../2026-10-31-setup-parts.md "Method", each built in the same command it runs.
repo=<checkout of perf/dense-pose-accumulate>
out="$repo/docs/measurements/2026-10-31-dense-pose"
for n in 01 02 03; do
  MSBUILDDISABLENODEREUSE=1 dotnet build "$repo/managed/Tf2DemoSalvage.Viewer3D" -c Release > "$out/$n-build.txt" 2>&1 && \
  TF2VIEW_WINDOW_POS=-3000,100 TF2VIEW_AUTOPLAY=1 TF2VIEW_VIEWPORT_SIZE=1280x720 \
    pwsh C:/Users/pinku/source/repos/PinKushin/run-exclusive.ps1 \
    "$repo/managed/Tf2DemoSalvage.Viewer3D/bin/Release/net10.0-windows/tf2demoview.exe" \
    D:/measure-f12/f12.dem --tick 30000 --spectate gummo --first-person --measure 20 +fps_max 0 +mat_queue_mode 2 \
    > "$out/$n.txt" 2> "$out/$n.err"
  echo "run $n exit=$?" >> "$out/status.txt"
done
```

## Predictions (made before any run; all are predictions)

**Where they come from.** §5 of the read counts about 32 removable full-skeleton passes per player build, at 78 bones each,
and about 157 per frame over 4.9 players **[interp]**.

- 28 of the 32 are grid-blend passes inside `_animation`. Those land in **base** and **layer sampling**.
- 2 are `Accumulate`'s seed and overlay, and only those land in the **accumulate remainder**.
- 2 are the `_overrideOf` fill and index, which land in **adjust**.

At 0.1-0.4 µs a pass **[interp]**, the whole saving is 0.016-0.063 ms per frame.

| # | Prediction | Falsified if |
|---|---|---|
| 1 | base + layer sampling falls from 0.274 to 0.225-0.265 (a fall of 0.01-0.05) | the three-run mean is above 0.274, or below 0.20 |
| 2 | the accumulate remainder moves by less than 0.01 from its before, because only 2 of the ~32 passes are in it | it falls by more than 0.02. That would mean something besides the seed and overlay moved, so look before believing it. |
| 3 | adjust stays at or below 0.004 | it is above 0.005 |
| 4 | roots total falls by 0.015-0.06 from 0.495 | the mean is above 0.495, or below 0.42 |
| 5 | blend, loop, ik and finish stay within ±0.02. Layer samples per rebuild, roots built, and eligible stay unchanged (±1). | any part moves by more than 0.02 (the run is suspect), or any count moves by more than 1 (the runs are void) |

**What would make prediction 1 wrong in the expected direction:**

- the per-pass cost is lower than assumed, because the rest seed's 136-byte `StudioBone` copies may be cheaper than modelled
- fewer samples are grids than assumed

Either would show as a fall below 0.01. The prediction does not exclude that outcome; it would put the change under the
0.01 ms bar of `docs/memory/a-win-above-0-01-ms-counts-the-target-is-1000-fps.md`.

**Controls and void conditions** are those of `2026-10-31-setup-parts.md`:

- `prepose 0 over 0`
- roots built 8-21, eligible 4-7
- the parts plus `unsplit` sum to the roots total
- `unsplit` ≥ 0 and ≤ 0.05
- layer sampling ≤ accumulate

## Result

Three runs of the command above (merge `e434a205`, `2026-10-31-dense-pose/`, driver `all.sh`, reader `summ.ps1 -Dir`), off
screen, no void condition (`prepose 0 over 0`; roots built 13.1-13.8; eligible 4.9-5.0; samples 12.4-13.2; `.err` empty), set beside the
frame-pair second after-set (`after-review/`), the same demo, tick, build configuration and instrument. Means per run
(n = 16-17), ms per rebuild:

| part | before 01 / 02 / 03 | after 01 / 02 / 03 | mean before → after |
|---|---|---|---|
| base + layer sampling | 0.268 / 0.273 / 0.280 | 0.268 / 0.259 / 0.253 | 0.274 → 0.260 |
| accumulate remainder | 0.065 / 0.070 / 0.070 | 0.067 / 0.060 / 0.062 | 0.068 → 0.063 |
| adjust | 0.004 / 0.005 / 0.004 | 0.002 / 0.003 / 0.002 | 0.0043 → 0.0023 |
| roots total | 0.481 / 0.497 / 0.507 | 0.492 / 0.471 / 0.461 | 0.495 → 0.475 |
| `anim` | 0.171 / 0.169 / 0.181 | 0.169 / 0.171 / 0.169 | 0.174 → 0.170 |
| blend / loop / ik | 0.074 / 0.040 / 0.026 | 0.074 / 0.039 / 0.026 | unchanged |

**Predictions:** 1 holds (sampling 0.260, inside 0.225-0.265). 2 holds (remainder −0.005, under the 0.01 line). 3 holds (adjust
0.0023). 4 holds (roots −0.020, inside 0.015-0.06). 5 holds (blend, loop and ik move by ≤ 0.001; counts within ±1).

## Second after-set (merge `ae7053e8`, after the review batch) - predictions made before the run

The review batch changed production code: `DenseOf` carries "known dense" for `_layered` and `_adjusted` so `Adjust` and `Build`
no longer re-scan them, `AsDense` lost its `bones <= 0` test, `BlendExpanded` gained a length guard. Three more runs into
`2026-10-31-dense-pose/after-review/`. Predictions [all predictions]: roots 0.455-0.485 (the first set's mean 0.475 less up to
0.02 for the dropped scans; the scans are about 78 compares each, so under 0.005 ms), adjust ≤ 0.003, base + layer sampling
0.245-0.270. **Falsified if** roots is above 0.50 (the carry or the guard cost more than they saved) or below 0.44; the effect of
the carry alone is below the noise and is not claimed.

### Result of the second after-set

`after-review/01.txt`..`03.txt` (n = 16 each; no void condition: `prepose 0 over 0`, roots built 13.3-13.9, eligible 4.9-5.0, samples
12.6-13.2, `.err` empty). Per run, ms per rebuild:

| part | 01 | 02 | 03 | mean | first dense set | before (frame-pair after-review) |
|---|---|---|---|---|---|---|
| base + layer sampling | 0.277 | 0.270 | 0.261 | 0.269 | 0.260 | 0.274 |
| accumulate remainder | 0.069 | 0.067 | 0.064 | 0.067 | 0.063 | 0.068 |
| adjust | 0.0020 | 0.0020 | 0.0020 | 0.0020 | 0.0023 | 0.0043 |
| roots total | 0.516 | 0.489 | 0.471 | 0.492 | 0.475 | 0.495 |

**Against the prediction:** roots 0.492 is above the predicted 0.455-0.485 and below the 0.50 falsifier (not falsified, not
confirmed); sampling 0.269 is at the top of 0.245-0.270; adjust 0.0020 holds.

## Verdict (final)

Honest reading of all six runs: **the roots total did not move measurably** (0.495 before; 0.475 and 0.492 after, with a
single set's own spread of 0.03-0.05 and runs that drift within a set). Base + layer sampling is lower in five of six after
runs against the 0.274 before mean (0.253-0.277), a move of about −0.01 ms, and `adjust` fell from 0.0043 to 0.0020, which is
mechanical and certain but 0.002 ms. The change is at the 0.01 ms bar and cannot be called a measured win beyond that. It stays
because the output is bit-identical (two differentials, 188,256 real grid blends and 504 real builds) and it removes real
passes, but it is **not** the 0.03-0.08 ms the read hoped for: the cost the read saw in "expansions" was smaller than
interpolated. The remainder (0.067) is not the grid blends or the seed; the boxed enumerators (M6) and the layer arithmetic
are what is left. Next, measure before cutting: time `Accumulate`'s own parts, not its expansions.

## Verdict (first set only, kept for the record)

A small win

A small win in the predicted direction: roots −0.020 ms per rebuild (−4%), sampling −0.014, adjust −0.002, the accumulate remainder
−0.005. Above the 0.01 ms bar but near the noise: the paired runs go 0.268→0.268, 0.273→0.259, 0.280→0.253 in sampling and
0.481→0.492, 0.497→0.471, 0.507→0.461 in roots, so one pair is flat and one is reversed, and the run-order drift seen in earlier sets
(the set's last run lower or higher by 0.03-0.05) is as large as the effect. Reported as "about −0.02 ms, measured, noisy",
not as a confirmed number. The change stays: output is bit-identical by two differentials and the removal is mechanical. The
remainder is still 0.063, so the boxed enumerators the read lists as M6 are the next place to look, not the grid blends.
