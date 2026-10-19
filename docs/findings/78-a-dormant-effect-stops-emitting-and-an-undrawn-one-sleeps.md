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

**Still interpolated:**

- **Points while undrawn.** An undrawn effect follows the points it last had. Valve reads the attachment from a
  model it has not drawn, and we do not pose an undrawn model to read it.
- **Off-camera effects never sleep.** "Not posed" is not "not rendered". An effect whose owner is posed but which
  the camera never sees sleeps after its no-draw time in the engine, because only `Render` pushes
  `m_flNextSleepTime` forward. Ours keeps simulating, because we mark undrawn only an effect we cannot place.

**A played frame is one step (D221) unless the owner's dormancy changes inside it.** Then the frame is cut at the
last tick before the change, so each piece has one dormancy state. Before this, dormancy was sampled only at the
frame's end, so a dormant tick inside a long frame was stepped over.

**This cut is a named departure.** The engine applies `SetDormant` per packet, then runs one
`Simulate(frametime)`, so a wake's `SkipToTime` runs at the frame's start curtime. Ours runs it at the gap's own
tick and splits the frame there. The order of a change inside a frame is kept, at the cost of one frame becoming
several steps.

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

**The slot is read from the data, not inferred.** The RTTI-anchored vtable of `C_OP_ContinuousEmitter` is the
`.rdata` block at `0x000654d0`. Its first entry is the complete-object locator (`0x00065bf0`, in `.rdata$r`), so the
table starts at `0x000654d8`. In that table `+0x68` is `Emit` (`0x00066d30`), `+0x70` is `StopEmission`, `+0x78` is
`StartEmission`, `+0x80` is `Restart`, and **`+0xe8` is `SkipToTime` (`0x000670f0`)**. The SDK header
counts one virtual fewer before `Emit`. My first account explained the gap as an extra entry in "the x64 build",
which was wrong: these objects *are* the SDK's x64 `particles.lib`, and the gap is in my count of the header.

`SkipToTime` does not restart the window; that is `Restart`'s job (`0x000670c0`). Read in the disassembly, it
does the following:

- returns if `curtime <= start time` (`COMISS`/`JBE`);
- takes a span ending now that starts at the start time for an emitter with a duration and a start
  time, and at 0 otherwise. The span is capped at 4.0 (`MINSS` against `0x000658ac`, which holds 4.0f). **The
  duration does not cap it.**
- stores the total as `span · rate` (`MOVSS [RSI], XMM0`) and **the emitted count as 0** (`MOV dword [RSI+4], 0`).
  Nothing else in the function writes `[RSI+4]`.
- births `min(floor(total), room)` particles, evenly over the span;
- unless `g_bDontMakeSkipToTimeTakeForever` (`.bss 0x2100`) is set, sets the collection's time to
  `max(now − no-draw time, span start)`. It then calls `CParticleCollection::Simulate(0.1, false)`
  (the call at `0x000673db` relocates to `?Simulate@CParticleCollection@@QEAAXM_N@Z`) until the time is reached.
  The loop counter is a float stepped through a double.

**So the double emission is real code, not a decompiler artefact.** The re-simulation's own `Emit` calls find the
emitted count at 0 and owe the whole total again. TF leaves the flag false: its line in
`CParticleMgr::UpdateNewEffects` is commented out (particlemgr.cpp:1826-1829). The flag is the only input the
disassembly could not settle. Valve's own commented-out line names the cost, so the TF2 run was not needed.

**Ported**: `ParticleEffect.StartEmission`, which `ParticleEffects` calls when an owner wakes.

`SetDormant` checks emission-stopped (`+0x69`) only on entering dormancy. So the latch follows that rule:

- An effect already stopped when its owner goes dormant never goes dormant, and does not wake.
- A stop that lands inside the gap does not cancel the wake. `StartEmission` clears `+0x69`, so the effect
  emits again afterwards.

Measured on synthetic systems, exact:

- A half-second emitter at 8 a second, woken at one second, goes from 4 particles to 24
  (4 + 8 at once + 8 + 4 in the re-simulation).
- A six-second emitter with a no-draw time of 1 s, woken at six seconds, goes from 48 to 120: the 4 s cap gives
  32 at once, and the rewind to 5 s gives 40 in the re-simulation.

**A second, independent route agrees on the slot.** `C_OP_InstantaneousEmitter::StartEmission` (`0x00066af0`)
ends in `JMP qword ptr [RAX + 0xe8]`. Its own vtable (the `.rdata` block at `0x00065380`, which starts at
`0x00065388` after its locator) holds at `+0xe8` the address `0x00066b20`. That address carries the object's
own symbol `?SkipToTime@C_OP_InstantaneousEmitter@@…`. So the two tables and two mangled names agree that
`+0xe8` is `SkipToTime`.

**And the instantaneous emitter wakes too**, which the first port missed:

- `StartEmission` restores the owed count from the drawn one (`MOV EAX,[R8+4]; MOV [R8],EAX`).
- Its `SkipToTime` zeroes the count when curtime is past start time + context offset + 2.0. The context offset
  (`ctx[2]`) is zeroed by `InitializeContextData` (`0x00066b40`, `MOV dword [R8+8], 0`) and is never written by `Emit`;
  only `Restart` sets it. So for an effect that has not restarted, the test is start + 2.0 (`ADDSS` against
  `0x000658a8`, which holds 2.0f).

So a burst woken within two seconds of its start fires again in full. Ported in the same
`ParticleEffect.StartEmission`, and tested at 1 s (fires again: 5 then 10) and at 2.5 s (does not: 5).

**`g_bDontMakeSkipToTimeTakeForever` is false.**

- *Read from published source:* a search of all of `source-sdk-2013` finds exactly two references, both commented
  out (`src/game/client/particlemgr.cpp:1827-1828`, the `extern` and the `= true`). No uncommented line sets it.
  A Valve comment is not its code (B463), so those two lines say only that nothing live assigns it.
- *Read from the object:* the flag `SkipToTime` tests (`CMP byte ptr [0x00002100], 0x0` at `0x0006738e`) is in
  the `.bss` section of `builtin_particle_emitters.obj`, which is zero-initialized. So it holds its default of
  false, and the re-simulation runs.

**Evidence class.** These objects are the SDK's `particles.lib`, not TF2's live `client.dll`. That they match
the shipped game is an assumption of the same kind as every other read from this library.
