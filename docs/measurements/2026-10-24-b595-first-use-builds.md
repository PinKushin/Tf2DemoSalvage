# First-use builds inside frames, f12 gummo first person (2026-10-24, B595 part 2)

Follows `2026-10-24-b595-frame-stalls-and-jit.md`, whose slow-frame table put 17 of 38 slow frames in camera → pose,
with `SLOW MOMENT` naming `viewmodel` (42-128 ms) or `baked` (19-49 ms).

## Question

Are those pose stalls first-use builds — a model packed for the first time, a static prop's colour mesh built on first
sight — and how many and how costly is each per 60 s?

## Prediction (written before the run)

Every `viewmodel` stall frame carries `models packed +N` with N ≥ 1 and pack ms close to the viewmodel column; every
`baked` stall frame carries `baked built +N` with N ≥ 1 and build ms close to the baked column. Total baked builds over
60 s is in the tens to low hundreds (one per static prop first seen).

## Instrument

`FrameCounters.Models` (`ModelFirstUses`): `EntityModelSet.ModelsPacked/PackTicks`, incremented in `Add` when it packed
anything, and `BakedBuilds/BakedBuildTicks`, incremented in `BakedColoursOf` on a miss that cached a result. Carried from
where the build runs, differenced at the frame boundary. Replaces the dead `model vertices` counter.
Control: the counters must read non-zero over the measurement's first frame (the static props pack on the first
`Add`, so `models packed` > 0 there by construction).

## Method

As part 1: `D:/measure-f12/f12.dem --tick 30000 --spectate gummo --first-person --measure 60 +fps_max 0`,
`TF2VIEW_AUTOPLAY=1 TF2VIEW_VIEWPORT_SIZE=1280x720`, Debug, via `run-exclusive.ps1`. One run.

## Results
