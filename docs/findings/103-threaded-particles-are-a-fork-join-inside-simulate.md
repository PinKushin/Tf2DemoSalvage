# 103. The engine's threaded particles are a fork-join inside `Simulate`, over the step alone (2026-10-24)

**Status: step 3 of §6 implemented and measured, 2026-10-26** (branch `perf/threaded-particles-sunfrom`, §8). Steps 1, 2 and
the trails' fork are not.

**The question.** The main-thread CPU profile (`git show e330651b:docs/measurements/2026-10-24-main-thread-cpu-profile.md`)
puts particles at about 24% of our main thread on f12 gummo first person (`FramePresenter.DrawOver` 23.7% inclusive,
`BurstEffects.Step/StepOver` 11.6%, `FrameParticles.BuildOver` 6.1%, `ParticleEffects.Gather` 1.9%), roughly 2 ms of
~8.6 ms (an *estimate*: sampled shares times the frame, measurements/2026-10-24-main-thread-cpu-profile.md at `e330651b`). The engine runs particle simulation on worker threads (`r_threaded_particles`). The assumption handed to this
audit: that simulation is a separable job we can move off the main thread for about that 2 ms. Is it?

**The answer, plainly: no.** The engine threads one part of the particle frame, `ProcessPSystem` (each awake root
effect's `Simulate`), as a fork-join that the game thread takes part in and joins before `Simulate` returns. Creation,
control points, sleep bookkeeping, the leaf system and the mesh build all stay on the game thread. On our side the
threaded part maps to `bursts/step`, 0.28-0.36 ms, and finding 94 already built exactly that, byte-identical, and
measured a 0.08 ms saving (finding 94, measurements/2026-10-22-threaded-particles.md). The rest of the 2 ms is work the engine also does serially, or a divergence (B577) that is
fixed by doing less, not by threading.

Evidence classes: *read from source* (SDK 2013, TF2's own client branch), *read from our code*, *measured* (only where
a measurement file is cited), *estimate* (labelled every time), *interpolated* (flagged).

## (1) The engine's structure (*read from source*)

**Where it runs.** `OnRenderStart` (`cdll_client_int.cpp`), on the game thread, in this order:
`view->OnRenderStart` (:2184) → `UpdateClientSideAnimations` (:2189) → `ProcessOnDataChangedEvents` (:2194) →
`SimulateEntities`, `PhysicsSimulate` (:2207-2208) → `ThreadedBoneSetup` (:2210) → **`engine->FireEvents()`** and
`tempents->Update()` (:2215-2218; this is where temp entities and dispatches CREATE effects) →
**`ParticleMgr()->Simulate( gpGlobals->frametime )`** (:2234) → `CalcAimEntPositions` (:2239) →
`AddVisibleEntities` (:2254). Rendering comes after `OnRenderStart` returns.

**`CParticleMgr::Simulate`** (`particlemgr.cpp:1452-1464`) calls `UpdateAllEffects` (:1938-1999):
- old-style `m_Effects` (`CParticleEffectBinding`): all serial — bbox, `SetDrawn(false)`, `m_pSim->Update`,
  `SimulateParticles`, `DetectChanges` (:1950-1984). The frame delta is clamped to 0.1 s (:1947).
- then `UpdateNewEffects( flTimeDelta )` (:1824-1936), the `.pcf` effects, in five parts:

| # | part | thread | cite |
|---|---|---|---|
| a | `pNewEffect->Update( dt )` on **every** effect: `m_hOwner->ParticleProp()->OnParticleSystemUpdated` (control points from entities, attachments) — *"this one can call into random entity code which may not be thread-safe"* | game, serial | :1844-1853; `particles_new.cpp:301-307` |
| b | `BuildParticleSimList`: skip if `flNow >= m_flNextSleepTime && m_nActiveParticles > 0` (asleep) or remove flag; a sleeping effect that `HasMoved()` still gets a bbox-only entry | game, serial | :1766-1799 |
| c | `EarlyRetireParticleSystems` (screen-area retirement); **off by default**, `cl_particle_retire_cost "0"` returns false at :1702 | game, serial | :1692-1762, :1862-1867 |
| d | `UpdateDirtySpatialPartitionEntities()`, then **`ParallelProcess( …, ProcessPSystem )`** over the list when `r_threaded_particles` (default **`"1"`**, :1543); serial loop otherwise | **workers + game** | :1869-1897 |
| e | `DetectChanges()` on every simulated effect (leaf-system reinsertion) — *"non-reentrant part"* | game, serial | :1899-1904 |

**What one job does** — `ProcessPSystem` (:1547-1586), one root effect: bbox recompute if first update, `SetDrawn(false)`,
`Simulate( 0 )` on the first frame or `Simulate( dt, bboxOnly )` if `ShouldSimulate()`, and `SetRemoveFlag()` if
finished. It touches its own effect (and, through `Simulate`, its children: the unit is the root). The step `dt` is a
file static, `s_flThreadedPSystemTimeStep` (:1545, :1839), written before the fork.

**The join.** `ParallelProcess` (`jobthread.h:952-959`) → `CParallelProcessor::Run` (:859-916): it queues
`min(nItems-1, NumThreads())` jobs, **the calling thread runs `DoExecute()` itself**, pulling items from a shared
interlocked pointer (:921-946), then `Abort()`s and `Release()`s each job. Run returns only when the list is drained, so
**the join is the end of part (d), inside `UpdateNewEffects`**, before part (e), before `CalcAimEntPositions`, and long
before the view renders. That the `Abort()` of a job already executing waits for it is *interpolated*: `CJob::Abort` is
in the closed `vstdlib`; the processor lives on Run's stack (:955), so returning with a worker still in it would be a
use-after-free, which is why the reading is the likely one. Not read.

**There is no overlap with rendering, and none across frames.** The simulation is a burst of parallelism in the middle
of the game thread's frame, not a background job.

**At draw time** (game thread, in the view's renderable lists): `CNewParticleEffect::DrawModel`
(`particles_new.cpp:509-601`) culls by max draw distance and, when culled, **writes `m_flNextSleepTime`** (:536) — so
the draw feeds the next frame's part (b); translucent effects call `Render( pRenderContext, … )` (:577), which builds
the mesh through the render context. Under `mat_queue_mode 2` those calls are recorded on the game thread and played
on `MatQueue` (findings 98, 99). So **the engine builds particle meshes on the game thread too**.

**What the engine does NOT thread:** creation (FireEvents/dispatch), control points (a), sleep selection (b), retirement
(c), leaf system (e), the old-style effects, the mesh build at draw, and the draw-time sleep push.

**TF2's closed side.** `particlemgr.cpp` and `particles_new.cpp` are TF2's own client source in this SDK drop; the
`CParticleCollection` simulate and renderers are in the closed `particles.lib`, linked into `client.dll`, and
`g_pThreadPool`'s worker count is in the closed `vstdlib` (finding 99 §6, not read). Nothing in TF2's shipped cfgs found
here changes `r_threaded_particles`; that was not exhaustively searched. **Not decompiled**: the conclusion above does
not depend on the closed parts, since the threading boundary is drawn in open source. `particle_sim_alt_cores` is
X360-only (:1881).

## (2) Our stages against the engine's

Costs are f12, first person, instrumented (`--measure` parts, finding 97 and 101's tables; `measurements/2026-10-22-f12-camera-breakdown.md`,
`2026-10-22-threaded-particles.md`). Shares in the CPU profile are sampled and are NOT used as costs here.

| ours | cost (ms) | engine stage | thread there |
|---|---|---|---|
| `BurstEffects.StepOver` search and rebuild of `_burstsNow` (`BurstEffects.cs:371-424`): dispatches 0.17-0.23, muzzles 0.18-0.27, beams/overheals 0.21-0.23 | 0.55-0.63 | creation in `FireEvents` once, on arrival; `Update` (a) for followed points | game, serial — and the search itself is **B577, a divergence** |
| `ParticleEffects.Bursts` serial part: offers, `_spent`, `LifeOf`, rebuild/replay, `new ParticleEffect`, caller callbacks `Model` (poses a model), `DormantAt`, `History` (`ParticleEffects.cs:408-779`) | inside the above | (a) + (b) + creation | game, serial |
| `ParticleEffects.Bursts` step of each played effect (sleep honoured, B565) | **0.28-0.36** | **(d) `ProcessPSystem`** | **workers** |
| `ParticleEffects.Update` trails (`ParticleEffects.cs:177-400`) | 0.08 | (a)+(b)+(d) for the rocket trails | split as above |
| `FadeUnoffered`, `Retire`, `IsGone` (`:779`, `:910`, `:948`) | small | `StopEmission`/remove flag, `ProcessPSystem`'s `SetRemoveFlag` | game (remove) |
| `ParticleEffects.Build`/`CollateEffects`/`Gather` (`:1021-1385`), sleep push | part of 0.65 | `DrawModel` → `Render`, `m_flNextSleepTime` push (:536) | game |
| `FrameParticles.BuildOver` rest: sprites, pixel visibility, beams, sun (`FrameParticles.cs:249-386`) | part of 0.65 | `DrawModel` of sprites/beams, `CGlowOverlay::DrawOverlays` | game |
| `Device3D.SetParticles` (`Device3D.cs:2634-2669`) | in `hand` | the queued mesh hand-off | game records, `MatQueue` plays |

So of ~2 ms, the part the engine puts on workers is the 0.28-0.36 ms step (*arithmetic* from the rows above). The search
(0.55-0.63) is a scheduling-of-WHEN divergence the engine avoids by creating once, and the build (0.65) is serial
game-thread work in the engine too.

## (3) Thread-safety hazards in our code (*read from our code*)

For any particle job, and for the rung-2 render thread (`ViewHalves`, `DrawFeeds`) that may overlap it:

1. **The corner lists are reused and referenced, not copied.** `ParticleEffects._byMaterial` keeps one
   `List<DetailSpriteVertex>` per material across frames (`ParticleEffects.cs:1407`); `Batches` wraps those same lists
   (`:1286-1299`); `Device3D.Drawable` stores `batch.Corners` by reference into `_particleBatches` (`Device3D.cs:2667`)
   and the draw reads them later. Today `SetParticles` begins with `Lock()` (`:2637`), a full drain, so the previous
   draw is finished before the next frame's corners are handed. **If the rung-2 overlap ever removes that drain for
   particles, the next frame's `Gather` would clear lists the render thread is reading.** This is the one hazard that
   exists without any new particle job. The same holds for `_batches` (`:1411`).
2. **The serial part calls back into the caller.** `ParticleBurst.Model` poses a model; `DormantAt`, `History` read the
   timeline. These are the engine's part (a), and stay on the game thread: a pose on a worker would race the pose that
   `PlaceCamera` and the draw read.
3. **Dictionary order is draw order.** `_bursts` enumerates in insert/remove order and a remove frees a slot an insert
   reuses; finding 94 §2 had to queue removes with the writes, in offer order.
4. **Plain counters.** `Steps`, `Slept`, `Gathered`, `ReplayTicks`, `ReplaySteps`, `BurstBuilds` and the `Parts`
   stopwatch are plain fields; `ParticleEffect.BirthsBySystem` is a `ConcurrentDictionary` (finding 94 §2). Plain `++`
   from workers would make every reported count meaningless (B243).
5. **Sound is emitted out of the step** (`FramePresenter.cs:392-411`: whizzes). Order of emission is output; it must
   stay in the serial part.
6. **Per-effect state is the safe unit.** Definitions are immutable records, operators are per effect,
   `ParticleRandom`'s table is static readonly (finding 94 §2). This is what made the step parallelisable.

## (4) The join point, ours

The engine's, mapped: after the serial offer pass of `ParticleEffects.Bursts` (and of `Update` for trails), fork the
played steps, join before the write-back in offer order and before `FadeUnoffered`/`Retire`. Everything after it in
`DrawOver` — `Build`, sprites, `SetParticles` — runs after the join, as `DetectChanges` and `DrawModel` do. This is
exactly finding 94's shape (`eb5f63ea` on `worktree-agent-a15ceb35cdf667d9a`). **Nothing crosses a frame**, so the
render thread never sees particle state mid-step.

## (5) Determinism — what proves it

Required: the threaded path's output is identical, frame by frame, to the serial path. Two instruments, both
synthetic (D38):

- **Effect level** — finding 94's `ParticleEffectsWorkersTests` (on its branch): 46 frames covering three materials, a
  child, a duplicate offer, a dormant gap, a stop, a material first seen mid-run and a seek; every frame's batch bytes,
  census and counters equal across serial, 2, 8 workers, a backwards schedule and an interleaved one; plus a separate
  assertion of the seek's draw order, since every run shares the commit code.
- **Presenter level, the B577 pattern** (`BurstEffectsStepTests.StepOver_LiveOwnedDispatchesOverAPlayback_OfferWhatTheWalkFromTheFirstOffered`,
  `BurstEffectsStepTests.cs:787`): one fixed LCG playback (several frames a tick, same-tick dispatches, RESET stops,
  posed and unposed owners, seeks both ways, a clock gone back), run through two presenters each with its own
  `ParticleEffects`, one serial and one threaded with a forced adversarial schedule; compare per frame the ordered
  offers, census, counters, **the batch corner bytes in order**, and the emitted sounds in order.
- **Sabotages that must redden it:** merge in completion order; commit inside the worker; remove in the serial pass;
  a worker `++` on `Steps`. Restore each by its inverse edit.

An output-level check on f12 remains open: finding 94 §3 showed `--shot` is not deterministic even serial against
serial, so a per-frame hash of particle batches (the rung-2 frame-hash instrument, `6e3c7124`, is the candidate) is
the instrument, not pixels.

## (6) Plan, in small steps, each ending at a green commit

Ranked by expected gain. **All ms are estimates** unless a measurement is named.

1. **B577's remaining split, serial** (engine parity: create on arrival, do not re-search). Split `dispatches` into
   attachment queries, dormancy closures and the 660-tick entity-less window; then muzzles skip finished flashes
   (finding 86) with a state-equivalence test. *Estimate:* up to 0.33-0.40 ms, B577's upper bound; the first live-list
   step moved nothing (`measurements/2026-10-23-owned-dispatch-live-list.md`), so expect less. Gate phase 1, commit.
2. **Hand the device a copy, or double-buffer the corner lists per view half** (hazard 1). Test: a synthetic frame N
   handed, frame N+1 built, frame N's handed corners unchanged. *Estimate:* 0 ms by itself, a small copy cost
   (~vertices × 32 B; *estimate*); it is the prerequisite for removing `SetParticles`' drain under rung 2. Commit.
3. **Port finding 94's step split only** (not the threaded build, which measured noise and is our addition), behind
   `r_threaded_particles` default 1 as the engine has it, on the persistent worker pool once D228's pool exists
   (memory: do not build a pool alone). Tests: §5. *Measured precedent:* 0.36 → 0.28 ms `bursts/step`, camera unchanged
   (`measurements/2026-10-22-threaded-particles.md`). *Estimate:* ≤0.1 ms. Commit, measure S/T/S/T.
4. **Do not thread the build.** The engine does not, and finding 94 measured no gain.

**Net estimate for particle threading: under 0.1 ms of the ~2 ms.** The particle share is reduced by step 1 (doing what
the engine does: less), not by threads; the threading lead with size remains the render-thread overlap (B581).

## (7) Risks and what is NOT established

- `CJob::Abort` waiting on a running job: *interpolated* (closed `vstdlib`).
- `g_pThreadPool`'s size in TF2: not read (finding 99 §6).
- Whether TF2's shipped cfgs set `r_threaded_particles`: not exhaustively searched.
- The step's 0.28-0.36 ms is f12-specific; a dense match with many awake effects could make step 3 worth more. Not
  measured on another demo.
- Step 2's copy cost: not measured.
- The 2 ms share comes from a sampled profile; the instrumented camera parts are the costs used here.

## (8) Implemented: the step split, behind `r_threaded_particles` (2026-10-26)

**What runs where** (*read from our code*). `ParticleEffects.Bursts` makes `UpdateNewEffects`' passes. Serial, in offer
order: every read and write of `_bursts`, `_spent` and `LifeOf`; every caller callback (`Model` posed once, `DormantAt`
asked for each step end by `Plan`, a rebuild's `History`); a rebuild's whole replay; creation's `Simulate( 0 )`. Forked
through `ParticleWorkers.For` (one effect each, the calling thread taking part, joined before return): the played steps,
in a **static** `Advance` that reads only its slot, so it cannot reach the collection, a counter or a callback. Serial
after the join, in offer order: `Commit`, which writes the entry, the counters (`Steps`, `Slept`, `ReplayTicks`,
`ReplaySteps`), a still frame's points and hitboxes; then `FadeUnoffered`, `Retire`. The build, collation and sleep push
stay serial, as `DrawModel` is (§6 step 4). A key offered twice flushes the pending slots first. A slot names its offer by
index, not by a copy, so B592's write-barrier copy is not reintroduced. **No world trace is reachable from a worker:**
`ParticleEffect` has no collision operator; the map's traces (`MapLevel.TraceBrushOnly`) are called by impacts' debris
(`ImpactParticles.TestForPlane`), beams, soundscapes and lighting, all on the game thread.

**The setting.** `r_threaded_particles` (`EngineConVars`, `ViewerSettings.ThreadedParticles`, help text), default 1,
read as `GetBool` over `GetInt` (`0.5` is off), reaching the fork through `ViewFrame`. Two parts are ours and named so:
the degree, physical cores − 1 with the game thread counted, leaving a core for `mat_queue_mode 2`'s render thread; and
a threshold (default 4, `TF2VIEW_PARTICLE_THRESHOLD`) under which a list runs serially, where the engine forks any list.

**Equivalence.** `ParticleEffectsWorkersTests` (46 frames, every case of §5, real threads at degree 2 and 8, backwards
and shuffled schedules, each asserting it forked; plus the seek's draw order and the duplicate offer) and
`BurstEffectsStepTests.StepOver_APlaybackOnWorkers_IsTheSerialPlayback` (B577's 3,000-frame playback through two
presenters, one serial: offers, census, counters, batch corner bytes and whizzes, frame by frame; unrendered it also
reproduces B592's fingerprint; > 100 forks asserted). **Sabotage:** removing in the serial pass reddened the seek-order
test and both fingerprints; deleting the duplicate-key flush reddened three worker tests. **Two survived, and why is
the finding:** committing inside the worker in completion order, and a worker `++` on `Steps` (paired with a `--` at
commit). The first is an equivalent mutant here: a played slot's key is already in `_bursts`, so its write never moves
dictionary order, and the only order-changing writes (an insert from a rebuild, a remove) are never on a worker. The
second is a race that only true contention loses and a deterministic schedule cannot show; it is prevented by `Advance`
being static, not by a test.

**Measured** (`measurements/2026-10-26-threaded-particles-and-sun.md`, f12 gummo first person, three runs each):
`bursts/step` 0.275-0.282 → 0.196-0.204 ms, **−0.07 to −0.09 ms, 0.8-1.0% of the frame** (measurements/2026-10-26-threaded-particles-and-sun.md); `camera particles` −0.06 to
−0.14; the frame's ranges touch. Thresholds 0, 4 and 16 are indistinguishable. Finding 94's 0.08 ms, reproduced.

**Not done:** the rocket trails' step (`Update`, 0.07-0.08 ms, measurements/2026-10-26-threaded-particles-and-sun.md `trails` part) is `ProcessPSystem` work in the engine and still serial
here — a divergence of schedule, filed in `docs/RISKS.md` with this entry; §6 steps 1 and 2 stand.

**What would falsify the main conclusion:** a closed-source read showing `particles.lib`'s `Simulate` spawns its own
jobs that outlive `UpdateNewEffects`, or a TF2 build where `Simulate` is not called from `OnRenderStart`.
