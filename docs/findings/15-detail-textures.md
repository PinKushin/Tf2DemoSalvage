# 15 — Detail textures

The `$detail` chain, read out of `source-sdk-2013` before writing any of it. This file is the
implementation roadmap and the record of what the source actually says, including the three places
where the obvious reading is wrong.

Evidence class throughout: **read from published source**, `src/materialsystem/stdshaders/`.
Anything measured on the corpus is marked as such.

## Why it is next

Measured on `cp_process_f12` (the map the viewer loads today), by summed drawn area:

| Missing feature | Units drawn | Materials |
|---|---|---|
| `$bumpmap` | 54,857,158 | 21 |
| `$detail` | 36,327,110 | 36 |

`$bumpmap` is larger but needs TF2's bumped lightmaps — four luxel sets per sample, a different
lightmap read, and a tangent basis per vertex. `$detail` needs one more texture and one more
`lerp`. It is the larger share of the *cheap* remainder, so it goes first.

## What the source says

### Where it happens

`lightmappedgeneric_ps2_3_x.h`, in order:

```c
HALF4 detailColor = HALF4( 1.0f, 1.0f, 1.0f, 1.0f );
#if DETAILTEXTURE
  #if SHADER_MODEL_PS_2_0
      detailColor = tex2D( DetailSampler, detailTexCoord );
  #else
      detailColor = float4( g_DetailTint, 1.0f ) * tex2D( DetailSampler, detailTexCoord );
  #endif
#endif
...
if ( bDetailTexture )
{
    albedo = TextureCombine( albedo, detailColor, DETAIL_BLEND_MODE, g_DetailBlendFactor );
}
```

**Detail modifies the albedo, before the lightmap multiply.** So it slots into our pixel shader
between the base/blend `lerp` and the `light` multiply, not at the end. `$detailtint` is a
pre-multiply on the sampled colour, and is dropped entirely on `ps_2_0` — a hardware fallback, not
a semantic one; we implement the `ps_2_b`/`ps_3_0` form.

### The twelve combine modes

`common_ps_fxc.h`, `TextureCombine`. Transcribed exactly:

| # | Name | Effect |
|---|---|---|
| 0 | `RGB_EQUALS_BASE_x_DETAILx2` | `base.rgb *= lerp(1, 2*detail.rgb, f)` |
| 1 | `RGB_ADDITIVE` | `base.rgb += f * detail.rgb` |
| 2 | `DETAIL_OVER_BASE` | `base.rgb = lerp(base.rgb, detail.rgb, f * detail.a)` |
| 3 | `FADE` | `base = lerp(base, detail, f)` — **all four channels** |
| 4 | `BASE_OVER_DETAIL` | `base.rgb = lerp(base.rgb, detail.rgb, f * (1-base.a))`; `base.a = detail.a` |
| 5 | `RGB_ADDITIVE_SELFILLUM` | post-lighting; `TextureCombinePostLighting` |
| 6 | `..._THRESHOLD_FADE` | post-lighting, remaps a widening band |
| 7 | `MOD2X_SELECT_TWO_PATTERNS` | `dc = lerp(detail.r, detail.a, base.a)`; then as mode 0 with `dc` |
| 8 | `MULTIPLY` | `base = lerp(base, base*detail, f)` — **all four channels** |
| 9 | `MASK_BASE_BY_DETAIL_ALPHA` | `base.a = lerp(base.a, base.a*detail.a, f)` — alpha only |
| 10 | `SSBUMP_BUMP` | detail modulates bumped lighting; needs `$bumpmap` |
| 11 | `SSBUMP_NOBUMP` | `base.rgb *= dot(detail.rgb, 2.0/3.0)` |

Modes 5 and 6 are applied **after** lighting and are a separate function. Mode 10 is out of scope
until `$bumpmap` lands. Modes 0–4, 7–9 and 11 are all implementable now.

Note that 3 and 8 write alpha as well as colour, and 4 and 9 write alpha specifically. Our alpha
test currently clips on the base texture's alpha *before* any of this — that is wrong for four of
the twelve modes and has to move after the combine.

### Defaults

`lightmappedgeneric_dx9.cpp`, `SHADER_PARAM` declarations — these are Valve's own defaults, not
inferred:

| Key | Default |
|---|---|
| `$detailscale` | `4` |
| `$detailblendmode` | `0` |
| `$detailblendfactor` | `1` |
| `$detailtint` | `[1 1 1]` |
| `$detailframe` | `0` |

### UV

`lightmappedgeneric_vs20.fxc` builds the detail coordinate from the **base** coordinate through a
scaled transform:

```c
SetVertexShaderTextureScaledTransform(
    VERTEX_SHADER_SHADER_SPECIFIC_CONST_2, info.m_nBaseTextureTransform, info.m_nDetailScale );
```

So `detailUV = baseUV * $detailscale` for the common case where `$basetexturetransform` is
identity. The helper's own comment says the transform is set unconditionally because "you'll always
have a detailscale".

**Detail and bumpmap share a texcoord slot** — `detailOrBumpAndEnvmapMaskTexCoord`, and the
comment says outright they are mutually exclusive "so that we have enough texcoords". That is a
DX9 register limit, not a rule about materials, and does not bind us. It does explain why
`$detail` and `$bumpmap` rarely appear together in TF2 content.

### The one thing here that is inference, not a read

`$detailtint` is a colour, and colours appear in Valve's own defaults in two spellings —
`aftershock.cpp` declares `"[1 1 1]"` and `cloak.cpp` declares `"{255 255 255}"`, both meaning
white. So brackets are floats and braces are bytes.

**The parser that decides that is not in `source-sdk-2013`.** Only `stdshaders` and `shaderapidx9`
ship; `materialsystem` itself, where `IMaterialVar` initialises a vector from a string, is closed.
The conclusion above is drawn from two published defaults that must both be the identity, which is
sound but is *inference*, not a read of the code that does it.

**Evidence class: interpolated.** To be settled by measuring the shipped VMTs — if every brace form
has components in 0–255 and every bracket form in 0–1, that is differential evidence from Valve's
own content. Until then it is flagged.

## Three traps

**1. The detail texture is NOT read as sRGB, except in mode 1.**

```c
bool bSRGBState = ( nDetailBlendMode == 1 );
pShaderShadow->EnableSRGBRead( SHADER_SAMPLER12, bSRGBState );
```

Reading it sRGB everywhere is the natural thing to do — it is a colour texture in a colour
pipeline — and it would be wrong on 35 of the 36 materials here. Mode 0's `2*detail` is a
*modulation*, not a colour, so it stays linear. This is the kind of thing that produces a plausible
picture that is quietly wrong everywhere, which is the failure mode this project keeps hitting.

**2. Mode 10/11 is chosen from the VTF's flags, not from the VMT.**

```c
if ( pDetailTexture->GetFlags() & TEXTUREFLAGS_SSBUMP )
    nDetailBlendMode = hasBump ? 10 : 11;
```

`$detailblendmode` absent therefore does **not** mean mode 0. If the detail texture carries
`TEXTUREFLAGS_SSBUMP` (`0x08000000`), the material's stated mode is overridden. `VtfTexture` reads
the flags field today but does not expose it; it has to.

**3. Grey is the identity, and that is a free test.**

When the fast path disables detail, the helper binds `TEXTURE_GREY` rather than unbinding:

```c
pContextData->m_SemiStaticCmdsOut.BindStandardTexture( SHADER_SAMPLER12, TEXTURE_GREY );
```

Grey is 0.5, and mode 0 is `lerp(1, 2*0.5, f)` = `1` for any blend factor. So a 0.5 detail texture
must leave the albedo bit-identical under mode 0. That is a prediction with an exact value, and it
falsifies a wrong `2*` or a wrong `lerp` argument order immediately — unlike "the picture changed",
which any of them satisfies.

## Implementation order

TDD, research first — this document is the research step.

1. **`VmtMaterial`** — `Detail`, `DetailScale` (4), `DetailBlendFactor` (1), `DetailBlendMode` (0),
   `DetailTint` (1,1,1). Each with a control: a material that omits the key gets the default, a
   material that sets it gets the set value.
2. **A `DetailCombine` function in Core**, pure, one method per mode, tested against the table
   above with hand-picked values where correct and broken differ. Mode 0 with grey is the identity
   case; mode 0 with black is `base * (1-f)`; mode 0 with white is `base * (1+f)`.
3. **`VtfTexture.Flags`** — exposed, with a test that an SSBUMP-flagged header reports it.
4. **`MapAssets`** — a third texture list beside `Textures`/`BlendTextures`, loaded through the
   same search path, and **logging every failure**, per the repo's no-silent-fallback rule. Then a
   measurement: how many of the 36 detail materials actually resolved. "Measure the output, not the
   capability."
5. **`WorldRenderer`** — bind to the free **t3** slot; per-batch constants for scale, factor, mode
   and tint. The camera buffer established the pattern; ~200 batches makes a per-batch write cheap.
   Bind to **both** VS and PS stages — the height-cut bug was exactly this.
6. **Move the alpha clip after the combine**, since modes 3, 4, 8 and 9 write alpha.
7. **`MapPictureTests`** — a picture with detail on and one with it off, asserting a specific
   surface changed, plus a control surface with no `$detail` that must be bit-identical.

## Two modes the source does not advertise

Both found by writing a sweep asserting the obvious property — *a blend factor of zero turns a mode
off* — and watching it fail. Neither is stated anywhere; both are visible in `TextureCombine` once
you know to look at where the braces are.

**Mode 4 replaces alpha outside the blend.**

```c
float fblend = fBlendFactor * (1-baseColor.a);
baseColor.rgb = lerp( baseColor.rgb, detailColor.rgb, fblend );
baseColor.a = detailColor.a;          // not inside the lerp, not scaled by fblend
```

At a factor of zero the colour is untouched and the alpha is still replaced. Since alpha feeds the
alpha test, that changes which pixels survive.

**Mode 11 has no blend factor at all.**

```c
baseColor.rgb = baseColor.rgb * dot( detailColor.rgb, 2.0/3.0 );
```

`$detailblendfactor "0"` does not turn mode 11 off. It is also a second grey identity — a detail of
(0.5, 0.5, 0.5) sums to 1.5, times 2/3 is exactly 1 — which a reading of "average the channels"
would get wrong by a factor of two.

The sweep now carries these as two named exclusions with a test each, rather than being narrowed
until it passed. **Evidence class: read from published source, found by measurement.**

## Status

**Implemented, 2026-08-13.** Measured on `cp_process_f12` with the modern VPKs mounted:

| | |
|---|---|
| Materials naming `$detail` | 34 of 285 |
| Resolved | 34 |
| Drawn with a detail texture | 34 |
| Not found | 0 |
| Sampled pixels that change with it on | 7,155 of 172,800 (4.1%) |

Zero misses, which is the bar this repo sets: the engine reads all of these, so anything short of
all of them is our defect.

The 4.1% is measured on a top-down overview at 640×360, where most surfaces are floors seen flat and
a detail texture is a subtle multiply. It is a floor on the effect, not a measure of it.

**Still open from this chain:** modes 5, 6 and 10 are implemented but unexercised — no material on
this map uses them, so they are transcription rather than measurement. Mode 10 additionally needs
`$bumpmap`, which is the next item.

## `_white` was not white, and it painted a chequer onto every model with a detail

*(measured on the running viewer, 20 August 2026)*

Every player in a first-person capture came out in purple and grey squares. The owner spotted it in
the picture; nothing in the suite could, because no assertion looks at what a player is coloured.

**The name was the bug.** `WorldRenderer._white` is built by `Missing()` — the magenta-and-black
chequer Source draws for an unresolved material, which this viewer implements deliberately so a
missing texture looks like a fault rather than like art. It is then used in two quite different
roles:

- as the fallback for a **base** texture that did not upload, which is exactly right, and
- as the neutral default for the **detail** and **bump** slots, which is not.

The second is defensible only under a condition nobody stated: the shader skips the detail combine
when the material's mode is −1, so a chequer bound for a material with no detail is never sampled.
Three of the five draw paths did the real lookup anyway —

```csharp
ComPtr<ID3D11ShaderResourceView> detail =
    _details[batch.MaterialIndex].Handle is not null ? _details[batch.MaterialIndex] : _white;
```

— and two, the model path and the decal path, bound `_white` unconditionally. So any **model**
material declaring `$detail` had a magenta chequer multiplied into its albedo, at whatever scale
`$detailscale` asked for. A medic's coat came out in purple and grey squares.

**Why it was confined to players, which is what made it hard to see.** The map and the props are
drawn by the paths that do the lookup, so the same frame showed a correct building, correct
brushwork and correct static props with chequered characters standing among them — which reads as a
character-specific fault, and sent the investigation at the player texture, the player material and
the player lighting in turn.

Four things were eliminated before the draw call was read at all, and each was worth eliminating:

| Suspected | Measured |
|---|---|
| the missing-material chequer being bound | **0** materials had a null handle |
| the material resolving to the wrong name | pairs to `models/player/medic/medic_red` |
| the `--colours` diagnostic view (also magenta) | not enabled |
| the VTF decode | `medic_red` decodes to a white coat and a red shirt, perfectly |

**The texture probe is the step that turned it round**, and only because it wrote a picture out
rather than a number. Its first version reported an average colour — four healthy browns — while
the screen showed chequered coats, because a checkerboard of magenta and grey averages to something
unremarkable. An average is exactly the wrong instrument for a pattern.

Fixed by giving the model and decal paths the same lookup the other three have. The deeper fix is
the name: a fallback that means "this is missing, look at it" and a fallback that means "nothing
here, carry on" are different values, and calling the first one `_white` is what let it be used as
the second.
