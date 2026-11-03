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

## Verdict
