---
name: demo-gototick-relative-is-the-second-argument
description: "TF2's demo_gototick takes <tick> [relative] [pause]; a two-argument call binds the second argument to relative, not pause, and seeks past wherever the demo already was"
metadata:
  type: reference
---

**`demo_gototick <tick> <relative>` is a relative seek, not tick-plus-pause.** The engine's own
handler (decompiled, `engine.dll` x64, `FUN_180075460`) binds `argv[2]` to `relative` and `argv[3]` to
`pause`, exactly the order its own syntax message states: `demo_gototick <tick> [relative] [pause]`.

**Confirmed by an owner golden capture that looked wrong until this was read.** `demo_gototick 51093
1` sought 51093 ticks forward of wherever the demo already was — `relative` true, `pause` at its
default — not to absolute tick 51093. The resulting capture showed a different, unrelated part of the
map and was first read as a real map-geometry divergence before the argument order was checked.

**The correct form for a golden comparison against a specific tick** is `demo_gototick <tick> 0 1` —
tick, relative OFF, pause ON. Always use all three arguments explicitly; never rely on a default for
the middle one.
