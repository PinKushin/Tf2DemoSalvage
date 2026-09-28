---
name: prove-the-equivalence-before-building-it
description: Two engine code paths that look different can be arithmetically identical for the models that exist — measure the inputs before implementing the second one.
metadata: 
  node_type: memory
  type: project
  originSessionId: 1530d8fa-540e-408a-bb73-09b13bdff510
  modified: 2026-09-09T03:55:03.234Z
---

**Before implementing a second engine path that differs from one already implemented, ask what the
difference needs to be OBSERVABLE — then measure whether that exists.**

B349: Valve writes a weapon's barrel bone two ways — a world path assigns the whole quaternion, a
viewmodel path reads existing angles and replaces one component. Looked like a divergence. **It's
not, for any model TF2 ships:** the relevant bone has an identity bind rotation in every model, and no
animation moves it — only ONE bone moves, at a different index. So read-modify-write on identity
produces the same output as the flat assign.

**Why this matters more than saving work:** a second implementation of an equivalent path is more
code to keep in step, another place to drift, and a future reader can't tell "deliberately
duplicated" from "accidentally divergent". Proving equality and recording it is the smaller artefact.

**How to apply:**
1. Write the difference as a condition — "these differ only when X".
2. Measure whether X occurs in the shipped data (usually one probe run).
3. If not, record it as a MEASURED non-divergence with what would falsify it.
4. Make the measurement repeatable (a probe reporting bind rotations and tracked bones for any
   weapon, rather than re-derived each time).

Related: [[the-base-is-not-the-behaviour]], [[most-of-a-decoder-is-untested]],
[[filing-a-divergence-is-not-fixing-it]], [[valve-parity-is-the-first-principle]].
