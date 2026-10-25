# First-use builds inside frames, f12 gummo first person (2026-10-24, B595 part 2)

Follows `2026-10-24-b595-frame-stalls-and-jit.md`, whose slow-frame table put 17 of 38 slow frames in camera → pose,
with `SLOW MOMENT` naming `viewmodel` (42-128 ms) or `baked` (19-49 ms).

## Question

Are those pose stalls first-use builds — a model packed for the first time, a static prop's colour mesh built on first
sight — and how many and how costly is each per 60 s?

## Prediction (written before the run)

Every `viewmodel` stall frame carries `models packed +N` with N ≥ 1 and pack ms close to the viewmodel column; every
`baked` stall frame carries `baked built +N` with N ≥ 1 and build ms close to the baked column. Total baked builds over
60 s is in the tens to low hundreds (one per static prop first seen).

## Instrument

`FrameCounters.Models` (`ModelFirstUses`): `EntityModelSet.ModelsPacked/PackTicks`, incremented in `Add` when it packed
anything, and `BakedBuilds/BakedBuildTicks`, incremented in `BakedColoursOf` on a miss that cached a result. Carried from
where the build runs, differenced at the frame boundary. Replaces the dead `model vertices` counter.
Control: the counters must read non-zero over the measurement's first frame (the static props pack on the first
`Add`, so `models packed` > 0 there by construction).

## Method

As part 1: `D:/measure-f12/f12.dem --tick 30000 --spectate gummo --first-person --measure 60 +fps_max 0`,
`TF2VIEW_AUTOPLAY=1 TF2VIEW_VIEWPORT_SIZE=1280x720`, Debug, via `run-exclusive.ps1`. One run.

## Results

Branch `perf/b595-precache-first-use`, Debug, 0 warnings. Raw stdout in the scratchpad, not committed. The viewer's log
landed in `AppData\Local\Packages\Claude_pzs8sxrjxfjjc\LocalCache\Local\Tf2DemoSalvage\` — the Claude package's
filesystem virtualisation — so a flush's latency here need not match a user's machine; before/after is still like for
like, since part 1 ran the same way.

**The prediction is refuted on both halves. Neither first-use build happens in play.**

| probe | commit | frames > 50 ms / ms | first uses in play | before play |
|---|---|---|---|---|
| 1 | `0bd80d71` | 2 / 253 (both `between`) | (no total yet) | — |
| 2 | `80e90697` (pre-commit build) | 7 / 559 | models packed **0**; baked built **434, 20.5 ms** | packed 735, baked 188 |
| 3 | `80e90697` | 6 / 656 | packed 0; baked 434, 19.2 ms | packed 735, baked 188 |

- **Models**: all 735 paths pack before the measurement opens; in play, 0. The viewmodel's models are not a first use
  in-frame on f12. The control as first written ("packed > 0 in the first measured frame") could not fire for that
  reason; the before-play count is the real control and it is non-zero.
- **Baked**: 434 colour meshes are built in play, at **~0.05 ms each, ~20 ms over 60 s**. No slow frame carries one.
  **Killed conclusion from part 1:** its `baked 19-49 ms, 12+ lines` came from `SLOW MOMENT` lines in the first ~5 s
  of the log, BEFORE the measurement window opened (probe 5: the window opened ≈ 19:28:56.7 by matching two ledger
  stalls to their log lines 23.3 s and 47.9 s in; every baked-heavy line is 19:28:48-53). And `SLOW MOMENT`'s `baked`
  column (`BakedTicks`) also covers the per-draw `StaticPropFullLighting` and `StaticPlusDynamicLights` calls, not the
  build alone. So precaching the colour mesh at load would save ~20 ms spread over a minute, not a stall.
- **Viewmodel**: probe 3 split the viewmodel's time by the pose counters: 110 and 118 ms viewmodel stalls with every
  counter (simulate, setup, anim, place, lighting, collate, reports) at 0. Probes 4-5 stamped its steps: resolve 0.1,
  build 0.1, add 0, Instances 0.1, **rest 90.3 / 93.5 / 92.1 ms**. The rest after `Instances` is the per-change log
  lines; the only one reaching the file at the default level is `viewmodel skin: N` (Information), which `scene.Changed`
  fires on every sequence change, several a second. Each is a flushed write on the main thread — B191's mechanism
  exactly (`FileLogWriter`, `AutoFlush = true`, kept by the owner's ruling). Probe 6 stamps that line alone.

**Fix, per B191's ruling** (stop writing lines nobody asked for, keep the flush): the skin line is written when the
skin changes. A diagnostic change, no render path touched. `MomentSceneTests.Pose_ViewmodelSequenceChangesWithTheSameSkin_LogsTheSkinOnce`
read 3 before and 1 after; the inverse edit reddened exactly that test (3226/3227).

**Load-time cost: moot.** No precache was built, so load time does not change. For the record, before play the model
set packed 735 paths and built 188 colour meshes (ms printed from probe 6 on).

### Before/after (written before the runs)

**Prediction:** with the fix, frames > 50 ms whose camera split is `pose` drop to zero (they were all the skin line in
probes 2-5: 3, 3, 3, 3); the other categories (particles, wait, sound, between) are untouched, so the total count falls
by about three a run but stays non-zero. Run-to-run spread in the total is large (part 1: 0-12), so ranges are reported.

Method: `before` = the inverse edit's build, `after` = `4f754a26`, both Debug copies in the scratchpad; runs
interleaved before/after × 3; per run: frames > 50 ms, ms in them, how many have `pose` as their largest camera piece,
`SLOW MOMENT` lines naming `skin log` > 10 ms.

### Before/after results

**A first grid is void.** `cp -n` into a `before` folder that was already in the scratchpad kept a stale binary from an
earlier session: mean 19-20 ms, no `pauses:` line, 46 sound decodes. The folder's DLLs were dated 23:18 against 19:40
for the build. Rerun with a fresh `vbefore` (DLLs 20:15, same sizes as `after`). That grid, interleaved, CPU 8-25% before
each run:

| run | variant | mean ms | frames > 50 ms / ms in them | pose-largest | `viewmodel skin` lines written | in-play `SLOW MOMENT` |
|---|---|---|---|---:|---:|---|
| 1 | before | 9.37 | 1 / 56 | 0 | 124 | none |
| 1 | after | 9.33 | 1 / 51 | 0 | 1 | none |
| 2 | before | 9.27 | 1 / 53 | 0 | 124 | none |
| 2 | after | 9.17 | 1 / 52 | 0 | 1 | none |
| 3 | before | 9.30 | 5 / 500 (2 `between`, 1 `wait`, 2 particles at start) | 0 | 122 | none |
| 3 | after | 9.34 | 3 / 287 (1 **pose 113 ms**, 1 `wait`, 1 particles at start) | 1 | 1 | none |

**The prediction cannot be confirmed from this grid.** The fix cut the skin line from 122-124 writes a minute to 1, as
designed. But in this grid no write was slow: every before-run `SLOW MOMENT` reads `skin log 0`, and no before run had a
pose stall. The slow flush that cost 90-118 ms a line in probes 2-5, about an hour earlier, did not recur. Its latency is
environmental and bursty (see the virtualised log path above), so a grid in a quiet hour shows nothing. Frames over 50 ms:
before 1-5 (53-500 ms), after 1-3 (51-287 ms). That is inside the run-to-run spread.

**A pose stall the fix does not touch.** After, run 3: a 113 ms `pose` at 21.9 s with **no `SLOW MOMENT` line**, so it
lies in `PoseOver` outside `MomentScene.Build`'s timers. Part 1 saw the same shape. It is still unattributed.

**Warm-up, before the window opens, is in every run.** All twelve logs hold the same four `SLOW MOMENT` lines in the
first ~5 s: 206-209, 44-52, 70-80 and 240-256 ms, with `baked` 47-54, 17-19, 47-52 and 89-101 ms and `advance`
(corpses) 127-139 ms in the last. A user sees these right after a load, but `--measure` does not count them. The new
counters split the `baked` part: the colour-mesh build before play is **188 meshes in 21-29 ms in all**. The rest of
`baked` is the per-draw full-lighting and dynamic-light calls on their first fills, so precaching the mesh would buy under
30 ms of the ~400 ms of warm-up. Model packing before play is **735 paths in 455-527 ms**.
