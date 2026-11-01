---
name: a-modulate-output-is-a-multiplier-not-light
description: "A Modulate/DecalModulate shader's output multiplies the framebuffer, so exposure must not scale it (TONEMAP_SCALE_NONE); a view-dependent look between two shots of the same thing points at exposure."
metadata:
  type: feedback
---

**A modulating material's colour is a multiplier on what is behind it, not light.** Valve's `Modulate` and
`DecalModulate` both end in `FinalOutput( …, TONEMAP_SCALE_NONE )` (`modulate_ps2x.fxc:37`, `decalmodulate_ps2x.fxc:44`).
Our shared pixel shader scaled every output by `LINEAR_LIGHT_SCALE`, so under an exposure of about a half every
scorch's neutral 128-grey surround halved the floor. The result was dark, straight-edged squares the owner called
"massive pixelation" (B635, findings 18).

**Why it is worth remembering:** it looked like a magnification, capture-path or build problem, because the same decals
looked clean from another camera. Whether the exposure reaches the world pass at all depends on the camera: only
frames that draw a 3D-sky or water view re-upload it (B636). The capture log's "drawn with tone-map scale" is not the
uploaded value. When two shots of one subject disagree by camera, pin `+mat_force_tonemap_scale 1` first. It is one cvar
and it separates exposure from everything else.

**How to apply:** any new path that draws a blend-multiplied material (`DST_COLOR` blends, mod2x) must skip the tone-map
scale and the fog colour's scale, as `PixelFog` mode 2 does. Before calling a texel "neutral", ask what multiplies the
shader's output after the texture.

**And it reads no light at all (B639, B638).** Neither modulating shader samples a lightmap or a vertex colour. The
linear blend's gamma compensation (2^2.2/2 for mod2x, 1 for plain) is the MATERIAL's unlit diffuse
(`UnlitDiffuseOf`), never a vertex colour, because a brush entity drawn by the model pass carries none. B639 was
filed as "lightens". A signed pixel count on the filed image showed it darkened. Count the sign before you chase a
direction.

Related: [[instrument-bugs-outnumber-decoder-bugs]], [[take-your-own-screenshot]].
