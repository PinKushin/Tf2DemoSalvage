---
name: determinism-does-not-require-flattening-a-draw
description: "Before replacing a random draw with a midpoint for reproducibility, read how the engine seeds its own — Source's draws are already pure functions of the thing being drawn for."
metadata: 
  node_type: memory
  type: project
  originSessionId: 4774a88b-811c-40bb-9c79-9b22dc0a4474
  modified: 2026-09-10T22:49:15.984Z
---

**A random draw and a reproducible replay aren't in tension in Source; assuming otherwise cost a
visible divergence.** `CParticleCollection::RandomInt` is a table lookup keyed on the particle's own
id plus a per-operator offset — a particle's lifetime is a pure function of that particle,
independent of frame rate or draw order. Replaying gives the same answer with nothing stored.

**What went wrong (B373, D152):** `Lifetime Random` was implemented as the MIDPOINT of min/max, citing
D136's replay determinism — but `rockettrail` declares 0.8/1.2, so every particle lived exactly one
second and the plume died all at once instead of trailing off.

**Why:** D136 asks for a SEED, not removal of the draw — flattening wears the same citation for a
different thing. The supporting claim (bounds were equal) was written without opening the file
([[nothing-is-closed]]#a-valve-comment-can-be-stale, applied to our own comments).

**How to apply:** when the engine has randomness and this project needs reproducibility, read where
the engine's randomness comes from first — Source already seeds particles/prediction/effects for the
same reason we want determinism, so the goals usually coincide. Where they truly conflict, seed the
draw; don't remove it ([[valve-parity-is-the-first-principle]],
[[a-filed-design-choice-may-not-be-one]]#an-unrecoverable-input-is-not-an-open-choice).

Flagged, not claimed: the table's contents ship only in `particles.lib`, so ours is stable but not
float-for-float Valve's ([[nothing-is-closed]]#absent-from-the-sdk-is-not-unreadable).
