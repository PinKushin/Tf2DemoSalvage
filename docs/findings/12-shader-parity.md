# What parity with Source's renderer actually requires

The goal is not "an overhead map that reads well" but a correct Source renderer — because the
first-person view needs one, and because once at parity the extra headroom of DX11 over DX9 can go
somewhere useful.

Parity is a finite list, and the map itself states it. Measured on `cp_process_f12` by reading every
material's VMT and weighting by the world area its surfaces cover — the same method that found
`tools/toolsblack` covering 4.8 million units while a brightness ranking had put a 4,096-pixel tool
texture at the top.

## Shaders the map uses

| area | materials | shader | state |
|---|---|---|---|
| 122,411,259 | 102 | `LightmappedGeneric` | drawn |
| 18,340,886 | 3 | `WorldVertexTransition` | drawn, two textures mixed by vertex alpha |
| 11,363,671 | 30 | `LightMappedGeneric` | same, and note the capitalisation differs |
| 9,022,902 | 46 | `Patch` / `patch` | resolved through to the included material |
| 1,538,304 | 1 | `UnlitTwoTexture` | **not implemented** |
| 154,588 | 5 | `UnlitGeneric` | drawn as ordinary albedo |

**Shader names vary in case across the same map** — `LightmappedGeneric`, `LightMappedGeneric`,
`Patch`, `patch`. Anything matching them must do so case-insensitively; this project already does,
and it is the kind of thing that silently drops thirty materials.

## Features the map asks for

| area | materials | key | state |
|---|---|---|---|
| 117,465,699 | 41 | `$surfaceprop` | not rendering — physics and footstep sound |
| **54,857,158** | **21** | **`$bumpmap`** | **not implemented** |
| **36,327,110** | **36** | **`$detail`** | **not implemented** |
| 18,340,886 | 3 | `$basetexture2` | done |
| 1,538,304 | 1 | `$additive` | done, second pass, SRC_ONE DEST_ONE |
| 1,451,017 | 63 | `$translucent` | approximated by the alpha-test clip; real blending needs sorting |
| 591,872 | 2 | `$alphatest` | done |
| 123,932 | 4 | `$selfillum` | not implemented, and small |

## Re-measured 2026-08-13, after `$detail` and `$bumpmap` landed

`RemainingParityProbe` now produces this table rather than it being assembled by hand. On
`cp_process_final`, area weighted:

| area | materials | shader |
|---|---|---|
| 122,447,608 | 167 | `LightmappedGeneric` |
| 49,077,935 | 32 | `LightMappedGeneric` |
| 22,419,344 | 3 | `WorldVertexTransition` |
| 663,167 | 2 | `UnLitGeneric` |
| 450,559 | 4 | `UnlitGeneric` |

**A third capitalisation turned up**: `UnLitGeneric` as well as `UnlitGeneric` and the two
`Lightmapped` spellings. Four variants of two names on one map.

| area | materials | key | state |
|---|---|---|---|
| 102,244,323 | 43 | `$detail` | **done** |
| 89,861,277 | 87 | `$surfaceprop` | not ours — physics and footstep sound, and a demo carries the results |
| 67,139,015 | 27 | `$bumpmap` | **done** |
| 22,419,344 | 3 | `$basetexture2` | done |
| 5,039,041 | 94 | `$translucent` | **done** — blended, sorted, depth written off |
| 2,728,616 | 51 | `$envmap` | not implemented, and gated on the first-person camera |
| 2,087,066 | 66 | `$decal` | **done** — 222 overlays placed, lit by the face beneath, Valve's depth bias |
| 1,412,608 | 2 | `$alphatest` | done |
| 538,742 | 5 | `$selfillum` | **done** — masked by the base texture's alpha |

**As of 2026-08-13 only `$envmap` is left: 2.7 million units of roughly 194 million, about 1.4%.**
Everything else on this list is drawn.

`$envmap` stays unimplemented deliberately rather than by omission. A reflection needs a view
direction and this camera looks straight down everywhere, so it would compute a nearly constant
reflection until the first-person camera exists. It is also the one feature that brings back the
per-vertex tangent basis bumped lighting turned out not to need, since a reflection is computed in
world space.

`$envmap` is on this map after all — 51 materials — which an earlier version of this document
implied it was not. It is also the one remaining feature that brings back the per-vertex tangent
basis that bumped lighting turned out not to need, since a reflection is computed in world space.

## What the two remaining features would need, measured

Lump sizes, **decompressed** — and that qualifier is the finding. Every BSP lump is LZMA packed
and the directory reports the packed length, so dividing that by a structure's stride gives a
fractional entry count that reads as a wrong stride. Measured before the decompression was added:
18.46 overlays and 16.88 cubemaps. Both are plausible enough to chase.

| Lump | Decompressed | Stride | Entries |
|---|---|---|---|
| 45, overlays | 85,536 | 352 | **243.00** |
| 42, cubemap samples | 688 | 16 | **43.00** |

Exact division both times, which confirms `doverlay_t` at 352 bytes and `dcubemapsample_t` at 16
before either reader exists.

The map's pakfile carries **86 cubemap-shaped textures against 43 samples** — exactly two each,
which is the LDR and HDR pair per sample. A second unrelated route agreeing with the lump count.

**Decals are the better next target despite `$envmap` being larger by area.** 243 overlays are
view independent: signs, scorch marks and arrows painted flat on floors and walls, all of them
visible from directly above. A reflection needs a view direction, and this camera looks straight
down everywhere — so `$envmap` would compute a nearly constant reflection until the first-person
camera exists. Its value is gated on that camera, not on the shader.

## The order to do them in, and why

1. **`$detail`** — 36 million units, and the cheapest of the two big ones: a second texture multiplied
   in at a scale the material states, which is most of what a surface looks like from close range and
   nothing at all from above. Needed the moment there is a first-person camera.
2. **`$bumpmap`** — 55 million units, and the expensive one, because TF2's lightmaps for a bumped
   material store **four sets of luxels per sample**: one flat and three in the bump basis. Reading
   only the first is what this project does now, which is right for unbumped faces and throws away
   three quarters of the data for the rest. Verify against the lump arithmetic before writing any of
   it — the existing check that samples tile the lighting lump to exactly 100.0% is the instrument
   that will say whether bumped faces are already being walked correctly.
3. **`$translucent` sorting** — 63 materials but small area. The alpha-test clip standing in for it is
   wrong in a way that matters more in first person than from above.
4. **`UnlitTwoTexture` and `$selfillum`** — one material each in practice.

## What this does not cover

HDR and tone mapping, water, the 3D skybox drawn as sky rather than culled, `$phong` and rim lighting
on models, and decals or overlays. Each is real; none is on this map's critical path by area, and the
list above is the part that can be checked off against a number.

## `$detail`, transcribed from the engine

`TextureCombine` in `materialsystem/stdshaders/common_ps_fxc.h` holds every mode. The default,
`TCOMBINE_RGB_EQUALS_BASE_x_DETAILx2` (mode 0):

```c
baseColor.rgb *= lerp( float3(1,1,1), 2.0*detailColor.rgb, fBlendFactor );
```

So at a blend factor of 1 it is `base * 2 * detail` — a detail texture averaging 0.5 grey leaves the
surface unchanged, which is why they are authored around mid grey. The other modes matter less by
area but are cheap to add once the plumbing exists:

| mode | effect |
|---|---|
| `MOD2X_SELECT_TWO_PATTERNS` | picks between detail red and alpha by base alpha, then mod2x |
| `RGB_ADDITIVE` | `base += factor * detail` |
| `DETAIL_OVER_BASE` | lerp toward detail by `factor * detail.a` |
| `FADE` | lerp base to detail by factor |
| `BASE_OVER_DETAIL` | lerp toward detail by `factor * (1 - base.a)` |
| `MULTIPLY` | lerp base toward `base * detail` |
| `MASK_BASE_BY_DETAIL_ALPHA` | modulates base alpha only |

Two material keys go with it: `$detailscale` (default 4) multiplies the base UV to get the detail
UV, and `$detailblendfactor` (default 1) is `fBlendFactor`. `$detailblendmode` selects the mode.

**What it needs from the renderer:** a fourth texture slot, and the scale, factor and mode reaching
the shader per material. The camera matrix already established the pattern — a constant buffer
updated between draws — and there are around two hundred batches, so a small per-batch write is
affordable.

## The camera modes this has to serve

The overhead view is the easy case and it is not the target. A SourceTV recording carries both
first-person and third-person views of players, and the viewer wants a free camera as well. So:

- **First person** — every approximation that survives at a distance fails here. `$detail` is most
  of what a wall looks like from a metre away, and `$bumpmap` is most of what it looks like lit.
- **Third person** — same shading, plus the player's own model, which needs the model shader path
  (`$phong`, rim lighting) rather than the world one.
- **Free camera** — no new shading, but it removes any excuse for view-dependent shortcuts, and it
  is what makes the height cut a stopgap rather than a feature.

The current renderer is correct for a top-down view of static geometry. Everything above is what
stands between that and a camera a person can fly.

---

## `$modblend` is dead, and the shipped VMTs say so

Evidence class: **measured on one machine**, against the live install, 2026-08-16. Reproducible by
anyone with TF2 installed; not asserted in a test, because it depends on a Steam library.

`$modblend` was this project's standing example of a question the SDK cannot answer — TF2 ships it in
real VMTs, no published shader declares it, and `CLAUDE.md` nominated it as the case where reaching
for a decompiler is the right next step rather than a last resort.

**It needed no decompiler. The answer is in the VMTs themselves.**

Three facts, in the order they were established:

1. **No published shader declares it.** `grep -ril '$modblend'` over
   `materialsystem/stdshaders` returns nothing, against 28 files for `$envmap` and 35 for
   `$detail`. Confirmed rather than assumed.
2. **No shipped binary contains the string.** Extracting every `$`-prefixed identifier from
   `bin/stdshader_dx9.dll` yields **515 parameter names** — `$envmap`, `$envmapfresnel`,
   `$envmapsaturation` and so on — and `$modblend` is not among them. Nor is it in any other
   `.dll` or `.exe` in `bin`, `bin/x64` or `tf/bin`.
3. **It IS in three shipped VPKs**, and every occurrence has the same shape.

That shape is the finding:

```
"Modulate"
{
	"$basetexture" "models/effects/cappoint_logo_blue"
//	"$additive" "1"
	"$alpha" "1"
	"$modblend" ".63"
	"$model" "1"
	"$mod2x" "1"
	"Proxies"
	{
//		"Equals"
//		{
//			"srcvar1" "$modblend"
//			"resultvar"  "$alpha"
//		}
		"Sine"
		{
			"Sineperiod" ".3"
			"SineMax" ".7"
			"SineMin" ".6"
			"resultVar" "$alpha"
		}
	}
}
```

**The only thing that ever read `$modblend` is commented out four lines below it.** An `Equals`
proxy copied it into `$alpha`; that proxy is disabled and a `Sine` proxy animates `$alpha` instead.
The same pattern appears in all three materials that carry the parameter — `cappoint_logo_blue`
(`.63`), `props_mvm/mvm_revive_hologram` (`1`) and `robo_marker` (`.63`) — so it is one template
copied three times, with its consumer commented out before it shipped.

**A material parameter that no shader declares is simply ignored by the material system.** So
`$modblend` does nothing, has done nothing since the proxy was disabled, and the correct
implementation of it is to implement nothing.

### Two things worth carrying away

**The decompiler was nominated for a question the game's own data files answered.** That is now the
second time in one session — the game event field widths were in a `.res` comment block. The
category "things the SDK cannot answer" was doing a lot of work in this project's source menu, and
two of its examples were answerable from shipped data, not shipped code.

**A live proxy turned up while looking for a dead parameter.** `cappoint_logo_blue` animates its
alpha with a `Sine` proxy — period `.3`, between `.6` and `.7`. That is a visible pulse on the
capture point logo, it is B80's "material proxies are not implemented" made concrete, and it was
found incidentally. Worth remembering that reading real assets pays for itself even when the
question was about something else.

## Auditing what is claimed against what is checked (2026-08-18)

The census says which parameters this project implements. Nothing said which of those had ever been
compared against the engine, and the two are very different questions:
`SdkCoverageTests` catches a parameter never implemented; only a behavioural test catches one
implemented **wrongly**. Of twenty-one claimed, **eight had no such test** — and three defects fell
out of writing them.

### The audit's own first result was wrong, which is the useful part

The opening sweep reported *all twenty* parameters as untested. That was a fact about the search: the
parameter names begin with `$`, which the shell read as a regex anchor, so every pattern matched
nothing. A positive control — "does a parameter I know is covered show up?" — caught it in one step.
Without it the conclusion would have been twenty redundant tests and eight real gaps missed. Filed as
the reason `docs/memory/an-empty-search-needs-a-control.md` exists.

### Boolean parameters are integers, and nine were compared against a string

`VmtMaterial` read nine flags as `Value(key) is "1"`. The material system does not: these are
declared `SHADER_PARAM_TYPE_INTEGER` — `SHADER_PARAM( SSBUMP, SHADER_PARAM_TYPE_INTEGER, "0", ... )`
— and the flag-valued ones become `MATERIAL_VAR_*` bits set from an integer read. **Nothing in that
path compares against the character `1`.**

So `"$translucent" "2"` drew translucent in TF2 and opaque here. It survived because it agrees with
the engine on every material Valve ships — and *that* is the shape worth remembering: "Valve always
writes 1" is a statement about Valve, not about the input a reader is handed, and a custom map's
materials go through the same code. (RISKS B115.)

### The translucent blend factors were never actually unknowable

The renderer carried a comment saying its alpha-blend factors were *interpolated*, because
`SetDefaultBlendingShadowState` lives in the closed material system. True of the function; false of
the definition. `public/shaderlib/BaseShader.h` declares `BlendType_t` with each mode's equation
written beside it:

```cpp
// src * srcAlpha + dst * (1-srcAlpha)
BT_BLEND,
// src * one + dst * one
BT_ADD,
```

That moves the claim from **interpolated** to **read from published source** — a different evidence
class entirely. Third time this session something filed as unavailable was sitting in a shipped
header, after the game event widths and `$modblend`.

### Every alpha-tested edge was cut at half

`$alphatestreference` was unimplemented and the shader clipped at a constant `0.5`. Valve enables
alpha testing from the flag and overrides the reference **only when the material states one above
zero** (`BaseVSShader.cpp:925`), comparing `GEQUAL`. A material asking 0.9 keeps only its most opaque
texels; ours kept everything above half, thickening every alpha-tested edge — foliage, grates,
chain-link, ladders. It reads as bad art, not as a bug. (RISKS B117.)

Two traps in one parameter. **Zero is not a cutoff**: an absent reference means "leave the API
default alone", and reading it as "clip at zero" keeps every texel and turns a grate into a sheet —
the exact inverse of the defect. And **the declared default is spelled differently by different
shaders**: `"0.0"` in the generic shaders, `""` in `depthwrite`. The conformance test pins the
meaning for that reason; its first draft pinned the empty string and failed against correct code.
