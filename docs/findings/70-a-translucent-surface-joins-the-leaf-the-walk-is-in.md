# 70 — A translucent surface joins the leaf the walk is in when it reaches it

B261 was filed as "translucent world surfaces and translucent entities are not interleaved". By the time it
was picked up the interleave itself already existed — B426 (2026-09-29) had ported
`DrawTranslucentRenderables`' per-leaf walk as `TranslucentInterleave.Plan`, and `TranslucentWorldOrderRenderTests`
draws a translucent model behind world glass covered by it. The filing had simply never been closed. What B426
left was one line marked *interpolated*: which leaf a translucent surface is drawn with, and in what order
within it. B457's port of the world walk made that answerable, so this closes it.

## What the engine does — read from engine.dll x64, in disassembly

- `R_DrawLeaf` (`0x1800df9d0`) calls `0x1800e8820` FIRST, which appends the leaf to the world list (a
  `CUtlVector` at +0x538, count at +0x548) and opens its translucent chains.
- `R_DrawSurface` (`0x1800dfbb0`), for a TRANS (0x20) surface, calls the chain append `0x1800d1140` with id
  `[+0x548] − 1` (`0x1800dfbe7`..`0x1800dfbfb`) — the NEWEST list entry, not any leaf the surface names.
- `0x1800db7b0` does the same for a translucent displacement (`0x1800db8a0`, chain +0x330).
- `DrawTranslucentSurfaces` (`0x1800e4fd0`) gathers one entry's chain into an array and draws it from the last
  element down (`0x1800e5154`..`0x1800e52b1`), then walks the displacement chain forwards (`0x1800e5328`..)
  and draws it through `0x1800c61f0`.

So:

- a surface on a NODE, drawn by `R_RecursiveWorldNode` between its children, goes with the last leaf of the
  near subtree — a leaf that need not name it;
- a surface the walk does not draw — facing away from the eye, or already marked by an earlier leaf — is on no
  chain and is never drawn;
- within a leaf the order is the walk's, reversed.

## What the port had instead, and why it was wrong in a visible way

`VisibleWorld.BlendedByLeaf` walked each visible leaf's LEAFFACES backwards and gave each face to the first leaf
naming it. That differs from the engine three ways: node surfaces went with a leaf naming them rather than the
walk's current one; a translucent face turned away from the eye (and not `$nocull`) was still submitted, because
the LEAFFACES list knows nothing of facing — only the rasterizer's back-face cull, where on, stood between it and
the screen; and displacements were placed by box (`NearestRank`) rather than by the leaf whose list reached them.
The first and third change which leaf — and so which side of a translucent entity — a pane draws on.

## The fix

`VisibleWorld.Surfaces` — the walk B457 ported for the overlay queue — now records, beside each surface and each
displacement it reaches, the place of the leaf it was in. `BlendedByLeaf` reads that: per place, the reached
surfaces last first, then the reached displacements in order. One walk now feeds the opaque overlay queue and
the translucent chains, as one walk feeds both in the engine.

Evidence class: read in disassembly. Tests: `SurfaceOrderConformanceTests.BlendedRuns_FromTheFrontSide_…`
(synthetic, red before the change), `TranslucentLeafRunsMapTests` (cp_process: the runs are exactly the reached
translucent surfaces, once each).

**Still not modelled**, unchanged from B426: the four water sort groups (`0x1800e4fd0`'s outer loop over
`>>22 & 3`); every surface is group 0. A displacement whose box is out of view is left out where the engine draws
it — off screen either way.
