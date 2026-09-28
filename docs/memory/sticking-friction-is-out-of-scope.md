---
name: sticking-friction-is-out-of-scope
description: IvpFrictionSystem::SolveTangentialPair's sticking branch is gated by a field this project's object model never writes — a stated divergence (D175), not an open port item
metadata:
  type: project
---

`IvpFrictionSystem::SolveTangentialPair`'s sticking dispatch is gated on a per-core pointer field
naming a persistent per-pair "sticking anchor" object. Five dedicated reads found only readers and
null-checks — never a write; its likely owner is `vphysics.dll`'s joint/constraint code, which this
project has never opened.

**Why:** this project ports no joint/constraint system, only body-against-world contact. Nothing ever
writes the field the sticking branch tests, so its condition is provably always false here — taking
only the non-sticking branch is COMPLETE, correct behaviour, not an approximation pending a future
port.

**How to apply:** don't chase this field's allocator again unless a body-against-body/joint port is
undertaken. See D175, `docs/HANDOFF.md` item 3. Related: [[valve-parity-is-the-first-principle]],
[[parity-is-the-search-not-the-defence]].
