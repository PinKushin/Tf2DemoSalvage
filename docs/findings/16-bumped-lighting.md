# 16 — Bumped lighting

`$bumpmap` is the largest remaining gap in the renderer: **54,857,158 drawn units over 21
materials** on `cp_process_f12`, against `$detail`'s 36 million. It is also the more involved of
the two, because a bump map is not a texture you sample and multiply — it changes how the *light*
is read, and the BSP stores four lightmaps per face to make that possible.

Read from `source-sdk-2013` before writing anything. Evidence class is **read from published
source** throughout unless marked otherwise.

## The three basis vectors

`src/public/mathlib/bumpvects.h`, exactly:

```c
#define OO_SQRT_3        0.57735025882720947f
#define OO_SQRT_2        0.70710676908493042f
#define OO_SQRT_6        0.40824821591377258f
#define OO_SQRT_2_OVER_3 0.81649661064147949f

const TableVector g_localBumpBasis[3] =
{
    {  OO_SQRT_2_OVER_3,  0.0f,       OO_SQRT_3 },
    { -OO_SQRT_6,         OO_SQRT_2,  OO_SQRT_3 },
    { -OO_SQRT_6,        -OO_SQRT_2,  OO_SQRT_3 },
};
```

Three unit vectors in tangent space, evenly spread around the surface normal and all leaning
equally towards it. Light is sampled from those three directions and recombined per pixel against
whatever direction the normal map says the surface actually faces.

**They are constants, not derived.** Writing them out of `sqrt` calls at runtime is the obvious
move and gives very slightly different values; Valve's are hard-coded to these exact floats, so
these exact floats go in the code.

## Four lightmaps per face, and the fourth is not a spare

A face flagged `SURF_BUMPLIGHT` (`0x0800`, in `bspflags.h`) gets four full sets of luxels rather
than one. From `vrad`'s size arithmetic:

```c
int nLuxels = (f->m_LightmapTextureSizeInLuxels[0]+1) * (f->m_LightmapTextureSizeInLuxels[1]+1);
if( needsBumpmap )
    lightdatasize += nLuxels * 4 * lightstyles * ( NUM_BUMP_VECTS + 1 );
else
    lightdatasize += nLuxels * 4 * lightstyles;
```

`NUM_BUMP_VECTS + 1` is four: one flat set plus one per basis vector.

**The layout is style-major, then bump set, then luxels** — four contiguous full lightmaps, not
four values interleaved per luxel. That question would otherwise need a guess, and `radial.cpp`
answers it outright:

```c
pdata[bumpSample] = &(*pdlightdata)[f->lightofs +
    (k * bumpSampleCount + bumpSample) * fl->numluxels * 4];
```

with `k` the light style and `bumpSampleCount` four when bumped and one when not.

**Consequence for what already works:** set 0 sits exactly where a non-bumped face's only set sits,
so the lightmaps this project reads today are already correct on bumped faces. Nothing is being
read wrong — three quarters of the data is simply being skipped. That is why bumped surfaces look
plausible rather than broken, and it is why this gap has been invisible.

## The combine, and the trap in it

`lightmappedgeneric_ps2_3_x.h`:

```c
float3 dp;
dp.x = saturate( dot( vNormal, bumpBasis[0] ) );
dp.y = saturate( dot( vNormal, bumpBasis[1] ) );
dp.z = saturate( dot( vNormal, bumpBasis[2] ) );
dp *= dp;

diffuseLighting = dp.x * lightmapColor1 +
                  dp.y * lightmapColor2 +
                  dp.z * lightmapColor3;
float sum = dot( dp, float3( 1.0f, 1.0f, 1.0f ) );
diffuseLighting *= g_TintValuesAndLightmapScale.rgb / sum;
```

**`lightmapColor1`, `2` and `3` are bump sets 1, 2 and 3. Set 0 is not read at all when a face is
bumped.** The natural assumption — that the flat set is the base and the three add detail — is
wrong, and it is wrong in a way that produces a perfectly reasonable picture: you would get the
right average brightness with the directional response layered on top of it, roughly twice as
bright and flat where it should be shaped.

The other easily-missed piece is `/ sum`. The three squared dot products do not sum to one, so
without the division the surface brightness swings with the normal direction rather than only its
shading. A wall would get brighter where it faces a basis vector.

**ssbump is a different combine entirely** (`BUMPMAP == 2`): no dot products, no saturate, no
squaring, no normalisation — the normal map's components are used directly as weights.

```c
diffuseLighting = vNormal.x * lightmapColor1 +
                  vNormal.y * lightmapColor2 +
                  vNormal.z * lightmapColor3;
```

That matters here because **TF2's own world materials commonly ship ssbump**, not ordinary normal
maps — `concrete/concretefloor007b` names
`concrete/concretefloor007b_height-ssbump` and sets `$ssbump 1`. So the ssbump path is likely the
*common* case on this corpus, not the exotic one. To be measured before it is believed.

## What this needs that does not exist yet

1. **A tangent basis per vertex.** The dot products are in tangent space, so each vertex needs `s`
   and `t` vectors from its `texinfo` plus the face normal. That is arithmetic on data already
   read.
2. **Four lightmap sets through the atlas.** `LightmapAtlas` packs one image per face today; a
   bumped face needs three more, and the shader needs to reach them. Three extra atlas pages
   keyed the same way is the least invasive shape.
3. **Normal map decode.** VTF formats used by normal maps include DXT5 and uncompressed BGRA;
   whether `VtfTexture` already covers every format the 21 materials use is a measurement, not an
   assumption.
4. **`$ssbump` detection.** Both the material key and the VTF's own `TEXTUREFLAGS_SSBUMP`, which is
   the same bit already read for `$detail` mode 10/11.

## Order

Research (this document) → tangent basis with unit tests → the four-set lightmap read with a
control that a non-bumped face still reads identically → atlas → shader → picture comparison with
bump on and off, plus the bit-identical control render that the detail work established.

## Status

**Researched, not implemented, 2026-08-13.** No measurement on the corpus yet beyond the drawn-area
count at the top. In particular the claim that ssbump is the common case on TF2 world materials is
**interpolated from one material** and needs counting.
