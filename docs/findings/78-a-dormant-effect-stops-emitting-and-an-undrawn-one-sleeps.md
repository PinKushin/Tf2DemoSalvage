# 78. A dormant effect stops emitting, and an undrawn one sleeps (2026-10-18)

Two particle lifetime rules had been *interpolated*: what an effect does while its owner is dormant, and
what it does while nothing draws it. Both are in the closed particles library, which ships in the TF2
x64 build as `particles.obj` inside the Ghidra project `tf2enginex64`. Both are now read rather than guessed.
The second rule also explains a cost that had been climbing during ordinary playback.

## What we believed

The census line (findings 36 §9) showed bursts replayed during plain playback, with no seek. Over 60
seconds on f12 at tick 30000, the replay counter went from 155,774 steps to 317,751. The dormancy rule
was an interpolation. `CParticleProperty::OwnerSetDormantTo` (particle_property.cpp:465-475) calls
`SetDormant` on each owned effect. We guessed that `SetDormant` held the effect still, did not simulate
it, and hid it. Then it resumed where it stood.

## What the library does — read in Ghidra, `particles.obj`

The functions keep their mangled symbols, so there was nothing to rename (D174). The offsets are into
`CParticleCollection`.

- **`CParticleCollection::SetDormant(bool)`** (`0x000e5c60`) changes state only when the flag changes
  (`+0x68`). Going dormant, unless emission is already stopped (`+0x69`), it calls
  `StopEmission(false, false, false)`. Waking, it calls `StartEmission(false)`. That is the whole function.
- **`CParticleCollection::Simulate(float, bool)`** (`0x000e62e0`) never reads `+0x68`. A dormant effect is
  simulated like any other. Its live particles age and die, and no new ones are born.
- **`StopEmission`** (`0x000e7070`) sets `+0x69` and calls each emitter's `StopEmission`.
  `C_OP_ContinuousEmitter::StopEmission` (`builtin_particle_emitters.obj`, `0x00067050`) sets a
  stopped flag in its context. **`StartEmission`** (`0x000e6fa0`) clears both flags. It calls each emitter's
  `StartEmission`, and the continuous emitter's version (`0x00067070`) calls a further virtual with
  the collection's current time. It also sets `+0x44` to the time of the last simulation plus the
  definition's `+0x234`.
- **`CParticleCollection::Render`** (`0x000e5960`) sets `+0x44` on every draw:
  `+0x44 = max(+0x44, last simulation time + definition +0x234)`. `CNewParticleEffect::DrawModel` does
  the same when it distance-culls an effect (particles_new.cpp:536). That line names the field
  `m_flNextSleepTime` and names the definition's field `m_flNoDrawTimeToGoToSleep`.
- **`CParticleMgr::BuildParticleSimList`** (particlemgr.cpp:1766-1799) skips an effect once
  `now >= m_flNextSleepTime` and it has live particles. It still updates the bounds of an effect that has moved.

**So dormancy is a stop, not a pause.** A dormant owner's effect fades like a stopped effect, and it
emits again when the owner wakes. **Sleep is the pause.** An effect that nothing has drawn for its
no-draw time is not simulated, and when it is drawn again it resumes where it stood.

## The replays during playback were a missing sleep

Each rebuild that was not part of a seek was logged once, with a temporary line. Nearly all of them were
ordinary new dispatches met two to four ticks late. Two were different: `xms_ornament_glitter`,
dispatched at ticks 2744 and 3298, was rebuilt during play and replayed about 28,000 ticks each. Its
owner was in the PVS and not dormant, but not posed. `DispatchedPoint` needs a pose for any point that
follows an entity. So the effect went unoffered, faded, was retired, and was replayed from its dispatch
tick when the owner was posed again.

The fix ports the sleep clock as read. Each burst keeps `m_flNextSleepTime`:

- creation sets it to the creation time plus the no-draw time;
- each drawn frame pushes it to at least now plus the no-draw time, after the frame's step;
- waking from dormancy sets it to the wake time plus the no-draw time;
- a step taken at or past it is skipped while the effect has live particles.

The no-draw time is the definition's `time to sleep when not drawn`, default `"8"`
(`DmxElementUnpackInit<CParticleSystemDefinition>`, particles.obj, at offset 0x234).

An owned effect whose owner cannot be placed is offered **undrawn**. "Not posed" stands for "not
rendered". Until its sleep time it simulates on the points it last had, then it stands. The first drawn
frame after that still sleeps, because `Simulate` runs before `Render`, and the next frame steps.
An effect that has never been built is not built undrawn, because there is no point to replay it on. It
is built when it can first be placed, which happens once per seek.

**Still interpolated:** while undrawn the effect follows the points it last had. Valve reads the
attachment from a model it has not drawn, and we do not pose an undrawn model to read it.

A played frame longer than a tick is now cut at each tick whenever the owner's dormancy is asked.
Before, dormancy was sampled only at the frame's end, so a dormant tick inside a long frame was stepped over.

## Changed as a result

- A dormant tick now fades the effect: it is stepped and draws, but nothing new is born. Before, the
  tick was held and the effect hidden. `Hidden` is gone.
- The requested test that "a dormant effect is not drawn" would assert the opposite of the library, so
  it was not written. The dormancy test now asserts that the effect is stepped and its count stays the
  same while dormant, and that it emits again after the wake.

## Waking calls `SkipToTime`, read but not ported

`C_OP_ContinuousEmitter::StartEmission` (`0x00067070`) does three things:

- returns early if asked for infinite emitters only and this emitter has a duration;
- clears its stopped flag;
- calls the virtual at `+0xe8` with the collection's current time.

In the SDK's `CParticleOperatorInstance` declaration that slot is `SkipToTime`. The x64 table carries
one extra entry before `StopEmission`: the collection calls `+0x70`/`+0x78` where the SDK header puts
`+0x68`/`+0x70`. So this is an *interpolation from the slot*.

`SkipToTime` (`0x000670f0`) does not restart the window, which is `Restart`'s job (`0x000670c0`).
Instead it does the following:

- emits at once the particles of a span ending now. The span starts at the window start for an emitter
  with a duration, and at zero for one without. It is capped at 4 seconds, and the duration does not
  cap it.
- sets the emitter's running total to that count and **its emitted count to 0**.
- unless `g_bDontMakeSkipToTimeTakeForever` is set, rewinds the collection's time to
  `max(now − no-draw time, span start)` and re-simulates forward in 0.1 s steps.

Read literally, two things follow:

- a beam waking from dormancy flashes up to four seconds of particles;
- the next `Emit` owes the same particles again.

That is either a real double emission or a misreading of a SIMD-heavy decompile. **It is not ported**
until a TF2 observation settles it. The check is to stand a medic's heal beam's owner out of PVS and back,
then count the beam's particles on the wake frame.
