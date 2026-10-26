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
| `camera pose` / drawlist | 0.4-0.5 | `UpdateVisibility` at `SetDormant` / `OnDataChanged` / `PostDataUpdate` | **on change** | five visibility filters rerun over the draw list every moment (`MomentScene`) | **divergence — B578** (83 item 5, not done) |
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
| 11 | B595 | hitches of 50-190 ms: first-use pose (viewmodel, baked) where the engine precaches; particles at start; render-thread wait; `between` | 46-133 (pose, 17 of 38 slow frames) | — | **Measured, not JIT**: 0 main-thread JIT in every slow frame after the first; `TieredCompilation=0` and ReadyToRun still stall (`measurements/2026-10-24-b595-frame-stalls-and-jit.md`). **Not first-use builds either**: 0 models packed in play, colour meshes ~20 ms over 60 s. The `viewmodel` stalls were the flushed `viewmodel skin` log line (90-118 ms), now written once per skin change; the `baked` attribution was warm-up before the window (`measurements/2026-10-24-b595-first-use-builds.md`). **6 of 7 remaining frames > 100 ms are the file log's flush** (80-126 ms, the writer's own timing; lock wait ~0); with the log at Warning, frames > 50 ms 17 → 6 over 5 runs, > 100 ms 7 → 0 (`measurements/2026-10-24-b595-log-writer-stalls.md`); fix options for the owner there. **The log's part is FIXED by D230** (a background writer thread): main-thread log flush 5.6-691.1 ms a run before, 0.0 after; frames > 100 ms 7 → 4, the 4 all in one run as `hud`/`pose` stalls with no log time beside a 10.5 s disk stall (`measurements/2026-10-24-d230-async-file-log.md`). **Cold-disk `hud` stalls (371, 233 ms) were 361/224 ms of main-thread archive reads of the kill-feed sheets at first draw**, which `CHud::Init`'s `SetupNewHudTexture` loads with the HUD (hud.cpp:457-474, :708-733); fixed by loading them in `HudTextures.SetUp`. VO and weapon-script reads are still in play, and the D230 run-4 stalls are unexplained (`measurements/2026-10-25-b595-sync-io-in-hud-and-pose.md`) |
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
