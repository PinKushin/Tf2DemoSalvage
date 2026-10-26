# 97. The per-frame pipeline against the engine's: where the engine does less work (2026-10-22)

**An audit, not a fix.** The viewer is 2.4-2.8x slower than TF2 on f12 (13.4 ms a frame against about 4.8 ms;
`docs/measurements/2026-10-22-tf2-vs-viewer-f12.md`), and findings 92-96 found the cost spread thin. This walks the
client frame stage by stage and asks one question of each: **does the engine CACHE, SKIP, AMORTISE or LIMIT this work,
and what is its per-frame trigger?** It extends finding 83 (the per-frame scope plan, 2026-10-19), which already
answered that for the entity loops; rows finding 83 settled are cited rather than re-read, and its status is updated.

**Nothing here was run.** Every millisecond is a `--measure` timer from the existing records, carried from the code
that used it (B243): `measurements/2026-10-22-f12-camera-breakdown.md` (R1/R2, f12 lcor, tick 30000, first person,
steady frame 11.9-12.0 ms) for `camera` and its parts, and the same records' raw `parts:` lines for `hud`. The `hud`
part means below are plain means of the 13 steady per-second lines (*arithmetic*, not frame-weighted; read from
`R1.txt`/`R2.txt`).

## (1) The engine's frame, in order (read from published source unless marked)

`CHLClient::HudUpdate` → `IGameSystem::UpdateAllSystems` (`cdll_client_int.cpp:1308`) → `FrameStageNotify`
(net update, `SimulateEntities`, `ProcessInterpolatedList`) → `CViewRender::Render` → `SetupVis` /
`BuildWorldRenderLists` → `CClientLeafSystem::BuildRenderablesList` → `DrawOpaqueRenderables` (each model's
`SetupBones` on demand, flex and lighting in `DrawModel`) → particles (`CParticleMgr::UpdateAllEffects`) → VGUI paint →
present. The engine-side halves (`CEngineVGui::Paint`'s root call, the event queue, the sound mixer, the material
system's draw submission) are **CLOSED** (`src/engine`, `src/materialsystem` hold no `.cpp`).

New citations this audit read itself:

- **VGUI layout runs only on invalidation.** `Panel::Think` calls `InternalPerformLayout()` only
  `if ( _flags.IsFlagSet( NEEDS_LAYOUT ) )` (`vgui2/vgui_controls/Panel.cpp:1105-1108`); the flag is set by
  `InvalidateLayout` (`:3908`) and cleared in `InternalPerformLayout` (`:3896`).
- **VGUI paint is per call, decided by the caller.** `PaintTraverse( bool repaint, bool allowForce )` paints when
  `repaint` is passed or the panel's own `NEEDS_REPAINT` is set (`:1151-1157`), and skips an invisible panel (`:1135`)
  or an empty clip rectangle (`:1174-1177`). Whether the engine's root call passes `repaint = true` every frame is
  CLOSED; that it does (an immediate-mode HUD) is **interpolated**.
- **A user message is dispatched once, on arrival.** `CUserMessages::DispatchUserMessage` calls each registered
  hook once with a copy of the message (`game/shared/usermessages.cpp:151-189`). Game events reach a HUD element through
  `FireGameEvent` the same way, per event; the queue that calls it is engine-side and CLOSED.
- **Detail props are reached only through the leaves near the eye that this view drew**:
  `BuildDetailObjectRenderLists` → `EnumerateLeavesInSphere(CurrentViewOrigin(), cl_detaildist)`
  (`detailobjectsystem.cpp:2839`), and `EnumerateLeaf` takes only props in leaves of this world-list build (`:2749`).
  Read by finding 83 (2026-10-20 section); not re-read here.

## (2) Stage by stage

ms are a frame, steady, R1 / R2 (*measured*) unless marked. "Same" means the engine does the same set of work on the
same trigger; it says nothing about per-call cost.

| our stage (timer) | ms | engine counterpart | engine trigger | ours | verdict |
|---|---:|---|---|---|---|
| `advance` (`Simulate`) | 1.10 / 1.07 | `ProcessInterpolatedList`, `SimulateEntities` | list; every entity | lerp list (B259 stage C) | **same** (finding 83) |
| `camera pose` / simulate | ~0.5 | `SimulateEntities` | every entity, not static props | entities, static props only when gathered (83 item 1, done) | **same** |
| `camera pose` / drawlist | 0.4-0.5 | `UpdateVisibility` at `SetDormant` / `OnDataChanged` / `PostDataUpdate` | **on change** | five visibility filters rerun over the draw list every moment (`MomentScene`) | **divergence — B578** (83 item 5, not done). **Narrowed (finding 104):** first-person and respawn-room are per frame in the engine too (`DrawModel`), so they are parity. Weapon, disguise and vision are cached; disguise and vision read inputs with no trigger. Waiting on the owner's choice |
| `camera pose` / collate | 0.2 | `CClientLeafSystem::PreRender` over `m_DirtyRenderables` | **changed only** | every entity visited, unchanged ones relinked by identity | divergence, small (83 item 7); not filed again |
| `camera pose` / anim | 0.1-0.2 | `UpdateClientSideAnimations` over `g_ClientSideAnimationList` | **list** | the whole draw list | divergence, small (83 item 3); not filed again |
| `camera pose` / setup, place, flex, baked, lighting | 0.3-0.4, 0.7-0.8, (0.34-0.45), 0.3-0.4, 0.2-0.5 | `SetupBones`, `DrawModel` flex and lighting | **in view**, on demand, bone cache per `g_iModelBoneCounter` | posed 26-29 of ~1,870, in view only; baked colour cached per entity (`_bakedByEntity`) | **same in scope**; per-call cost not compared. The light cache's per-frame build budget is not established on our side (open since finding 93) |
| `camera pose` / sample, viewmodel | 0.3, 0.3 | net update and viewmodel draw | per frame | per frame | **same** |
| `camera particles` / bursts (search and rebuild) | 0.55-0.63 of 0.91-0.99 | `engine->FireEvents()` once a frame; a temp entity or dispatch creates its effect **once, on arrival** (client half; receive path CLOSED) | **on arrival** | `BurstEffects.StepOver` re-searches every kind's timeline window and rebuilds `_burstsNow` every frame (`BurstEffects.cs:371-424`) | **divergence — B577** (83 item 2, not done). Of it, beams and overheals (0.21-0.23) are STATE and stay per frame |
| `camera particles` / bursts step | 0.35-0.36 | `CNewParticleEffect::Update` on every effect, `ProcessPSystem` only on awake ones | list minus sleeping | every live effect, sleep honoured (B565, finding 84) | **same** |
| `camera particles` / build | 0.65-0.66 | particle mesh build at draw | every drawn effect, every frame | every drawn effect | **same** |
| `camera particles` / decals | 0.52-0.53 | decal placed when its event arrives | **on arrival** | walked the whole match | **fixed** after this record (finding 93, `2026-10-22-model-decal-window.md`) |
| `camera setcamera` / detail | 0.26-0.28 | `BuildDetailObjectRenderLists`, per view | **per view, leaves within `cl_detaildist` drawn this build** | `DetailSprites.Build` / `DetailModels.Build` walked **every** detail prop on the map on every move | **fixed — B580** (2026-10-23): leaf runs within the fade maximum; 0.54-0.55 → 0.002 ms on gummo, cull hash identical (`measurements/2026-10-23-b580-detail-reach.md`) |
| `camera setcamera` / batches, overlays, sky | 0.17-0.19, 0.11-0.12, 0.13 | `SetupVis`, `BuildWorldRenderLists` with `g_WorldListCache` | per view; a PVS re-mark only on a new cluster is the engine's (`r_oldviewcluster`, CLOSED, **interpolated**) | rebuilt on a move; the PVS re-tested every move | not established; at most `batches` |
| `sound` | 0.20-0.21 | `engine->SetAudioState`, the mixer | per frame; mixer CLOSED | per frame | not compared (CLOSED; too small to rank) |
| `hud` / events | 0.27-0.28 | user messages and game events dispatched **once, on arrival** (`usermessages.cpp:151-189`) | **on arrival** | `HudEventFeed.Advance` walks **every** game event and **every** user message of the match, every frame, to keep those in `(last, tick]` (`HudEventFeed.cs`, both `foreach` loops) | **fixed**, B579: a window search (`LandedWindow`). Empty view 0.13-0.20 → 0-0.02 ms (`measurements/2026-10-23-hud-event-feed-window.md`) |
| `hud` / paint | 0.52-0.54 | `PaintTraverse` per visible panel | per frame (root call CLOSED, interpolated) | per frame, glyph cache shared (`VguiSurfaceHost`) | **same in scope** (interpolated) |
| `hud` / layout | 0.06 | `InternalPerformLayout` only on `NEEDS_LAYOUT` (`Panel.cpp:1105`) | **on invalidation** | not read whether ours lays out every frame | not established; at most 0.06 ms |
| `hud` / state, tools, think, precache | 0.05, 0.04, 0.03, 0.01 | — | — | `string.Join` of the panel models every frame (`FramePresenter.Hud.cs:248`) | too small to rank |
| `draw` | 2.11-2.25 | material system submission, queued to its own thread under `mat_queue_mode` | per frame | per frame, on the render thread | **not established**: our timer is not split, and the engine's threading of submission is CLOSED and not read here (**interpolated** from the cvar's existence only) |
| between frames | — | — | — | `TransportBar.ShowTick` repaints a WinForms control every frame | already **B572** |

## (3) The divergences, ranked by measured milliseconds on our side

Each figure is the timer of the WHOLE part the divergence lives in, so it is an **upper bound** on what the fix
removes; none is a sampled share.

| rank | B | divergence | measured part | what the fix would remove (ESTIMATE) | basis of the estimate |
|---|---|---|---:|---:|---|
| 1 | B577 | bursts re-searched and rebuilt from the timeline every frame | 0.55-0.63 (bursts minus step) | 0.2-0.35 | the event kinds only (explosions, tracers, blood, splashes, sentries, dispatches, muzzles: 0.33-0.40); beams and overheals stay per frame; an offered-once burst still costs its offer. **2026-10-23:** event share split as dispatches 0.17-0.23 and muzzles 0.18-0.27, the rest about 0.04 (measurements/2026-10-22-f12-steady-frame). The owned-dispatch walk is now a live list (`2ec8bd21`, offers identical) and **saved nothing** (measurements/2026-10-23-owned-dispatch-live-list.md). The cost is the per-live-offer work, not split yet (B577) |
| 2 | B578 | visibility filters rerun over the draw list every moment | 0.4-0.5 (drawlist) | 0.1-0.3 | the part also holds `PlayerProps.Add` and weapon resolution (finding 83: not split); a first-person camera change still re-judges all |
| 3 | B579 | the HUD's event feed walks the whole match's events and user messages every frame | 0.27-0.28 (hud events) | 0.2-0.25 | the walk is linear in the match and the crossed set is usually empty; `Resolve` of a crossed event stays. Finding 93's same-shape fix on model decals is the precedent, but its own saving was not measured as a frame difference |
| 4 | B580 | every detail prop on the map walked on every move | 0.26-0.28 (setcamera detail) | 0.1-0.2 | **FIXED; the estimate was low**: the same timer read 0.54-0.55 before the fix and 0.002 after, and the frame fell 0.8 ms (`measurements/2026-10-23-b580-detail-reach.md`) |
| 5 | (83 item 7) | leaf-system visit over every entity | 0.2 (collate) | under 0.1 | an identity relink is already cheap |
| 6 | (83 item 3) | client-side animation pass over the draw list | 0.1-0.2 (anim) | under 0.1 | — |
| 7 | B584 | `ForActivity` scanned every sequence per call; the engine hashes once (`studio.cpp:1684`) | 0.78-0.80 (pose setup) | 0.6-1.0 (code-read) | **FIXED; the estimate was 2-3x high**: alone, `setup` → 0.69-0.70, `models` 0.10 → 0, frame −0.2 to −0.35 (`measurements/2026-10-24-b583-b584-model-lookups.md`) |
| 8 | B583 | the draw loop probed the model maps by path six times a prop; the engine holds the pointer | — (no own timer) | 1.0-1.5 (code-read) | **PARTLY FIXED; null result**: no timed stage moved against the B584-only build (`measurements/2026-10-24-b583-b584-model-lookups.md`) |
| 9 | B586 | three reports keyed per prop before the Debug check | below 0.05 (reports, before; the column has one decimal) | 0.15-0.3 (code-read) | **FIXED; null result**: the reports run after the cull, for ~246 collated instances, not ~1,850 (`measurements/2026-10-24-b586-b585-report-guard-and-matrices.md`) |
| 11 | B595 | hitches of 50-190 ms: first-use pose (viewmodel, baked) where the engine precaches; particles at start; render-thread wait; `between` | 46-133 (pose, 17 of 38 slow frames) | — | **Measured, not JIT**: 0 main-thread JIT in every slow frame after the first; `TieredCompilation=0` and ReadyToRun still stall (`measurements/2026-10-24-b595-frame-stalls-and-jit.md`). **Not first-use builds either**: 0 models packed in play, colour meshes ~20 ms over 60 s. The `viewmodel` stalls were the flushed `viewmodel skin` log line (90-118 ms), now written once per skin change; the `baked` attribution was warm-up before the window (`measurements/2026-10-24-b595-first-use-builds.md`). **6 of 7 remaining frames > 100 ms are the file log's flush** (80-126 ms, the writer's own timing; lock wait ~0); with the log at Warning, frames > 50 ms 17 → 6 over 5 runs, > 100 ms 7 → 0 (`measurements/2026-10-24-b595-log-writer-stalls.md`); fix options for the owner there. **The log's part is FIXED by D230** (a background writer thread): main-thread log flush 5.6-691.1 ms a run before, 0.0 after; frames > 100 ms 7 → 4, the 4 all in one run as `hud`/`pose` stalls with no log time beside a 10.5 s disk stall (`measurements/2026-10-24-d230-async-file-log.md`). **Cold-disk `hud` stalls (371, 233 ms) were 361/224 ms of main-thread archive reads of the kill-feed sheets at first draw**, which `CHud::Init`'s `SetupNewHudTexture` loads with the HUD (hud.cpp:457-474, :708-733); fixed by loading them in `HudTextures.SetUp`. The D230 run-4 stalls are unexplained (`measurements/2026-10-25-b595-sync-io-in-hud-and-pose.md`). **The VO and weapon-script reads now happen before play, as the engine's do** (`S_PrecacheSound`; `CPrecacheRegister::Precache` over every `PRECACHE_WEAPON_REGISTER` class): main-thread reads in play 36 → 0 a minute (`measurements/2026-10-25-b595-part6-main-thread-reads-in-play.md`); held on f12 by `CorpusMainThreadReadsInPlayTests`. The weapon precache runs 10-15 s before the measure window; the after-runs' extra slow frames were machine load, which inflates both builds alike (8 interleaved runs, same file) |
| 12 | B596 | the comparison ran the Debug build; the engine's numbers are a retail build | — (whole frame) | — | **Not a cause; closed**: Debug 9.18 ms (8.58-9.81) vs Release 9.38 (8.62-9.79) first person, 3.54 vs 3.69 empty; same IL flags, runtime and JIT; the release zip is the same program (`measurements/2026-10-24-debug-vs-release-f12.md`) |
| 10 | B585 | a `float[16]` per instance; the engine reuses POD entries | 0.011 (place lookups matrix) | 0.2-0.4 (code-read) | **Redone the engine's way 2026-10-25 (`perf/b585-matrix-pool`): a per-list pool written in place, no compare.** Own timer 0.0136-0.0165 → 0.0089-0.0120 ms (~0.005 ms, 0.05% of a ~10 ms frame); frame, allocation, gc and gen0 inside the same-build spread. The first try (copy-on-change, 0.011 → 0.021) was reverted (`measurements/2026-10-25-b585-matrix-pool.md`) |

**Total: the divergences live in at most 1.8-2.1 ms of a 12 ms frame (*arithmetic*, sum of upper bounds); the fixes
are estimated at 0.6-1.2 ms together.** That is 5-10% of the frame. TF2's frame is about 4.8 ms; closing the 2.4-2.8x
gap needs about 7 ms.

## (4) What this says, and the recommendation

**The engine's pipeline does not do much less WORK than ours in the readable stages; it does the same work for less
per call.** Of the per-frame stages in the table, the ones where the engine caches, skips or waits for a change and we
do not add up to about two milliseconds of upper bound. The pose, particle build, HUD paint, advance and setcamera
batches are the same set on the same trigger. The rest of the gap is per-call cost (finding 96: arithmetic at most
1.3-2.3 ms; the rest spread) and the one stage nobody has split: **`draw`, 2.1-2.25 ms, the largest single phase after
`camera`.** That is the one row where the engine may differ structurally (submission on its own thread) and the one
row this audit cannot see into.

Fix order (each starts with the conformance test that pins the engine's trigger, then the measurement):

1. **Split the `draw` timer** (a carried timer per pass, as `setcamera` was split in finding 83). It is instrument
   work, not a fix, and it decides whether `draw` holds a divergence worth more than items 2-5 together. Verify by its
   parts summing to `draw` within 5%.
2. **B579, the HUD event feed**: a cursor that moves forward from the last tick, like the model-decal window. Smallest
   change, and the trigger is unambiguous (per message on arrival, read). Verify: `hud events` timer before and after,
   two runs a side; a counter of events visited a frame must read the crossed count, not the match's.
3. **B577, bursts offered once**: finding 83 item 2's plan stands; the receive path is CLOSED and should be read
   first. Verify: the per-kind `bursts/*` timers and an offer count of exactly one per event.
4. **B580, detail props through the leaves near the eye.** Verify: `setcamera detail`, and golden shots identical.
5. **B578, visibility at the change.** Verify: `drawlist`, split first. **Split, 2026-10-24 (measured):** the cost was
   not the five verdicts but a value-hashed `HashSet<SceneProp>` in the first-person block, 0.28-0.32 ms; by reference
   0.04 (`measurements/2026-10-24-drawlist-parts.md`). A wrong turn kept on purpose: the plan read the cost as
   re-judging per moment, and the empty view, which runs four of the five filters for 0.02 ms, is what refuted it.

**Expected saving, all four: 0.6-1.2 ms (ESTIMATE)**, conservative because finding 94's threading estimate was seven
times too high and finding 93's decal fix moved its own timer and not visibly the frame. Each verifies only on its own
timer and two runs a side; a frame-level difference under ~0.5 ms is inside run-to-run spread (R1/R2 differ 2.4% on
`camera`).

## (5) Not verified

- **`draw`'s contents** and whether TF2 submits on another thread (`mat_queue_mode`): not split here, not read
  (CLOSED). The largest open question in this audit.
- **The light cache's build budget** on our side against the engine's (two builds a frame, from finding 93's record;
  not re-read).
- **Whether our VGUI lays out every frame** (at most 0.06 ms) and whether the engine's root paint passes
  `repaint = true` each frame (CLOSED, interpolated as yes).
- **Particle and decal numbers predate finding 93's decal fix**; `decals` would read lower on today's main.
- The `hud` means are unweighted means of per-second lines (*arithmetic*); the camera parts are frame-weighted. They
  are not mixed in any sum above.
- The PVS re-mark on a cluster change (`r_oldviewcluster`) is the engine's from memory, not the SDK: interpolated.

## (6) A later read of the main thread, 2026-10-24 (read from code, not measured)

A read of `main` @ `01faad62` filed eight more per-frame costs as **B583-B590** in `docs/RISKS.md`. Their code-read
estimates sum to roughly **2.3-3.9 ms** of the ~13 ms frame. That sum is **unmeasured** (ESTIMATE): it must be measured,
with stage timers or the benchmark harness on branch `perf/where-main-thread-goes`, before any of the eight is called a cause.

## (7) The main thread sampled, 2026-10-24 (measured)

A `dotnet-trace` sample of the main thread on f12 with gummo in first person agrees with `--measure`'s phase timers
within ~2 points (measurements/2026-10-24-main-thread-cpu-profile.md). The empty-view control and the
`mat_queue_mode 0` control move it as expected.

- **The timers already account for the frame.** `camera` (pose, setcamera, particles) is ~80% of the work.
- **Mode 2 has no main-thread wait**, measured: 0.4% of samples.
- **The pose is all `EntityModelSet.Instances`.** Bones are ~12%; lighting is ~9%, with `LevelLighting.SunFrom` alone
  at 5.8% exclusive.
- **These are shares, not milliseconds.** The sampler roughly doubles the frame (fault 3 in that document).
  B583-B590's estimates still need stage timers.

## (8) Four profile leads timed, 2026-10-24 (measured)

Section (7), on branch `docs/main-thread-cpu-profile`, is the main-thread profile
(`measurements/2026-10-24-main-thread-cpu-profile.md`), which named four leads by share. Each was changed with an
equivalence test and timed in Release: interleaved, three runs a side, gummo first person and the empty view. Records:
`measurements/2026-10-24-muzzle-flash-log-line.md` and `measurements/2026-10-24-burst-walk-and-pose-copies.md`.

- **B591, the muzzle-flash log line, is the only frame-level gain, and a small one.** The frame went from 8.65 to
  8.56 ms in gummo and from 3.44 to 3.38 ms in the empty view, faster in all six pairs
  (measurements/2026-10-24-muzzle-flash-log-line.md).
  - The control was the log itself: 113 lines a run before, 0 after.
  - The empty view paid the cost too, because the line was written before the first-person check. So it was not the
    null control the record predicted.
- **B593, the pose copies in `ScenePropTrack.At`, saved 0.03 ms in `advance`** in every pair and both views
  (measurements/2026-10-24-burst-walk-and-pose-copies.md). That is real at the timer's resolution, and it does not show
  in the frame.
- **B592, the burst walk by value, is a null result.** The profile put 2.7-5.4% of samples under
  `List<ParticleBurst>.Enumerator`. None of it showed in `camera`, even in the empty view
  (measurements/2026-10-24-burst-walk-and-pose-copies.md).
  - **This is the lesson to keep:** an exclusive share on a write-barrier copy is mostly the sampler's GC-poll bias
    (fault 4), not cost.
  - The two copy leads were worth about 0 and 0.03 ms.
- **B594, `LevelLighting.SunFrom`, was not changed, because the engine's caching is already ported.** That is
  `LightCache<T>`, from `LightcacheGet` at `engine.dll` `0x1801b9cd0`: at most 2 builds a frame.
  - The one uncached caller is the first-person undrawn pass, which lights a model nothing draws.
  - Whether that pass carries the 5.8% needs a call counter first. It is OPEN.
  - **Counted, and it does not** (`docs/measurements/2026-10-24-b594-undrawn-pass-sun.md`): on f12 gummo first person
    the pass gets 6 props, builds 0 instances and makes 0 sun calls, in 0.025 ms. f12 has no monitor, so B511's gate
    returns before any lighting — the uncached caller is never called. The world pass makes 0.02-2.0 sun calls a frame.
    The wrong turn worth keeping: a code-read estimate of 0.2-0.4 ms, from reading the call and not its gate. The first
    counter run read all zeros, world included — the record was dropped by `MomentPresenter`'s `with` copy.

## (9) Where the garbage comes from, 2026-10-25 (measured)

The engine allocates nothing per frame in steady state; the viewer allocated ~1.04-1.2 MB a frame on f12 gummo first
person, and the .NET collector's pause for it was 0.20-0.27 ms a frame (measurements/2026-10-25-b585-matrix-pool.md).
A `GCAllocationTick` trace attributed it by
type, call site and thread (measurements/2026-10-25-allocation-attribution.md):

- **81% is the main thread and 18% the queued render thread.** `--measure`'s `render thread … bytes a frame` column was
  the main thread's own, mislabelled since the queue gave the viewer a real render thread (B600). A brief that read it
  as the render thread's went looking on the wrong thread.
- **No single big allocator; three kinds of small one in hot loops.** Closures and delegates were 17.5% of the bytes,
  compiler iterators 10.2%, boxed enumerators 10.0%: a lambda capturing a local per call (even on a cache hit), a
  `yield` per vertex, a `foreach` over an interface-typed `List`. The engine's C++ has none of these costs, so a direct
  port of a loop can add garbage the original never had.
- **The three largest sites went** (B599, measurements/2026-10-25-b599-three-allocation-sites.md): the decal
  projection's per-vertex iterator, `Device3D.Classify`'s capturing cache factory per instance on the render thread,
  and `DetailSprites.Build`'s per-frame lists. Allocation fell 0.30 MB a frame (−26%) in gummo, gen0 collections −28%,
  the gc pause ~0.045 ms a frame; the frame mean did not separate from the run spread (measurements/2026-10-25-b599-three-allocation-sites.md).
- **The model decals are projected again every frame, in the empty view too**, with a `Projected[]` per call on the
  large-object heap (93 MB per 200 s). Whether the engine keeps a decal's projected vertices or re-projects is not
  read yet; it decides whether the fix is a pool or a cache.
- **Two instrument facts learned on the way.** The queue verifier's `frames checked` counts every frame handed to the
  queue since the device was made, ~730,000-850,000 of them the loading overlay, so it says nothing about the measured
  frames. And `TF2VIEW_FRAME_HASH` with `TF2VIEW_FIXED_FRAME` is not deterministic run to run in its image, particle and
  HUD hashes (an A-against-A control differs on 99%, 7% and 0.6% of labels); only its pose and cull hashes agree, so
  it cannot by itself show a change bit-identical.

## (10) The four parity fixes and the engine's sequence pick, 2026-10-25

  - **The parity half was fixed 2026-10-25:** on a map with a monitor the pass now lights through the light cache with
    dynamic lights, as the engine's monitor draw does (B594 (b)). Read from code and the disassembly already cited;
    held by a synthetic test, not by a monitor-map shot.

**Four visible divergences closed on 2026-10-25, with the owner's approval (`fix/four-parity-divergences`)** — each read
from the SDK before the test, each red before the code:

- **B584's weight ≤ 0 was a refusal the engine does not make.** `iabs(actweight)` with 0 raised to 1
  (`studio.cpp:1822-1829`); the slow loop that does not raise 0 is compiled out (`studio.h:33` sets its switch to 0).
  The wrong turn worth keeping: the old comment called the refusal "Valve's rule"; it was the rule of a branch that is
  never built.
- **The heaviest pick itself was the divergence, and it was defended with the wrong function** (D233, the same day).
  The deterministic rule had been called engine-matching because it equals `SelectHeaviestSequence`
  (`animation.cpp:413-437`) — a function the player path never calls. The player path calls `SelectWeightedSequence`
  every frame (`multiplayer_animstate.cpp:1170`), which rolls `RandomInt` over the weights (`animation.cpp:308-335`), and
  keeps the roll only when the activity changed (`:1171-1172`): random once, then held — which is reproducible enough that
  the "a random pick flickers" objection was about a stateless port, not about the engine. Ported with the hold, the
  negative-weight keep (`:291-297`) and one addition, a seed from (entity, activity, tick begun) so a seek repeats itself.
  Measured (`measurements/2026-10-25-d233-activity-tuples-cost.md`, read with the probe): only 19 of 1,339 activities on
  the f12 model set have more than one candidate, so the change is visible on few animations.
- **"Tick begun" was first a lie on every seek** — the first cut keyed a seek on the landing tick, so the same moment
  could show two variants depending on how you reached it. The owner chose *"Recover real start (Recommended)"*. The fix
  that held was to stop remembering the start at all: it is RECOVERED, by one backward scan over whole ticks, on every
  activity change as well as on a seek, so play and seek are the same code rather than two codes kept in agreement. Two
  things the engine settled on the way (*read from published source*): a dead player is not animated and
  `ClearAnimationState` keeps the main sequence (`multiplayer_animstate.cpp:136-146`), so absence must be passed over,
  not end the stretch — he respawns into the variant he died in; and a gesture needs no scan, because `AddToGestureSlot`
  rolls once (`:616-688`) and each gesture carries its own start. The wrong turn worth keeping: scanning through the frame's
  own player sampler, which looks like the obvious reuse, would have reset the demo player's and prediction's per-tick
  caches on every seek — the scan got a side-effect-free one-player sampler instead. What the scan cannot do is in RISKS
  B584 (a 31 s cap; a stretch shorter than one frame gap, which play itself never sees).
- **B580's sphere is bare `cl_detaildist`.** Reading it found that production never passes the FOV factor at all, so the
  zoomed case it guards is not reachable yet, and that the engine's sphere admits whole leaves, which a run box under-reaches
  when zoomed (interpolated; filed as B580's residuals).
- **B598, the zoomed local player**, is hidden from every view, a monitor's included, because the zoom test returns
  before the base `ShouldDraw` that a monitor's force-draw reaches (`c_tf_player.cpp:8014-8020`).

**The batch-31 review (2026-10-26) corrected two of these and confirmed one claim** — read from code, each held by a
synthetic test red under sabotage:

- **Hidden is not gone, again.** The B598 fix took the zoomed sniper out of the undrawn list as well as the drawn one, so
  no pass held him and `SkinningOf` — `CModelRender::AddDecal`'s own bone setup — refused his blood. It is the same
  lesson as the first-person player's (B511): `ShouldDraw` decides drawing, not existence. He is now moved to the undrawn
  list, simulated but unposed, and handed to no monitor; the monitor test that had asserted "posed 0" had encoded the
  regression.
- **The scan's recorder was on his track, not his networked velocity.** The previous bullet's side-effect-free sampler
  was right about the caches and wrong about the motion: the frame moves a viewed recorder by `LocalPlayer` (prediction's
  velocity, else `m_vecVelocity`), the scan by differenced positions. The scan now asks the demo player, side-effect free,
  whether the frame has a view, and takes `LocalPlayer`'s speed from a cache-free prediction.
- **"Posing waits for the previous draw" (B585's pool) holds on the seek paths too**, which the review doubted: they call
  the moment presenter's pose directly, but both drain the render thread first (B581's `Lock`), and nothing between submits
  a draw. Only the decal replay's drain lacked a test; it has one.
- **The weapon-script precache moved to the install's open** (`LevelSystems.OpenGame`), from the first frame's step:
  `C_World::Precache`'s inputs are the install's, so the first map read is the point before play that has them.

**The batch-31 second review (2026-10-26)** — what it found, and one thing its fix broke on the way:

- **The recorder's motion in the scan has a price** (*measured*, RISKS B602): one movement prediction per probe is 14 ms of a
  seek on a POV match and 102 ms in one sample at the scan's cap, a lower bound (measurements/2026-10-26-pov-recorder-seek-scan.md). The obvious cache — predict once a packet and step forward — is not the
  same answer, because the engine's `_Update` restores each packet's own networked state; it was measured, not built.
- **The scan and the frame now find the current packet the same way**, through one index; on a demo whose packet ticks
  run backwards they had disagreed. **The wrong turn worth keeping:** the first index counted `dem_signon`s, which
  `ReadPacket` reads whenever reached. A real POV demo's signons carry the server's tick (146,124 against packets from 0 on
  etf2l-12025), so "the first packet past the tick" was the first command everywhere and the frame restarted on every read.
  The synthetic tests had only tick-0 signons and passed; the measurement probe's control — a fresh frame had a view where
  the scan said none — caught it before any figure was taken. The instrument's control found a bug in the subject.
- **The weighted pick reaches every tuple**: a saturated total had made two of three fuzzed tuples unreachable; the
  `INT_MIN` weight is interpolated (RISKS B601). And the profiler's abandoned frames, and the install's first open under
  two threads, were each fixed with a test.
- **One review claim the source does not support** (RISKS B598's re-read): during a monitor's draw an in-eye target IS drawn,
  because `InFirstPersonView` defers to the local player's force-draw flag (`c_baseplayer.cpp:1927-1930`, `:1980-1984`),
  which `ForceTempForceDraw` sets (`c_tf_player.cpp:9866-9868`); left for the lead to rule on.

**The owner's three rulings on it (2026-10-26)** — one restored the engine, one cut a cost, one was falsified:

- **The monitor ruling went to the engine** (*read from published source*, RISKS B598): a followed player stays in a zoomed
  recorder's monitor, and only the recorder is withheld. His props now have their own never-posed pass instead of taking
  the whole undrawn list out of the monitor.
- **A rewind no longer re-reads the demo** (*measured*, measurements/2026-10-26-pov-recorder-seek-scan.md): the start-point
  loop had stopped at the first command of any type past the target — the 146,124-ticked signon at index 0 again, the same
  writer quirk as the index above, in a second loop. Bounded by that index: 57,715 commands and 6.98 ms a rewind, mean, to
  40 and 0.016 ms (measurements/2026-10-26-pov-recorder-seek-scan.md). The equivalence test that proved it changes no answer could not see the 32 + 8 back-off at all until a
  single-player demo, whose interpolation target lags eight ticks, was added; sabotage on the other cases stayed green.
- **The hybrid seek recovery was measured and not shipped** (*measured*, RISKS B602, B604). The plan: answer the scan's
  recorder by his networked speed and predict only near `MOVING_MINIMUM_SPEED`, the one speed threshold the main activity
  has (`multiplayer_animstate.cpp:33`). The margin that gave zero mismatches was 1,022.6 u/s, which predicts on 99.9% of
  probes — the hybrid is exact at exact's cost. **What the disagreements turned out to be is the finding:** 1,339 of 1,392
  were prediction answering 0 u/s where the networked speed said ~215-240, the next packet sided with the networked speed in
  1,356 of them, and the frame draws prediction's 0. The reference the hybrid had to match is itself wrong there (B604).
  The track route, the brief's named cheap half, was worse on every count: a 13.7 u/s median error against 0, and
  80,003 u/s through teleports.
- **The still-standing recorder was the world trace, not the movement code** (*measured by manipulation; read from published
  source*, RISKS B604). The 2009 badlands demo, the only prediction test on real bytes, has median error 0. Read gap by gap
  against the next packet on etf2l-12025, the failures had one shape: on the ground, hull clear at the start, every bit of
  velocity removed in the first command. An instrument on `TryPlayerMove`'s zeroing sites put most of them at the stuck
  re-trace after a sweep that had reported clear. The sweep was wrong. `BspLeafTree.ClipToBrush` dropped an entry fraction
  that came out negative, which happens whenever the box starts nearer the face than `DIST_EPSILON`. Every movement trace
  stops exactly that far short, so a player resting against a wall or a slope passed into the brush and was stopped dead.
  Quake 2's `CM_ClipBoxToBrush` keeps any entry above −1 and clamps it to 0 (`qcommon/cmodel.c:1086-1089`), and our own
  box sweep already did. The first quote of that rule came through a web summarizer. It was right, but it was not read
  until the raw file was. Reading the raw file also turned up a second epsilon case, the early return at `:1052`. Taken
  as Quake 2 has it, it made the count against the server worse (by 7 gaps), so Source is not assumed to match its ancestor
  there, and it was reverted and filed. **Our code
  comment had quoted the rule as `enterfrac >= 0`.** That was a misremembered citation, written down with confidence.
  Clamping took the still-against-running gaps from 7,723 to 2,376. The remainder starts inside geometry that a mismatched
  map build would explain.
- **Then the ancestor turned out to be the wrong source, because the descendant was published** (*read from published
  source; interpolated to the brush clip*, RISKS B604). A review pointed at `public/collisionutils.cpp`:
  `IntersectRayWithBox` and `IntersectRayWithOBB` are Source's own clip for boxes and oriented boxes, called with
  `DIST_EPSILON`, and they disagree with Quake 2 in every epsilon branch. They clamp the entry's numerator *before*
  comparing it (`:1176-1189`), so at a corner with two faces inside the epsilon the first side wins. Ours kept the
  least-negative raw value, which is a different plane to slide along. They have no −1 bound (`:1201`), so a step shorter
  than the gap stops at 0. Under Quake 2's bound it passed into the brush, which is B604's own failure, reached by
  another road. Allsolid is "left at or before the start, or never" (`:1242`), not "never got out". The early return is
  `d2 > 0`. A start inside is fraction 0 with a conventional (1, 0, 0) normal. The lesson is about sources rather than
  arithmetic. "The SDK does not ship `cmodel.cpp`" was true, and it was read as "the SDK says nothing about the clip". It
  says a great deal, two files over. The probe is the control on whether that interpolation helps: see
  measurements/2026-10-26-pov-recorder-seek-scan.md, "Source's clip".
- **And the footstep probe, read on a real match, mostly starts solid** (*measured*, RISKS B605). It was assumed that the
  clip change would move step surfaces on every demo. Measured on a SourceTV match against the clip of before B604's clamp,
  about 8 samples of 3,993 changed: 4 went from no surface to one, and the surface named matched the floor underfoot in 8 more
  (631 → 639). That they are the clamp's and not this change's is *arithmetic* (for a 64-unit probe the two rules differ only
  at a corner tie), not measured against `30790a30`. 3,320 of the 3,993 start solid under every form of the clip, because a SourceTV
  origin sits exactly on the floor plane and `d1 = 0` is not outside. Predicting the effect from the arithmetic was right
  about direction and wrong about size.
