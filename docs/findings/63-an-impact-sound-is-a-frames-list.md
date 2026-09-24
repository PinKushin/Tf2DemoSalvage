# 63 — An impact sound is a frame's list

**Question.** A soldier dies on f12 at 26976 and his corpse lands. TF2 plays 11 `body_medium_impact` sounds in 26990–27060.
The first port played about 32, then none at all, then 29. Why?

## Four defects, found in the order they hid each other

**1. The friction solve treated every body as one kilogram.** vphysics' `IvpRigidBody::BuildJacobian` (`0x18009d010`)
writes the mass row's fourth lane from `core+0x4c` — the inverse mass — against the row's constant `1.0`, and sums the
off-diagonal term over x, y and z only. The port used a constant 1 and added `w·w` into the cross term. On the paired barrel
drop (`vphysics-drop phy` against `ivp-phy-drop`) the binary slid the barrel at tick 18 where the port spun it. After the fix
both raise 39 collision events on the same ticks through tick 98, and rest within 0.15 units. *Differential, against the
shipped binary.* It moved the f12 count very little: the rest was not in the solver.

**2. The first frame froze for 17 seconds.** The sound step's cursor started at `int.MinValue`, `tick - from` overflowed
negative, passed a catch-up guard of 16 ticks, and walked two billion ticks. Gate phase 3 caught it as 3–5 samples where 19
were required. Main failed too, which is what ruled out the machine. *Measured, with a CPU trace.*

**3. After a seek, no corpse made a sound.** The background corpse record starts behind playback, so the render thread
simulates the new corpse itself — and only the record reported impacts. Then, once both reported, a cursor that stepped
past the record's reach lost the ticks at the handoff, and a frame slower than 16 ticks was taken for a seek. The fix is one
listener: `CorpsePhysics` raises `ImpactHeard` from whichever path posed the corpse that frame. *Measured, with a control:*
the record's own sounds at ticks 2–18 were logged the whole time the live ones were missing.

**4. The list is the frame's, not the tick's.** `CPhysicsSystem::PhysicsSimulate` (`game/client/physics.cpp:440`) calls
`physenv->Simulate( frametime )`, which crosses however many ticks the frame covers, and only then
`physicssound::PlayImpactSounds( m_impactSounds )`. `AddImpactSound` (`vphysics_sound.h:82`) merges by surface, and merges
everything once more than four are listed — across the whole frame. The port merged per tick and played every tick.

## The reference was a slow client

TF2's impacts fall on a clean four-tick rhythm: 27025, 27029, 27033, 27037, 27041, then 27049, 27053. That is a frame every
~60 ms — the capture ran at about 16 fps. So **the count is a function of the frame rate**, in TF2 as in the port, and a
comparison is only fair at the same rate. At `+fps_max 16` the port plays 15 in the window, on the same four-tick cadence,
starting at 26994 to TF2's 26995. *Measured.* The remaining difference is a quiet stretch in TF2 at 27014–27022 where the
port sounds twice: the corpse's motion, not the sound path.

## What stays open

Friction and scrape sounds (`PhysFrictionSound`, TF2's `body_medium_scrape_rough_loop1` at 27057) are not built.
