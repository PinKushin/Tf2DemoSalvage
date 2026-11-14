# B613 review: does the decoder's reused property list bloat every checkpoint?

## Observation

Review of `perf/f12-decode` (Fable, 2026-11-14): `EntityDecoder._reading`, the list `ReadProperties` now reuses
(`e39bdd8d6`), is instance state inside the checkpoint graph (`StateCopy.Of`, `DemoTimeline.Builder.Checkpoint`). It is
cleared before each read, not after, so between reads it holds the last entity's properties: every checkpoint would copy
its backing array at full capacity, with the stale `ElementShapes` arrays it references, and `Reuse.Unchanged` never
shares it because it changes between any two checkpoints. A reading of the code, not a measurement.

## Question

On the f12 demo at the decided checkpoint spacing, how many `DecodedProperty[]` elements, and how many bytes, do the
checkpoints create and keep because of `_reading`, and does clearing it after `OfClass` remove them?

## Prediction

Fixed before the run: with `_reading` left full, the checkpoints create one `DecodedProperty[]` per checkpoint (about 720
for 24 minutes at 2 s) of a few hundred elements each, so a few hundred thousand elements and a few tens of MB kept,
under `decoder` in the kept-by-owner list. Cleared after use, the array is still copied at its capacity (the list keeps
it) but holds no `ElementShapes` references, so the element count stays and only the referenced `int[]` arrays go.

Falsified if the `DecodedProperty[]` created count does not scale with the checkpoint count, or if the bytes are under
1 MB (then the risk is real but immaterial).

## Method

The `timeline-windows` probe's `cost` mode walks f12 into checkpoints and prints, carried from the walk itself, what
checkpoints after the first CREATE by type, the retained heap, and what each owner KEEPS. Release, under the
machine-wide lock, from snapshots of the Release build (as in `2026-11-14-f12-decode-allocation.md`):

```
pwsh C:/Users/pinku/source/repos/PinKushin/run-exclusive.ps1 -TimeoutMinutes 120 \
  tools/Tf2DemoSalvage.Probe/bin/snap-<label>/Tf2DemoSalvage.Probe.exe \
  timeline-windows demostf-cp_process_f12-2026-08-08-2207 2 60 cost
```

`before` is `snap-round3`, the code at `a8ee4c326` (identical but for two comments). `after` is `snap-review2`, the build
with every review item applied: `_reading` cleared after `OfClass`, the objective and beam keys as static `ElementKeys`,
`PointCamera`'s table a const.

**Second question, the same review:** what the whole build allocates after the review's key changes. `snap-review2`'s
`timeline-cost` (which now counts `GC.GetAllocatedBytesForCurrentThread`, not the process-wide count) run twice, and once
under `dotnet-trace collect --profile gc-verbose`, whose tick total is process-wide and so compares with the earlier
8,026 MB:

```
pwsh C:/Users/pinku/source/repos/PinKushin/run-exclusive.ps1 -TimeoutMinutes 120 "C:/Program Files/Git/bin/bash.exe" \
  <scratch>/run.sh review2 trace     # timeline-cost, then the same under gc-verbose
```

## Control

The probe's own: its owners-sum line must say "covers it", and kept-by-owner is printed against the measured retained
heap. A `DecodedProperty[]` row that is absent in `before` would mean the instrument cannot see this array at all, not that
it is absent: the prediction is that it IS present there.

## Environment

The owner's 12-core machine, shared with other agents; worktree `.claude/worktrees/agent-a38c7c0e3f3cf49ea`, branch
`perf/f12-decode`.

## Result

## Verdict
