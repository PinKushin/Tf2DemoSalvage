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

## Verdict
