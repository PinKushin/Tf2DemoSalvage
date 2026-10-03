---
name: A flag that changes state belongs in the state machine
description: Carry game data INTO the decode when it changes a branch; an output filter only works for a flag that chooses between outputs (B437).
type: feedback
---

A class-script flag was kept in the scene and applied as a filter on Core's output, because only the scene had the
installed game. That works for a flag that CHOOSES between finished outputs (which landing gesture, which reload
name). It cannot work for one that changes STATE: `bValidAirWalkClass` decides whether `HandleJumping`'s air-walk
block runs, and so whether the jump's bookkeeping is suspended — no filter downstream can undo a branch already
taken. The fix was to carry the scripts into `DemoTimeline.Build` (`IClassAnimationScripts`, implemented by
`PlayerClassModels`), and the viewer opens the install before the decode.

**How to apply:** when engine behaviour depends on game content, ask whether the flag picks an output or steers
state. If it steers state, carry the content into the layer that steps the state — don't split it across layers.

Also from B437: a comment saying "every shipped X has flag Y false" contradicted a measurement in the same repo
(`ClassAirwalkTests`: soldier and medic set `DontDoNewJump`). Before trusting such a claim, grep for the test that
measured it. And a field the engine CLEARS is not a behaviour until something READS it
(`m_bCurrentFeetYawInitialized` is written twice and read nowhere).
