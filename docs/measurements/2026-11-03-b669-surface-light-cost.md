# What surface lights cost a frame on f12 (B669)

## Observation

Review of `fix/b669-pipes`: `LocalLights.Split` now considers 108 more lights on cp_process_f12 with no `r_worldlightmin` cut, so it
traces about a hundred surface lights for each new light cache entry and for each model under a live dlight. Only load time was
looked at (`trace-cost`, B669). Nobody has observed the frame.

## Question

Does the branch cost a first-person playback frame on f12 more than `origin/main`, overall and in the pose's `lighting` column?

## Method

A clock on our own code, on one scene, in the order A B B A, repeated: A is `origin/main` `e6f8a67a`, B is the branch's head, each
built Debug from a clean copy and run from its own folder so neither rebuild can touch the other.

```
TF2VIEW_AUTOPLAY=1 pwsh run-exclusive.ps1 <A or B>/tf2demoview.exe D:/measure-f12/f12.dem --tick 30000 --first-person --measure 20 +fps_max 0
```

The numbers kept are the rate line's frame ms and the moment line's `lighting` column, each a mean over the interval, per run.

## Control

- Each run's ticks advanced should be about 66.7 x 20. A short run is not counted.
- A against A and B against B give the spread: a difference between A and B smaller than that is not a difference.
- `lighting` must be nonzero on both, or the column is not reading this work.

## Prediction

The cost is in new cache entries and dlight frames only, so frame ms within the A-A spread and `lighting` raised on B. Falsified if frame
ms moves by more than the spread.

## Environment

Shared desktop, other agents' runs queued on the same lock. Load is whatever each run's line reports; a run beside a busy machine shows
as spread rather than being hidden.

## Result

A = `e6f8a67a`, B = `97a72e39`, Debug, `--measure 20`, f12 tick 30000, first person, in this order on 2026-11-03 from 07:47. Parsed from the
tool's own printed lines by `D:/b669/abba.py`; `lighting` is the mean of each run's per-interval `pose = lighting` figure (16-17 samples).

| run | side | ticks advanced | frame ms | lighting ms |
|---|---|---|---|---|
| 1 | A | 1340 | 7.05 | 0.681 |
| 2 | B | 1347 | 7.02 | 0.688 |
| 3 | B | 1347 | 7.01 | 0.688 |
| 4 | A | 1347 | 7.01 | 0.687 |
| 5 | A | 1347 | 6.83 | 0.629 |
| 6 | B | 1347 | 7.15 | 0.744 |
| 7 | B | 1347 | 6.94 | 0.700 |
| 8 | A | 1347 | 7.10 | 0.638 |

A: frame 6.998 (6.83-7.10), lighting 0.659 (0.629-0.687). B: frame 7.03 (6.94-7.15), lighting 0.705 (0.688-0.744). The last five samples of
each run, once the cache has filled, read A 0.32-0.42 and B 0.40-0.46.

Control: every run advanced about 1,340 ticks against 1,334 expected. `lighting` is nonzero on both.

**What is not drawn:** skinning is on the GPU where Valve's runs on the processor; muzzle flashes, shell ejection and `DoAnimationEvents` are
unchecked since D129. Ragdolls and particles are drawn. The workload is one first-person demo section; the shared machine's other agents
were queued on the same lock, not running beside it, as far as the lock shows.

## Verdict

**Prediction confirmed.** The branch's frame is +0.03 ms against a same-side spread of 0.21-0.27 ms, so no frame cost was observed; `lighting`
rose by about 0.05 ms, near or just outside its spread of 0.06, on a machine that can only just resolve it. Nothing changes because of it.
