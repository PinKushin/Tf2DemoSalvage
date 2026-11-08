---
name: a-box-leaf-walk-needs-bounds-in-the-fixture
description: BspLeafTree.EnumerateLeavesInBox tests node and leaf boxes; a hand-built tree with zero bounds returns no leaves, while the swept walk ignores them
metadata:
  type: project
---

`BspLeafTree.EnumerateLeavesInBox` rejects a node or leaf whose stored box (`dnode_t` mins/maxs at bytes 12 and 18,
`dleaf_t`'s at 8 and 14, shorts) does not overlap the query box. `EnumerateLeavesAlongSweptBox` does not read those
boxes. So the one-node `Wall()` tree used by the shadow tests (`new byte[32]` node, `new byte[64]` leaves) works for
the swept walk and returns NOTHING from the box walk.

Found 2026-11-08 (B702 brush receivers): every "receives" test came back empty at once, which looked like a production
bug. The tell was that every positive case failed together while every negative case passed.

**Why:** the fixture was copied from a test that only used the swept walk.

**How to apply:** a hand-built tree for any box query needs real bounds. `ShadowBrushReceiversConformanceTests.Wall()`
writes ±4096 into the node and both leaves. When a whole family of positive tests fails at once on a synthetic BSP,
check the fixture's bounds before the code. See [[fixtures-are-the-weak-point]] and
[[instrument-bugs-outnumber-decoder-bugs]].
