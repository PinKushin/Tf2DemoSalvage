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
| `BuildRenderablesList` / `CollateRenderablesInLeaf` (report §2, scope note at `:1918`) | **in view** — the view's leaf list | the same leaf gather (B262) inside `ClientLeafSystem` | in view | inside collate | **same** |
| static props: `RENDER_FLAGS_STATIC_PROP` renderables (`clientleafsystem.cpp:660-666`), drawn by `staticpropmgr->DrawStaticProps` (`viewrender.cpp:4041/4047`) | added once at level load; never dirty; never on the entity list. **Body CLOSED** (`staticpropmgr.cpp` not in the tree) | `MomentScene.Build` appends `StaticProps` to `_drawn` every moment (`MomentScene.cs:344`) | **every static prop, every frame**, through every pass below | part of every column below; the static-prop count of the 1,870 is not measured | **we rebuild what Valve keeps** |
| `SimulateEntities` → `C_BaseEntity::Simulate` loop (`cdll_client_int.cpp:2010-2015`); `C_BaseAnimating::Simulate` (`c_baseanimating.cpp:5155`) does `DoAnimationEvents` and little else | **every entity** (`C_BaseEntityIterator`) — but not static props, and cheap per entity | `EntityModelSet.Simulate`/`SimulatePass` (`EntityModels.cs:1305`, `:1322`): per prop a model resolution, `EntityFor`, cycle advance, events, placement | every prop of the pass, **static props included** | simulate 1.2–1.4; empty 1.2–1.3 | **we walk everything** — Valve's set is entities only, and its per-entity work is the events |
| `C_BaseAnimating::UpdateClientSideAnimations` (`c_baseanimating.cpp:6368`); membership set `:4692`, `:6312`, cleared `:4696`, `:2301` | **list** — `g_ClientSideAnimationList`, entries with `FCLIENTANIM_SEQUENCE_CYCLE` | `EntityModelSet.UpdateClientSideAnimations` (`EntityModels.cs:5178`), called on `_drawn` and `_undrawn` (`MomentScene.cs:843`, `:847`) | the whole draw list, static props included; a class-name compare and a `with` copy per corpse | not timed apart (inside the camera phase); 661 MB/20 s of `SceneProp` copies on the corpse worker (finding 82) | **we walk everything** |
| `ProcessInterpolatedList` (`c_baseentity.cpp:3123`), list joined `:2834`, left `:2928` | **list** — `g_InterpolationList` | B259 stage C's lerp list and wake queue | list | `advance` 2.6 (not split) | **same** (B259 closed this one) |
| `ProcessTeleportList` (`c_baseentity.cpp:3079`), set `:2643` | **list** | not located on this branch | — | — | not assessed |
| `UpdatePVSNotifiers` (report §1, scope note `:369`) | **list** — PVS notifiers | `CulledByVisibility` judged per prop inside `Instances` | every prop | inside drawlist/collate | **we walk everything** (small) |
| `UpdateVisibility` (report §4, `:2618`) | **changed** — event sites only (`SetDormant`, `OnDataChanged`, `PostDataUpdate`) | the visibility filters, rerun each moment over `_drawn`: first-person, weapon, disguise, respawn-room, vision (`MomentScene.cs:485-541`), plus `PlaceFrame` (`EntityModels.cs:4403`) indexing every prop | every prop, static props included | drawlist 1.4–1.6 | **we rebuild what Valve keeps** |
| `ProcessOnDataChangedEvents` (report §1, `:425`) | **changed** — `g_DataChangedEvents` | `SceneProp`/`ScenePose` init-only records, re-handed each tick; B259 stage C hands unchanged tracks back as the same object | changed, mostly (fix 2's counter: 1,577–1,580 of 1,605–1,618 unparented props are the same object) | — | **same in shape**; the consumers above do not yet use it |
| `CClientThinkList::PerformThinkFunctions` (report §7, `:3939`) | **list** — due entries of `m_ThinkEntries` | `MomentScene.ThinkDynamicLights(props, …)` (`MomentScene.cs:331`) and per-system steps | every prop for the lights | not timed | **we walk everything** (unmeasured) |
| `C_BaseAnimating::SetupBones` (`c_baseanimating.cpp:2796`), last frame's mask (`:2920`), `ThreadedBoneSetup` over `g_PreviousBoneSetups` (`:2782`) | **per caller** — draw and attachment queries | `Instances`' setup on posed props; attachment queries (fix 3) | per caller; no second build per frame (finding 82: 0 rebuilt) | setup 0.6–1.2; attachments ~0.95 | **same** |
| bone pose storage: `RagdollGetBoneMatrix` writes into the caller's accessor (`ragdoll_shared.cpp:573`) | no allocation | `SkinnedModel.PoseOf`/`PoseBetween`, `StudioPoseBlend.Expand` allocate `StudioBonePose[]` | per posed prop | 1,339 MB/20 s; GC pause 2.2–2.4 ms a frame overall | **we rebuild what Valve keeps** (storage, not scope) |
| model lighting: per drawn model, light cache budgeted per frame (`LightcacheGet`, `Lighting.Frame`) | **in view** | `Lighting.ModelLightingAt` per instance inside `Instances` | in view | lighting 1.0–1.8 | **same** in scope; per-call cost not read |
| flex / place / baked: done in `DrawModel` for drawn models | **in view** | `FlexFor` (`EntityModels.cs:763`), place, baked inside `Instances` | in view | place 1.1–2.3 (flex 0.8–1.7), baked 0.4–0.9 | **same** in scope; per-call cost not read |
| `CParticleMgr::UpdateAllEffects` (`particlemgr.cpp:1938`); `CNewParticleEffect::Update` on every effect (`:1844`); `ProcessPSystem` only on `BuildParticleSimList` survivors (not asleep, or moved); sleep via `m_flNextSleepTime` (`particles.h:1324`, written `particles_new.cpp:536`, read `particlemgr.cpp:1775`) | **list**, minus sleeping effects. **The sleep rule's other writers and `CParticleCollection::Simulate` are CLOSED** (`src/particles` absent) | `ParticleEffects` (`ParticleEffects.cs:29`) steps every running trail and burst; `ParticleEffect.Step` (`ParticleEffect.cs:369`) | every live effect, every frame | particles 3.6–8.4; **2.2 empty** | **we walk everything** — the "not drawn → sleep" half is Valve's, its exact rule is CLOSED |
| particle *creation*: effects arrive as `TE_*`/`DispatchParticleEffect` messages when their packet is processed (report §1, data-change events) | **changed** — at arrival | `BurstEffects.StepOver` (`BurstEffects.cs:339`): every frame searches the timeline windows (`Explosions.Between`, tracers, blood, splashes, muzzle flashes, dispatches, heal beams, overheals, `:363-404`) and rebuilds `_burstsNow` | every frame, a window of the timeline | search 1.5 a frame | **we rebuild what Valve keeps** |
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
- **Measure.** First: how many of the 1,870 are static props (a probe over the level, not the desktop). The
  saving is bounded by that share of drawlist 1.4 + simulate 1.3 + collate 0.4.

### 2. Particle creation becomes event-driven, like `TE_*` messages processed at their packet

- **Change.** Offer a burst once, at the tick its event is crossed, instead of re-searching the timeline windows
  every frame and rebuilding `_burstsNow` (`BurstEffects.cs:363-404`). A seek still replays from its checkpoint
  (D217).
- **Conformance test first.** A synthetic timeline with one explosion at tick T, stepped frame by frame at
  several frames per tick: the explosion is offered on exactly one frame, at the first frame whose tick ≥ T;
  and a counter shows zero searches on frames where no tick boundary was crossed.
- **What it could break.** The windows exist for effects whose start is before the current tick (a frame lands
  mid-window after a seek, `ExplosionWindowTicks`), and heal beams/overheals, which are states, not events
  (`docs/memory/a-loop-is-state-not-an-event.md`) — those stay per-frame, as Valve's beam is an entity effect.
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

### 4. Pose storage written in place, like `RagdollGetBoneMatrix` into the caller's accessor

- **Change.** `PoseOf`/`PoseBetween`/`StudioPoseBlend.Expand` write into a buffer the entity owns instead of a
  new `StudioBonePose[]`, the same shape as finding 82's fix 1.
- **Conformance test first.** Asked twice for the same prop, the second call allocates 0 bytes and reuses the
  buffer (the `IvpRagdollTests` pattern); a differential against the allocating version, bone by bone.
- **What it could break.** Any caller that keeps a returned pose past the next call (the corpse record's
  `Store` was the one in fix 1). Every holder must copy.
- **Measure.** 1,339 MB/20 s on the render thread; the GC pause (2.2–2.4 ms) is shared with the corpse worker,
  so the frame saving is not predictable from the bytes — measure the pause, not the allocation.

### 5. Visibility judged at the change, like `UpdateVisibility` at its event sites

- **Change.** The moment's visibility filters (`MomentScene.cs:485-541`) rerun only for props whose inputs
  changed (their pose object, the round state, the viewer's vision flags, the followed player), like
  `UpdateVisibility` called from `SetDormant`/`OnDataChanged`/`PostDataUpdate` (report §4).
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
