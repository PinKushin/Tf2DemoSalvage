# Measurement 2026-10-22: does removing one struct-copy site move its timer (B574)

## Observation

In the trace T1 (`2026-10-22-f12-camera-breakdown.md`, `T1-17308-bulkmove.txt`), 7.8% of the render thread's
`BulkMoveWithWriteBarrier` samples sit under `List<ParticleBurst>.Enumerator.MoveNext` inside `ParticleEffects.Bursts`.
That is about 138 of 8,028 samples (1.7% of a 12 ms frame, about 0.2 ms). Every one of those samples has `PollGCWorker`
as its leaf. B574 asks whether such samples are time spent copying or the sampler's suspension charged at a safe point.

## Question

`ParticleEffects.Bursts` walks the frame's bursts with `foreach` over an `IReadOnlyList<ParticleBurst>`. That copies each
`ParticleBurst` (a record struct of about ten fields, most of them references) out of the list. If it walks them by
reference instead, how much does its own timer, `camera particles/bursts/step`, fall?

## Prediction (two readings, fixed before the run)

- **If the trace's samples are copy time:** `bursts/step` falls by about 0.2 ms, from about 0.35-0.36 to about 0.15.
- **By arithmetic** (*arithmetic*, not measured): f12's census holds about 26 bursts. Walking them copies 26 structs of
  about 100 bytes a frame, under a microsecond in all, so `bursts/step` should not move by more than the noise
  (0.02 ms).
- The second reading is the one predicted. If it holds, the trace's attribution of BulkMove time to this site is the
  sampler's charge, not copy cost. That settles B574's reading for this site: struct copies at this count are not the
  frame. If `step` falls by 0.1 ms or more, the first reading holds and B574's copies are real time.

## Method

The change: iterate `CollectionsMarshal.AsSpan` by `ref readonly` when `live` is a `List<ParticleBurst>`, and `live`
otherwise. Nothing else changes, and the output is the same: the same items in the same order. ABBA interleave of base
(`F:/tdsb`, which is checked out at `a43b12b4`, before the decal change) against this change. **Confound:** the base also
lacks the decal window. So the comparison is `bursts/step` only, which the decal change does not touch, not `camera`.
Each run rebuilds both trees, then runs the command of the breakdown record, with load recorded. Runs B1 A1 A2 B2 B3 A3,
raw in `2026-10-22-burst-copy/`.

## Control

- Both builds print `camera particles/bursts/step` and the census `bursts 26` (the same offers) at the same ticks.
- Base `step` agrees with the breakdown's R1/R2 (0.35, 0.36) within 15%.

## Environment

Debug, RTX 3060, i5-10600K. Load before / after: B1 19/16/18 / 8/6/8, A1 11/12/7 / 10/7/11, A2 10/6/7 / 5/7/8,
B2 5/8/13 / 4/7/5, B3 4/6/20 / 10/3/6, A3 7/3/5 / 4/6/9.

## Result

| run | build | bursts/step ms | last census |
|---|---|---:|---|
| B1 | base | 0.36 | t31282, bursts 26 |
| A1 | by reference | 0.35 | t31282, bursts 26 |
| A2 | by reference | 0.35 | t31282, bursts 26 |
| B2 | base | 0.37 | t31288, bursts 26 |
| B3 | base | 0.36 | t31262, bursts 30 |
| A3 | by reference | 0.34 | t31285, bursts 26 |

Controls: every run prints `bursts/step`. Base 0.36-0.37 against R1/R2's 0.35/0.36 (met). The census is 26 bursts except
B3, whose last line fell 20 ticks earlier at 30 (same offers, read mid-change; not a different scene).

## Verdict

**The arithmetic reading holds; the trace's reading is falsified for this site.** Base mean 0.363, change 0.347: 0.017 ms,
at the timers' resolution and far under the 0.2 ms the trace's 1.7% share predicted. The trace charged about 0.2 ms to a
copy that costs a few hundredths at most. So at this site the `BulkMoveWithWriteBarrier` samples (every one with a
`PollGCWorker` leaf) are the sampler's suspension at a safe point, not copy time. **For B574:** a struct-copy site's share
in the sampled trace is not evidence of its cost, and a copy site is worth removing only when its own timer, or an
allocation count, says so. The change was not kept, since it measured as nothing. The source is restored exactly
(`git status` clean on `managed/`).
