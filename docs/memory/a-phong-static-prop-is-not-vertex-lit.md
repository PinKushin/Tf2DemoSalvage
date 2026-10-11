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
**Its state is its own, too.** Cube: computed, 162 `R_LightVec` rays (`FUN_1801b74f0`, `r_radiosity` 4), not the leaf's
(`StaticPropAmbient`). Sun: leaf `flags & 5`, then one `MASK_OPAQUE` (`0x4081`) world-only ray to `SURF_SKY`
(`FUN_1801b60a0`, `0x1801b9570`). `MapLevel.StrikesSky` traced `MASK_SOLID` and its comment said `MASK_OPAQUE`: a
lighting point inside a non-opaque brush started solid, and "the dome is not sunlit" got reported from the viewer's own
failed trace. An absence from our instrument needs a control (`prop-light <map> <model> <open-air point>`).
