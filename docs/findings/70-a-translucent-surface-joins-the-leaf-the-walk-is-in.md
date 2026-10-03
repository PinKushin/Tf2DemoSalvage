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

A displacement whose box is out of view is left out where the engine draws it — off screen either way.

## The water sort groups

B426 and B457 both filed the same omission: every surface treated as one group. Read in disassembly, engine.dll x64:

- **Marking**, `0x180100b60`, called from `0x180104530` at load with the world's head node: it recurses the tree,
  RETURNS at a leaf whose contents are exactly 1 (CONTENTS_SOLID alone, `0x180100b6b`), and ORs `0x20000` into a
  leaf's surfaces when its water data ID (+0x42) is not −1, `0x40000` otherwise — skipping surfaces with `0x10800`
  (displacement, water surface) — then the same bit into each displacement on the leaf's displacement list
  (`0x180100bee`..`0x180100c3f`).
- **Assignment**, `0x180104530` (only at DX level 80 and up, which is every modern client): `0x10000` → `0xc00000`,
  group 3; `0x20000` and `0x40000` both → 2; `0x20000` → 1; `0x40000` → 0 (`0x1801045b5`..`0x1801045f4`). A surface
  with neither prints a warning (at most ten) and stays 0. These are `MAT_SORT_GROUP_*`, `ivrenderview.h:50`.
- **The opaque world**, `0x1800e5e10`: a counter from 3 down to 0 indexes the table `0x18038ea98` = {0, 1, 2, 3}
  with the draw flag `8 >> n`, so groups draw **3, 2, 1, 0** — each its displacement chain (`0x1800e3a90`), brush
  chains (`0x1800e27d0`), overlay queue (`0x1800da3a0`) and `RenderOverlays`, then world decals (`0x1801158d0`).
- **The translucent world**, `0x1800e4fd0`: the same table walked FORWARDS with flag `1 << n`, so a leaf's groups
  draw **0, 1, 2, 3**, each its surfaces last first then its displacements and their overlays (`0x1800c61f0`, once
  per group).

The opposite directions are the point: opaque from the water surface down, translucent from above the water up.

Ported: `VisibleWorld.SortGroup` (the marking and assignment, `0x10000` read as texinfo SURF_WARP as B457 already
does — *interpolated*), `OverlayRenderLists.Order`'s per-group batches, `BlendedByLeaf`'s per-group runs with
`TranslucentLeafRuns.RunGroup`, and `ForTranslucentLeaves` flushing displacement overlays at each group's end.

**Still ours.** The main view draws every group in one `DrawWorldLists` call, as the client's `CSimpleWorldView`
does when no water is in view; the water views that split groups across reflection and refraction passes are not
modelled. World decals are not drawn per group, and the opaque surfaces are not either — the latter is invisible
under the depth test.

## The map test needed a chosen eye

The first cp_process assertion compared sets, so two sabotages passed it: dropping place 0 (the eye's leaf held no
glass) and filing node surfaces one leaf late (a set does not care which leaf). The second test searches for an eye
in front of a translucent leaf face whose view files glass at place 0 AND on a node — measured, the first candidate
qualifies: face 127, eye (−864, −2760, 808), 19 filed, 1 at place 0, 15 on nodes — and asserts each surface's leaf:
a leaf face must be listed by its leaf, a node face's leaf must lie under the node's child on the eye's side. Both
sabotages now redden it.
