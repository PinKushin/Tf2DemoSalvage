---
name: settle-a-constant-in-the-disassembly
description: A claim about WHICH memory a value came from is settled in the disassembly, never in decompiled C — Ghidra invents local names and reuses them for unrelated values. DisasmWithData.java prints every memory operand's four lanes on the instruction that reads it.
metadata:
  type: feedback
---

**Two wrong conclusions in one session, 2026-09-06/07, and both were made in the DECOMPILED C rather
than in the disassembly.**

- **`DAT_1800eea1c` was taken for π** because its neighbour `DAT_1800eea18` genuinely is 2π — already
  established elsewhere in the same document as the 2π disable test. It is `1.0e-16`, an is-it-zero
  epsilon. A whole conclusion was written up and committed off it: that a TF2 ragdoll never reaches
  the general constraint path. It does.
- **`uVar6` was read as "the mask from `_UNK_1800ff104`"** in one expression and as a comparison
  result three lines later. Ghidra reuses a local name for unrelated SSA values, and nothing on the
  page says which is which.

**The owner asked how to fix it, so it is a tool and a trigger rather than an intention.**

## The tool

`D:\ghidra-proj\scripts\DisasmWithData.java` — disassembly with every memory operand resolved to its
four lanes, printed on the same line as the instruction that reads it:

```
1800386df  MOVAPS XMM4,xmmword ptr [0x1800ff130]   ; 1800ff130 = {00000000/0.0, …, ffffffff/NaN}
1800387f5  MULPS  XMM7,xmmword ptr [0x180124f70]   ; 180124f70 = {3f800000/1.0, 1.0, 1.0, 3f000000/0.5}
```

```bash
JAVA_HOME="…jdk-21…" "/d/ghidra_12.1.2_PUBLIC/support/analyzeHeadless.bat" "D:\ghidra-proj" \
  tf2vphysics -process vphysics.dll -noanalysis \
  -scriptPath "D:\ghidra-proj\scripts" -postScript DisasmWithData.java <addressHex>
```

**`DisasmAt.java` already printed the instructions and was not enough.** It prints Ghidra's symbol —
`DAT_1800ff130` — and not its contents, so settling a constant still meant a separate `DumpFloats`
run. A separate run is exactly the step that gets skipped in favour of remembering, which is how the
π mistake happened. Thirteen lines of output now carry every constant a whole function touches.

## The rule

**Any claim about which memory a value came from, or what a constant is, is settled in the
disassembly.** The address is IN the instruction, so it cannot alias. A decompiler local cannot
carry that claim, because the name is Ghidra's invention.

Decompiled C is still the right thing to read for CONTROL FLOW and for the shape of an expression.
The split is: shape from the decompiler, identity from the disassembly.

## The trigger, so it is not a resolution to forget

**The tell is a sentence naming a `DAT_` or `_UNK_` symbol, or saying "the mask from X".** That is
the moment to run the script — not after the paragraph is written.

A second tell, weaker but earlier: reaching for a value because it is *adjacent* to one already
known. Adjacency is the case where dumping feels most redundant and is most likely to be wrong —
`1800eea18` and `1800eea1c` are four bytes apart and are 2π and 1e-16.

## It paid immediately

The question it was built for — what fills `geom+0x2d0`, the vector whose lane 0 multiplies every
ragdoll limit clamp — took two calls and no guessing:

```
1800386df  MOVAPS XMM4,[0x1800ff130]      XMM4 = {0,0,0,~0}
1800387c3  MOVAPS XMM13,XMM4
180038816  MOVUPS [R15+0x110],XMM7        geom+0x110 = the bisector m
180038822  ANDPS  XMM4,XMM7               XMM4 = {0,0,0, m.w}
180038825  ANDNPS XMM13,XMM0              XMM13 = {XMM0.x, XMM0.y, XMM0.z, 0}
18003882e  ORPS   XMM13,XMM4              XMM13 = {XMM0.x, XMM0.y, XMM0.z, m.w}
18003884f  MOVUPS [R15+0x2d0],XMM13
```

Three scalars inserted lane by lane, plus the bisector's fourth component. The decompiler had
suggested that shape; the disassembly established it.

Related: [[an-empty-search-needs-a-control]] and [[print-a-value-somebody-can-recognise]] are the
same discipline pointed at instruments rather than at reading — report the value that was USED,
carried from where it was produced, never one recalled or recomputed by a second route.
