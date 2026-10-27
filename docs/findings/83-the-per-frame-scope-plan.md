# 83. The per-frame scope plan: what Valve walks each frame, and what we walk (2026-10-19)

**A plan, not a fix.** Nothing here changes code or was measured on the desktop. The costs are finding 82's
(f12, tick 30000, first person unless marked "empty", Debug); the Valve column is read from published source
(`F:/src/source-sdk-2013/src`, quoted in the engine reader's per-frame report, sections 1–9). B259 already filed
the floor as "four passes walk every prop"; this finding extends that table to every per-frame loop the engine
has, and orders the work.

**The one question asked of each loop:** what set does it iterate — every entity, a maintained list, only what
changed, or only what is in view?

## The table

Evidence classes: Valve column read from published source unless marked CLOSED; "ours" read from this branch
(f4e46e50); cost measured (finding 82) or "not timed".

| Valve loop (file:line) | Valve scope | ours (file:line) | our scope | cost (finding 82) | verdict |
|---|---|---|---|---|---|
| `CClientLeafSystem::PreRender` (`clientleafsystem.cpp:528`, flag cleared `:592`) | **changed** — `m_DirtyRenderables`, set by `RenderableChanged` (`:1283`) / `AddRenderable` (`:711`) | `EntityModelSet.Collate` (`EntityModels.cs:7042`) → `ClientLeafSystem.Collate` (`ClientLeafSystem.cs:303`) | every prop; since fix 2, an unchanged unparented object skips the keyed relink | collate 0.4–0.5 (was 1.1); empty 0.3–0.4 | **we walk everything** (cheaply now); the visit itself remains |
| `BuildRenderablesList` / `CollateRenderablesInLeaf` (`clientleafsystem.cpp:1813`, `:1574`) | **in view** — the view's leaf list | the same leaf gather (B262) inside `ClientLeafSystem` | in view | inside collate | **same** |
| static props: `RENDER_FLAGS_STATIC_PROP` renderables (`clientleafsystem.cpp:660-666`), drawn by `staticpropmgr->DrawStaticProps` (`viewrender.cpp:4041/4047`) | added once at level load; never dirty; never on the entity list. **Body CLOSED** (`staticpropmgr.cpp` not in the tree) | `MomentScene.Build` appends `StaticProps` to `_drawn` every moment (`MomentScene.cs:344`) | **every static prop, every frame**, through every pass below | part of every column below; the static-prop count of the 1,870 is not measured | **we rebuild what Valve keeps** |
| `SimulateEntities` → `C_BaseEntity::Simulate` loop (`cdll_client_int.cpp:2010-2015`); `C_BaseAnimating::Simulate` (`c_baseanimating.cpp:5155`) does `DoAnimationEvents` and little else | **every entity** (`C_BaseEntityIterator`) — but not static props, and cheap per entity | `EntityModelSet.Simulate`/`SimulatePass` (`EntityModels.cs:1305`, `:1322`): per prop a model resolution, `EntityFor`, cycle advance, events, placement | every prop of the pass, **static props included** | simulate 1.2–1.4; empty 1.2–1.3 | **we walk everything** — Valve's set is entities only, and its per-entity work is the events |
| `C_BaseAnimating::UpdateClientSideAnimations` (`c_baseanimating.cpp:6368`); membership set `:4692`, `:6312`, cleared `:4696`, `:2301` | **list** — `g_ClientSideAnimationList`, entries with `FCLIENTANIM_SEQUENCE_CYCLE` | `EntityModelSet.UpdateClientSideAnimations` (`EntityModels.cs:5178`), called on `_drawn` and `_undrawn` (`MomentScene.cs:843`, `:847`) | the whole draw list, static props included; a class-name compare and a `with` copy per corpse | not timed apart (inside the camera phase); 661 MB/20 s of `SceneProp` copies on the corpse worker (finding 82) | **we walk everything** |
| `ProcessInterpolatedList` (`c_baseentity.cpp:3123`), list joined `:2834`, left `:2928` | **list** — `g_InterpolationList` | B259 stage C's lerp list and wake queue | list | `advance` 2.6 (not split) | **same** (B259 closed this one) |
| `InterpolateServerEntities`'s all-entity `Interp_UpdateInterpolationAmounts` loop (`c_baseentity.cpp:3246-3251`) | **every entity, but only on the frame `IsSimulatingOnAlternateTicks()`/`IsEngineThreaded()` changes** | none | — | — | **same** (nothing to do per frame) |
| `ProcessTeleportList` (`c_baseentity.cpp:3056`), set `:2643`, cleared `:3079` | **list** — `g_TeleportList` | per track, inside its sample (`ScenePropTrack.LastNoInterpolationParity`, `ScenePropTrack.cs:1392`) | not established; inside the sample | inside `advance` 2.6 | not assessed: whether the sample visits only listed tracks was not read |
| `IGameSystem::UpdateAllSystems` / `PreRenderAllSystems` (`igamesystem.cpp:301-320`) | **every registered system** (a handful, not entities) | `LevelSystems` and the per-system steps in `FrameSequence` | per system | — | **same** in shape |
| `C_BaseAnimating::InvalidateBoneCaches` (`c_baseanimating.cpp:3151`), called once from `OnRenderStart` (`cdll_client_int.cpp:2163`) | **one counter**, `g_iModelBoneCounter++`, once a frame | `_boneFrames.Advance()` (`EntityModels.Instances.cs`, in `Instances`) | one counter, **but bumped on every pass** (world, viewmodel, undrawn) until finding 105 D | — | **was different, read as same**: each post-pass attachment query rebuilt a drawn skeleton and rolled its mask; now once a frame, at the world pass |
| `engine->FireEvents()`, `tempents->Update()`, `beams->UpdateTempEntBeams()` (`cdll_client_int.cpp:2215-2221`) | **queued events, fired once**; live temp entities and beams. How a TE reaches the queue is engine-side, **CLOSED** (`src/engine` has no `.cpp`) | `BurstEffects.StepOver` search (below) | a timeline window every frame | search 1.5 | see the particle-creation row |
| `PhysicsSimulate()` (`cdll_client_int.cpp:2208`); `C_PhysPropClientside` (`physpropclientside.cpp:233`, think `:404`) | the client physics objects | `CorpsePhysics.Advance` (`CorpsePhysics.cs:207`) on the frame; the D181 record on a pool worker | live corpses; the record walks every corpse of the demo | 6,533 MB/20 s allocated on the worker; the GC pause is 108 ms a second either view | **we do work Valve does not** (the whole-demo record is D181's own choice, not a Valve loop) |
| `C_BaseEntity::AddVisibleEntities` (`cdll_client_int.cpp:2254`) | **list** — client-created unacknowledged predictables only | none | — | — | **same** (nothing to do) |
| `ShouldDraw` (`c_baseentity.cpp:1437`) | **per renderable reached by the in-view gather** | `prop.Pose.RenderMode == None` and the tally inside `Instances` | in view | inside place | **same** |
| `UpdatePVSNotifiers` (`cdll_client_int.cpp:2070-2134`) | **list** — PVS notifiers | `CulledByVisibility` judged per prop inside `Instances` | every prop | inside drawlist/collate | **we walk everything** (small) |
| `UpdateVisibility` (`c_baseanimating.cpp:3161`) | **changed** — event sites only: `SetDormant` (`c_baseentity.cpp:4021`), `OnDataChanged` CREATED (`:3319`), `PostDataUpdate` (`:2649`, `:2655`) | the visibility filters, rerun each moment over `_drawn`: first-person, weapon, disguise, respawn-room, vision (`MomentScene.cs:485-541`), plus `PlaceFrame` (`EntityModels.cs:4403`) indexing every prop | every prop, static props included | drawlist 1.4–1.6 | **we rebuild what Valve keeps** |
| `ProcessOnDataChangedEvents` (`cdll_client_int.cpp:2020-2067`) | **changed** — `g_DataChangedEvents` | `SceneProp`/`ScenePose` init-only records, re-handed each tick; B259 stage C hands unchanged tracks back as the same object | changed, mostly (fix 2's counter: 1,577–1,580 of 1,605–1,618 unparented props are the same object) | — | **same in shape**; the consumers above do not yet use it |
| `CClientThinkList::PerformThinkFunctions` (`client_thinklist.cpp:186-266`) | **list** — due entries of `m_ThinkEntries` (entries exist only after `SetNextClientThink` ≠ `CLIENT_THINK_NEVER`, `:71`) | `MomentScene.ThinkDynamicLights(props, …)` (`MomentScene.cs:331`) and per-system steps | every prop for the lights | not timed | **we walk everything** (unmeasured) |
| `C_BaseAnimating::SetupBones` (`c_baseanimating.cpp:2796`), last frame's mask (`:2920`), `ThreadedBoneSetup` over `g_PreviousBoneSetups` (`:2782`) | **per caller** — draw and attachment queries | `Instances`' setup on posed props; attachment queries (fix 3) | per caller; no second build per frame (finding 82: 0 rebuilt — **an instrument fault**: it asked `BuiltThisFrame` after the later passes had moved the clock, so it could only read 0; finding 105 D) | setup 0.6–1.2; attachments ~0.95 | **was different**: the rebuilds above |
| bone pose storage: `SetupBones` builds into stack arrays `Vector pos[MAXSTUDIOBONES]; Quaternion q[MAXSTUDIOBONES];` (`c_baseanimating.cpp:2965-2966`) and writes the entity's own cached bone array | no allocation | `SkinnedModel.PoseOf`/`PoseBetween`, `StudioPoseBlend.Expand` allocate `StudioBonePose[]` | per posed prop | 1,339 MB/20 s; GC pause 2.2–2.4 ms a frame overall | **we rebuild what Valve keeps** (storage, not scope) |
| model lighting: per drawn model, light cache budgeted per frame (`LightcacheGet`, `Lighting.Frame`) | **in view** | `Lighting.ModelLightingAt` per instance inside `Instances` | in view | lighting 1.0–1.8 | **same** in scope; per-call cost not read |
| flex / place / baked: done in `DrawModel` for drawn models | **in view** | `FlexFor` (`EntityModels.cs:763`), place, baked inside `Instances` | in view | place 1.1–2.3 (flex 0.8–1.7), baked 0.4–0.9 | **same** in scope; per-call cost not read |
| `CParticleMgr::UpdateAllEffects` (`particlemgr.cpp:1938`); `CNewParticleEffect::Update` on every effect (`:1844`); `ProcessPSystem` only on `BuildParticleSimList` survivors (not asleep, or moved); sleep via `m_flNextSleepTime` (`particles.h:1324`, written `particles_new.cpp:536`, read `particlemgr.cpp:1775`) | **list**, minus sleeping effects. **The sleep rule's other writers and `CParticleCollection::Simulate` are CLOSED** (`src/particles` absent) | `ParticleEffects` (`ParticleEffects.cs:29`) steps every running trail and burst; `ParticleEffect.Step` (`ParticleEffect.cs:369`) | every live effect, every frame | particles 3.6–8.4; **2.2 empty** | **we walk everything** — the "not drawn → sleep" half is Valve's, its exact rule is CLOSED |
| particle *creation*: the client fires queued events once a frame (`engine->FireEvents()`, `cdll_client_int.cpp:2215`); the receive path of a `TE_*`/`DispatchParticleEffect` into that queue is **not read** (engine-side, CLOSED here) | **queued, fired once** as far as the client half shows | `BurstEffects.StepOver` (`BurstEffects.cs:339`): every frame searches the timeline windows (`Explosions.Between`, tracers, blood, splashes, muzzle flashes, dispatches, heal beams, overheals, `:363-404`) and rebuilds `_burstsNow` | every frame, a window of the timeline | search 1.5 a frame | **we rebuild what Valve keeps** |
| `CViewRender::SetupVis` (`viewrender.cpp:1895`) / `BuildWorldRenderLists` (`:3498`, `g_WorldListCache` `:3513`) | **in view**, with a world-list cache | `WorldRenderer.SetCamera` (`WorldRenderer.cs:4388`) | in view | setcamera 1.0–4.5 | **not assessed**: what inside `SetCamera` costs 1–4.5 ms is not attributed; read before planning |

## The plan, ordered by milliseconds removed per unit of risk

Every item is "make X driven like Valve's Y". Each starts with the conformance test that pins Valve's behaviour,
then is measured with the `measure` skill (first person AND the empty view, two runs a side, golden shots at
ticks 30480 and 30700). Nothing here is proposed that Valve does not do.

### 1. Static props leave the per-moment entity walk, like `RENDER_FLAGS_STATIC_PROP` renderables

- **Change.** Static props are registered with the leaf system once per level (as `AddRenderable` at load) and
  never appended to `_drawn`; only the in-view leaf gather reaches them, and from there `Instances` poses them.
  Our own comment at `MomentScene.cs:339-341` already states the engine fact: `SimulateEntities` never sees a
  `CStaticProp`.
- **Conformance test first.** A synthetic map with N static props and zero entities, camera facing away:
  `SimulatePass`, `UpdateClientSideAnimations`, `PlaceFrame` and every visibility filter visit **0** props
  (a counter carried from each loop, B243); facing them, the leaf gather returns exactly the ones in view.
- **What it could break.** Anything that finds a static prop through `_drawn` or by entity index (the screen
  fade B432, distance fade B268, `StaticPropLighting`, decals hitting props, the pixel pick). Each needs a route
  from the leaf system instead.
- **Ambiguous, CLOSED.** What `staticpropmgr` does per frame for fade and lighting is not in the tree. Port only
  the client-side half that is readable (registration, never dirty); keep our fade as it is.
- **Measure, and this gates the rank.** First: how many of the 1,870 are `map.Assets.StaticModels`
  (`LevelSystems.cs:235`) — a probe over the level, not the desktop. cp_process_f12 has 1,353 static prop
  placements (finding 76), so the share is plausibly most of the 1,870, but that is interpolated, not measured.
  **The ceiling is smaller than the columns suggest**: a static prop leaves `SimulatePass` at its first
  `ResolvedFor(...).Skinned` test, and since fix 2 the collation reuses its handle by identity, so the saving is
  mostly the visibility filters, `PlaceFrame`, `UpdateClientSideAnimations` and those two residual visits. Time
  each pass's static-prop share with a carried counter before building; if it is under ~1 ms, item 4 goes first.
- **Done (2026-10-20, `feat/static-props-once`).** The count-first step was skipped: the owner's experiment (empty
  view, frame 8.7–9.0 → 5.8–6.8 ms with static props dropped entirely) had already settled the rank.
  `EntityModelSet.StaticProps` is packed by the first `Add` after it is set, filed with the leaf system by the first
  world pass after that, and filed again only for a new list, tree or level. Only the props a view collates run
  `SimulatePass`'s two halves (`SimulateEach`/`SettleEach`, same code as before), so a static prop in view is drawn
  exactly as it was. The moment's draw list no longer holds static props, so the visibility filters, `PlaceFrame` and
  `UpdateClientSideAnimations` never visit one. Conformance: `StaticPropGatherConformanceTests` (facing away, 0
  visits; facing two of three, those two; filed once over several frames; an entity as the control).
  - **Measured** (f12, tick 30000, Debug, three runs, one before, two after; the machine was shared with another
    agent's probe and the first seconds of each after run are contended, so the quiet windows are what is quoted).
    Empty view (`TF2VIEW_CAMERA="1613 -2821 154 89 0"`), every window not discarded: frame before 7.7–9.0 ms, after
    6.5, 7.2, 8.4, 8.4, 8.5, 9.7, 10.3 ms. Discarded: the after run's first eight windows (21–95 ms, `sound` up to 14.8
    and `camera` up to 29), which ran beside the other agent's probe. The phases match the owner's experiment:
    `simulate` 1.2–1.4 → 0.5–0.7, `drawlist` 0.4–0.6 → 0.1–0.2, `advance` 1.1–1.2 → 0.8–0.9, `camera` 4.7–5.3 →
    2.9–4.6. **The frame total does not cleanly reach his 5.8–6.8 ms**: it overlaps the bottom of that range in two
    windows, and contention is the likely reason, which was not shown. The `posed N of M selected` lines were cut from
    the captured output, so how the selected count changed is not reported. First person after: `simulate` 0.5–0.7
    (finding 82: 1.2–1.4), `drawlist` 0.4–0.7 (was 1.4–1.6), and a frame of 13–16 ms in its quiet windows. There is no
    first-person before-run, so that frame has no comparison.
  - **Output.** Golden shots before and after, same build settings: first person at tick 30000, and a free camera
    (`1613 -2821 154 5 90`) showing the map's trees. Both pairs are identical pixel for pixel outside the fps overlay
    (rows 2–11, x 697–824, the frame-time text).
  - **Engine read (CLOSED source, `engine.dll` `0x1800f1bd0`).** The static prop's per-draw function sets up lighting
    only: the ambient cube and the local lights, with no sequence, cycle or animation step. Ours still runs the simulate
    half for an in-view static prop, so a static prop with an autoplay or realtime sequence animates here and not in
    the engine. That was true before this change. It is not fixed here because the task required identical output.
    Filed as **B560**.
  - **What changed for an off-screen static prop.** Only a gathered static prop has an animating entity, so
    `TraceHitboxes` and `PosedHitboxes` (and the attachment queries) answer null for one no view reached this frame;
    before, every static prop was simulated every frame and answered. A bullet strike on an unviewed static prop falls
    to `BulletStrikes`' unposed rule. This is parity, not a loss: the engine traces a static prop through its collision
    model by index (`IStaticPropMgr::TraceRayAgainstStaticProp`, `IStaticPropMgr.h:47`) and decals one through
    `AddDecalToStaticProp` (`:64`), never through hitboxes, and its draw (`0x1800f1bd0`, above) builds no bones.
    Pinned by `TraceHitboxes_OnAStaticProp_AnswersOnlyWhenAViewReachedIt`.
  - **Review fixes (2026-10-20).** A skip frame (B504) now takes the static props out of the leaf tree and files them
    again on the next drawn frame, so its cull counts entities only. Before this fix, statics the frustum rejected
    pushed `CulledByVisibility` to 0. That is the same handle churn the old build had when a skip left them off the
    draw list. The tally is asked for the drawable static props, which is what the handles count. The level-change
    repack and a mid-level list swap are pinned by tests that go red when either `_staticsPacked = false` is deleted.
  - **Measured after rebasing onto `merge/batch-5`** (2026-10-20, the same empty view, the desktop free). Frame in the
    quiet windows: 6.0, 6.3, 6.6, 6.7, 7.3, 7.4, 8.3, 8.6, 8.7, 9.4 ms. Not quoted: three windows of 15.8–46 ms, one
    with `sound` 28.9, and the first window, which was loading. Phases: `simulate` 0.5–0.7, `drawlist` 0.2, `advance`
    0.7–0.9, `camera` 2.9–4.2. This run reaches the owner's 5.8–6.8 ms in five windows.

### 2. Particle creation becomes event-driven, like the client's `engine->FireEvents()`

- **Valve's side, as far as it is read.** The client fires its queued events once a frame
  (`cdll_client_int.cpp:2215`); how a temp entity or particle dispatch enters that queue is engine-side and
  CLOSED here. Read it (`engine.dll`, decompiler on D:) before porting, so the "once at arrival" rule is
  observed, not assumed.
- **Change.** Offer a burst once, at the tick its event is crossed, instead of re-searching the timeline windows
  every frame and rebuilding `_burstsNow` (`BurstEffects.cs:363-404`). A seek still replays from its checkpoint
  (D217).
- **Conformance test first.** A synthetic timeline with one explosion at tick T, stepped frame by frame at
  several frames per tick: the explosion is offered on exactly one frame, at the first frame whose tick ≥ T;
  and a counter shows zero searches on frames where no tick boundary was crossed.
- **What it could break.** The windows exist for effects whose start is before the current tick (a frame lands
  mid-window after a seek, `ExplosionWindowTicks`), and heal beams/overheals, which are states, not events
  (`docs/memory/a-loop-is-state-not-an-event.md`) — those stay per-frame here. How Valve drives the medigun
  beam per frame (a `ClientThink`, a particle property, or other) is **not read**: none of the report's §7
  `ClientThink` overrides is the medigun.
- **Measure.** search 1.5 ms a frame now; the event share of it versus the state share (beams) is not split.

### 3. The animation list, like `g_ClientSideAnimationList`

- **Change.** `UpdateClientSideAnimations` and the cycle-advance half of `SimulatePass` walk a list of props
  that joined it (`PostDataUpdate` with `m_bClientSideAnimation`, `c_baseanimating.cpp:4692`, left at `:4696`
  and `:2301`) instead of the whole draw list. Static props (item 1) and non-animating brushes never join.
- **Conformance test first.** A prop whose networked `m_bClientSideAnimation` is 0 is not visited; set to 1 it
  is visited from that update on; removed, it leaves. The loop reads no draw or PVS state (`:6376-6379` tests
  only the flag) — a hidden animating prop is still advanced.
- **What it could break.** Corpse sequence selection (B323) rides this pass; corpses must join the list at
  creation (`ForceClientSideAnimationOn`, `:6312`). The `with` copy per corpse is the allocation to remove with it.
- **Measure.** Not timed apart today; add the timer first (B243: carried from the loop).

### 4. Pose storage written in place, like `SetupBones`' stack arrays and cached bone array

- **Change.** `PoseOf`/`PoseBetween`/`StudioPoseBlend.Expand` write into scratch the pass owns and the entity's
  own bone array, as `SetupBones` builds into `pos[MAXSTUDIOBONES]`/`q[MAXSTUDIOBONES]` on the stack
  (`c_baseanimating.cpp:2965-2966`), instead of a new `StudioBonePose[]` — the shape of finding 82's fix 1.
- **Conformance test first.** Asked twice for the same prop, the second call allocates 0 bytes and reuses the
  buffer (the `IvpRagdollTests` pattern); a differential against the allocating version, bone by bone.
- **What it could break.** Any caller that keeps a returned pose past the next call (the corpse record's
  `Store` was the one in fix 1). Every holder must copy.
- **Measure.** 1,339 MB/20 s on the render thread; the GC pause (2.2–2.4 ms) is shared with the corpse worker,
  so the frame saving is not predictable from the bytes — measure the pause, not the allocation.

### 5. Visibility judged at the change, like `UpdateVisibility` at its event sites

- **Change.** The moment's visibility filters (`MomentScene.cs:485-541`) rerun only for props whose inputs
  changed (their pose object, the round state, the viewer's vision flags, the followed player), like
  `UpdateVisibility` called from `SetDormant`/`OnDataChanged`/`PostDataUpdate` (`c_baseentity.cpp:4021`, `:3319`, `:2649`).
- **Conformance test first.** One per filter: an unchanged prop with unchanged inputs is not re-judged
  (a counter); a change to each input re-judges exactly the props it governs.
- **What it could break.** Filters whose inputs are global per frame (first-person depends on the camera mode,
  vision on the viewer) — those re-judge all on change, which Valve also does through its own event sites. The
  risk is a missed input: each filter's input list must be read from its code, not assumed.
- **Measure.** drawlist 1.4–1.6 ms; how much is the filters versus `PlayerProps.Add` and weapon resolution is not
  split — split it first.

### Later, and why not first

- **6. Particle sleep, like `BuildParticleSimList`.** Effects not drawn and not moved stop simulating.
  2.2 ms in the empty view says this is large, but the sleep rule's writers and `CParticleCollection::Simulate`
  are CLOSED (`src/particles` absent): the one readable write is `particles_new.cpp:536`. Read the closed library
  (decompiler, D:) before porting, or the port is a guess. Another agent owns particles; hand this over.
- **7. The leaf system's visit, like `m_DirtyRenderables`.** Collate is 0.4 ms after fix 2; a dirty list removes
  the visit itself. Small, after items 1 and 5 shrink the set it walks.
- **8. `SetCamera` 1.0–4.5 ms.** Not attributed. Read and time its parts before comparing it with
  `SetupVis`/`BuildWorldRenderLists` and `g_WorldListCache`.
- **9. Think list for dynamic lights**, like `PerformThinkFunctions` over due entries. Not timed; time first.

## What is not claimed

- No item's saving is predicted as a number beyond its column's ceiling: finding 82 records one profile that
  promised more than a patch delivered ("tried and not kept"), and its lesson stands — rank by a direct timer.
- `SimulateEntities` walks **every entity** in Valve too. Item 1 removes the static props from our walk; it does
  not make the entity walk list-driven, because Valve's is not.

## The release candidate against the fixed instrument (2026-10-20)

Alternating before, after, before, after, per view; f12 (`demostf-cp_process_f12-2026-08-08-2207.dem`), tick 30000, Debug
viewer, `--measure 20 +fps_max 0`, one machine with nothing else running (the owner was asleep, no agent building or
measuring, CI remote). **Before is `1c190074`** (yesterday's main plus the `--measure` fix of finding 82, so both sides count
playback and not the opening seek); **after is `dbcbdfab`**, the beta.45 tag. The frame rate climbs inside each run as caches
warm, so the number quoted is the last one-second window of each run:

| view | before | after |
|---|---|---|
| first person | 64 and 70 fps (14.8 and 14.4 ms) | 88 and 80 fps (11.1 and 12.3 ms) |
| empty view (camera `1613 -2821 154 89 0`) | 107 and 115 fps | 140 and 156 fps |

About +25-30% in first person and +30-40% in the empty view. `advance` fell from 2.2 to 1.0-1.2 ms (first person) and
from 1.0-1.1 to 0.7 ms (empty view); `camera` fell by 1.5-2.5 ms in both. One "after" window reported a 237 ms frame
(a single stall) and is left out. **Against TF2** (400-600 fps in the owner's recollection, unmeasured; 893-1236 fps on
`cp_badlands`, finding 82): still far short. The leftovers are the `camera` phase, `setcamera` (1-4.5 ms, unattributed),
the particle step (about 2 ms even with nothing in view) and a collector pause of about 2.3 ms a frame.

## `setcamera` attributed, and the corpse record's allocation cut (2026-10-20)

Measured; one machine; Debug viewer; f12 tick 30000, `--measure 20 +fps_max 0`, last one-second window of each run. Six
runs allowed, so the order was A first person, A traced, A empty view, then B first person, B empty view, B traced —
**not alternated**. A is `d089b7ad` (the split below, no fix); B is `bc44895c`.

**`setcamera` is now split where it runs** (`Device3D.SetCamera` times each part and hands them over as
`IViewerDevice.SetCameraParts`; the rate line prints `camera setcamera <part>`, B243). The control: the parts sum to the
parent within rounding (0.57 = 0.15 + 0.05 + 0.04 + 0.31 + zeros).

| A, first person | total | batches | overlays | sky | water | detail | view, upload |
|---|---:|---:|---:|---:|---:|---:|---:|
| first window (34 fps) | 2.35 | 1.12 | — | — | 0 | 0.63 | 0 |
| last window (76 fps) | 0.57 | 0.15 | 0.05 | 0.04 | 0 | 0.31 | 0 |

**What this run shows of "1-4.5 ms":** in it `setcamera` falls from 2.35 to 0.57 ms as the run warms (the same work at 34
fps costs four times what it costs at 76). Whether the release-candidate figure came from early windows is not known: its
logs were not kept, so that figure stays unexplained. In the empty view, where the camera holds still, `setcamera` is
0.01-0.02 ms. The cull gate fires only on a move, and nothing is rebuilt without a change. There is also no GPU upload of
unchanged data to make conditional: `upload` reads 0.

**A divergence in the largest part, `detail` (0.3 ms), found and not fixed (no runs left to measure a fix).** Valve
rebuilds per view, as we do. But `CDetailObjectSystem::BuildDetailObjectRenderLists` (`detailobjectsystem.cpp:2789`,
read from published source) reaches props only through `EnumerateLeavesInSphere(CurrentViewOrigin(), cl_detaildist)`
(`:2839`). `EnumerateLeaf` (`:2742`) then takes only that leaf's props drawn in this world-list build
(`DrawDetailObjectsInLeaf(leaf, m_BuildWorldListNumber, …)`, `:2749`). `DetailModels.Build` and `DetailSprites.Build`
instead walk **every** detail prop on the map on every move, and test distance per prop. The fix is to drive both from
the leaves within `cl_detaildist` of the eye that this view's cull reached.

**Not checked, interpolated:** whether the engine skips re-marking the PVS while the view stays in one cluster. The
memory of `R_MarkLeaves` keeping `r_oldviewcluster` is the engine's and is not in the SDK, so it is unverified.
`WorldVisibility.Leaves` tests the PVS again on every move. That is at most `batches`' 0.15 ms.

**The D181 corpse record's allocation** (`dotnet-trace --profile gc-verbose`, `alloc-trace … all <window> any
CorpsePhysics.Advance`; the probe now takes that root filter). Control: the render thread's share is 4,160 MB/20 s in A
and 4,291 in B, the same window.

| MB per 20 s | A | B |
|---|---:|---:|
| the worker thread, all of it | 5,884 | 4,908 |
| under `CorpsePhysics.Advance` | 2,738 | 1,947 |

**The noise floor:** code this work did not touch also moved between the two traces (`IkFor` 1,578 → 1,477,
`UpdateClientSideAnimations` 650 → 617). So about 100-185 MB of the worker's −976 is window-to-window noise. The −791
under `CorpsePhysics.Advance` clears that floor.

What came out, each with a test in `IvpCorpseStepDifferentialTests`:

- **The closure in `IvpLedgeSide.FromLedge`** (`<>c__DisplayClass22_0`, 281 MB). The table factory captured `ledge`, so a
  closure was made on every call, cache hit or miss. Now the miss is in a method of its own.
- **A fresh `IvpLedgeSide` on every pair look** (486 MB). The engine builds the side on its stack. Here the collision
  object keeps the last side, keyed by the ledge's point list, the cache it stood on and that cache's new `Refreshes`
  count. The record is immutable, so a side handed back holds the same values.
- **Per-step working sets in `CorpsePhysics`** (`Retire`'s two collections, 54 MB; the births array, the seeded set and an
  interface enumerator) **and a new delegate per corpse per step** (`PoseIntoAccessor`, 52 MB). The collections are now
  owned and cleared. `IvpRagdoll.PoseHook` is one delegate.

**Bit-identity** is pinned by `Simulate_ACorpseDroppedOnAFloor_EveryPoseHashesAsBefore`. It runs a two-element corpse
kicked onto a slab for 200 ticks, hashes every pose bit, and was captured before any of these changes. Control: impacts
are above zero. Sabotage: gravity -800.0001 reddens it. Over ticks 20-199 that test's step allocates 973,928 bytes before
and 841,992 after, and the second figure is held as a ceiling. **That golden drives `IvpRagdollWorld` directly, so the
`CorpsePhysics` working-set reuse and `PoseHook` have no before/after differential of their own.** They are covered only
by the existing record-against-live tests, where both sides run the new code.

**The frame.** GC pause: 1.97 → 1.83 ms a frame in first person (9.63 → 9.20 MB a frame), 0.89 → 0.80 in the empty view.
Last window: 76 → 80 fps in first person and 146 → 154 fps in the empty view. With one run each and windows that move by
±10 fps, both the fps and the GC differences are within run-to-run spread.

**Left, by size, under `CorpsePhysics.Advance` in B:** `IvpMotionCache`'s `IvpMatrix?[21]` slots (337 MB; the engine
keeps them in the searcher's storage) together with the cache and `IvpSearchSide` objects (134 + 65 MB) made per search
in `IvpSimulation.Searchable`; `IvpContactRecord.Build` (162 MB); and the friction solve's `double[]` (135 MB). **Outside
it, in `EntityModels.cs`, which another job was splitting, so not touched:**

- `IkFor`'s closure (`DisplayClass283_0`, 554 MB) and `Predicate` (267 MB). The captured `shared` of
  `errors.RemoveAll(held => held.Item1.Chain == shared.Chain)` makes a closure on every rule, and a Predicate whenever
  the remove runs. A hand-written compaction loop makes neither.
- The caller's `List<(StudioIkRule, …)> asked = []` per prop per tick (the 500 MB `ValueTuple`4[]` growth). It is kept as
  `posed.IkErrors`, so the fix is a per-entity list owned by the posed entity, or copy-on-keep.
- `UpdateClientSideAnimations`' `SceneProp` copies (382 MB).
