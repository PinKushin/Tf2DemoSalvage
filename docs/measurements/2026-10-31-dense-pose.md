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

Not run. This record was written, with its predictions, before any measurement. Running it needs the desktop lock and
the viewer, and both were outside this change's scope.
