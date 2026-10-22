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

## Result

## Verdict
