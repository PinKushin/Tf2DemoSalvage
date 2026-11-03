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

Run 2026-11-03. A at 05:12, B at 05:33, B at 05:38, A at 05:43, each through `run-exclusive.ps1`, with other agents' `--shot`
and probe runs between them, never beside them. No build overlapped. Every run: tick 30000 to 31347, ticks advanced 1,347.

| run | frames | mean ms | `lighting` all, frame-weighted | `lighting` past the first four | `allocated` MB a frame |
|---|---:|---:|---:|---:|---:|
| A | 2,212 | 9.04 | 0.654 | **0.489** | 0.80 |
| B | 2,254 | 8.88 | 0.572 | **0.382** | 0.83 |
| B | 2,143 | 9.33 | 0.641 | **0.420** | 0.85 |
| A | 2,254 | 8.87 | 0.635 | **0.489** | 0.80 |

The `lighting` column, each 100-rebuild report, ms:
- A 05:12: 2.1 2.1 1.3 1.1 0.2 0.5 1 0.8 0.3 0.2 0.9 0.7 0.5 0.2 0.5
- B 05:33: 2.1 2.8 1.6 0.8 0.2 0.2 0.8 0.6 0.2 0.2 0.7 0.7 0.4 0.2 0.3
- B 05:38: 2.2 2.9 1.8 0.9 0.5 0.3 0.8 0.8 0.3 0.1 0.7 0.7 0.4 0.1 0.3
- A 05:43: 1.7 2.1 1.4 1.1 0.7 0.3 0.5 0.9 0.8 0.3 0.2 0.8 0.9 0.4 0.2 0.3

**Output.** `--shot` of tick 30000, SHA-256 prefix, A `0DA81E0B033CBB64`, A again `0DA81E0B033CBB64`, B `0DA81E0B033CBB64`: all
byte-identical, the control included. One frame after a seek, so it cannot see the per-frame budget; that is
`ModelSunAt_APointPastTheBudgetAskedAgainNextFrame_BuildsItsOwnEntry`.

**An added pair, after the allocation reading above, to ask two things the first four did not print.** A at 06:08: 2,254 frames, 8.88
ms, 0.80 MB a frame, `sun per frame … light cache` 1.548 mean of its 19 samples. B at 06:17: 2,197 frames, 9.11 ms, 0.80 MB a
frame, 1.559. So entries are built at the same rate, and B's allocation came back to A's.

**Sabotages, each restored by the inverse edit; `LevelLighting`+`LightCache` tests, 42 in the run:**
- No `Frame` in the question: `ModelSunAt_APointPastTheBudgetAskedAgainNextFrame_BuildsItsOwnEntry` fails, alone.
- No point in the question: that one and `ModelSunAt_WithAnotherPointAskedInBetween_LooksInTheCacheAgain` fail.
- Never remembered, the old behavior: `ModelSunAt_AfterModelLightingAtTheSamePointAndFrame_LooksInTheCacheOnce` fails, alone.
- A delegate made per lookup: `ModelLightingAt_AHitInAnotherCell_AllocatesNothing` fails, alone.

## Verdict

**Mostly confirmed.** The column past warm-up fell 0.107 ms and 0.069 ms in the two adjacent pairs of A B B A, against a predicted
0.05 or more: 0.09 ms mean, 1.0% of the 8.9 ms frame. The frame means (8.87-9.04 against 8.88-9.33, spread 0.17 and 0.45 ms) are too
noisy to show it, as the Method said they would be. Bytes identical and the entries built at the same rate. **The allocation clause
is inconclusive, not confirmed:** B read 0.83 and 0.85 in the ABBA and 0.80 in the added pair, A 0.80 three times; a hit is measured
at zero bytes and nothing added allocates, but the first two B readings are unexplained. The change stays, as it costs nothing and
makes the model path one lookup as the engine's is.
