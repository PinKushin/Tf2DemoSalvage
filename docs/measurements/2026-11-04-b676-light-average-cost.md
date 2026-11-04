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

Read the `lighting` column of the pose line and the frame rate, both means over the window.

## Control

The two settings' run order alternates, so drift shows as disagreement inside a setting. Ticks advanced near 66.7 per second of the window, as the skill says. The same line's `posed N of M` must agree between the settings, or the two runs did not draw the same scene. And `+r_lightaverage 0` must reach the draw at all: `Instances_AlongARealWalkThroughARealMap_DrawTheEnginesRecurrenceOverTheRawStates` holds that switch on the production path.

## Environment

Machine as in the records beside this one. The branch head at the run, `fix/b688-b676`. Load before and after in the Result.

## Result

## Verdict
