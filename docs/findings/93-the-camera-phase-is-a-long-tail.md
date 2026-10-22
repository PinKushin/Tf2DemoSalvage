# 93. The camera phase is a long tail, and what could run beside it (2026-10-22)

**The question.** Finding 92 put about 60% of the steady f12 frame in the `camera` phase. What is inside it, which parts
cost something the engine does not pay, and — since no single part is large — which parts could run on other threads
with byte-identical output? Evidence classes: *measured*, *read from source* (SDK or our code), *arithmetic*,
*estimate* (a number reasoned from measured ms, not measured itself), *interpolated*.

## (1) Inside `camera` (measured, `measurements/2026-10-22-f12-camera-breakdown.md`)

About 7.1 ms of a 12 ms frame at light load. Its four parts sum to the phase within 0.01 ms:
- **pose** 3.9-4.0, which is the moment rebuild. Placing models 0.7-0.8, simulate 0.5, draw list 0.4-0.5, lighting
  0.2-0.5, the rest 0.1-0.4 each.
- **particles** 2.4. Bursts 0.9-1.0, build 0.65, decals 0.52.
- **set-camera** 0.7.

Nothing above 0.8 ms. A trace on the same scene agrees (`UploadCameraOver` 59.9% against 60.2%). The exposure count
(B575) is not inside `camera`.

## (2) Fixed: the model decals walked the whole match each frame

`StepModelDecals` walked all 12,518 client bullets and every entity impact, every frame, to keep those landed since the
last one. The engine places a decal as its event arrives. The step now binary-searches the window in each list, both of
which are in tick order. A list out of order is walked whole, so the same items are visited in the same order either way.
**Measured, ABBA pair** (`measurements/2026-10-22-model-decal-window.md`): `decals` 0.53 → 0.19 ms, `camera`
6.97 → 6.57, census unchanged at 75 model decals. The whole frame does not resolve the difference. Tests:
`StepOver_AMatchOfBulletsOutsideTheFramesWindow_ExaminesOnlyTheOneThatLandedInIt` and
`StepOver_BulletsOutOfTickOrder_AreAllExaminedAndTheFramesStillLands`, both red against the full scan and under sabotage.

## (3) Two leads that measured as nothing

- **The dynamic-light merge** copies the whole world-light list for each model draw that sees a dynamic light, but on f12
  it never ran: 331,621 draws asked, none saw a light (*measured*, `measurements/2026-10-22-dynamic-light-merge.md`). The
  first run read zero with no control, so a count of draws that ASK was added before the second run. Whether TF2 makes a
  model-lighting dynamic light in this window is a parity question, left open.
- **B574's struct copies.** The trace charged `ParticleEffects.Bursts`' copy of each burst out of its list with about
  0.2 ms (1.7%), every sample with a `PollGCWorker` leaf. Walking by reference moved its own timer 0.363 → 0.347 ms
  (*measured*, `measurements/2026-10-22-burst-copy.md`), so *arithmetic* wins: 26 copies a frame cost hundredths. **A copy
  site's sampled share is not its cost.** The change was not kept.

## (4) Design note: what could run on other threads (investigation, no code)

**The bound.** Parity bounds what is drawn and simulated, not how many cores do it (D89/D129/D131 as the coordinator
stated them). A threaded path is allowed if its output is identical to the serial one.

**What Valve threads** (*read from source*):
- Particle systems are simulated in parallel by default: `r_threaded_particles "1"` and `ParallelProcess(...,
  ProcessPSystem)` over the frame's sim list (`particlemgr.cpp:1543`, `:1869-1884`).
- Bone setup has a threaded path that is off and development-only: `cl_threaded_bone_setup "0"`, `FCVAR_DEVELOPMENTONLY`
  (`c_baseanimating.cpp:2743`). So are the leaf system and renderables: `cl_threaded_client_leaf_system "0"`
  (`clientleafsystem.cpp:35`) and `r_threaded_renderables "0"` (`viewrender.cpp:122`).
- `mat_queue_mode` (read in `viewpostprocess.cpp:1480`) queues the material system's calls to its own thread. That is
  the overlap of draw with the next frame's simulation.

| part | ms now (measured) | independent unit | shared state it touches | serial residue | estimated saving |
|---|---:|---|---|---|---:|
| particle step + build | 0.35 + 0.65 | one effect | its own state; control points read from posed attachments (read-only once pose ends); the offered/spent sets; the output vertex list | the sets and the concatenation, in the serial order | 0.5-0.7 |
| burst offerers | 0.55 | one offerer (eleven) | the shared offer list they append to | merging their lists in the fixed offerer order | 0.2-0.3 |
| bone setup + anim + flex + place build | about 1.2-1.4 | one move-parent tree (root and its followers) | a follower's bone merge reads its parent's bones; the readable-bone cache; per-entity flex stream | trees in parallel, children after parents inside each | 0.6-0.9 |
| lighting | 0.2-0.5 | none | `LightCache`: an LRU whose order and two-builds-a-frame budget depend on call order (`LightCache.cs`) | all of it, as a serial pass after bones | 0 |
| simulate (client animation) | 0.5 | none safely | fires animation events: sounds and particles, whose order is output | all, unless events are recorded per entity and replayed in entity order | 0-0.3 |
| draw list, collate | 0.4-0.5, 0.2 | leaf | visible sets | small; Valve keeps it serial | 0 |
| model decals | 0.19 | none | one pool ring that retires its oldest; order is output | all | 0 |
| set-camera | 0.7 | — | pose reads its frustum and views | a dependency, not a candidate | 0 |
| draw + hud (outside camera) | 2.1-2.3 + 1.0 | the whole frame | the device | overlapped with the next frame's simulation, as `mat_queue_mode` does, from an immutable frame snapshot | up to 3 |

**Estimates**, every one: the saving column assumes four or more idle cores, a CPU-bound frame (finding 92: the GPU is
6-10% busy), and scheduling overhead of about 10-20 µs a parallel region. None is measured. The particle row is the one
Valve itself does in parallel, and the safest place to start. The queued device is the largest, and the most work: a frame
must be snapshotted so that the next frame's simulation cannot change what is being drawn.

**What must not be parallelized, because order is output** (*read from our code*): the light cache's LRU and miss budget;
the model-decal pool ring; animation events, sounds and particle offers; and the decal sounds emitted in iteration order
(`StepModelDecals`). The rule for every threaded part: workers write only into per-unit buffers, and one serial step
merges those buffers in the order the serial path would have produced them.

**Determinism and the test** (proposal):
1. **Synthetic, in `Core.Tests`/`Scene.Tests`.** Build a demo (`SyntheticDemo`) with parented wearables, several effects
   and an animation event. Run the frame with the serial path, then the threaded path at 1, 2 and 8 workers, and once with
   a test scheduler that runs units in reverse. Assert byte equality of the instance list (bone matrices, flex streams,
   lighting), the particle vertex buffers, and the ordered sound and event list. The reverse scheduler is the control: a
   merge that relies on completion order must turn it red.
2. **Output level, f12** (lcor, a `*Diagnostic`): play 200 frames serial and threaded from tick 30000 and compare a hash
   of each frame's instance list and particle buffers. Every frame must match, and the diagnostic reports the first frame
   that differs.
3. **Measured after**, by the same ABBA method as (2), with the parts timers carried from the worker that did the work.
   A part timed on the main thread around a parallel region measures the wait, and must be named as the wait.
