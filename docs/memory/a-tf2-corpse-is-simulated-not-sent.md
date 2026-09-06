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
**initial conditions and nothing else** (`c_tf_player.cpp:519`).

**Why it matters:** there are two ragdoll families and only one of them is on the wire.
`C_ServerRagdoll`/`DT_Ragdoll` networks per-element `m_ragPos`/`m_ragAngles` (`ragdoll.cpp:423`) and
owns no physics objects (`GetElement` returns `NULL` unconditionally, `ragdoll.cpp:646`). **TF2's
death ragdoll is not that family.** So a demo hands us a position, a force and a force bone, and
every pose after the first frame is something the client computed.

**How to apply:** there is no "read the corpse's pose off the wire" shortcut and no amount of
sequence-picking finishes B316 — resting a corpse in `ACT_DIERAGDOLL` is a stopgap that puts a body
on the ground rather than what the engine draws. Drawing corpses correctly requires simulating, which
is what the `vphysics.dll` reading in `docs/findings/51` is for. Two numbers come free and should not
be guessed: gravity is `sv_gravity`, and the physics step is the demo's own `interval_per_tick`, NOT
the frame time — Valve fixes it deliberately, *"helps keep ragdolls stable"* (`physics.cpp:177-180`).

Confirmed by two independent routes that agree: the call site and the send table. See
[[death-is-ef-nodraw-not-an-animation]] for the neighbouring fact about how death is expressed at
all.
