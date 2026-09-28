---
name: a-tf2-corpse-is-simulated-not-sent
description: "DT_TFRagdoll sends initial conditions only; every corpse pose after the first frame is client-simulated, so there is no wire shortcut."
metadata: 
  node_type: memory
  type: project
  originSessionId: 4774a88b-811c-40bb-9c79-9b22dc0a4474
  modified: 2026-09-06T17:35:16.323Z
---

`C_TFRagdoll::CreateTFRagdoll` calls `InitAsClientRagdoll` (`c_tf_player.cpp:920`), the ordinary
client ragdoll path into the client's own `IPhysicsEnvironment`. `DT_TFRagdoll` sends
`m_vecRagdollOrigin`, `m_vecForce`, `m_vecRagdollVelocity`, `m_nForceBone` and appearance flags —
**initial conditions only** (`c_tf_player.cpp:519`).

**Why it matters:** two ragdoll families exist and only one is on the wire. `C_ServerRagdoll`/
`DT_Ragdoll` networks per-element `m_ragPos`/`m_ragAngles` (`ragdoll.cpp:423`) and owns no physics
objects (`GetElement` returns `NULL` unconditionally, `ragdoll.cpp:646`). **TF2's death ragdoll is not that family** —
a demo hands us position, force and force bone; every pose after frame one is client-computed.

**How to apply:** no "read the pose off the wire" shortcut exists, and no sequence-picking finishes
B316 — resting a corpse in `ACT_DIERAGDOLL` is a stopgap, not what the engine draws. Correct drawing
requires simulating (`docs/findings/51`, `vphysics.dll`). Two numbers come free and shouldn't be
guessed: gravity is `sv_gravity`; the physics step is the demo's own `interval_per_tick`, NOT frame
time (Valve fixes it deliberately — *"helps keep ragdolls stable"*, `physics.cpp:177-180`).

Confirmed by two independent routes (call site + send table) that agree. See
[[death-is-ef-nodraw-not-an-animation]] for the neighbouring fact.
