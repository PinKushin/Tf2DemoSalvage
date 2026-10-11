---
name: the-engine-rewrites-data-between-the-file-and-the-shader
description: "A correct file read plus a line-for-line shader port is not parity; the material system renormalizes bumped lightmaps on upload (B514 part 4)."
metadata:
  type: project
---

**Right data and Valve's own shader still drew bump-lit faces at 0.63 of TF2.** Between vrad's bytes and the pixel
shader sits a closed step: the bumped lightmap page writer (`materialsystem.dll FUN_1800249c0`, read in disassembly)
rescales the three directional sets, per luxel and per channel, so their average equals the flat set
(`scale = avg != 0 ? flat / avg : 0`), after styles and dlights are summed. vrad's raw directional average is not the
flat set, so uploading the file's samples as they are is wrong even though every offset and the shader are right.

**How to apply:** when the file read and the shader both check out and the picture is still off, look for the UPLOAD
step — whatever builds the texture or buffer the shader samples — before measuring exposure, bloom or anything
downstream. Three parts of B514 went to bloom and exposure first. In code: `BspLightmaps.NormalizeBumped`, behind
`StoreSets`, which every producer of directional sets must go through on floats, before `Store` quantizes.
`BspFaceLighting.Styles` now carries the raw sets for every bump-lit face, because a normalized stored set cannot be
re-normalized after a dlight is added. Findings 76, "B514 part 4".
