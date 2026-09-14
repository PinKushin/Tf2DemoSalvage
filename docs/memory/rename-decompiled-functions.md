---
name: rename-decompiled-functions
description: D174 — once a Ghidra function is read, rename it for what it does (Type::Member of its port); cite names, not FUN_ addresses; old references convert when their file is edited.
metadata:
  type: feedback
---

**The owner, 2026-09-14:** *"rename the functions to tell us what they do, rather than having generic fun_ names"* — a human
reverse engineer does not keep the decompiler's names, because they are harder to read.

**Why:** a `FUN_` address means nothing to whoever reads it. Either a note somewhere says what it does, or it is re-derived every
time; a name makes both unnecessary, in the decompiler's output as much as in the repo.

**How to apply:** after reading a function, rename it in the Ghidra project with `mcp__ghidra__rename_function_by_address`, then
`mcp__ghidra__save_program`. Name a ported function `Type::Member` after the port member it became; name an unported one by what
it writes ([[an-unused-method-may-be-the-engines]]), under IVP's class where the evidence names one. Never name an unread function.
New code and docs write the name, with the address beside it only where instructions must be found. Convert a file's old `FUN_`
references when you next edit that file, not by a scripted pass. Full entry: D174.
