---
name: the-jit-picks-the-nan-destination
description: A C# + or * lets the JIT choose which operand SSE keeps when two NaNs meet; pin it with Addsd/Mulsd, never license it away
metadata:
  type: feedback
---

When two NaNs meet in `ADDSD`/`MULSD`, SSE keeps the destination operand's NaN. For a C# `+` or `*` the JIT picks which operand is the destination — even at Tier0 it emitted `s·dw + w` with `w` first where vphysics.dll has the product (`DOTNET_JitDisasm` showed it). It is NOT a language limit: `Addsd(d, s) = IsNaN(d) ? d + d : d + s` (and `Mulsd`) returns the destination's NaN, quieted, whatever order the JIT emits. `IvpLinearSystem.Addsd`/`Mulsd` exist for this.

**Why:** I first filed a NaN sign bit as a difference C# "cannot" avoid and relaxed the replay to match any NaN with any NaN. The owner asked whether `unsafe` or anything could change it; the honest answer was yes, and under Valve parity a divergence is fixed, not licensed.

**How to apply:** In a bit-exact port, route every commutative floating-point op through destination-first helpers, with the destination read per instruction from the disassembly — it is not uniform (unrolled SSE blocks swap operands in some lanes; see [[valve-parity-is-the-first-principle]]). Prove it with a NaN-seeded sweep, and sabotage one operand order to show the sweep can tell. Before calling any difference unavoidable, ask what would pin it. See also [[most-of-a-decoder-is-untested]] for finding inputs with the oracle against a sabotaged port.
