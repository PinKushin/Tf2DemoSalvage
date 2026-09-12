---
name: per-item-apis-hide-quadratic-reads
description: "A read-one-face API that takes the whole file re-decompresses its lumps every call; correct, testable, and quadratic over a map."
metadata: 
  node_type: memory
  type: project
  originSessionId: 9b3a8b35-1dc8-47b0-a320-73b01288f10c
  modified: 2026-09-09T03:54:44.135Z
---

`BspDisplacements.ReadTriangles(file, surface)` took the map's bytes and one face, so it parsed the
header and LZMA-decompressed both displacement lumps **on every call**. cp_process_final has 578
displacements, so a world build decompressed the same two lumps 578 times — about 830 ms, paid
again on every viewport resize. Full screen fires several resizes in a row and dropped to roughly
one frame a second.

`BspTerrain.Create(file)` reads the lumps once; the per-face overload delegates to it and stays,
because asking about one face is a real thing to want.

**Why:** nothing about the slow shape is visible at the call site or in a test. Each call is
correct and fast in isolation; only the loop is quadratic, and a per-item API invites the loop.

**How to apply:** when an API takes a whole container plus one item, check what it re-derives per
call before putting it in a loop. In this repo the same shape applies to any lump reader, and to
texture upload — geometry and textures were rebuilt together on resize when only geometry depends
on the camera. Note also what could NOT catch it: the full-screen UI test opens no demo, so it has
no map, so fast and slow predict the same observation. Wrong condition, not a missing assertion.
Related: [[fixtures-are-the-weak-point]], [[bsp-lumps-are-compressed]].

---

## `linq-is-a-test-tool` — never on a hot path, a judgement off one

**Never LINQ on a hot path. Off one, it is allowed when what it buys outweighs the cost — and it is
always a cost.** Tests may use it freely. Recorded as D107.

The owner, 2026-08-27, on a `string.Join` over a `Select` in the trace writer: *"linq can be slow if
its in a hot path, i dont like link in the program proper so performance stays high, its really only
a test thing in this project."* Then, correcting my first write-up which had made it an outright
ban: *"if its not on a hot path and the better things linq does overrides the downsides, i am open to
having it in the program, but it is a performance hit."*

**The overstatement was mine, and worth remembering as its own lesson.** I wrote the rule stricter
than its author intended AND argued in the entry against the two-standard approach he actually
holds — a rule written down more absolutely than it was given gets cited later as if it were. See
[[name-the-reading-you-picked]].

**How to apply**, as a judgement rather than a keyword ban:

1. Hot path? No, whatever the query buys.
2. Otherwise, does it genuinely read better or fail less often? If yes, take it knowing the cost. A
   wash goes to the loop.

**Hot here means** per-message decode and text, per-entity instancing and posing, per-batch drawing —
where B181, B189 and B191 all landed. Map load, asset resolution, config parsing and one-shot
reporting are not.

**The debt is unmeasured in the way that matters.** 45 of 356 files in `managed/` declare
`using System.Linq;`, but the useful number is how many do it on a hot path, and nobody has counted
that. Do not convert cold queries for tidiness — this project prefers
[[measure-the-output-not-the-capability]].
