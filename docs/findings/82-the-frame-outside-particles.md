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

## Fix 2: a prop's leaf handle lives with the object, as the engine's lives on the entity (2026-10-19)

**Where the empty view's floor is, profiled.** CPU samples of the render thread over 16 s of the empty view, restricted
to the two pose columns that do not fall with the view (`cpu-trace … under:<method>`): `Collate` was 741 samples and
`SimulatePass` 841, of 7,545. Inside `Collate`, hashing the `(entity, model path, occurrence)` key was 24.6% and the
dictionaries keyed on it another 26%. **The leaf gather itself was already the engine's** (B262: `ClientLeafSystem`,
`BuildRenderablesList` over the view's leaves). What was not the engine's is how a prop finds its own handle.

**The engine, read from published source.** A renderable's handle is held by the entity (`m_hRender`), and
`CClientLeafSystem::PreRender` touches only the renderables that called `RenderableChanged`
(`clientleafsystem.cpp:543`, quoted in B259 stage C). An entity that did not move is not visited at all.

**Ours.** Every frame, every drawable prop recomputed its key, hashed a model path to count occurrences, looked up
its filed box and its handle by that key, and the stale sweep and the handle map walked every key again — about 1,870
string hashes several times over, to change nothing for 98% of them.

**The control first.** Two counters at the site, carried in the moment-cost line: of the unparented props collated,
how many were the SAME object the previous collation saw, and how often a key's occurrence was above zero. f12, first
person: **1,577–1,580 of 1,605–1,618 unparented props a frame are the same object**, because B259 stage C hands an
unchanged track back as the object it built. **0 repeated keys**, at entity 0 or anywhere else.

**The change.** An unparented prop that arrives as the same object as last frame, whose handle nobody claimed yet this
frame, is filed by identity: its handle and box are reused with no keyed lookup, no box and no re-link
(`EntityModelSet.Collate`). Everything else — a new object, a parented prop, the first frame of a level — takes the old
keyed path, whose occurrence is now the first one no object claimed this frame. Claims, the stale sweep and the
per-handle map are keyed by the integer handle. `SceneProp` and `ScenePose` are init-only records, so the same object
cannot have moved; a path's model frames are written once a level and `LevelShutdown`/`Tree` forget every filing.

**One deliberate difference from the keyed path**, which is the engine's side of it: two props sharing entity 0 and a
model that arrive in the other order keep their own handles; the keyed path paired first-seen with occurrence 0 and
re-linked both. f12 has no such pair.

**Tests** (Scene.Tests 3,151 → 3,155), each sabotaged:

| test | reddens when |
|---|---|
| `Instances_SameObjectsAgainstFreshCopies_DrawTheSameEveryFrame` — a differential, the same frames fed as the same objects and as fresh copies (which never hit the identity path), through moves, a parenting, a parented child that stays the same object while its parent moves, an unparenting, entity-0 twins, a departure and a return | parented props are filed by identity too (the child stops following its parent) |
| `Instances_ANewTwinBeforeAnOldOne_BothDraw` | the identity path ignores a handle already claimed this frame |
| `Instances_AfterLevelShutdownWithAnotherModel_FilesFromTheNewOne` (existing, same object across the shutdown) | `LevelShutdown` keeps the identity filings |
| many existing Scene tests (corpses, worn items, placement) | the stale sweep removes the wrong handles |
| `Instances_TheSameUnparentedObjectNextFrame_IsFiledByIdentityAndNotRelinked`, `Instances_TwoEntityZeroTwinsSwapped_KeepTheirFilingAndAreNotRelinked` | pin the counter and the twin rule |

A frames-version check was written first and then removed: no sabotage of it could redden anything, because a path's
frames cannot change within a level, so it guarded nothing.

**Measured**, f12, Debug, `--measure 20`:

| | mean frame | camera | collate | simulate | gc pause |
|---|---:|---:|---:|---:|---:|
| first person, before (fix 1's two runs) | 22.58, 23.10 ms | 13.67, 14.08 | 1.1–1.2 | 1.3–1.4 | 2.28, 2.35 |
| first person, after | **21.99, 21.21** | **12.73, 12.51** | **0.4–0.5** | 1.2–1.3 | 2.27, 2.22 |
| empty view, before | 11.86 (at 1c190074), 11.32 ms | 6.61, 6.21 | 1.1 | 1.2–1.4 | 1.28, 1.12 |
| empty view, after | **10.50, 10.61** | **5.49, 5.55** | **0.3–0.4** | 1.2–1.3 | 1.05, 1.09 |

`collate` falls by about 0.7 ms a frame in both views, and the mean frame by about 0.8–1.2 ms. **Golden shots**, tick
30700, first person and the default spectator view, before and after: 30 and 55 pixels differ, all inside the
frame-rate readout's box.

**What the empty view still walks**, and it is the next owner: `simulate` 1.2–1.3 ms with nothing posed —
`SimulatePass` runs placement (`PlacementOf` 14% of it), frame selection (`SelectFor` 12.8%) and a case-folded
model-path lookup (12.2%) for every prop. The engine computes an absolute transform lazily behind
`EFL_DIRTY_ABSTRANSFORM` and resolves a model when the entity takes an update, so both are the same divergence this
fix closed for the collation. Not started here.

## Particles born a second on f12, for B557 (2026-10-19)

**The instrument**: `ParticleEffect.BirthsBySystem`, counted at the birth (`BirthAt`, the one place a particle is
made — continuous, burst and initial particles all pass through it), read by `--measure`'s `particles born a second:`
line over the counted playback. **Not counted apart**: births during a seek's replay inside the window, which this
window does not contain after the opening fix.

f12, first person, tick 30000, two runs: **2,871 and 2,763 born a second in all.** By system, run 1 (run 2 within
10%):

| system | born a second |
|---|---:|
| rocketjump_smoke | 1,181 |
| medicgun_beam_blue_trail_stage1 | 151 |
| medicgun_beam_red_trail_stage1 | 117 |
| rockettrail | 115 |
| rockettrail_burst, rockettrail_fire | 96 each |
| medicgun_beam_blue_trail_stage2 / stage3 | 83 / 77 |
| Explosion_Debris001 | 76 |
| blood_spray_red_01_far | 65 |
| medicgun_beam_red_trail_stage2 | 64 |
| blood_impact_red_01_chunk | 61 |

**`rocketjump_smoke` is 41% of every particle born.** That is the one to compare first when B557 has TF2's own
numbers. Its definition (`particles/rockettrail.pcf`'s neighbour `ROCKETJUMPTRAIL.PCF`) was not read here: the
`particles` probe names the file but does not print an emitter's `emission_rate`, so the declared rate × the number
of jumping players is the next measurement, not a conclusion. Nothing here was changed.

## Fix 4, reverted: a prop's model and placement resolved once per object (2026-10-19)

**Reverted in review, the same day.** Its own measurement below is NEUTRAL — the same frame and pause within the one
pair taken after its leak was fixed — and a mechanism nothing measurably needs is dead code here: unread or neutral
is removed, not kept on the chance it helps. It was also not free to carry. It added two swapped maps, a generation
counter and a sweep that had to be remembered in every frame function of every set, and forgetting one had already
happened once: before the fix described below, the D181 corpse record's set held every corpse object of the demo, a
leak that made the first version measurably worse. And it was the mechanism the review's high finding (a prop filed
by identity keeping an empty box after its model arrived) would have had to be threaded through. That finding is fixed
instead where the engine fixes it, by re-filing on a model change (`SetModelPointer` → `UpdateVisibility` →
`RenderableChanged`, `c_baseentity.cpp:1790`, `:3950`), which needs no resolution cache. The verdict of the section
after this one, "tried and not kept", stands again. What it measured is kept below as the record.

The owner's rule, which reverses the "not kept" below: *"if its parity but worse we need to figure out why, if its
parity but the same we use it"*. So the patch went back on and was measured on a quiet machine, all runs first
person, f12, Debug, tick 30000, `--measure 20`, binaries built from `58edf0fd` (before) and `58edf0fd` + the patch
(after) into separate folders, run alternating; no run waited on the lock.

| pair | before: mean / gc pause | after: mean / gc pause |
|---|---:|---:|
| 1 | 22.99 / 2.35 ms | 25.98 / 3.30 ms |
| 2 (both slow) | 31.10 / 3.20 | 33.31 / 4.24 |
| 3 | 22.55 / 2.30 | 24.27 / 3.17 |
| 4 | 22.09 / 2.26 | — (stopped) |

**Measurably worse, every pair: +1.7 to +3.0 ms a frame, +0.9 to +1.0 ms of GC pause.** The cause, found from the
summary's own memory line: GC heap 4,543 MB against 3,800, and `Entry[SceneProp, ResolvedModel][]` 47 MB of large
allocations. The resolutions were swept only by the world collation, and the D181 corpse record drives a private
`EntityModelSet` that never collates: every tick hands it a new corpse object, so it held every corpse object of the
demo. **Fixed by sweeping in `AdvanceCorpsesAlone` too, and by holding a resolution for one frame** (two maps swapped
each frame, not a sweep every 256). Test: `CorpseRecordTests.AdvanceCorpsesAlone_ThreeHundredTicksOfNewObjects_HoldsOnlyTheLastTwo`,
red without the record's sweep.

**After the fix, one pair** (the machine was busier by then; both sides alike): after 40.25 ms / 4.29 ms pause, before
41.85 / 4.52, GC heap 3,761 MB on both. The same within the pair, so kept. The direct `simulate` timer could not
separate them at this noise. **Golden shots** recreated from the named builds, first person at ticks 30480 (heal beam,
overheal particles) and 30700: differences only on the frame-rate readout's line (1,180 px in 685,2–973,12 at 30480,
whose readout text differs in width; 62 px in 697,2–823,10 at 30700). Scene.Tests 3,160.

## Tried and not kept: resolving a prop's model and placement once per object (2026-10-19)

**The engine, read from published source.** `CalcAbsolutePosition` returns at once while `EFL_DIRTY_ABSTRANSFORM` is
clear (`c_baseentity.cpp:4360`), and a model is set when the entity takes an update. So the animation pass's
per-prop placement and model-path lookups looked like the same divergence fix 2 closed.

**What was built.** Each prop object held its resolved `ModelFrames`, baked batches and, while unparented, its
placement matrix, redone when the model set changed (an `Add` that added, `LevelShutdown`). Five tests, each
sabotaged red: a counter that the same object resolves nothing, an update and a level change resolve once, a sweep
forgets objects no longer asked for, and the fix-2 differential upgraded to a skinned model compared bone by bone,
which reddened when parented props kept their placement.

**What it measured: nothing.** `simulate` in the empty view read 1.3–1.5 ms after, against 1.2–1.3 before, inside
the noise of a machine shared with other agents' viewers that run. **The first version used a
`ConditionalWeakTable` and was worse, not neutral: the GC pause went from 2.3 to 17.8 ms a frame**, because every
collection scans its dependent handles. That is a measurement worth keeping on its own.

**Why the profile promised more than there was.** The 12% "case-folded hash" and 14% `PlacementOf` were leaf and
inclusive shares under the sampler that suspends threads at their GC polls (the instrument note above), and inside
a pass where 35% of samples were polls. The shares were real places, not real milliseconds. **The lesson, filed for
the next profile here: rank by a direct timer before building, and treat a sampled share inside `SimulatePass` as a
place to look, not a cost.** The patch is kept outside the tree; nothing of it is committed.

## `between`: what repaints between frames (2026-10-19)

**The instrument**: `cpu-trace … under:Control.WndProc` and `under:ViewerSession.Idle` over 16 s of the empty view.

- **Window painting is 271 of 7,545 render-thread samples, 3.6%, about 0.4 ms of the 1.5 ms `between`.** 70% of it
  is one `Label`'s paint — the transport's tick readout (`TransportBar.UpdateTickLabel`) — with the transport
  panel's own background (`ScrollableControl.OnPaintBackground`, 25%) and the scrub bar's value (`TrackBar.set_Value`,
  7%) after it.
- **None of it is a repeated repaint.** `ShowTick` runs every frame, but `TrackBar.Value` and `Control.Text` both
  return early when the value is unchanged, so a repaint happens only when the TICK changes — 66 times a second,
  which at 50–90 frames a second is most frames. That is the readout showing new content, not the same picture
  painted again. Valve's demo UI updates its tick readout every frame too, in VGUI inside its own frame.
- **So there is no redundant repaint to remove**, and the remaining ~1.1 ms of `between` is the idle loop and
  message pump themselves (`Idle`'s own leaf share, the `PeekMessage` it calls). The one possible saving is the panel
  background behind the label, about 0.1 ms, by stopping the label resizing its parent; it would change how the
  readout is laid out, which the task's "the window looks the same" forbids without the owner's eye. Not done.

## The particle phase by direct timers (2026-10-19)

**Why timers and not the profile**: the sampled shares above over-promised once already. So each piece is a
Stopwatch where it runs, carried to the `--measure` summary's `particle cost a frame:` line (`ParticleCost`,
`BurstEffects.SearchTicks`/`BurstTicks`, `EntityModelSet.Attachment*`). Attachment queries are counted only from
outside the animation pass (effects, decals, sounds), with the skeletons each built counted at the build, and a
rebuild of an entity that already had bones readable this frame counted separately.

**f12, Debug, first person, two runs on a quiet machine, neither waited on the lock:**

| | run 1 | run 2 |
|---|---:|---:|
| mean frame | 22.31 ms | 22.24 ms |
| `camera particles` part, steady seconds | 2.6–5.0 ms | |
| timeline search for this frame's bursts | **1.55 ms** | **1.47 ms** |
| starting and stepping the bursts found | 0.69 ms | 0.68 ms |
| attachment queries a frame | 282.9 | 284.1 |
| skeletons those queries built | 9.3 | 9.4 |
| of those, rebuilt after this frame's first build | **0** | **0** |
| time in those queries' bone setups | 0.94 ms | 0.95 ms |

The attachment queries sit inside the search (the muzzle flashes and beams ask for attachments while they are
found), so the search's 1.5 ms holds most of the 0.95.

- **No second skeleton.** No query rebuilt bones the frame had already built: the per-frame cache works as the
  engine's (`c_baseanimating.cpp:2874`, `:2911`). The suspected second divergence is absent.
- **About nine skeletons a frame are built only to place an effect**, for entities this frame does not draw. Our
  query asks for `UsedByAnything`. Whether the engine's `GetAttachment` asks for less is the next read.
- **The search itself is the larger owner**: 1.5 ms a frame to find what the timeline offers this frame.

## Fix 3: an attachment asks for the attachment bones (2026-10-19, parity)

**The engine, read from published source.** `GetAttachment` calls `CalcAttachments`, which is
`SetupBones( NULL, -1, BONE_USED_BY_ATTACHMENT, gpGlobals->curtime )` (`c_baseanimating.cpp:2088-2094`), and
`SetupBones_AttachmentHelper` then fills every attachment from the bones (`:2055-2086`). `SetupBones` widens a request
by what is already readable and by last frame's mask (`:2920`, ported in `AnimatingEntity.SetupBones`).

**Ours** asked `UsedByAnything` (`EntityModelSet.PosedAttachmentMatrix`). Now `UsedByAttachment`.

**Test**: `AttachmentQueryTests.AttachmentPosition_OnAnEntityNothingDrew_BuildsOnlyTheAttachmentBones` — an entity
the undrawn pass held but built nothing for, queried once, reads `UsedByAttachment` (0x200) readable; red before the
change at 0x7FF00, the whole mask. Scene.Tests 3,156.

**Measured, and it buys nothing here**: attachments 0.96 / 0.90 ms a frame and 9.4 skeletons built after, against
0.94 / 0.95 and 9.3–9.4 before; search 1.81 / 1.44 ms, mean frame 22.64 / 22.28 ms. That is the engine's widening at
work: the entities these queries build were drawn on an earlier frame, so last frame's mask makes the build whole
anyway. Kept because it is the engine's request, and the same where it costs nothing (the owner's rule). **Golden
shots**, first person at ticks 30350 and 30480 (heal beams and overheal particles on screen), before and after: 56
and 48 pixels differ, all inside the frame-rate readout's box.
