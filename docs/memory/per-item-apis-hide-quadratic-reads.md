---
name: per-item-apis-hide-quadratic-reads
description: "A read-one-face API that takes the whole file re-decompresses its lumps every call; correct, testable, and quadratic over a map."
metadata: 
  node_type: memory
  type: project
  originSessionId: 9b3a8b35-1dc8-47b0-a320-73b01288f10c
  modified: 2026-09-09T03:54:44.135Z
---

`BspDisplacements.ReadTriangles(file, surface)` took the whole map's bytes and one face, so it
parsed the header and LZMA-decompressed both displacement lumps on EVERY call. cp_process_final has
578 displacements — decompressed the same lumps 578 times per world build (~830ms), paid again on
every resize; full screen dropped to ~1fps.

Fix: `BspTerrain.Create(file)` reads the lumps once; the per-face overload delegates and stays.

**Why:** nothing about the slow shape is visible at the call site or in a test — each call is correct
and fast in isolation; only the loop is quadratic.

**How to apply:** when an API takes a whole container plus one item, check what it re-derives per
call before looping it. Same shape applies to texture upload rebuilt on resize when only geometry
depends on the camera. Note what could NOT catch it: a UI test opening no demo has no map, so fast and
slow predict the same observation (wrong condition, not a missing assertion). Related:
[[fixtures-are-the-weak-point]], [[bsp-lumps-are-compressed]].

---

## `linq-is-a-test-tool` — never on a hot path, a judgement off one

**Never LINQ on a hot path. Off one, allowed when what it buys outweighs the cost.** Tests may use it
freely. D107. Owner: *"linq can be slow if its in a hot path, i dont like link in the program proper
so performance stays high, its really only a test thing in this project"* — then, correcting an
earlier overstatement that banned it outright: *"if its not on a hot path and the better things linq
does overrides the downsides, i am open to having it in the program, but it is a performance hit."*

**How to apply, as judgement not a keyword ban:**
1. Hot path? No, whatever the query buys.
2. Otherwise, does it genuinely read better or fail less? If yes, take it knowing the cost; a wash
   goes to the loop.

Hot means per-message decode, per-entity posing, per-batch drawing — where B181, B189 and B191 all
landed. Map load, asset resolution, config parsing are not. Do not convert cold queries for tidiness —
see [[measure-the-output-not-the-capability]].
