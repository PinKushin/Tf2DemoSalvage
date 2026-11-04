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

**Order of work, stated:** the first pair ran before this file was committed, as the commit carrying it had been refused and the wrapper script hid the run from the gate. Nothing above Result changed after it but the second pair's sentence, added before that pair ran.

Each `--measure 20`, 18 lines, each line a mean over 100 moments. `build de531107`'s predecessor for the first pair and `de531107` for the second; the viewer's code is the same.

| pair | setting | `light averaged` a moment | `posed N` of about 1,860 selected | fps range |
|---|---|---|---|---|
| no camera flag | off | 0 on every line | 1 to 2 | 173 to 478 |
| no camera flag | on | 2 to 2.8 | 1 to 1.8 | 198 to 428 |
| `--first-person` | off | 0 on every line | 24.6 to 43.6 | 15 to 180 |
| `--first-person` | on | 22.2 to 63.3, mean about 51 | 17.9 to 43.7 | 68 to 195 |

**Control met:** the on runs count, so the off runs' zero means the switch arrived, through a config's `+r_lightaverage 0`, `ViewerPresenter.Show`, the moment and the pose. `posed` agrees within the scene's own variation. The first-person off run fell to 15 to 45 fps over its last eight lines, the on run did not; whatever that was, it was not the average, which was off.

`light averaged` above `posed` is worn items and the viewmodel, which pass through the draw and do not count as posed props.

## Verdict

**Prediction confirmed for the switch** (off 0, on not) **and for the first-person count** (22 to 63, in 20 to 200); **falsified with no camera flag**, 2 a moment, because that camera sees one model. So `2026-11-04-b676-light-average-cost.md`'s six runs, made with no camera flag, compared a scene the average barely touches: its fps table says nothing about the cost, and its 60 happens to be near the first-person count. **The cost is the probe's 237 ns at worst times 63 models: 0.015 ms a moment, 0.3% of the 5.1 ms frame this scene runs at its best, with no allocation.** No frame-level comparison resolves that, and none is claimed.
