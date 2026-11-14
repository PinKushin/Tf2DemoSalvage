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

**Checkpoints** (`timeline-windows … 2 60 cost`, 727 checkpoints at 133 ticks, both runs; raw lines, abridged to the
rows that changed and the totals):

```
before (snap-round3)
    created          725 objects      2,969,600 elements  Tf2DemoSalvage.Core.Schema.DecodedProperty[]
  walk 137.5 s; 727 checkpoints; retained heap 1255.9 MB
  a resume copies 42.0 MB on average, largest 55.9 MB
    owner decoder._reading                                  1 objects        4,096 elements  DecodedProperty[]
    kept decoder._reading                              498.5 MB
      decoder._reading               DecodedProperty[]                             498.5 MB
    kept by owners 1219.3 MB against a retained heap of 1255.9 MB
  control: two windows leave their checkpoint unchanged

after (snap-review2)
    (no DecodedProperty[] row among the types created)
  walk 133.6 s; 727 checkpoints; retained heap 758.0 MB
  a resume copies 42.0 MB on average, largest 55.9 MB
    owner decoder._reading                                  1 objects        4,096 elements  DecodedProperty[]
    (no decoder._reading row among the owners kept; frames 223.7 MB now leads, as before)
    kept by owners 721.5 MB against a retained heap of 758.0 MB
  control: two windows leave their checkpoint unchanged
```

Every other created and kept row is identical between the two runs. Full outputs: the scratch files `ckpt-before.txt` and
`ckpt-after.txt`. The owners-sum control said "covers it" in both.

**The whole build's allocation after the review** (`snap-review2`, `timeline-cost`, per-thread counter, two runs; then the
gc-verbose trace's process-wide tick total):

```
demostf-cp_process_f12-2026-08-08-2207 58.2 85.54s 0.03s 0.01s 0.42s 42.68s 17.08s 0.23s 24.75s 0.56s 96589 4755 13 0.09 1.52s 8025MB
demostf-cp_process_f12-2026-08-08-2207 58.2 93.38s 0.03s 0.01s 0.41s 50.06s 17.36s 0.23s 24.95s 0.56s 96589 4755 13 0.11 1.52s 8025MB
trace: 74533 large ticks over 87.0 s; 8085 MB   (round3's trace: 8086 MB)
```

## Verdict

**The reused list was 498.5 MB of the f12 checkpoints' 1,255.9 MB retained heap, and clearing it after `OfClass` removes
all of it: retained heap 758.0 MB, every other owner unchanged.** The prediction is **falsified on size**, in the
direction that made the review right: not "a few tens of MB" but half a gigabyte, because the list's capacity reached
4,096 entries (the largest entity read) and each checkpoint copied all 4,096 at about 170 bytes apiece, 725 times. Cleared,
the backing array is all defaults, the next checkpoint finds it unchanged and shares it, and no `DecodedProperty[]` is
created after the first. A resume still copies one 4,096-entry array (the "owner" row), as predicted.

The review's key changes (objective arrays as static `ElementKeys`, beam ends, `PointCamera`'s const table) save **no
measurable bytes**: 8,025 MB per-thread and 8,085 MB in the trace, against 8,026 and 8,086 before. That is as it should be
for the objectives, whose keys were already cached; what they remove is a `ConcurrentDictionary` lookup and a string hash
per key, about 150 a frame, which is CPU. The per-thread counter read 8,025 MB twice. The earlier file's "deterministic to
the megabyte" holds for the final binary but not in general: the baseline moved by 19 MB between runs, so the probe's
comment now says what still lands in the count.
