---
name: per-item-apis-hide-quadratic-reads
description: "A read-one-face API that takes the whole file re-decompresses its lumps every call; correct, testable, and quadratic over a map."
metadata: 
  node_type: memory
  type: project
  originSessionId: 9b3a8b35-1dc8-47b0-a320-73b01288f10c
  modified: 2026-08-12T23:47:10.135Z
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
Related: [[real-data-hides-bugs-small-inputs-expose]], [[bsp-lumps-are-compressed]].
