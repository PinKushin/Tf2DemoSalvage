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

## Beam strips: last-list shortcut in `SpriteStripBatches.Add` (same day)

The new `camera particles/build/*` and `…/entities/*` parts put f12's particle build at 1.05 ms: systems 0.38, entities 0.59,
of which beams 0.54 (211 beams a frame, one quad each). Inside the beam draw, the halo path was 0.20 ms, `strips.Add` 0.13
and the corner build 0.05. Passing the material by reference and reading the corners through a span changed nothing
(0.133 ms), so that change was dropped. Reusing the last list when the path (by reference) and pass repeat took `strips.Add` to 0.098 ms and
beams to 0.57 ms, so about −0.035 ms a frame.

**Instrument note:** three runs in between measured a stale binary. Every build since 18:42 failed on Sonar errors, but the
command chained the run after `grep | head`, which always succeeds. Those runs' figures are discarded.
Gate a run on `0 Error(s)` in the build log, never on a filtered grep's exit code.

## The profiler's struct-copy lead, falsified (same day)

`cpu-trace` on a fresh `dotnet-trace` of HEAD (f12, game thread 30532, `under:ViewerSession.RenderFrame`, the last 25 s)
put `Buffer.BulkMoveWithWriteBarrier` at 22.9% inclusive. `callers:` spread it over dozens of sites at 0.2–1.5% each,
and `SceneProp.get_Pose` (a copy of the 48-field `ScenePose`) came to about 2.5%. **Prediction:** binding `Pose` to a
field, which copies nothing on read, saves about 0.18 ms of a 7.2 ms frame.

| run | frames | frame mean | camera |
|---|---|---|---|
| B1 (field) | 2758 | 7.25 | 5.63 |
| B2 (field) | 2762 | 7.24 | 5.77 |
| A1 (property, HEAD) | 2807 | 7.13 | 5.67 |
| A2 (property, HEAD) | 2335 | 8.57 | 6.77 (load) |

**Falsified:** no change. The EventPipe sampler lands on safepoints, and this copy routine is one, so its share is the
sampler's bias, not cost. The experiment is reverted. **Rule:** a `cpu-trace` inclusive share is a lead until a `--measure` A/B
moves by the predicted amount.
