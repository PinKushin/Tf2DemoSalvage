---
name: the-jit-picks-the-nan-destination
description: A C# + or * lets the JIT choose which operand SSE keeps when two NaNs meet; pin it with Addsd/Mulsd, never license it away
metadata:
  type: feedback
---

When two NaNs meet in `ADDSD`/`MULSD`, SSE keeps the DESTINATION operand's NaN. For a C# `+`/`*`, the
JIT picks which operand is the destination — not a language limit. `IvpMath.Addsd`/`Mulsd`/`Addss`/
`Mulss` exist to pin it, reading destination-first per instruction from the disassembly.

**Why:** a NaN sign-bit difference was first filed as something C# "cannot" avoid, relaxing the
replay to match any NaN with any NaN. Under Valve parity a divergence is fixed, not licensed.

**How to apply:** in a bit-exact port, route commutative float ops through destination-first helpers.
Prove with a NaN-seeded sweep, sabotage one operand order to confirm the sweep can tell. Before
calling any difference unavoidable, ask what would pin it.

**An operand-order sabotage that reddens nothing may be dead, not untested** — a NaN's payload is
invisible to a comparison, so where a sum only feeds a comparison or solver result, its destination is
unobservable at any output. Trace each path to an output before hunting for inputs; still write the
destination the binary has.
