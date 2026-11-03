---
name: a-decompile-drops-the-float-return
description: "Ghidra's decompile shows a function that returns a float in xmm0 as `void`; read the disassembly (XORPS XMM0 / MOVAPS XMM0,.. before RET) before concluding \"returns nothing\" (B666)."
metadata:
  node_type: memory
  type: feedback
  originSessionId: a7745234-ed21-42a5-a0c8-7c85003d2f62
  modified: 2026-11-03T02:14:09.207Z
---

Ghidra's decompiler typed `FUN_1801b8e20` and `FUN_1801b9570` (engine.dll light attenuation) as `void`; both return the light's ratio in XMM0, 0.0 when the light is rejected. B453 and B665 argued from "it returns nothing, so the trace is only debug drawing" and left the engine's per-light visibility trace unported until B666.

**Why:** the decompile of a caller used the value (`fVar11 = FUN_1801b9570(...)`, `if (0.0 < fVar11)`) while the callee's own decompile had `return;` with no value. That mismatch was the tell.

**How to apply:** when a callee decompiles to `void` but its caller branches on its result, or the callee ends in `XORPS XMM0,XMM0` / `MOVAPS XMM0,..` before `RET`, read the disassembly's return paths before concluding anything about what it decides. Related: [[an-unanalysed-ghidra-program-shows-no-xrefs]], [[a-valve-comment-is-not-its-code]].
