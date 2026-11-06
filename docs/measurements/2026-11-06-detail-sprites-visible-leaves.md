# Detail sprites built only for the view's leaves (2026-11-06)

**Change:** `DetailSprites.Build` takes `visibleLeaf` (`WorldCulling.IsMainLeaf`) and skips every prop in a leaf
outside the world list, as Valve builds the sprite mesh only from the world list's translucent leaves
(`viewrender.cpp:4591-4639`, `ShouldDrawDetailObjectsInLeaf`, `clientleafsystem.cpp:1383`). Drawn output is
unchanged: the translucent pass already drew only world-list leaves' quads. A new cull clears `DetailBuiltFor`.

**Command:** `TF2VIEW_AUTOPLAY=1 run-exclusive.ps1 tf2demoview D:/measure-f12/f12.dem --tick 5000 --first-person --measure 20 +fps_max 0`,
debug build. Control is the same build with the predicate forced to null (`CanCull: false`), run back to back in the same session.
The figures are means of the 18 per-second samples. The tool prints no interval-wide breakdown by part.

| | frame mean | frames | setcamera | setcamera detail |
|---|---|---|---|---|
| control (every leaf) | 8.37 ms | 2390 | 1.44 ms | 0.57 ms |
| visible leaves only | 7.72 ms | 2592 | 1.24 ms | 0.39 ms |

**Verdict:** detail build −0.18 ms (−32%), setcamera −0.20 ms. Frame mean moved −0.65 ms, more than this change
alone can account for, so most of it is run-to-run noise. Not drawn, per D129: see the `tf2-measure` skill's list.
