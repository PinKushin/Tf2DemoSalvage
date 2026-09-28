---
name: rename-decompiled-functions
description: D174, reversed 2026-09-14 — renaming is no longer mandatory on every newly-read function (token cost, no script shortcut exists); existing renames stay, case-by-case renaming still fine.
metadata:
  type: feedback
---

Owner: *"rename the functions to tell us what they do, rather than having generic fun_ names"* —
clarified same day: naming every NEW function as it's read, not just renaming what's already read.
Then: *"i want the reverseing to look like it was done by a pro"* — names live in the disassembler's
database (Ghidra), variables/parameters/fields/vtables named too as understanding grows, names that
can be recovered (library signatures, assertion source paths) beat invented ones.

**How to apply (while active):** after reading a function, rename it in the Ghidra project and save.
Name a ported function after the port member it became; name an unported one by what it WRITES
([[an-unused-method-may-be-the-engines]]). Never name an unread function. Convert a file's old
references when you next edit that file, not by a scripted pass.

**Reversed 2026-09-14.** Owner: *"that shit took a lot of tokens, and it still needs a lot... unless
the real reason it took so many tokens was it didnt script but the rename cant really be scripted to
save tokens."* Correct — the rename call is cheap, but naming requires the same close read a port
needs anyway, doubling down on an already token-heavy activity during a budget-constrained week.

**No longer the default.** Existing renames stay; renaming a specific function is still fine with a
concrete reason (it recurs a lot, it's about to be cited repeatedly) — just not automatic. Full
entry: D174 (with its reversal).
