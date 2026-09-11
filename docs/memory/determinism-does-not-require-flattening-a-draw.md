---
name: determinism-does-not-require-flattening-a-draw
description: "Before replacing a random draw with a midpoint for reproducibility, read how the engine seeds its own — Source's draws are already pure functions of the thing being drawn for."
metadata: 
  node_type: memory
  type: project
  originSessionId: 4774a88b-811c-40bb-9c79-9b22dc0a4474
  modified: 2026-09-10T22:49:15.984Z
---

**A random draw and a reproducible replay are not in tension in Source, and assuming they were cost
a visible divergence.** `CParticleCollection::RandomInt` is a table lookup,
`s_pRandomFloats[ ( m_nRandomSeed + nRandomSampleId ) & RANDOM_FLOAT_MASK ]`
(`src/public/particles/particles.h:1782`), and the sample id is the **particle's own id** plus a
per-operator offset (`:1801`). So a particle's lifetime is a pure function of that particle:
independent of frame rate, of how many were born before it, and of operator order. Replaying gives
the same answer without anything being stored.

**What went wrong (B373, D152).** `Lifetime Random` was implemented as the MIDPOINT of
`lifetime_min` and `lifetime_max`, with a comment citing D136's replay determinism — and a second
claim that the two bounds were equal anyway. `rockettrail` declares 0.8 and 1.2. Every particle in a
rocket trail lived exactly one second, so the plume died all at once instead of trailing off.

**Why:** D136 asks for a **seed**, not for the removal of the draw — it says the adaptation is that a
draw *"must be seeded per corpse so scrubbing backwards shows the same one twice"*. Flattening is a
different thing wearing the same citation. And the supporting claim about the bounds was written
without opening the file, which is the `a-valve-comment-can-be-stale` section of [[nothing-is-closed]]
applied to our own comments.

**How to apply:** when a value is random in the engine and this project needs it reproducible, go
read where the engine's randomness comes from before deciding anything. Source seeds its particle
streams, its shared prediction and its client effects for the same reason this project wants
determinism, so the two goals usually coincide — and a conflict between them is evidence the engine
has not been read yet ([[valve-parity-is-the-first-principle]] and its
`parity-is-the-first-hypothesis` section). Where they truly conflict, the seed is the adaptation and
the draw is not negotiable (the `an-unrecoverable-input-is-not-an-open-choice` section of
[[a-filed-design-choice-may-not-be-one]]).

One thing stays flagged rather than claimed: the table's CONTENTS ship only in `particles.lib`, so
ours is uniform and stable but not float-for-float Valve's — the `absent-from-the-sdk-is-not-unreadable`
section of [[nothing-is-closed]] names how to settle it.
