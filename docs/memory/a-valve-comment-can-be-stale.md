---
name: a-valve-comment-can-be-stale
description: "An SDK comment naming a constant is a claim about the code, not about the shipped game — check the data."
metadata: 
  node_type: memory
  type: feedback
  originSessionId: 4774a88b-811c-40bb-9c79-9b22dc0a4474
  modified: 2026-09-06T13:48:16.509Z
---

`gamebspfile.h` says *"All detail prop sprites must lie in the material detail/detailsprites"*. That
sentence was quoted in this project's own source and used to hardcode the material. **All 234
installed TF2 maps override it** through `worldspawn`'s `detailmaterial`, and only 49 name that one:
`_trainyard` on 42 maps, `_2fort` on 38, `_sawmill` on 32. Both reference maps were wrong —
`koth_harvest_final` wants `_harvest`, `cp_granary` wants `_granary` — so every grass capture ever
taken here used the wrong texture (B364).

**Why:** the comment is true per map — one sheet at a time, dictionary entries are sub-rectangles of
it, nothing in the lump names a material. Every structural claim holds; only the literal NAME is
wrong, and the name is the half that gets transcribed into a constant. The override lives in a
different file (`detailobjectsystem.cpp:1516`) from the comment (`public/gamebspfile.h`), so reading
the struct never meets it.

**How to apply:** when an SDK comment names a specific constant — a material, a path, a limit —
treat that as a lead, not a fact, and ask the shipped data how many maps or models actually use it.
The symptom is invisible by construction: the wrong sheet still draws grass, the wrong path still
resolves, and the code is doing exactly what the comment promised.

See [[shipped-data-settles-what-closed-code-cannot]], [[a-default-is-not-a-constant]] and
[[the-base-is-not-the-behaviour]] — same shape, different source.
