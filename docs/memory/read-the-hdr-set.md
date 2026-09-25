---
name: read-the-hdr-set
description: TF2's default is mat_hdr_level 2, so every lighting input comes from the HDR set; "HDR washes out" was never the data
metadata:
  type: project
---

At TF2's default HDR level the engine reads the HDR half of every paired input: lightmaps from lumps
58/53, leaf ambient from 55/51, world lights from 54, cubemaps from `<name>.hdr.vtf`, static prop
lighting from `sp_hdr_<n>.vhv` when lump 59 sets bit 2. Each choice was read out of `engine.dll` /
`materialsystem.dll` on 2026-09-25 (functions renamed in `tf2enginex64.gpr` / `tf2materialsystem.gpr`).

**Why:** the project had preferred LDR for months on the claim that HDR "washes out" and "is authored
brighter". Measured on koth_harvest_final, both lightmap lumps and all 652 `.vhv` pairs are identical;
the wash-out came from applying LDR's x2 and reading lump 53 through lump 7's offsets. The owner's rule
is that defaults are the game's highest quality ([[defaults-are-highest-quality]]).

**How to apply:** a new lighting input gets its HDR lump or file first, LDR only as the engine's own
fallback. Before claiming two sets differ, run the `lighting-lumps` or `vhv-pair` probe. Linear data
(HDR bakes, lightmaps, bump maps, masks) is never uploaded sRGB — `SamplerSrgb` holds Valve's per-slot table.
