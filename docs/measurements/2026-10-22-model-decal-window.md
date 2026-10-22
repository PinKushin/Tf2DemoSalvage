# Measurement 2026-10-22: the model decals' step, looking only at this frame's landings

## Observation

`2026-10-22-f12-camera-breakdown.md`: `camera particles/decals` is 0.52-0.53 ms a frame on f12, and in its trace
`DecalEffects.StepModelDecals` is a 2.3% leaf of the render thread, its own loop body. That loop walked every client bullet
of the match (12,518 on f12) and every server impact on an entity, each frame, to keep those with `from < tick <= now`.

## Question

With the step changed to binary-search the window in the two tick-ordered lists (falling back to the whole list when one
is out of order), on f12 tick 30000 first person 1280x720 `+fps_max 0`: how much do `camera particles/decals` and the
frame drop, and is the drawn output unchanged?

## Prediction

`camera particles/decals` falls by 0.2-0.4 ms a frame (the leaf's share of a 12 ms frame is about 0.28 ms); the census line
still prints `model decals 75` at the same tick; the frame falls by about the same amount, inside the run-to-run noise.
Falsified if `decals` falls by under 0.1 ms.

## Method

Two builds of the Debug viewer: before = this branch's `a43b12b4` (copied out of `bin` to the scratch directory before the
change was built), after = the change. Interleaved runs Before1, After1, Before2, After2, each the command of
`2026-10-22-f12-camera-breakdown.md` with the build's `tf2demoview.exe`, through `run-exclusive.ps1`, load (3 x 2 s)
before and after each. Steady = per-second lines after the first five, frame-weighted (`parts.ps1` in the breakdown's
folder). Raw output in `2026-10-22-model-decal-window/`.

## Control

- **Same content:** both builds' census lines report the same model decal count and bursts at the same tick; a different
  count means the change altered what is placed, and the timing comparison is void.
- **Instrument:** `camera particles/decals` is timed by the same `Part` call in both builds (the change is inside it).
- Before1 and Before2 agree within 15% on `decals`, or the pair is void.
- The unit tests `StepOver_AMatchOfBulletsOutsideTheFramesWindow_ExaminesOnlyTheOneThatLandedInIt` (1 of 1,002 bullets
  examined) and `StepOver_BulletsOutOfTickOrder_AreAllExaminedAndTheFramesStillLands` were red against the full scan
  ((1, 1002) and (1, 2)), and the first reddens with the window's edge moved by one (`<=` to `<`), the second with the
  order check forced true.

## Environment

RTX 3060, i5-10600K, Debug. Load before / after: Before1 9/3/4 / 4/8/8, After1 11/7/4 / 10/7/6, Before2 5/5/2 / 7/2/20,
After2 9/3/8 / 9/6/4.

## Result

| run | camera | particles/decals | frame ms | census |
|---|---:|---:|---:|---|
| Before1 (scratch copy) | 11.09 | absent | 49.76 | tick 31241, model decals 75 |
| After1 | 6.65 | 0.19 | 10.96 | tick 31286, model decals 75 |
| Before2 (scratch copy) | 10.65 | absent | 17.84 | tick 31292, model decals 75 |
| After2 | 6.50 | 0.19 | 11.29 | tick 31278, model decals 75 |

**Deviation: the before runs are void.** The copy of the base build run from the scratch directory printed no `particles/*`
parts at all, and a camera and frame far above every base run on this branch. So it did not behave like the base build, and
the instrument control (the same `Part` in both builds) failed. No before figure is taken from it. The base build's own
runs, R1 and R2 of `2026-10-22-f12-camera-breakdown.md`, came from the same binary path, the same command and a similar
load (4-22%), about an hour earlier. They read `decals` 0.52 and 0.53, `camera` 7.16 and 6.99, and frame 11.86 and 11.99.
Those runs are the comparison, and they are **not interleaved**.

## Verdict

**Prediction confirmed, against the non-interleaved base runs:** `camera particles/decals` fell from 0.52-0.53 to
0.19 ms (-0.33 to -0.34), inside the predicted 0.2-0.4. The census reports 75 model decals in every run, so the output is
unchanged. `camera` fell from 6.99-7.16 to 6.50-6.65, and the frame from 11.86-11.99 to 10.96-11.29. The frame's drop is
larger than the decal saving and is inside run-to-run noise, so it is not claimed. An interleaved before/after from a
correct before binary remains to be done.

## Second pair: interleaved, from a built-in-place base (method fixed before running)

**Why.** The first pair's before binary was void. **Method:** the base is built in its own worktree, `F:/tdsb` at
`a43b12b4`, so it runs from a normal tree, not from a copied `bin`. Each run rebuilds both trees (`dotnet build
managed/Tf2DemoSalvage.Viewer3D`) in the same command and then runs one of them. The order is B1 A1 A2 B2 B3 A3 (ABBA, so
a drift in load hits both sides alike). The command, load sampling and steady window are as above. Raw output goes in
`2026-10-22-model-decal-window/P-*.txt`. **Control:** every run, base and change, must print `camera particles/decals`
and the census `model decals 75`. A base run without the parts is void, as in the first pair. The base runs must also
agree with R1/R2 of the breakdown within 15% on `decals` (0.52-0.53).
**Prediction:** `decals` base 0.45-0.60, change 0.15-0.25. The difference of the means is 0.25-0.40 ms.

### Second pair: result

All six runs had load at or under 14%. Every run printed `camera particles/decals` and the census `model decals 75`
(controls met). Base `decals` 0.49-0.59, within 15% of R1/R2's 0.52-0.53 (control met).

| run | build | camera | particles/decals | frame ms | load before / after |
|---|---|---:|---:|---:|---|
| B1 | base | 7.08 | 0.51 | 11.16 | 14/3/5 / 12/8/10 |
| A1 | change | 6.54 | 0.18 | 12.51 | 6/4/9 / 2/6/7 |
| A2 | change | 6.54 | 0.19 | 11.40 | 9/5/4 / 9/6/5 |
| B2 | base | 7.01 | 0.59 | 11.59 | 9/5/4 / 8/6/8 |
| B3 | base | 6.82 | 0.49 | 11.59 | 3/5/7 / 4/7/5 |
| A3 | change | 6.62 | 0.19 | 11.38 | 11/9/4 / 12/9/8 |

### Second pair: verdict

**Prediction confirmed on an interleaved pair.** `decals` base mean 0.53, change 0.19: a 0.34 ms saving, with the ranges
disjoint (0.49-0.59 against 0.18-0.19). `camera` base 6.82-7.08, change 6.54-6.62: 0.40 ms lower on the means, also
disjoint. The whole frame does not resolve the saving: base 11.16-11.59, change 11.38-12.51. A1 is the slowest run, so
no frame figure is claimed.
