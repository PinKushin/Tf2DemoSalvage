# Measurement 2026-11-03: what do static props cost the light cache, and what does leaving it save?

## Observation

B674, from the B669 review, read from our code and not run: `EntityModels.Instances.cs` calls `_lighting.For` for every static
prop before it knows the prop's state, then replaces the answer with the handle's. Read 2026-11-03 in `engine.dll`: in
`0x1800f1bd0` a non-null `param_4` never reaches `LightcacheGet` (`0x1801b9cd0`), only `0x1801ba590`, flags 7 or `6 + bit`; the
batched `0x1800eb5e0` chooses the same way; `CStaticPropMgr::PrecacheLighting` (`0x180205b20`) makes a handle
(`0x1801b8350`) for every renderable prop. So on the engine's path no static prop has an entry. A prop with no colour mesh and
not bump mapped takes flags 7 in the engine (`*param_7 == 0`) and the light cache's answer in ours, and keeps it.

## Question

On f12: how many `Entry` calls, lookups and builds a frame come from static props, from other models and from neither; how many
static draws a frame are the third class above; and with static props out of the cache, what changes in the pose's `lighting`
column, the frame, allocation and the pixels of one shot?

## Hypothesis

Static props are most of the lookups. Taking them out shortens the column by most of its hit path.

## Prediction

- Count run: static prop lookups are above half of all lookups a frame.
- ABBA: `lighting`, frame-weighted past the first four intervals, falls by at least 0.05 ms, B against A, in each adjacent pair.
- `allocated MB a frame` does not rise.
- **Builds: no direction predicted.** A static prop's entry today warms a cell a player can hit later, so a moving model may build
  more once static props stop, while lookups fall. Reported, not predicted.
- Shot: A against A byte-identical. B against A: differs only where parity changes it, on moving models lit by a different entry's
  point, and on third-class props when the count shows any.

## Method

A = `f7ec0bb6`, built as it is. COUNT = A with counters on a side branch (`measure/b674-count`), used for the count and nothing else,
because its counters run on the frame path. B = this branch with the B674 change and nothing of B675's. Each built once and copied
to `D:/b674/{A,COUNT,B}`.

**Count:** `TF2VIEW_AUTOPLAY=1 D:/b674/COUNT/tf2demoview.exe D:/measure-f12/f12.dem --tick 30000 --first-person --measure 20 +fps_max 0`
under `run-exclusive.ps1`. Read the side branch's line: `Entry` calls, lookups and builds by caller, per frame; static draws and
third-class draws per frame.

**Frame:** the same command on `D:/b674/{A,B}`, order A B B A. Read the summary's frames, mean ms, `allocated`, each interval's
`lighting` and frames, and `sun per frame … light cache`. Frame-weighted over all intervals and past the first four, in ms and as a
percent of the run's own frame.

**Output:** `tf2demoview.exe D:/measure-f12/f12.dem --tick 30000 --first-person --shot <file>` from A, A again and B, then a byte
comparison and, where they differ, a count of differing pixels and where they fall. One frame after a seek runs below frame 60, where
no miss is throttled, so the shot cannot see the budget; a test speaks to that.

## Control

- Count: lookups by caller must sum to `LevelLighting.LightCacheLookups` differenced over the same window, read where it is counted.
  Static draws must be above zero.
- Ticks advanced about 66.7 x 20 (about 1,334), and the `lighting` column non-zero.
- Shot: A against A identical, or the comparison cannot speak about B.
- That static props stop asking is pinned by a test whose sabotage is reported with the result.
- A run that overlapped a build is void.

## Environment

RTX 3060, i5-10600K; Debug build (optimized, D176), the build the owner runs. Commits in Method; load with the result.

## Result

Run 2026-11-03, each under `run-exclusive.ps1`. Commits: COUNT `020ebfe0`, A `ff40b283` (`f7ec0bb6` plus this file), B `66d142e6`. No build
overlapped. Load was not read; other agents were working, and B1 and A2 ran slower in every phase, render passes included
(Sky3D cpu 0.211 and 0.200 ms against 0.180 and 0.185).

**Count**, tick 30000 to 31340, 2,853 frames, mean 7.01 ms. The line is cumulative from the map's load, last sample:

| caller | `Entry` calls | lookups | builds |
|---|---:|---:|---:|
| neither (`ComputeLighting`) | 1 | 1 | 0 |
| static props | 766,982 | 383,491 | 1,375 |
| moving models | 216,416 | 62,780 | 1,901 |

The control: 1 + 383,491 + 62,780 = 446,272, the `LightCacheLookups` printed beside it. Static draws 383,491, so one lookup a draw; third-class
draws 0. Between the first sample (76 frames) and the last, about 2,777 frames: static props 135.5 lookups and 0.45 builds a frame, moving
models 22.5 lookups and 0.68 builds a frame. Static props are 86% of lookups. The sample's own window has no line for them: the cumulative
reading is what was printed.

**Frame**, order A B B A, ticks 30000 to 31347, 1,347 advanced in every run:

| run | started | frames | mean ms | `lighting` all, frame-weighted | `lighting` past the first four | `allocated` MB a frame | `sun … light cache` mean of 19 samples |
|---|---|---:|---:|---:|---:|---:|---:|
| A | 11:25 | 2,845 | 7.03 | 0.500 | **0.319** | 0.75 | 1.467 |
| B | 11:29 | 2,511 | 7.97 | 0.361 | **0.266** | 0.81 | 1.057 |
| B | 11:32 | 2,929 | 6.83 | 0.264 | **0.188** | 0.75 | 0.966 |
| A | 11:36 | 2,584 | 7.74 | 0.551 | **0.350** | 0.79 | 1.493 |

The `lighting` column, each rebuild report, ms:
- A 11:25: 1.8 2.5 1.9 1.2 0.6 0.1 0.2 0.8 0.5 0.2 0.1 0.3 0.5 0.4 0.1 0.4
- B 11:29: 0.6 1.2 1.2 0.8 0.5 0.1 0.3 0.5 0.4 0 0.1 0.3 0.3 0.4 0.1 0.5
- B 11:32: 0.5 1.3 1 0.4 0.3 0.1 0.1 0.4 0.3 0.1 0.1 0.2 0.2 0.3 0.1 0.2
- A 11:36: 2.1 2.6 1.9 1.2 0.6 0.1 0.1 0.8 0.6 0.2 0.1 0.6 0.6 0.5 0.1 0.3

**Output**, `--shot` of tick 30000, SHA-256 prefix: A `2D92FC09F2A9C509`, A again `2D92FC09F2A9C509`, B `9EAE71159A9BDBB5`, B again `28BBF95AF172D2BA`.
- A against the second B: rows 0-40 and 40-300 no pixel; rows 300-551, 17,096 pixels, largest channel difference 33, all at x 422 to 588. That is the first-person
  viewmodel; the world, every static prop in view included, did not move a pixel.
- **The first B against the second, the same binary:** rows 0-40, 24,620 pixels, largest 206, the whole width; nothing below them. So the top band
  varies run to run and this change is not its cause: A against the second B has none. One run in four showed it; not investigated.
- One frame after a seek, so the shot cannot see the per-frame budget. No TF2 capture was compared, so which viewmodel shade is right is the engine read, not an observation.

**Sabotages, each restored by the inverse edit**, `StaticPropHandleConformanceTests`, 5 tests:
- Static props asking the cache again: the three `…_AsksTheCacheNothing` fail, alone.
- The handle's state only under the bump flag: `Instances_AStaticPropWithNoColourMeshAndNoBump_DrawsTheHandlesFullState` fails, alone.
- No handle for a prop with no mesh: `Instances_AStaticPropWithNoColourMeshAndNoBump_AsksTheCacheNothing` fails, alone.

**Added after the run, to ask what the first shots could not. They correct the shot clause below.**
- More `--shot`s of the same tick: A `3A0CE4B336F68516` three times; this branch's head (`b5be4cdd`, B plus B675) `9EAE71159A9BDBB5` twice, which is the first B's hash. A's first two against its later three: rows 0-40 only, 24,620 pixels, largest 206. So **both binaries take either state of the top band** and "one run in four" was too few shots to say. A against A matched by chance, so it was no control. That is B677.
- The shot clause rests on the pair with the band equal: A `3A0C…` against the head `9EAE…`, rows 0-300 no pixel, rows 300-551 17,096 pixels, largest 33. The same count as A against the second B.
- **`baked`, past the first four, read from the same four logs, frame-weighted:** A 0.042, B 0.051, B 0.043, A 0.052. It did not rise, so the column's fall is not work moved to the handle's side.
- **At load, from the same logs:** `baked built 959, static states 1353, styled lists 1353` in A and in both B runs. So the third class has no handle of its own to add across all of f12, not only among props drawn in the window: it is empty on this map.
- **`Pose_ABakedStaticPropHoldingNoDecalUnderALiveDlight_AllocatesNothingForTheDecalLight`'s changed control still fails when it should:** with `DecalLightOf` building for a prop that holds no decal, it fails, beside the two tests that assert the same thing by counts. Restored by the inverse edit.

## Verdict

Clause by clause:
- **Static props are above half of lookups: confirmed**, 86%. The third class is empty on f12's frames, so it changes no pixel here.
- **The column: confirmed.** Past warm-up it fell 0.053 ms and 0.162 ms in the two adjacent pairs, against a predicted 0.05 or more: 0.11 ms mean, 1.5% of a 7.4 ms frame.
  It prints at 0.1 ms an interval, so these are means of quantised values, and the first pair clears the prediction by 0.003. The frame means (A 7.03 and 7.74, B 7.97 and 6.83) follow the load and cannot resolve it.
- **Allocation: falsified on one pair.** B read 0.81 against A's 0.75 in the first pair, 0.75 against 0.79 in the second. It moves with the slow runs on both sides, and no line added allocates, but
  the prediction was that it would not rise and in one pair it did.
- **Builds, unpredicted: they fell.** `sun … light cache`, one a build, is 1.01 mean in B against 1.48 in A. What static props stop building is more than what moving models then build themselves.
- **The shot: confirmed.** A against A identical, and B against A differs on a moving model only, the viewmodel, lit from another point's entry. No static prop moved.

The change stays: it is what the engine does, and it costs the column less.
