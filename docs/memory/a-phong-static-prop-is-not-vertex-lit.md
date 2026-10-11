---
name: a-phong-static-prop-is-not-vertex-lit
description: "Any $phong or $bumpmap material flags its model USES_BUMPMAPPING at load; the engine then refuses that static prop its .vhv colours and lights it per draw (B514)."
metadata:
  type: project
---

**A plausible `.vhv` does not mean the engine draws it.** `studiorender.dll FUN_180057da0` ORs
`STUDIOHDR_FLAGS_USES_BUMPMAPPING` (0x80) into a model at load for any material with `$phong` nonzero or a `$bumpmap`
needing a tangent space. `engine.dll FUN_1800eb1a0` refuses such a model static lighting (DX level >= 80): no colour
mesh, lighting state `FUN_1801ba590` flags 7 (cube + style-0 + styled + dlights). The skin shader could not read a
baked colour anyway and is always half-Lambert (`skin_ps20b.fxc:220-222`). 394 of cp_process_f12's 1,353 placements.

**How to apply:** before trusting "this prop is baked", check `StaticPropVertexLighting.RefusesColourMesh(frames.StudioFlags)`.
When a reading says "the draw does X with input Y", read the CALLER that decides Y — B426 read the model draw
(`0x1800f1bd0`) and not the function one above it, and the conclusion stood from 2026-09-28 to 2026-10-10.
The dome's brightness in TF2 was a mapper's `light` beside it, not the sun: ask which light before assuming the sun.
