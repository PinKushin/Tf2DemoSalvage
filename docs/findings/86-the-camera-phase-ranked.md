# 86. The camera phase, ranked to the millisecond (2026-10-21)

**The question.** f12 (`demostf-cp_process_f12-2026-08-08-2207`, tick 30000) runs at 13.5–14.5 ms a frame in first
person, and real TF2 runs this scene at about 600 fps. Which work in our frame is the largest, what does Valve do for
it, and which of those can be removed without changing what is drawn?

**How it was measured.** `--measure 20 +fps_max 0`, Debug (D176), first person and the empty view
(`TF2VIEW_CAMERA="1613 -2821 154 89 0"`, no `--first-person`). Each run took the machine-wide lock, with a load sample
inside the lock before and after. A run is void if it drew clearly fewer frames, or showed a clearly higher `between`,
than its sibling runs. The one-second load sample cannot see contention in the middle of a run; two runs were voided
on this rule (an empty run of 1,914 frames against 2,600, and another of 1,551 against 1,880). **Steady seconds:** the
per-second lines with the first five seconds dropped, frame-weighted. The first seconds are tier-0 code (finding 84):
`jit:` reports 3.5–4.0 s of compiling inside the 20 s window, and those seconds read 19–30 ms. So the 20 s mean of
17.3–17.8 ms is the ramp plus a steady frame near 14 ms. That agrees with the brief's 13.5–14.5 ms, and it is not a
regression: a 60 s run of the release build `c01ac93d` measured 13.58 ms.

## The instrument

Below the `camera` phase, `pose` was already split by the moment's line and `setcamera` by the device. `camera
particles` was one number holding 1.3–1.8 ms that no counter named, in the empty view as well as first person. That
part is now timed at each step of `FramePresenter.DrawOver` (lights, decals, sounds, impacts, trails, bursts, build,
hand). The burst search inside `bursts` is timed at each offerer (explosions, tracers, blood, splashes, sentries,
dispatches, muzzles, players, beams, overheals, step). Every timer is read where its step runs and carried to the rate
line (B243). `FrameParts.Lap` replaces the two copies of the timing helper.

## The ranked table, before any fix (steady seconds, ms a frame)

Nested parts are indented under their parent and are not added to it again. *Measured*, one run each with the full
split (`n86bfp1`, `n86bempty1`). Two earlier runs without the particle split agree on the phases.

| bucket | first person | empty view |
|---|---:|---:|
| **camera** | **9.37** | **3.39** |
| · pose (the moment) | 4.98 | 0.74 |
| · · place: build (flex 0.86 of its lookups 0.93) | 1.23 | — |
| · · lighting | 0.94 | — |
| · · setup | 0.91 | — |
| · · baked | 0.46 | — |
| · · anim | 0.48 | — |
| · · sample / drawlist / viewmodel / simulate / collate / rest | 0.38 / 0.45 / 0.34 / 0.29 / 0.26 / 0.40 | 0.33 / 0.18 / — / 0.26 / 0.20 / — |
| · particles | 3.43 | 2.64 |
| · · bursts (the search and the step) | 1.72 | 1.69 |
| · · · dispatches | 0.68 | 0.66 |
| · · · step | 0.43 | 0.41 |
| · · · beams / muzzles / overheals | 0.27 / 0.19 / 0.12 | 0.25 / 0.25 / 0.10 |
| · · decals | 0.73 | 0.43 |
| · · build | 0.67 | 0.32 |
| · · lights / trails / sounds / impacts / hand | each under 0.1 | each under 0.1 |
| · setcamera (detail 0.37, batches 0.22, sky 0.15, overlays 0.15) | 0.91 | under 0.1 |
| **draw** | 2.23 | 0.50 |
| **the collector's pause** (no phase names it: it lands wherever an allocation does) | 1.82 | 0.78 |
| **between** (20 s mean) | 1.73 | 1.42 |
| **advance** | 1.14 | 0.72 |
| **hud** (frame 0.62, paint 0.50) | 1.05 | 0.94 |
| **sound** | 0.28 | 0.30 |

**The largest buckets that were not yet named:**
- In the empty view, `bursts` is the largest single piece of work, at 1.7 of 7.8 ms, and nothing is being drawn.
- The collector pauses 1.8 ms a frame in first person. 1.82 × 57 fps and 0.78 × 130 fps are both about 100 ms a second.
  Drawing nothing halves the frame and leaves the pause per second unchanged, so the allocator is mostly not the
  render thread (finding 82's reading, still true).

## Valve, for the top buckets

- **The dispatch offers.** *Read from published source.* Each frame, `CParticleMgr::UpdateNewEffects` runs `Update` on
  every live effect (`particlemgr.cpp:1844-1853`). Through `OnParticleSystemUpdated` and `UpdateControlPoint`, that
  re-reads a FOLLOW point's attachment; a non-follow point is read only at creation, unless its entity has a move
  parent (`particle_property.cpp:544-547`). `ProcessPSystem` flags an effect for removal once `IsFinished`
  (`particlemgr.cpp:1581-1584`), and `BuildParticleSimList` skips a flagged one (`:1777`).
  **Ours re-offered every stopless entity dispatch fired since the demo began, every frame.** That was 434 a frame at
  tick 30000 and 1,169 at tick 90000 (1,235 owned on f12), each with an attachment query for a FOLLOW point, a dormancy
  closure and a new `ParticleBurst`, for `ParticleEffects` then to skip. The engine's cost grows with live effects;
  ours grew with demo time (dispatches 0.66 ms at tick 30000, 1.17 ms at 90000). **A divergence; fixed below.**
- **The collector.** *Measured* by an EventPipe allocation trace (`alloc-trace … all`) over 20 s of playback, taken
  after load. **Control:** 10,147 MB over 20 s, against the `gc:` line's 8.98 MB × about 1,130 frames ≈ 10.1 GB.
  - 5,515 MB came from the corpse worker (`CorpseRecord`, D181): `CorpsePhysics.Advance` 1,898 MB, `IkFor` 1,807,
    `UpdateClientSideAnimations` 781.
  - 4,546 MB came from the render thread.
  - By type: `StudioBonePose[]` 1,635 MB; `IkFor`'s closure 652 MB and its error tuples 585 MB; `float[]` 601 MB;
    `SceneProp` copies 474 MB; `(float, float, float)[]` 411 MB, 353 of it under `LevelLighting.SunFrom` on a
    light-cache build.
  - Valve's `SetupBones` and IK context build into stack arrays and vectors they own (`c_baseanimating.cpp:2965-2966`,
    `bone_setup.cpp:3319`), and allocate nothing per frame. **Storage, not scope: a divergence in shape. The largest
    single type is fixed below.**
- **Pose (place, lighting, setup).** Each part is under 1 ms. The scope is Valve's already (in view, finding 83's
  table). Not read further here.

## Fix 1: a finished entity particle is not offered again while playing (parity)

`ParticleEffects.IsGone(key)` says whether a played frame would do nothing more with a burst: finished, or retired as
spent. `BurstEffects.AddParticleDispatches` asks it before building an owned dispatch's offer. A seek, or a clock that
went backwards, still offers everything, because that frame rebuilds from the offer (D221).

**Why nothing on screen can change.** For a key `IsGone` answers true, the consumer's loop already ended at `continue`
before reading the offer's point or definition (`_spent`, or `running.Effect.Finished`). An unoffered finished burst is
retired as spent, which `IsGone` also answers. So the only state that differs is the finished marker held in the census.
*Measured:* steps 27.6 a frame both sides, handed to the draw 19.6 against 19.8 (run noise), effects alive 360 → 37
(the markers). **No golden shot was taken, and one could not tell the two apart:** `--shot` captures the frame that
ends the opening seek, and a seek frame takes the old path.

| f12 tick 30000, steady seconds | before | after (two runs) |
|---|---:|---:|
| dispatches, first person | 0.68 | 0.26, 0.26 |
| dispatches, empty | 0.66 | 0.24 |
| bursts, first person / empty | 1.72 / 1.69 | 1.35, 1.32 / 1.32 |
| camera, first person / empty | 9.37 / 3.39 | 8.80, 8.80 / 3.07 |

Tests: `ParticleBurstTests.IsGone_AFinishedBurstWhilePlaying_IsTrueUntilASeekRebuildsIt` (red against a stub that
answers false). `BurstEffectsStepTests.StepOver_AnEntityParticleFinishedWhilePlaying_IsNoLongerOfferedUntilASeek` was
red three times: against the stub; with the seek and clock guard replaced by an always-true expression; and with the
clock half alone disabled (`tick >= Math.Min(_ownedTick, tick)`). **The seek half has no sabotage of its own.**
Two older tests in that file had used a no-emitter system as the stand-in for a dispatched effect, so it finished on
its first step. Their offers are now of a system that emits continuously, and the new test uses its own no-emitter
system.

## Fix 2: an IK chain is cleared in place (parity of storage)

`IkFor` cleared a chain with `errors.RemoveAll(held => held.Item1.Chain == shared.Chain)`. The lambda captures
`shared`, so C# made the closure at the top of every rule's loop iteration, whether the clear ran or not. Now it is an
in-place compaction: the same elements are removed and the order is kept. *Measured* on the unit
(`IkFor_ARepeatCallThatClearsAChain_AllocatesNoClosure`): 216 bytes a call with the closure, put back as the sabotage,
and 24 without. The 24 is `BlendedAnimations`' `[(animation, 1f)]` for a plain sequence, which is left as it is.

| f12 tick 30000, first person, two runs a side | before (fix 1) | after |
|---|---:|---:|
| allocated a frame | 8.42, 8.55 MB | 7.75, 7.77 MB |
| collector's pause a frame | 1.67, 1.68 ms | 1.55, 1.57 ms |
| 20 s mean frame | 16.98, 17.18 ms | 16.51, 16.49 ms |
| empty view: allocated / pause / mean | 3.79 MB / 0.67 / 7.37 ms | 3.53 MB / 0.65 / 7.26 ms |

No output can change: it is the same list operation.

## After both fixes (steady seconds, first person, two runs)

The camera phase is 8.49–8.56 ms (from 9.37), and the 20 s mean frame is 16.5 ms (from 17.3–17.8). **What remains is
spread out:**
- No single named bucket is over 1 ms except `draw` (2.2), the collector's pause (1.55) and `between` (1.6).
- Under `camera`: place-build 1.15 (flex 0.82), bursts 1.35, lighting 0.85, setup 0.80, build 0.65, decals 0.55.

Each further item is worth well under 1 ms. Fixes stopped here, at two; the brief asked for about three, and the next
candidates are listed in B567.

## Stalls: frames over 100 ms per minute

f12 tick 30000, first person, `--measure 60`, counted from the log after the viewer exited, from the first frame after
the opening state:

| run | build | frames over 100 ms | of them |
|---|---|---:|---|
| base | `c01ac93d` | 1 | the frame that ends the seek, 153 ms |
| A | fixes 1 and 2 | 9 | the seek frame 145 ms; viewmodel pose 97, 114, 128 ms (`SLOW MOMENT … viewmodel`); `camera particles` 110, 105 ms; sound `STALL decoding` 402 and 399 ms (`bulletLtoR08.wav`, `draw_primary.wav`); one 125 ms `draw` |
| B | fixes 1 and 2, same binary as A | 1 | the frame that ends the seek, 129 ms |

So the count is **1 per minute in two runs of three**. That is the frame ending the seek, as finding 84 measured, and
without it the count is 0. Run A's eight other stalls did not recur on the same binary, and the load samples were quiet
(6% and 3%). **Their cause is not established.** Two of them have the signature finding 84 left open: decodes of about
400 ms whatever the file's length (17,178 frames and 45,824 frames), which is not decode time. The viewmodel and
particle stalls are first seen here. *Interpolated, not shown:* first use of code still at tier 0, or contention the
load samples could not see.

## Wrong turns

- **The first ranking read the 20 s mean** (17.6 ms), which is the ramp plus the steady frame. The steady seconds are
  the ones that compare across runs.
- **A temporary counter printed counts through the timer's units** (count × ticks per millisecond) to find the 434
  offers. It never reached a commit.
- **A build that failed was measured once.** The run script went on to the old binary after `2 Error(s)`. That run was
  thrown away.
