---
name: sticking-friction-is-out-of-scope
description: IvpFrictionSystem::SolveTangentialPair's sticking branch is gated by a field this project's object model never writes — a stated divergence (D175), not an open port item
metadata:
  type: project
---

`IvpFrictionSystem::SolveTangentialPair`'s sticking dispatch (`FUN_180085a80`) is gated on a per-core pointer
field (informally `core+0x58`) naming a persistent, per-pair "sticking anchor" object. Five dedicated reads across
one session found only readers and null-checks of this field — never a write — and its likely owner is
`vphysics.dll`'s joint/constraint code, which this project has never opened.

**Why: this project ports no joint/constraint system and implements only body-against-world contact**
(`IvpFrictionLinking.LinkContactByCore` throws for any other pairing). Nothing in this codebase, ever, writes the
field the sticking branch tests — so its dispatch condition is provably always false here, not merely unported.
`IvpTangentialSolve.SolveContact`/`SolveOncePerPair` taking only the non-sticking branch is therefore the
COMPLETE, correct behaviour for every contact this project's simulation can produce, not an approximation
pending a future port.

**How to apply:** do not chase this field's allocator again unless a body-against-body/joint/constraint port is
undertaken — that would need to open the joint/constraint side of the binary first and re-open D175. See
`docs/DECISIONS.md` D175 for the full reasoning, and `docs/HANDOFF.md` item 3 for the exact decompile citation
(`FUN_180085a80`, obtained in full after four attempts at an untruncated read).

Related: [[valve-parity-is-the-first-principle]], [[parity-is-the-search-not-the-defence]].
