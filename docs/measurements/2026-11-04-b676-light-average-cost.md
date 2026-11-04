# What B676's light average costs a frame

## Observation

B676 ports `FUN_1800f4310`: every moving model's draw now blends its cube and slots with its own history. It runs per model per frame, and the
owner's yardstick counts anything from 0.01 ms (`docs/memory/a-win-above-0-01-ms-counts-the-target-is-1000-fps.md`). Nothing has been clocked.

## Question

How many milliseconds and what percent of a frame does the average add on the parity demo's busiest scene?

## Hypothesis

Tens of nanoseconds a model: eighteen blended channels and at most sixteen light comparisons, allocating nothing.

## Prediction

About 60 moving models drawn on f12 at tick 30000, at under 200 ns each: under 0.012 ms in the `lighting` column, too small for a frame mean to resolve, whose run-to-run spread is larger. Falsified by a `lighting` column more than 0.05 ms over the control.

## Method

Same binary, same demo and tick, the average off (`+r_lightaverage 0`) and on, alternating, three runs of each, under the machine lock with the window off screen:

```bash
TF2VIEW_WINDOW_POS=-3000,100 TF2VIEW_AUTOPLAY=1 pwsh C:/Users/pinku/source/repos/PinKushin/run-exclusive.ps1 \
  managed/Tf2DemoSalvage.Viewer3D/bin/Debug/net10.0-windows/tf2demoview.exe \
  tools/corpus/local/demostf-cp_process_f12-2026-08-08-2207.dem --tick 30000 --measure 20 +fps_max 0 [+r_lightaverage 0]
```

Read the `lighting` column of the pose line and the frame rate, both means over the window. That column prints to 0.1 ms, coarser than the prediction, so the draw itself is clocked too: `dotnet run --project tools/Tf2DemoSalvage.Probe -c Release -- light-average-cost`, the worst case (four lamps and the sun, all blended), on and off alternating, in nanoseconds and bytes a draw, carried from the loop's own clock and `GC.GetAllocatedBytesForCurrentThread`.

## Control

The two settings' run order alternates, so drift shows as disagreement inside a setting. Ticks advanced near 66.7 per second of the window, as the skill says. The same line's `posed N of M` must agree between the settings, or the two runs did not draw the same scene. And `+r_lightaverage 0` must reach the draw at all: `Instances_AlongARealWalkThroughARealMap_DrawTheEnginesRecurrenceOverTheRawStates` holds that switch on the production path.

## Environment

Machine as in the records beside this one. The branch head at the run, `fix/b688-b676`. Load before and after in the Result.

## Result

Commit `cc8ec922`'s viewer, f12 at tick 30000, window off screen, 18 per-second lines a run. Means of those lines, `D:/measure-f12/b688/parse.py`:

| run | off fps | off pose ms | off alloc MB/frame | on fps | on pose ms | on alloc MB/frame |
|---|---|---|---|---|---|---|
| A1 | 324.9 | 0.71 | 0.65 | 260.8 | 0.82 | 0.73 |
| A2 | 323.3 | 0.67 | 0.63 | 284.7 | 0.79 | 0.68 |
| A3 | 328.0 | 0.69 | 0.65 | 326.4 | 0.68 | 0.65 |
| B1 | 332.4 | 0.68 | 0.60 | 311.1 | 0.72 | 0.67 |
| B2 | 271.8 | 0.76 | 0.73 | 329.3 | 0.69 | 0.64 |
| B3 | 332.5 | 0.69 | 0.64 | 322.0 | 0.72 | 0.66 |

**Set A is not clean:** a test run of this branch overlapped A1 and another agent held the machine until it started, and each of A1, A2 and B2 sits 40 to 70 fps under the others whichever setting it had. So run-to-run spread is about 60 fps here, and the frame mean cannot see this. The `lighting` column printed 0 in every sampled line of both settings.

**The draw itself**, `light-average-cost`, Release, the worst case of four lamps and the sun, 2,000,000 draws:

```
average on : 411.8 ns a draw, 0.0 bytes a draw
average off: 201.7 ns a draw, 648.0 bytes a draw
average on : 237.0 ns a draw, 0.0 bytes a draw
average off: 132.5 ns a draw, 648.0 bytes a draw
```

The first pass of each is before tiered JIT settles. The off path allocates because it rebuilds the list from the state, on a setting nobody defaults to; the on path allocates nothing.

**Controls.** Ticks advanced: not read, as this table was parsed from lines truncated at 260 characters. For the same reason **`posed N of M` was cut off every line, so the control that the two settings drew one scene was not met**; they share one demo, tick and binary. The switch reaching the draw is held by the test named above. **The number of moving models was not counted**, so the 60 below is the estimate from the prediction.

## Verdict

Prediction **falsified on the per-draw figure** (237 ns against "under 200 ns") and **confirmed on the falsifier that decides anything**: no `lighting` column rose by 0.05 ms. At the estimated 60 models that is about 0.014 ms a frame on a 3 ms frame, about 0.5%, allocating nothing, which the frame mean cannot resolve. A parity port of this size stays.
