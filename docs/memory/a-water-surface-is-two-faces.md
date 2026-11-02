---
name: a-water-surface-is-two-faces
description: vbsp writes a water surface as two coplanar faces, the downward one with $bottommaterial; only the hardware cull separates them, and this renderer draws the world two-sided (B657).
metadata:
  type: project
---

**A water volume's surface is two faces in one plane.** The upward face carries the surface material. vbsp writes the
downward face with that material's `$bottommaterial` (`utils/vbsp/faces.cpp:1255-1289`). The engine's world walk keeps
both, because a surface in a water leaf skips the plane-side test (`VisibleWorld.Surfaces`, engine.dll `0x1800e0600`).
The `Water` shader's hardware cull is what hides the far one. It is off only in the editor (`water.cpp:393-397`).

**This renderer draws world batches two-sided** (`_bothSides`), so any pair of coplanar opposed faces z-fights, and the
one that draws first wins. On ctf_2fort that was `water_2fort_beneath`. From above, the whole river was the beneath's
grey-green fog colour, 20-50 levels off TF2's brown. `WaterBatch` now culls back faces.

**Why it took a session:** every first suspect was a shader quantity, such as the reflection, the cubemap, the fog or
the LOD. Every material constant was right, but for the wrong material. The instrument that settled it was a scratch
build that made the water output its own fog colour. One flat colour over the whole surface named which material drew.

**How to apply:** when a world surface looks like the wrong material, first ask whether a coplanar back face is drawing.
Check which material's constants actually reached the pixel before reading any shader branch.
