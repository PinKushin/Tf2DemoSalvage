---
name: a-count-is-not-a-span
description: N samples span N-1 intervals. Brush lightmap coordinates were divided by the luxel count for years; a displaced pattern is invisible in tile averages. Check a coordinate at a point with a gradient, and name the convention a rectangle uses.
metadata:
  type: feedback
---

**A lightmap of `n` luxels runs from luxel 0's centre to luxel `n-1`'s: a span of `n - 1`.** vrad lights luxel `s`
AT luxel coordinate `s` past the face's mins (`utils/vrad/lightmap.cpp:847-866`, `width = size + 1`).

**What happened (B521, 2026-10-11).** `BspSurfaces` divided `s - mins` by `size + 1`; `LightmapAtlas` maps a face's
0..1 from first texel centre to last. Every brush face's light drew squeezed toward its mins by `size / (size + 1)` —
up to a luxel (16 units) at the far edge, a quarter of a four-luxel face. Displacements had the mirror slip
(`(0.5 + f*span) / count`: half a luxel in at each end). Both now go through `LuxelMapping.Coordinate`.

**Why it survived:** a flat wall is unchanged by a shift, and tile averages read 98-102%. It shows only where light has
an edge — a spotlight pool — as one tile OVER beside one UNDER. An over/under pair on one surface means DISPLACED, not
missing or extra light.

**How to apply:**

- Two conventions exist for "a face's own 0..1": the engine's `(s - mins + 0.5) / count` over a rectangle `count`
  texels wide, and centre-to-centre `(s - mins) / (count - 1)` over a rectangle inset half a texel. Either is right;
  mixing one producer with the other rectangle is this bug. State which one a rectangle type uses.
- A test that asserts "inside (0, 1)" passes for both and for the mix. Assert the END values: exactly 0 and exactly 1
  at the first and last sample.
- Check a sampled value at a point ON A GRADIENT against the source data blended by hand
  (`pixel-face <map> <camera> <px> <py>` names the face and prints its luxels at the point).
- Before hunting a missing light, ask whether the light is there and displaced.
  See [[ask-which-input-differs-before-bisecting]], [[instrument-bugs-outnumber-decoder-bugs]].
