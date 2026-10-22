# 94. Threads buy little of the camera phase, and bone setup is not ready for them (2026-10-22)

**Status: the code described below is NOT on main.** It matched serial output byte for byte on synthetic scenes but
saved nothing measurable on f12, and rewrote most of `ParticleEffects.cs` (+609/-165 lines) plus a worker-count
setting for no gain, so it was not merged. It is kept on the branch `worktree-agent-a15ceb35cdf667d9a` (commits
`eb5f63ea`, `ba46fc25`) and is the starting point if a later change makes the threaded build worth having. This record
and its measurement stay because the wrong estimate (about seven times too high) and what falsified it are the point.

**The question.** Finding 93's design note named two parts of `camera` that could run on worker threads with
byte-identical output: the particle step and build (estimated 0.5-0.7 ms) and bone setup per move-parent tree (estimated
0.6-0.9 ms). What does each actually save, and what stops it? Evidence classes as in 93.

## (1) What Valve threads, and how (*read from source*)

`CParticleMgr::UpdateNewEffects` (particlemgr.cpp:1824-1900) runs in three parts:
- a serial loop calling every effect's `Update`, with the comment *"this one can call into random entity code which may
  not be thread-safe"* (:1844-1853);
- `BuildParticleSimList`, serial, which picks what to simulate (sleep, remove flag, bounds-only; :1766-1799);
- `ParallelProcess( …, ProcessPSystem )` over that list when `r_threaded_particles` is 1, its default (:1543, :1869-1884).

The unit is one root effect: `Simulate` recurses into its children. `ProcessPSystem` touches only that effect (:1547-1586).
Retirement and drawing stay serial. So threading the step is parity of mechanism; threading the BUILD (our quads) is our
addition, allowed only because the bytes are the serial bytes.

## (2) What was built (`eb5f63ea`)

- **`ParticleEffects.Bursts` is split as Valve splits it.** A serial pass in offer order does every read and write of the
  collection (`_spent`, `_offered`, `LifeOf`'s cache, the rebuild decision, `new ParticleEffect`, which calls the caller's
  `Sheets`), asks every caller callback (`Model`, which poses a model; `DormantAt`; `History`), and runs a rebuild's whole
  replay, because a replay asks `History` for every tick. A played burst gets a plan instead: its step ends and the owner's
  dormancy at each, asked on this thread. The ends do not depend on the effect: a sleeping step and a stepped one move to
  the same end, and a finished one only stops early. The workers then run the played steps, one effect each, reading only
  the plan. A last serial pass writes every burst back in offer order.
- **The trap the split had to avoid: dictionary order is output.** `_bursts` enumerates in the order of its inserts and
  removes, and a remove frees a slot a later insert reuses. The build walks `_bursts`, so if the dormant/undrawn removes
  ran in the first pass and the writes in the last, a seek that drops one burst and starts another would draw them in a
  different order. Removes are therefore queued with the writes and applied in offer order. A key offered twice in one
  frame flushes what is pending first, so the second offer sees the first's write, as the serial loop did.
- **`Build`** decides what is drawn and pushes sleep times serially, gathers each effect into its own slot on the workers,
  and appends the slots in order. That reproduces both the serial concatenation and the order in which a material is first
  seen, which is the batch order.
- **What stays serial, and why:** the trails' `Update` (0.08 ms, and its loop mutates three collections per entity),
  `FadeUnoffered` and `Retire`.
- **Thread-safety evidence** (*read from our code*). The definitions (`ParticleSystem`, `ParticleFunction`) are immutable
  records. `ParticleOperators.All()` makes new, stateless operators for each effect. `ParticleRandom`'s table is static
  readonly. The only shared write is `ParticleEffect.BirthsBySystem`, a `ConcurrentDictionary` count whose sum does not
  depend on order. `ParticleSprites` and `ParticleSpriteTrails` read a store and write only into the list they are given.
  `EntityDormancy.IsDormant`, the f12 feed's `DormantAt`, is a read, but it is asked serially anyway.
- **`cl_frame_workers`** (ours: TF2 sizes its thread pool from the machine and has no cvar for it) defaults to
  `ProcessorCount / 2`, capped at 8. 1 is serial. It reaches `ParticleEffects.Workers` through `ViewFrame`.

**The equivalence test** (`ParticleEffectsWorkersTests`, synthetic). 46 frames of bursts, covering three materials, a
child, a duplicate offer, a dormant gap, a stop, a material first seen mid-run, and a seek that inserts one burst ahead of
another it drops. Every frame's batch bytes, census and counters are equal across serial, 2 workers, 8 workers, a
backwards schedule and an interleaved one. A separate test asserts the seek's draw order outright, because every run shares
the commit code and so a comparison between runs cannot see a wrong commit. Sabotaged:
- merging gathers in completion order reddened the backwards and interleaved tests;
- committing inside the worker reddened them too;
- removing in the serial pass reddened the seek-order test.

Each was restored by its inverse edit.

## (3) What it saved (*measured*, `measurements/2026-10-22-threaded-particles.md`)

S/T/S/T on f12 30000, first person, one binary:
- `bursts/step`: 0.36 → 0.28 ms (the main thread's wait, not the work);
- `build`: 0.68-0.73 serial, 0.68-0.70 threaded, which is noise;
- `camera`: 6.55-6.57 serial, 6.58-6.60 threaded.

**Finding 93's estimate for this row was about seven times too high.** The step moves; the build barely does. The likely
reason, *not measured*: the gather is a small share of `build`, which also holds collation and the sprite and sun builds,
and the merge copies every vertex a second time. The threaded build is kept because it is identical and not slower, but it
earns nothing on f12. **Unverified: an output-level identity on f12.** The fixed-step capture meant for it is not
deterministic even serial against serial (`TF2VIEW_FIXED_FRAME` did not stop the wall clock advancing the demo), so the
pictures prove nothing either way. The seek-based fallback failed the same control: `--shot` at ticks 30100, 30200 and
30300, taken at 1, 8 and 1 workers, gave three different PNG hashes at every tick, and the two serial shots' sidecars are
identical except for their capture time and file name. So the capture is not deterministic even where the recorded state
is the same (*measured*; the cause was not looked for). The identity rests on the synthetic test. An output-level check
needs an instrument that hashes the particle batches and instance list of each frame directly rather than the pixels.

## (4) Bone setup: not attempted, and why (*read from our code*)

Setup plus anim measured 0.4-0.6 ms (finding 93 (1)), so the 0.6-0.9 estimate was already mostly flex and place, which run
after the serial lighting call.

**A wrong first reading, kept because it was committed.** The first version of this finding listed "about ten plain
`Dictionary` caches" on `PropModels.SkinnedModel` (one per model, shared by every entity wearing it) as the first blocker,
read from their assignment lines alone. Their declarations say otherwise: `_ikRules`, `_bonesByGroup`, `_remapByGroup`,
`_locks`, `_autoLayers`, `_relativeSequences`, `_boneWeights`, `_boneMaps` and `_groupBones` are all `ConcurrentDictionary`
(PropModels.cs:1746-1771, :2259-2262). The two `??=` fields, `_ikChains` and `_autoplay`, compute a pure value from the
model, so a race only computes it twice. The model caches are not a blocker. Content's own decode caches below
`LocalsInto` (`StudioAnimation` and the sequence readers) were not audited. What remains:
- **Static counters and timers inside the pose:** `SkeletonPose.PoseBuilds`, `AnimationTicks` and `AnimationCalls` are
  static `++`/`+=`. That is a race, and every `Builds*` delta and the `anim` part would stop meaning anything.
- **A follow chain crosses trees by design.** `AnimatingEntity.SetupBones` asks `Follows.SetupBones` from inside itself
  (bone_merge_cache.cpp:130), and the frame-check and readable-bone guards are plain fields. A worker per tree is only
  safe if the partition is the whole move-parent closure, computed serially.
- **A pre-pass must reach exactly the set the draw loop reaches**, with its skips (`RenderMode.None`, no geometry, the
  lookups) and its order relative to `_lighting.For`. The light cache's LRU and its two-builds-a-frame budget are output.

Each of these is fixable: per-thread counters summed afterwards, a serial partition, and a pre-pass that is the loop's
own selection. But the work they unlock is at most 0.4-0.6 ms, before scheduling cost. Scaled by the particle row's
sevenfold overestimate, the saving is under 0.1 ms. That is an *estimate*, and not measured. It is left for its own task.

## (5) Does the draw/HUD overlap look feasible?

The 3 ms item (draw 2.1 + hud 1.0) was not attempted. On this evidence, **the overlap is now the only threading lead
left with a size worth the work.** The two per-unit parts gave 0.08 ms and a refactor blocker. What it needs is unchanged
from finding 93: an immutable snapshot of the frame, so that the next frame's simulation cannot change what is being
drawn. **Its "up to 3 ms" is the same kind of estimate that just came in seven times too high.** So the first step is to
measure how much of draw plus HUD is CPU work that could run beside the next frame's simulation, before building a
snapshot. The particle split shows the shape that works here: serial decisions, per-unit buffers, and a merge in serial
order.
