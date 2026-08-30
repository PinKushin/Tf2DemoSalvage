---
name: key-a-lookup-on-the-question
description: Deriving every case from case zero makes case zero load-bearing; key on the input the engine keys on, not on one case's answer.
metadata:
  type: project
---

When a value varies by some selector — a skin family, a team, a language, a quality level — there is
a tempting shortcut: resolve **case zero**, then express every other case as a *diff from* that
resolved answer. It halves the storage and it reads as an optimisation.

It makes case zero load-bearing for every case, and it fails in two ways that look nothing alike.

**Measured, 2026-08-29 (B229).** A `.mdl` mesh's `material` field is a **skinref**, and
`g_skinref[skin][skinref]` returns the texture index — Valve's own comment, at
`utils/motionmapper/motionmapper.h:134`. This project resolved family zero for each mesh and stored
every other family as a swap FROM that resolved material index.

1. **Case zero can be unresolvable while the case you want is fine.** `cp_fulgur` places
   `props_aquatic/pipe_256.mdl` at skins 1 and 12 of 15 and packs exactly those two textures — not
   family zero's. Family zero resolved to −1, a swap keyed on −1 was refused, and 19,274 triangles
   drew in the missing-material chequer on a map the game renders perfectly.
2. **The derived key need not be unique.** "What does texture X become at skin 1" has *two* answers
   the moment two meshes share texture X at family zero and differ above it. That fault was in the
   code the whole time with no symptom, and its symptom would have been a mesh wearing a
   neighbour's texture — plausible, and far harder to spot than magenta.

**Why it survives testing:** the degenerate case is overwhelmingly common. Almost every model has
one skin family, where the table is the identity and every reading agrees with every other. The
control map had zero failures before and after the fix. See [[most-of-a-decoder-is-untested]].

**How to apply:** key the lookup on the same input the engine keys on, and build the table for
**every** case including zero, so nothing has a special case. Ask "is case zero privileged here?" —
if the answer is only "it is the one that is always present", it is not privileged, it is assumed.
Related: [[ask-valve-before-designing-not-after]], [[a-constant-carries-no-scope]],
[[an-optimisation-is-not-a-skippable-departure]].
