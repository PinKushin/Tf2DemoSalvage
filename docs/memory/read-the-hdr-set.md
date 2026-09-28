---
name: read-the-hdr-set
description: TF2's default is mat_hdr_level 2, so every lighting input comes from the HDR set; "HDR washes out" was never the data
metadata:
  type: project
---

At TF2's default HDR level, the engine reads the HDR half of every paired lighting input (lightmaps,
leaf ambient, world lights, cubemaps, static prop lighting) — each traced to `engine.dll`/
`materialsystem.dll`.

**Why:** this project had preferred LDR for months on the claim HDR "washes out" — measured, both
lightmap sets and all 652 static-prop lighting pairs are identical; the wash-out came from applying
LDR's brightness scale while reading the HDR data through the LDR offsets. Owner's rule: defaults are
the game's highest quality ([[defaults-are-highest-quality]]).

**How to apply:** a new lighting input gets its HDR lump/file first, LDR only as the engine's own
fallback. Before claiming two sets differ, run the relevant probe. Linear data (HDR bakes, bump maps,
masks) is never uploaded sRGB.
