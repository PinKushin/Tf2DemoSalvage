---
name: name-the-cvar-before-filing-a-branch-unused
description: "A disassembled branch gated on an unnamed DAT_ cvar was filed as 'not the prop's own lighting' and left unported; it was the static-prop DECAL light, r_decalstaticprops default 1 (B642). Name the cvar and the struct it writes first."
metadata:
  type: feedback
---

**Before filing a disassembled branch as unused or "someone else's", name the cvar that gates it and the struct it
writes.** Findings 67 read `engine.dll` `0x1800f1bd0`'s branch under `DAT_18069c368` as "a second combined state …
not the prop's own lighting" and did not port it. Weeks later, B642 needed the light a static prop's decals take. It
was that branch. The cvar was `r_decalstaticprops`, default `"1"`, so the branch runs on every frame. Its target
`param_8` was the `DrawModelInfo_t`, so `+0x44`/`+0x8c` were the draw's ambient cube and local lights.

**How to name them cheaply:** the ConVar object sits 0x38 before the `m_pParent` pointer the code reads
(`*(DAT + 0x58)` is `m_nValue`). A `DATA` cross-reference to the object leads to its static constructor, which passes
the name, the default and the help string. For a struct parameter, read the caller that builds it on its stack and
count the fields against the published header.

**Why it matters:** a branch read as dead stays dead in every later read that trusts the finding. Here it hid the
answer to an open question that was then filed as needing a fresh decompile.

Related: [[instrument-bugs-outnumber-decoder-bugs]], [[an-unused-method-may-be-the-engines]].
