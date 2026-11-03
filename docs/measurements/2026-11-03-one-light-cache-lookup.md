# Measurement 2026-11-03: does one light cache lookup per model cut the pose's `lighting` column?

## Observation

`measurements/2026-11-03-brush-clip-once.md` ruled out the brush tree's repeats. The lead read `ModelLighting.For` and
`LevelLighting` on 2026-11-03: `For` asks `lightAt` and then `sunAt` for the same point, and each of `ModelLightingAt` and
`ModelSunAt` calls `Entry`, so each model makes two leaf walks, two dictionary lookups and two closures, where the engine's model
path makes one `LightcacheGet` (`0x1800f1bd0`, `0x1801b9cd0`) and reads both from its entry. An exploratory run, not quotable,
counted a few hundred `Entry` calls a frame against about one entry built, exactly two per model, and the build's own time at
under half the column.

## Question

What does answering the second question of a model from the first's lookup save in the `lighting` column and in allocation on f12,
with the same output?

## Hypothesis

The hit path is most of what the column holds when no entry is built, and half of it repeats the other half.

## Prediction

- The `lighting` column, frame-weighted past the first four intervals, falls by at least 0.05 ms, B against A in each adjacent pair
  of the A B B A order.
- `allocated MB a frame` does not rise.
- A shot of the same tick through A and through B is byte-identical, as A's two shots are.

## Method

A = `d3d150fd` built as it is. B = the same with the change. Each is built once and copied aside (`D:/nfw-scratch/{A2,B2}`).

**Frame:** `TF2VIEW_AUTOPLAY=1 tf2demoview.exe D:/measure-f12/f12.dem --tick 30000 --first-person --measure 20 +fps_max 0`, under
`run-exclusive.ps1`, order A B B A. Read: the summary's frames, mean ms, `allocated`, each interval's `lighting` and frames.
Reported frame-weighted over all intervals and past the first four, in ms and as a percent of the run's own frame.

**Output:** `tf2demoview.exe D:/measure-f12/f12.dem --tick 30000 --first-person --shot <file>` from A, A again and B, then a byte
comparison of the files.

## Control

- Ticks advanced about 66.7 x 20 (about 1,334), and the `lighting` column non-zero, or the window cannot see the change.
- The shot: A against A must be identical, or the comparison cannot speak about B.
- The lookup count is pinned by a test, `LightCacheLookups`, read where the lookup is made; the sabotage of that test is reported
  with the result.
- A run that overlapped a build is void.

## Environment

RTX 3060, i5-10600K; Debug build (optimized, D176), the build the owner runs. The commits are in Method; load is with the result.

## Result

## Verdict
