# 105 - Two off-thread moves: parallel bone setup (A) and HUD paint off the game thread (B)

Status: step 1 (instruments) built and measured, both GO (section D); the forks are being built on `perf/offthread-bones-hud`. Evidence classes labelled: *source* (SDK), *code* (ours), *measured* (named file), *estimate*. Read on the hitscan-implementation worktree plus `f049abfb` (queued render thread, finding 103) for the threading seams. Both designs are within-frame fork-joins on the same pool; forks never nest and never coexist (bones, then particles, then HUD, in time), so the core budget is shared, not summed.

## 0. The frame as it runs today (code, `f049abfb`)

`FrameSequence.Run` (`managed/Tf2DemoSalvage.Presentation/FrameSequence.cs:122-132`): `TakeShot`, `Simulate` (sample, `MomentScene.Build`, `Pack`/`Add`, `UpdateClientSideAnimations`), `PlaceCamera` (`ViewerPresenter.Device.cs:78-109`: fly, `SetCamera` cull, `FinishDrawBeforePose` wait at `:184`, `PoseOver` -> `MomentScene.Pose` -> `Instances` world/undrawn/viewmodel, then `_effects.DrawOver` particles), `UpdateListener`, `ProjectWorld` (`PoseNow` no-op, `_posed` guard `MomentPresenter.cs:319`), `BuildOverlay`, `Draw` (mode 2: hand-off to `MatQueue`, one frame in flight; mode 0: serial draw).

Existing fork: `ParticleWorkers` (`849d94c0`, `Parallel.For`, `Degree = PhysicalCores() - 1`, threshold 4, injectable `_schedule` for adversarial tests). Finding 103 section 4: fork-join inside `DrawOver`, nothing crosses a frame.

## A. Parallel bone setup

### A1. Engine (source)
`C_BaseAnimating::ThreadedBoneSetup` (`c_baseanimating.cpp:2777-2794`): `ParallelProcess` over `g_PreviousBoneSetups` - last frame's roots (`!GetMoveParent()`, `nBoneCount >= 16`, appended at `:2897-2902`), `mdlcache->BeginLock/EndLock` around, `SetupBonesOnBaseAnimating` passes `boneMask -1` -> `m_iPrevBoneMask` (`:2827-2830`). Called at `cdll_client_int.cpp:2210`, after `SimulateEntities`/`PhysicsSimulate`, before `FireEvents` and particles. Per-entity `m_BoneSetupLock.TryLock` returns false on contention (`:2845-2850`). `cl_threaded_bone_setup "0"`, `FCVAR_DEVELOPMENTONLY`, "currently crashes" (`:2741-2744`). Children (merged items) are never in the list; they are built on demand in the draw via `CBoneMergeCache` (`bone_merge_cache.cpp:130`).

### A2. Ours - the work to move (code)
`EntityModelSet.Instances` (`managed/Tf2DemoSalvage.Scene/EntityModels.Instances.cs`): `_boneFrames.Advance()` `:116`, `Simulate` `:126` (sets per-entity `IBonePose` state), `AdvanceCorpses` `:149`, `Collate` `:207` fills `_ordered`, draw loop `:221` calls `animating.SetupBones(UsedByAnything, seconds)` `:385` per skinned prop, then `Skinning` `:401` (`EntityModels.Placement.cs:238-257`).

`AnimatingEntity.SetupBones` (`managed/Tf2DemoSalvage.Animation/Animating/AnimatingEntity.cs:228-364`): frame guard `:250-270`, readable-subset early-out `:279`, ragdoll hook `:318-330`, recursive `parent.SetupBones` `:350`, merge `:358`, `_pose.Build` `:361` -> `SkeletonPose.Build` (`SkeletonPose.cs:323`).

**Move**: `_pose.Build` + merge of each root (`Follows is null`), as a pre-pass between `Collate` (`:207`) and the draw loop (`:221`), over this frame's collated roots (`_ordered`) - an addition above Valve (no speculative last-frame list, no wasted builds; output identical because the draw loop then takes the cache hit at `:279`). Children stay serial in the draw loop (as the engine).

Reads: per-entity `IBonePose` state (written by `Simulate`, before the fork), `StudioBone[]` (immutable), `SkinnedModel` caches, `BoneFrameCounter.Frame` (read-only during the pass). Writes: per-entity `BoneAccessor` arrays, `_written`, `_builtOn/_previousMask/_accumulatedMask/_lastBoneSetupTime`, `SkeletonPose` scratch - all per entity.

### A3. Hazards (code, file:line)
1. `SkeletonPose.PoseBuilds` / `AnimationTicks` / `AnimationCalls` are static mutable (`SkeletonPose.cs:133,136,155`; `++` at `:330,337,338`), read as deltas at `EntityModels.Instances.cs:124,139,142,153,383,388,427` and `EntityModels.Queries.cs:221,228`. Worker `++` corrupts every counter (B243).
2. `EntityModelSet.SetupTicks/BuildsDrawing/Posed` (`EntityModels.cs:397-412`, written `Instances.cs:387-389`) - serial loop today; the pre-pass needs worker-local accumulators summed at the join.
3. A child's `SetupBones` writes its parent: `_accumulatedMask |=` `AnimatingEntity.cs:272`, and the frame guard `:250-270`. Rule: roots only in the fork; children only after the join.
4. `Ragdoll` delegate (`AnimatingEntity.cs:56`, run `:326`) reaches the IVP simulation; thread-safety unverified -> exclude `Ragdoll is not null` from the fork.
5. `PropModels.SkinnedModel` lazy state: `_ikChains ??=` `PropModels.cs:3046`, `_autoplay ??=` `:3066` (benign double-init); other caches already `ConcurrentDictionary` (`:1746-1771`, `:2224-2227`); `_skeletonsPosed` is `Interlocked` (`:1048`). Verify `StudioBones.FromQuaternion/Concatenate` and `StudioAnimation.Pose` hold no static scratch (not read here).
6. `SkeletonPose.Log` Debug lines and `BoneMergeCache.Report` go through `FileLogWriter` (`lock`, `FileLogWriter.cs:91`) - safe, order changes; log is not output.
7. `EntityModelSet._entities/_frames/_entityModels` dictionaries - read-only during the fork; writers (`Add`, `Precache`, `Packing.cs:142,404`) run outside the pose.
8. `mat_queue_mode 2`: bone arrays are reused in place and hashed by the verifier (`Device3D.cs:3365-3395`); covered because the fork sits inside the pose, after `FinishDrawBeforePose` (`ViewerPresenter.Device.cs:184`). Must stay there.
9. `AttachmentRebuilds` instrument (`Queries.cs:222,235`) changes meaning: a pre-posed root reads as "built before". Document.

### A4. Independence and order
Independent: roots = players, sentries/dispensers/teleporters, doors/props with skeletons; viewmodel arms are a separate `Instances` call, small, serial. Dependent: wearables, weapons, attached models (`Follows`), parented non-merged props, corpses. Order: engine = previous frame's roots, speculative; ours = this frame's collated roots, exact. Mask: pre-pass asks `_previousMask` as the engine's `-1` does; first frame / newly visible root -> `_previousMask == 0` -> skip, draw loop builds it as today. Cores: pool degree `PhysicalCores() - 1`; MatQueue is idle during the pose; the particle fork runs after the join. On 2 physical cores: serial. Fork only when roots >= 2 and root bones >= 16 and measured root cost passes the go/no-go.

### A5. Bit-identical output and its tests
Floating point is per-entity sequential, so identical by construction; differences possible only from shared-state races (A3).
- `EntityModelsTests.Instances_RootsPrePosedOnWorkers_SameBonesAsSerial` (synthetic, D38): ~6 players with 2 wearables each, one weapon, one corpse with a ragdoll delegate, one parented non-merged prop; serial vs forked with an adversarial injected schedule; compare every `ModelInstance.Bones` float array byte-for-byte, `Posed`, `BuildsDrawing`, each entity's `ReadableBones`, and the next frame's `_previousMask`.
- Sabotages that must redden: a child in the root list; drop the `Ragdoll` exclusion; `++` a static counter from a worker.
- Output-level: 200-frame f12 run, serial vs threaded, the rung-2 frame hash (`6e3c7124`, hashes bones) equal frame by frame.

### A6. Measure first, go/no-go
Existing: `SetupTicks`, `SkinTicks`, `BuildsDrawing/Simulating/Placing/Corpses`, `PoseBuilds`, `AnimationTicks`. Missing: `SetupTicks` split root vs child, roots per frame, max single-root ticks (critical path). Parallel time ~ max(slowest root, roots total / degree) + fork overhead (finding 94 measured ~0.08 ms net gain on a 0.36 ms job: overhead ~0.03-0.05 ms, measured precedent).
- Go: roots total >= 0.4 ms and roots >= 4 per frame. Estimate: 1.0 ms root total on 7 workers -> ~0.2 ms + 0.04 overhead -> **0.5-0.7 ms**. If children dominate (24 players x ~3 cosmetics ~ 100 builds) wave 2 decides.
- No-go: roots < 0.3 ms.

### A7. Staged plan
1. Instrument: `SetupRootTicks`, `SetupChildTicks`, `RootsPosed`, `MaxRootTicks` into the cost line; measure f12 S/S. Decide.
2. Serial pre-pass over `_ordered` roots with `_previousMask` (`AnimatingEntity.SetupBonesFromPreviousMask(seconds)` exposing the engine's `-1`), excluding `Ragdoll`; A5 test asserts identical bones. Measure: cost unchanged.
3. Generalise `ParticleWorkers` -> one `GameWorkers` with the schedule hook (D228: one pool, never a second).
4. Make the static counters race-free: `[ThreadStatic]` partials summed at join, or `Interlocked`; worker-local `SetupRootTicks` summed at join. Tests: counts equal serial vs forked.
5. Fork the pre-pass via `Workers.For(roots)` behind `cl_threaded_bone_setup` (engine name; our default on is above Valve and needs the owner's say, D-entry). Measure S/T/S/T; 200-frame hash equality.
6. Optional wave 2 (children in parallel after the roots join): `_accumulatedMask |=` (`:272`) made `Interlocked.Or`, parents pre-built with the union of their children's `FollowBoneSetupMask`. Only if step 1 shows children >= 0.4 ms.

## B. HUD paint off the game thread

### B1. Engine (source)
VGUI paint is on the game thread; under `mat_queue_mode 2` the surface's draw calls are recorded and played on `MatQueue` (findings 98/99). No engine counterpart for building the panel tree on a worker - wholly above Valve.

### B2. Ours - what `BuildOverlay` reads and writes (code)
`FramePresenter.BuildOverlay` (`managed/Tf2DemoSalvage.Presentation/FramePresenter.Hud.cs:180-292`):
- Reads live, not a snapshot: `_window` delegates (`Gdi`, `Game()`, `MapName()`, `At(tick)` -> `ViewerPresenter.HudWindowAt` `ViewerPresenter.Frame.cs:110-123`), `_overlay` record, `_hudEvents.Advance` cursor `:235`, `HudStateAt` `:262` -> `HudStates.For` (`HudStates.cs:29-80`) + `IdTargets.With` (`FramePresenter.cs:241-242`) -> traces against posed hitboxes (`IdTargets.cs:50-53`; `TraceHitboxes` `Queries.cs:252` calls `SetupBones` `:261`) - so HUD state depends on the pose; `_models.Precache` writes `_frames` `:253`; `HudArchives()` `:153-168` rebuilt on `ChosenHud`.
- `VguiHud.Frame` (`VguiHud.cs:146-374`): `VguiRichText.HudClock` static write `:164`; scheme reload `:183-211`; `Think/SetState` `:226-228`; game events -> `SoundEmitter` -> `PlayHudSound` -> `HudSound(...)` (`FramePresenter.Hud.cs:318`) `:233-271`; lifecycle, user messages, `ItemEffectMeters.Update`, scoreboard, `SpectatorGui.Update` `:274-328`; then `SolveTraverse` `:335`, cards `:343-347`, `PaintTraverse(_host.List)` `:372`. Model panels inside paint: own `AnimatingEntity` + clock (`VguiMdlPanel.cs:517`, `VguiModelPanel.cs:628`), `SetupBones` (`VguiMdlPanel.cs:324,340,381`, `VguiModelPanel.cs:729`) -> static `PoseBuilds++`; `MdlCache.*` (`VguiModelPanel.cs:298,370,384,419`) -> `_frames` reads (`EntityModels.cs:284`).
- Writes: `VguiDrawList` quads/models (`VguiDrawList.cs:90-91`), glyph cache pages + versions (`VguiGlyphCache.cs:22-24`, rasterised on first use via `IVguiGdi` `:42-63`), `VguiTextureSizes._sizes` (`VguiTextureSizes.cs:20,37-44` -> VPK read + VTF decode), `FrameParts` (`VguiHud.cs:361-370,380-390`). Hand-off: `FrameOverlay` -> `DrawScene` -> `Device3D.DrawFrame(..., vgui)` (`Device3D.cs:2862-2873`); device resolves textures by name at draw via `SetVguiResolver` (`FramePresenter.Hud.cs:207`) - already runs on `MatQueue` in mode 2.

### B3. Frame N while main continues to N+1? No.
One frame of HUD latency is a visible change (target ID, ammo, timers lag the picture). Rejected. The design is a within-frame fork: the forked half must finish before this frame's VGUI pass, and before the next frame's `BuildOverlay` (panel state) and `Pack` (`_frames` writer).

### B4. The split
- **Serial half (game thread, after the pose)**: everything through `VguiHud.cs:328` and the tools panel's inputs read as values. This half owns every side effect: event cursor, sounds, `Precache`, scheme reload, `HudClock`.
- **Forked half (one pool worker)**: `SolveTraverse`, cards, `PaintTraverse` (`:335-373`), and `VguiTools` layout+paint, into the frame's `VguiDrawList`. `FrameOverlay` carries the list plus a completion handle; the device waits at the VGUI pass (mode 0: inside `DrawFrame`; mode 2: `MatQueue` waits there, world draw overlaps the paint). The game thread also waits before `Pack` in the next `Simulate`.
- Overlap gained: mode 0 - paint overlaps the serial world draw. Mode 2 - paint overlaps hand-off + `TakeShot` + next frame's sample/`Build` (~0.5-0.8 ms of `advance`, estimate) and `MatQueue`. Tight in mode 2; the pre-`Pack` join may stall if paint exceeds that window - measure.

### B5. Hazards (code, file:line)
1. `VguiRichText.HudClock` static (`VguiHud.cs:164`) - written in the serial half, read in paint; safe within-frame only.
2. `SkeletonPose.PoseBuilds` static `++` from panel `SetupBones` during paint while the next frame's `Instances` reads deltas (`Instances.cs:124-153`) -> wrong counts unless joined before `Simulate`'s pose. Same fix as A3.1.
3. `_frames` read by panels (`EntityModels.cs:284`) vs writers `Add` (`Packing.cs:142`) and `Precache` (`FramePresenter.Hud.cs:253`). Join before `Pack`; or prefetch each panel's `ModelFrames` in the serial half (`ModelsToPrecache()` already names them, `VguiHud.cs:87-92`).
4. `FrameParts.Add` from paint closures (`VguiHud.cs:361-370`) concurrent with the game thread's `Part(...)` - plain `Dictionary`. Worker-local `FrameParts`, merged at join.
5. GDI on a worker: glyph rasterisation `VguiGlyphCache.cs:42-63` -> `IVguiGdi.GetCharAbcWidths/DrawGlyph/GetGlyphOutlineGray8` on DCs created on the UI thread (`CreateFont`, `IVguiGdi.cs:40`). Win32 permits a memory DC from another thread if not concurrent; the join guarantees one user. Unverified -> UI-suite check. `VguiFontManager` recreation on resize (`VguiSurfaceHost.cs:68-74`) stays serial.
6. `HudArchives()` reached from paint via `VguiTextureSizes.resolve` and `.res` loads for new cards (`VguiHud.cs:343-346`); `_hudArchives` can be swapped by a `ChosenHud` write on the UI thread. Capture the `GameArchives` reference before the fork. VPK reader thread-safety already exercised concurrently in mode 2 - verify once, record.
7. Sound: `SoundEmitter` -> `PlayHudSound` (`FramePresenter.Hud.cs:256-259,318`) must never fire from the forked half. Prove with a counting emitter: zero emissions during `PaintTraverse`/`SolveTraverse` with every element visible.
8. `_overlay` field (`:67`) read by `PlayHudSound` - serial half only after 7 holds.
9. `VguiHud.Viewport.*` delegates set in the serial half (`:219-224`, `:240-243`) read in paint - set before fork. `VguiSurfaceHost.Localize` lazy `:45-61` - force in the serial half.
10. Panel particles (`TfPlayerModelPanel`) step in paint by `FrameTime` (`VguiHud.cs:171`): per-panel state, `ParticleEffect.BirthsBySystem` is `ConcurrentDictionary` - fine.
11. `Device3D.SetVguiResolver` (`:205-208`) stays serial (device members take the engine `Lock`).

### B6. Bit-identical output and its tests
- `VguiHudTests.Frame_PaintOnAWorker_SameDrawListAsSerial` (synthetic HUD state with scoreboard, spectator cards, death notices, a model panel, text in three fonts): compare `Quads`, `Models` (bones bytes), glyph page RGBA + versions, `_sizes` asks, in order, serial vs forked (forced-schedule hook). Sabotages: emit a sound from paint; read `HudClock` after a worker write of the next frame; drop the pre-`Pack` join with a fake `Add` racing.
- Sound control test (B5.7).
- UI suite: HUD text capture at a fixed f12 frame, serial vs forked, pixel-equal (GDI on a worker).
- 200-frame f12 hash: extend the rung-2 frame hash to include the VGUI list; serial vs forked equal.

### B7. Measure first, go/no-go
Existing parts (`--measure`): `hud setup/events/precache/state/frame/tools` (`FramePresenter.Hud.cs:227-289`) and inside `frame`: `hud scheme/think/gameevents/updates/layout/cards/paint` + per-child names (`VguiHud.cs:224-373`). Needed: the forkable sum (`layout + cards + paint` + tools' paint share - split `hud tools` into inputs vs paint) against the serial sum; and in mode 2 the window between hand-off and `Pack`.
- Go: forkable >= 0.3 ms, and (mode 2) forkable <= that window, else the pre-`Pack` join stalls. Estimate (0.75-0.97 ms HUD, paint typically the larger half): **0.3-0.6 ms** off the game thread, minus ~0.03 ms fork.
- Check first whether `hud state` (`HudStates.For` per-frame `Names/AccountIds`, `IdTargets` traces) is a serial reduction worth more than the thread; doing less beats forking (finding 103's conclusion for particles).

### B8. Staged plan
1. Measure (B7) and the sound control test. Decide.
2. Split `VguiHud.Frame` into `Think(state, events, ...)` and `Paint(list)`; `VguiTools` likewise; `BuildOverlay` unchanged in output. Test: draw list identical to the unsplit path (synthetic).
3. Hoist from the paint half: captured `GameArchives`, forced `Localize`, panel `ModelFrames` prefetch (or keep the pre-`Pack` join), worker-local `FrameParts`.
4. `FrameOverlay` carries a completion handle; `Device3D.DrawFrame` waits before the VGUI pass; `MatQueue` path same; `Simulate` joins before `Pack`. Behind our own cvar (no engine name exists, e.g. `tf2view_threaded_hud`), default decided by the measurement. Tests B6. S/T/S/T.
5. Counter fixes shared with A (A7.4) so the join-before-pose constraint is belt-and-braces, not load-bearing.

## C. Shared: cores, nondeterminism, interplay
- One pool (`GameWorkers`, from `ParticleWorkers`), degree `PhysicalCores() - 1`; `MatQueue` and the game thread are the other two. Timeline per frame: [bone fork, join], [particle fork, join], HUD serial half, [HUD paint on 1 worker overlapping the draw]. No two forks run at once; the HUD worker runs while `MatQueue` draws, so it needs >= 3 physical cores, else serial.
- Nondeterminism sources are only shared mutable state: the static counters (A3.1, B5.2), `FrameParts`, `HudClock`, lazy `??=` publishes (benign), log order (not output). Per-entity and per-panel arithmetic is sequential and unchanged, so output is identical by construction once those are fenced; the tests above prove the fence holds.
- `mat_queue_mode 2`: both forks stay inside the window the engine's `FinishDrawBeforePose` already protects (bones) or hand a completion the draw waits on (HUD); the in-place bone reuse covered by the verifier (`Device3D.cs:3365-3395`) stays covered.
- Owner decisions to record (D-entries): `cl_threaded_bone_setup` default on (engine ships 0, dev-only); the HUD fork's name and default; this-frame roots instead of the engine's last-frame list.

Totals (estimate): A 0.5-0.7 ms, B 0.3-0.6 ms, against a ~9.5 ms frame; both conditional on the step-1 measurements.

## D. Step 1 measured: both GO (2026-10-26)

*Measured*, `measurements/2026-10-26-offthread-bones-and-hud-go-no-go.md`, f12 gummo first person, mode 2, three runs:
- **A**: eligible roots (at least 16 bones, no move parent, no ragdoll) 0.53-0.56 ms a frame over 6.3 builds (measurements/2026-10-26-offthread-bones-and-hud-go-no-go.md); slowest 0.12 ms; children 0.04 ms, so no wave 2. The count was wrong in A6's estimate (six roots, not tens): the critical path is ~two roots deep on five threads, so the saving ceiling is ~0.3 ms (*estimate*).
- **A3 missed a hazard**: `AnimatingEntity.BeforeBuild` (`EntityModels.Simulation.cs:599-663`, `BuildBlend`) runs inside every root's request and reaches `EntityModelSet` (`LayersFor` counts `SubstitutedFlinches`, model lazy caches). It is idempotent within a frame (`pending.Ready`), so the pre-pass runs it serially for every root before the fork and the worker's call finds it already done.
- **The fork's first measurement found a parity defect instead** (`measurements/2026-10-27-threaded-bone-setup.md`, inconclusive on the frame): with the pre-pass the eligible builds fell from 6.1-6.4 to 4.7 a frame, so the same-work control failed. *Read from source*: the engine bumps its bone counter once a frame (`InvalidateBoneCaches`, `OnRenderStart`, cdll_client_int.cpp:2163); `Instances` bumped ours on the world, viewmodel and undrawn passes. Every attachment query after those passes (the effects') was a first touch on a stale clock: it rebuilt a drawn skeleton and rolled `_previousMask` to the attachment bit, so next frame an item drawn before its wearer built the wearer narrow and the wearer's own call built it again. Finding 82's "0 rebuilt" was an instrument that asked `BuiltThisFrame` after the clock had moved and could only read 0. Fixed: once a frame, at the world pass (`AttachmentQueryTests` hold both halves). *Measured* (`measurements/2026-10-27-bone-clock-and-threaded-setup.md`): the fix alone is −0.43 to −0.53 ms a frame, attachment builds 7.4-7.5 → 3.5; on that base, with the same work, the fork takes `setup` down 0.13-0.17 ms and the frame by an amount inside the noise, and its pose hash equals the serial one on every label. The fork stays default off. The coordinator's amendment (attachment-queried roots in the pre-pass, as `g_PreviousBoneSetups` lists every caller's) is measured per caller in the particle cost line; after the fix, what those queries still build is entities nothing drew, the only ones a speculative last-frame list would add.
- **B**: forkable 0.58-0.61 ms against a 0.95 ms window to the next `Pack`, overrunning on 0.7-1.8% of frames by 0.002 ms a frame (measurements/2026-10-26-offthread-bones-and-hud-go-no-go.md). B7's check: `HudChat` is 0.30-0.33 ms, half the paint (measurements/2026-10-26-offthread-bones-and-hud-go-no-go.md) — doing less there may beat the fork.
