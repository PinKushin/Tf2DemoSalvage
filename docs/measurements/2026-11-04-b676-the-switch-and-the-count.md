# Whether `+r_lightaverage 0` reaches the draw, and how many models the average runs on

## Observation

`2026-11-04-b676-light-average-cost.md` could not say either. Off and on came out equal within 60 fps of spread, which a switch that never arrived would also produce, and its `posed N of M` was cut off by its own truncation, so its "60 models" is an estimate. The `lighting` column it named prints to 0.1 ms, so its falsifier could not have fired.

## Question

Under `+r_lightaverage 0`, does the viewer's own count of averaged models fall to 0? And how many models a moment does it run on at f12 tick 30000, which with the probe's nanoseconds gives the cost?

## Hypothesis

The link is `ViewerPresenter.Show`'s two arguments, so the count falls to 0 and is some tens otherwise.

## Prediction

Off: `light averaged 0` on every moment line. On: between 20 and 200 a moment. Falsified by a nonzero count off, or zero on.

## Method

`--measure 20` once each way, untruncated, the same demo and tick as the cost record, the same command, reading `light averaged` and `posed` from each moment line. The count is `ModelLightAverage.Draws`, incremented in the draw past the two switches and differenced across `MomentScene.Pose`, so it reports what ran. Cost is that count times the probe's settled 237 ns; the probe's is the worst case, four lamps and the sun.

Second pair, `--first-person` added, off then on, because a first run without it (below) found the camera seeing one model.

## Control

The on run must show a nonzero count, or the instrument reads nothing and the off run's zero means nothing. `posed` must agree within the scene's own variation between the two.

## Environment

Machine as in the record beside this one. The branch head at the run, `fix/b688-b676`.

## Result

## Verdict
