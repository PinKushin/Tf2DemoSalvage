---
name: rename-decompiled-functions
description: D174, reversed 2026-09-14 — renaming is no longer mandatory on every newly-read function (token cost, no script shortcut exists); existing renames stay, case-by-case renaming still fine.
metadata:
  type: feedback
---

**The owner, 2026-09-14:** *"rename the functions to tell us what they do, rather than having generic fun_ names"* — a human
reverse engineer does not keep the decompiler's names, because they are harder to read.

**The owner, clarifying the same day:** *"the big thing was to have any new ones be done like a human normally does, just so i
can follow along better, and maybe even actually help if i knoww what a function is suppose to do"* — renaming what was already
read is fine but was not the ask; naming every NEW function as it is read is.

**And then:** *"yea lets do it that way from here on out then, i want the reverseing to look like it was done by a pro"* —
after being told how professionals work: names live in the disassembler's database (Ghidra `L`), not in find-and-replaced text;
variables, parameters, struct fields, globals and vtables are named too, as understanding grows, a half-understood function under a
tentative name; names that can be recovered (library signatures, RTTI, assertion source paths) beat invented ones. `vphysics.dll`
has no RTTI class names, but its assertion paths name IVP's source files (`ivp_collision\ivp_mindist_recursive.cxx`).

**Why:** a `FUN_` address means nothing to whoever reads it. Either a note somewhere says what it does, or it is re-derived every
time; a name makes both unnecessary, in the decompiler's output as much as in the repo.

**How to apply:** after reading a function, rename it in the Ghidra project with `mcp__ghidra__rename_function_by_address`, then
`mcp__ghidra__save_program`. Name a ported function `Type::Member` after the port member it became; name an unported one by what
it writes ([[an-unused-method-may-be-the-engines]]), under IVP's class where the evidence names one. Never name an unread function.
New code and docs write the name, with the address beside it only where instructions must be found. Convert a file's old `FUN_`
references when you next edit that file, not by a scripted pass.

**Reversed 2026-09-14.** The owner: *"that shit took a lot of tokens, and it still needs a lot... unless the real reason it took
so many tokens was it didnt script but the rename cant really be scripted to save tokens."* Correct — the rename call itself is
cheap, but naming requires the same close read a port needs anyway, so mandating a rename on every function doubles down on an
already token-heavy activity during a budget-constrained week. **No longer the default.** Existing renames stay; renaming a
specific function is still fine when there's a concrete reason (it recurs a lot, the address is actively confusing, it's about
to be cited repeatedly) — just not automatic anymore. Full entry: D174 (with its reversal).
