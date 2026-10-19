# 82. The frame outside particles (2026-10-19)

The owner's priority on 2026-10-18 was the renderer, Valve parity and bugs, fps above all. The question here is where
the viewer's frame goes when the particles are set aside (another agent's subsystem, finished), ranked by
milliseconds, and which owners are costs Valve does not pay.

## The setup

f12 (`demostf-cp_process_f12-2026-08-08-2207.dem`), tick 30000, `--first-person --measure 20 +fps_max 0`, one viewer
at a time under `run-exclusive.ps1`, RTX 3060. **What is drawn and what is not** (D129): skinning is on the GPU where
Valve's is on the processor. Corpses ARE simulated (IVP ragdolls, 12 held in these runs, and every corpse in the demo
recorded on a worker, D181), and particles ARE drawn; both are in the frame below. The `tf2-measure` skill's "no
projectiles, no particles, no ragdolls" is stale on two counts; projectiles were not checked here. Particles are ranked
but not worked on. TF2's own reference is 0.8–1.1 ms a frame on cp_badlands (the `tf2-measure` skill), a different map, so an order
of magnitude rather than a pair.

## First: the measurement counted the opening seek (instrument, fixed)

**What we believed.** The mean frame was 38–41 ms (finding 80's runs: 34–43 ms), and the HUD paint (`HudDeathNotice`),
`hud updates`, the camera and the capture phase were the suspects.

**What the numbers said, measured.** Three runs before the fix gave 43.45, 41.16 and 45.58 ms. But:

- **The tick control failed.** Run 1 counted 461 frames × 43.45 ms = 20.0 s of "playback" and advanced only 773 ticks,
  which is 11.6 s at 66.7 ticks a second. About 8 s of the 20 were not playback.
- **The first per-second line was three frames of about 2.3 s each**: `capture` 890–993 ms (that phase runs
  `ApplyOpeningState`, the seek to `--tick`), `camera particles` 951–992 ms (the replay after the seek),
  `camera pose` 163–166 ms. `HudDeathNotice 136.75` and `hud updates 20.66` appear in that line ONLY; in every steady
  line the whole HUD is 1.2–1.5 ms. **The HUD suspect was the seek.**
- **The phase-sum control failed too**: the phases summed to 38.9, 37.3 and 40.2 ms against those means, 9–12% short,
  while every line read `unaccounted 0`. `unaccounted` spans only `FrameSequence.Run`, and the frame clock
  (`FrameClock.Drew`) is stamped mid-sequence, inside `PlaceCamera`. So the first counted frame's interval carried the
  tail of the uncounted seek frame before it — about 2 s, the size of the gap (1.9–2.3 s over each run).

**The fix** (`ViewerSession.RenderFrame`): a frame counts only when the opening state had been applied before its
sequence began, and only after a frame that was itself eligible, so the seek frame and the one whose interval holds
its tail are both out. The per-second lines, the allocation tally and the GC counters restart where counting does.
Two columns were added to the summary: `between`, the mean frame the clock used less the mean of the sequence's own
total (the idle loop and the message pump; valid as a mean only), and a `gc:` line from the runtime's own counters
(`GC.GetTotalPauseDuration`, `GC.CollectionCount`, `GC.GetTotalAllocatedBytes`).

**Every mean before 2026-10-19 carried about 8 s of opening** — finding 80's frame times and its "large allocations
per 20 s" included the seek's allocations, so its deltas were diluted, not wrong in sign.

**Controls after the fix**, measured: ticks advanced 1316–1336 in 20 s (1333 expected); the phases plus `between`
sum to the mean within 0.01 ms (22.81 against 22.82).

## The frame, quiet (after the instrument fix)

Means over every counted frame, ms. Debug is what the owner runs (D176).

| run | mean | sound | camera | advance | hud | draw | between | gc pause |
|---|---:|---:|---:|---:|---:|---:|---:|---:|
| Debug 1 | 22.82 | 0.89 | 13.31 | 2.66 | 1.32 | 2.88 | 1.74 | |
| Debug 2 | 22.86 | 0.62 | 13.60 | 2.55 | 1.29 | 2.83 | 1.95 | |
| Debug 3 | 27.36 | 0.81 | 15.67 | 3.14 | 1.45 | 3.23 | 3.06 | |
| Debug 4 | 24.70 | 0.51 | 15.06 | 2.91 | 1.39 | 2.98 | 1.83 | 2.68 |
| Release 1 | 22.73 | 0.46 | 13.58 | 2.67 | 1.36 | 2.91 | 1.74 | |
| Release 2 | 23.00 | 0.44 | 13.89 | 2.71 | 1.30 | 2.84 | 1.80 | |
| Release 3 | 23.12 | 0.64 | 13.84 | 2.62 | 1.32 | 2.81 | 1.88 | |

`project` and `capture` are 0.00–0.01 in every run and are left out. Debug 3 ran slow across every phase alike.

**None of the gap is Debug-only.** Release is 22.7–23.1 ms against Debug's 22.8–22.9 in the quiet runs, because
Debug is compiled optimized (`<Optimize>true</Optimize>`, `Directory.Build.props`, D176). The difference is noise.

**The camera phase, split** (per-second lines, steady seconds): `camera pose` 6.6–9.7 ms (the moment rebuild:
drawlist 1.4–1.6, lighting 1.0–1.8, simulate 1.3–1.4, collate 1.1–1.2, place 1.1–2.3 of which flex 0.8–1.7, setup
0.6–1.2, baked 0.4–0.9), `camera particles` 3.6–8.4 ms, `camera setcamera` 1.0–4.5 ms, `camera view` under 0.15.

**The collector: 2.68 ms a frame paused, 11.6 MB allocated a frame, 1.9 collections a frame** (1,352 gen0 and 228
gen1 in 810 frames, no gen2), measured from the runtime. Valve allocates nothing a frame that a collector would have
to recover. The pause lands inside whichever phase allocated, so no phase names it.

## The empty view: the per-frame floor

`TF2VIEW_CAMERA="1613 -2821 154 89 0"` without `--first-person` (with it, the camera follows the player and the
override is ignored, so the "empty" runs taken first with `--first-person` were not empty: they posed 24–35 props, the
same as a normal run, and are discarded).

| | mean | camera | advance | hud | draw | between | gc pause | allocated |
|---|---:|---:|---:|---:|---:|---:|---:|---:|
| empty, Debug | 11.86 | 6.61 | 1.29 | 1.22 | 0.71 | 1.51 | 1.28 | 6.41 MB |

Posed 0 of 1,870 selected, 1,650 hidden by PVS. With nothing drawn the frame is still 11.9 ms: the moment rebuild
walks every prop (`simulate` 1.3, `collate` 1.1 with nothing in view — B259), the particles still step (2.2), and
6.4 MB is still allocated a frame. Engine reading for these follows below.

## The CPU profile

`dotnet-trace collect --profile dotnet-sampled-thread-time` of the Debug viewer, the render thread over 16 s of
playback, read by the new `cpu-trace` probe. **Instrument note:** 41% of the render thread's samples have
`Thread.PollGCWorker` as the leaf. That is a thread stopped at a GC poll, and the EventPipe sampler suspends the
runtime to take its samples, so that share is partly the instrument's own; the runtime's pause counter (2.68 ms of
24.7) is the number believed. The frames above the poll still name where the thread was.

Inclusive, render thread: the camera upload 60.4%, of which `MomentScene.Pose` 34.4% and `EntityModelSet.Instances`
32.9%; the particle draw 19.3%; the device draw 13.5%; `Simulate` 11.3%. **Control, against the phase timers of the
same kind of run:** camera 60.4% against 57–61%, pose 34% against 30–38%, draw 13.5% against 12.6%. The coarse level
agrees. **The leaves do not carry weight**: a thread reports at its next GC poll, which biases leaves toward code that
polls (`BulkMoveWithWriteBarrier`, `Array.Copy`), and "who reached PollGC" says where polls were hit, not who
allocated. Allocation is measured below by its own instrument instead.

## Who allocates: two threads, and the larger is not the render thread

`dotnet-trace --profile gc-verbose` (an allocation tick every ~100 KB, every heap), read by `alloc-trace … all <from>
<to> [thread]` over 17 s of playback. **Control:** 10,446 MB per 20 s, against the `gc:` line's 11.6 MB × ~880
frames ≈ 10.2 GB.

| thread | MB per 20 s | under |
|---|---:|---|
| the render thread | 3,834 | `FrameSequence.Run` |
| a pool worker | 6,533 | `CorpseRecord.Run` (D181): `CorpsePhysics.Advance` 3,157, `IkFor` 1,531, `UpdateClientSideAnimations` 661, `IvpRagdoll.State` 428 |

**The worker is steady, not a load tail**: 6,377 MB/20 s over the first 8 s of the window and 6,759 over the last 8.
Its CPU profile is `CorpseRecord.Run` at 100%: the record of every corpse in the demo, stepped straight through on a
pool thread, runs for the whole measurement. **And the pause is the worker's clock, not the draw's**: the collector
paused 2.68 ms × 40.5 fps = 108 ms a second in first person and 1.28 × 84.3 = 108 ms a second in the empty view.
Drawing nothing halved the frame and left the pause per second unchanged.

By type, every thread: `StudioBonePose[]` 1,339 MB (all on the render thread: `SkinnedModel.PoseOf`/`PoseBetween`,
`StudioPoseBlend.Expand`), `(Vector3, Quaternion)[]` 1,306 MB (`IvpRagdoll.State`, all on the worker), the `IkFor`
closure 560 MB and its `(StudioIkRule, …)[]` 508 MB, `SceneProp` copies 430 MB (`UpdateClientSideAnimations` in the
record).

## Fix 1: a ragdoll's state is written into its own buffer (2026-10-19)

**The engine, read from published source.** `RagdollGetBoneMatrix` has each element's physics object write its matrix
straight into the caller's bone accessor — `element.pObject->GetPositionMatrix( &pBoneToWorld.GetBoneForWrite(
boneIndex ) )` (`game/shared/ragdoll_shared.cpp:573`). Nothing is allocated to read a ragdoll's pose.

**Ours.** `IvpRagdoll.State()` built a new `(Vector3, Quaternion)[]` on every call: the live corpse step, the fall
watch, `PoseIntoAccessor` and the D181 record's `Store` each called it, 1,306 MB per 20 s. Only `Store` keeps a pose.

**The change.** `State()` writes into one array the ragdoll owns and returns it, valid until the next call; `Store`
copies a pose that moved (`[.. state]`) and keeps sharing the tick before's array for one that held, as it did.

**Tests.** `IvpRagdollTests.State_AskedTwice_AllocatesNothingTheSecondTimeAndReusesItsBuffer` (0 bytes on the second
call, same array, same values) reddens when `State` allocates a fresh array again.
`CorpseRecordTests.Run_OneCorpse_RecordsWhatStraightThroughPlayComputes` — the record against straight-through play,
tick by tick — reddens when `Store` keeps the shared buffer instead of copying it (every tick would read the last
pose). Animation.Tests 5,339 (5,338 passed, 1 skipped), Scene.Tests 3,151.

**Measured**, f12, Debug, `--measure 20`, two runs a side:

| | mean frame | gc pause / frame | allocated / frame | gen0 + gen1 collections |
|---|---:|---:|---:|---:|
| before | 24.70, 22.41 ms | 2.68, 2.61 ms | 11.60, 11.93 MB | 1,580, 1,793 |
| after | 22.58, 23.10 ms | 2.28, 2.35 ms | 10.42, 10.73 MB | 1,550, 1,560 |

About 1.2 MB a frame and 0.3 ms of pause a frame less; the mean frame moves inside its own noise. **The picture is
unchanged**: `--shot` at tick 30700, first person, before and after, differ in 48 pixels, all inside the frame-rate
readout's box (805,2–823,10), which reads a different time every run. No corpse is in that view, so the shot proves
the scene, not the corpse pose; the record test above is what holds the pose.
