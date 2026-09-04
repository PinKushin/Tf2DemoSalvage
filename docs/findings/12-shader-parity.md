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
`$modblend` does nothing in TF2, has done nothing since the proxy was disabled, and the correct
implementation of it here is to implement nothing.

### "Dead" was too strong, and the correction is worth keeping (2026-08-19)

The heading above says *dead*. The owner challenged that while
[`$vertexcolor`](28-vertex-colour.md) was being worked out: a parameter this engine ignores may be
one Source uses for another game or another path, rather than one nothing uses anywhere. That was
exactly right about `$vertexcolor` — it is live in `cable_dx6.cpp` and in the fixed-function
`decal.cpp`, just unreachable from a DX9 `LightmappedGeneric` world face.

So the same test was run here, properly this time and with a positive control, because the original
claim rested on an absence and five absence claims in this project have turned out to be facts about
the grep.

**`$modblend` is absent from 21 TF2 binaries**: `client`, `engine` and `server` at 2007, 2008, 2009,
2011, 2013 and live, plus `MaterialSystem.dll`, `StudioRender.dll`, `shaderapidx9.dll` and all five
`stdshader_*.dll`. Every sweep carried a control that fired — `vertexcolor` on the engine-side
binaries, `$detail` on the shader DLLs. So the absence is real and not a search artifact.

**But two things stop this being a refutation of the challenge.**

The structural difference is that `$vertexcolor` is a **MATERIAL_VAR flag** — engine-level, present
across the whole family by construction, which is why live consumers were findable in shipped
source. `$modblend` would be a **SHADER_PARAM**, which exists only where some shader declares it. So
"live elsewhere in the family" is a weaker prior here. That is an argument about likelihood, not
evidence.

And the evidence that survives points the other way: **three shipped VMTs declare it, and VMT keys
do not appear by accident.** Some shader, tool or branch had it. What is established is that no TF2
binary of any era reads it; what is not established is that nothing ever did, and no other Source 1
game was available on the machine to check (the only other Source-family title installed is CS:GO,
which is Source 2).

**So the accurate statement is the one written for `$vertexcolor`: not reachable here, origin
unknown.** The implementation advice is unchanged — implement nothing — but the reasoning behind it
is now "no path in this game reaches it" rather than "it is dead", and those differ for anyone
carrying this reading to another Source project.

### And then the origin turned out to be knowable after all

The paragraph above settled for "origin unknown" and proposed fetching HL2 or an older SDK to
narrow it. Neither is needed, and two things already in hand say why.

**All three declaring materials are TF2 content, and two are Mann vs Machine.**
`models/effects/cappoint_logo_blue`, `props_mvm/mvm_revive_hologram`, `robo_marker` — authored for
TF2 in 2012, not inherited HL2 or CS:S boilerplate. So "another Source game uses it" has a dating
problem before any binary is opened: the parameter appears only in content written years after the
games it would have to have come from.

**And `$modblend` was never a shader parameter.** A material proxy resolves its inputs by *name
lookup on the material* (`functionproxy.cpp:210`):

```cpp
char const* pSrcVar1 = pKeyValues->GetString( "srcVar1" );
...
m_pSrc1 = pMaterial->FindVar( pSrcVar1, &foundVar, true );
```

against `IMaterial::FindVar( const char *varName, bool *found, bool complain = true )`
(`imaterial.h:484`). Any key written into a VMT becomes a material var, so a proxy can read a name
an artist invented. That is a normal Source idiom, not a workaround.

Which makes `$modblend` **an artist-authored variable holding a constant for a proxy to copy**, and
every piece of evidence falls out of that at once:

| Observation | Explanation |
|---|---|
| No shader declares it | It is not a shader parameter and never was |
| No binary of any era names it | Nothing engine-side would ever name it |
| Only three VMTs carry it | One authored template, copied twice |
| Its only consumer is a commented-out `Equals` proxy | That proxy is precisely what it was for |

The `Sine` proxy animating `$alpha` is the replacement: someone wired a constant through `Equals`,
decided it should pulse instead, and commented out the `Equals` — leaving the constant behind with
nothing reading it.

**So the `$vertexcolor` analogy does not transfer, and now for a citable reason rather than a
hunch.** `$vertexcolor` is a `MATERIAL_VAR_*` flag: engine-level, shared across the family by
construction, and therefore genuinely capable of being live somewhere this game never reaches.
`$modblend` is a name someone typed. The challenge that produced this section was still worth
making — it corrected "dead" to something defensible, and it forced the test that turned an
unexamined absence into a measured one.

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

## A parameter can be present in the file and absent from the material (B326)

**5,415 of the 30,684 materials TF2 ships declare `$selfillum`, and this project read it in none of
them.** Not because the flag is unimplemented — `VmtMaterial.IsSelfIlluminated` has existed
throughout — but because those materials declare it inside a sub-block the reader did not descend
into:

```
"LightmappedGeneric"
{
	"$basetexture" "signs/exit"

	">=DX90"
	{
		"$selfillum" "1"
	}
}
```

A block named for a DirectX support level gates its keys on that level. The reader treated depth-two
keys as belonging to somebody else — correct for `Proxies`, correct for a patch's `replace`, and
wrong for these.

### The measurement, and what it corrected

Filed first from ONE material — `gold_player.vmt`, whose `$envmap` is gated this way while its
`$envmaptint` is not, so a golden corpse carried the tint of a reflection it had no cubemap for. The
filing guessed at the scope and guessed wrong in both directions. Walking every shipped material
settled it:

| block | materials | | key inside `>=DX90` | materials |
|---|---|---|---|---|
| `>=DX90` | 5,688 | | `$selfillum` | 5,415 |
| `<dx90` | 281 | | `$envmap` | 59 |
| `>=dx90_20b` | 10 | | `$envmaptint` | 58 |
| `<dx90_20b` | 5 | | `$selfillummask` | 53 |

```bash
dotnet run --project tools/Tf2DemoSalvage.Probe -c Release -- vmt-blocks ">=DX90"
```

Two corrections fell out of it. **The spellings guessed at — `>=DX80`, `>=DX70`, the `if($...)`
forms — appear nowhere in TF2's materials**; only those four exist. And **the cubemap that found the
gap is 59 materials against `$selfillum`'s 5,415**, so the defect worth naming is not the one that
was noticed.

### `<dx90` must stay refused

The symmetry is not decoration. The low blocks carry `$bumpmap` (60), `$baseTexture` (56),
`$outlinecolor` (54) and `$fallbackmaterial` (51) — a whole cheap-hardware path. Flattening every
sub-block would read those instead, swapping a common bug for a rarer and stranger one.

### `<Shader>_DX<n>` is a different mechanism, and the first reading of it was wrong

Written first as: *"Ignoring them happens to be right here because every one TF2 ships is a low-end
fallback."* That was an inference from the block NAMES — fallback sounds like low-end — and nothing
had been looked at inside one. It is wrong.

```bash
dotnet run --project tools/Tf2DemoSalvage.Probe -c Release -- vmt-blocks "LightmappedGeneric_DX9"
```

| inside `LightmappedGeneric_DX9` | materials |
|---|---|
| `$bumpmap` | 89 |
| `$envmaptint` | 84 |
| `$normalmapalphaenvmapmask` | 64 |
| `$envmap` / `$envmapcontrast` / `$envmapsaturation` | 49 each |
| `$parallaxmap` | 8 |

Bump maps, cubemap reflections and parallax — the DX9 path's features, not a cheap substitute for
them. About **520 of the ~800 blocks name DX9 or HDR_DX9**, and a Direct3D 11 renderer satisfies
both. Filed as B328.

The mechanism itself is a shader FALLBACK, which the SDK states plainly:

```cpp
SHADER_FALLBACK
{
    if( g_pHardwareConfig->GetDXSupportLevel() < 90 )
        return "LightmappedGeneric_DX8";
    return 0;
}
```

`lightmappedgeneric_dx9.cpp:139-145`, with `DEFINE_FALLBACK_SHADER( LightmappedGeneric,
LightmappedGeneric_DX8 )` registering the substitute (`lightmappedgeneric_dx8.cpp:21`). A VMT block
named for a shader supplies parameters for the material WHEN THAT SHADER IS THE ONE IN USE.

**What is NOT established:** no shader is registered as `LightmappedGeneric_DX9` anywhere in
`source-sdk-2013` — only helper types and functions carry that spelling — so 403 materials name a
block for a shader the published SDK does not declare. Either TF2's engine registers it (the SDK is
one branch's snapshot), or the material system matches these by some other rule.

**The BEHAVIOUR is settled by the shipped data regardless**, which is the useful move when the code
is closed. One material decides it:

```
// envmaptint_fix
"LightmappedGeneric"
{
	"$basetexture" "Tile/tilefloor018a"

	 "LightmappedGeneric_DX9"
	{
		"$bumpmap" "tile/tilefloor018a_normal"
		"$envmap" "env_cubemap"
		…
	}
}
```

Under "ignore these blocks", Valve authored a bump map that draws on no hardware at all. That is not
a tenable reading of shipped content, so the block applies and this project now takes it — level 90
and above, non-HDR, prefix matching the material's own shader.

### The effect on TF2 content is nil, and saying so is the point

`cp_process_final` is unchanged by the fix: 55 of 412 materials carry a cubemap and 16 are masked by
normal-map alpha, before and after, measured by disabling the rule and re-running. Every material
that loses a key to it is **Half-Life 2 content TF2 mounts** — `TILE/TILEFLOOR018A_C17`,
`PLASTER/PLASTERWALLPAPER006A_C17`, `MODELS/COMBINE_DROPSHIP/DROPSHIPSHEET`,
`MODELS/PROPS_VEHICLES/CAR002A_01`.

Worth writing down for two reasons. It is the honest scope of the change, against a measurement that
could easily have been reported as "403 materials fixed". And it locates the population that WOULD
be affected — a community map built on HL2 assets — which is exactly the kind of content this
corpus, ten official specimens and a pile of competitive matches, contains none of.

## Evidence

**Shipped data plus convention — NOT read-from-source, and the difference is load-bearing.**
`source-sdk-2013` publishes `shaderapidx9` and `stdshaders` but not the material system's VMT
loader, so the merge cannot be quoted from Valve. What is measured is which spellings exist in
30,684 files and what each contains; that `>=` means "at least this level" is the convention those
files are written against. The DirectX level this project reports is **95**, Source's own numbering
for shader model 3.0, chosen as a constant because this renderer has one backend — a
machine-dependent value would make a material's parameters vary by GPU.

**Not established:** whether the engine's own comparison is on the same scale for the `_20b`
suffixed forms, which are parsed here as "90, with a suffix that rides along". Ten materials use
`>=dx90_20b` and five use `<dx90_20b`; every reading that puts them at or below 95 gives the same
answer for this renderer, so the corpus cannot distinguish them.

## `$selfillummask`, and what "unimplemented" was hiding (B327)

Landing B326 made the parameter census go red in the same run, on a parameter nobody had seen:
`$selfillummask`. It had been in TF2's materials all along and in nothing this project could reach,
because **all 53 materials that declare one declare it inside a `>=DX90` block**.

It is not a new effect. Valve's shader writes the masked and unmasked cases as ONE expression:

```hlsl
float3 vSelfIllumMask = tex2D( SelfIllumMaskSampler, i.baseTexCoord.xy );
vSelfIllumMask = lerp( baseColor.aaa, vSelfIllumMask, g_SelfIllumMaskControl );
diffuseComponent = lerp( diffuseComponent, g_SelfIllumTint * albedo, vSelfIllumMask );
```

`vertexlit_and_unlit_generic_ps2x.fxc:441-443`. The control is 1 exactly when a mask is bound, so
the mask-less case collapses to `baseColor.aaa` — which is what this project already did. The fix
replaces the third argument of a lerp; everything else was already right.

**The parameter's own declaration is the clearest statement of it anywhere in the SDK**, and it is a
one-line comment rather than code: *"If we bind a texture here, it overrides base alpha (if any) for
self illum"* (`vertexlitgeneric_dx9.cpp:62`).

Four details that are easy to get wrong, all of them Valve's:

| detail | where |
|---|---|
| gated on `$selfillum` — a mask alone is inert | `vertexlitgeneric_dx9_helper.cpp:289` |
| sampled on the BASE coordinates, not its own set | `…ps2x.fxc:441` |
| full RGB, not one channel — glow can be tinted per channel | same |
| absent means "the base alpha decides", NOT "nothing glows" | the lerp's first argument |

The last is the one that would produce a plausible wrong picture: a resolver that substituted the
base texture for a missing mask would glow wherever the albedo is bright rather than wherever it is
transparent.

Measured: 15 of 412 materials on the map the render tests load.

### The general shape, worth carrying

**A census over what real data ASKS FOR is a different instrument from a coverage list of what the
SDK declares**, and only the first can catch a parameter that was unreachable. `SdkCoverageTests`
enumerates 489 shader parameters and could never have flagged this, because `$selfillummask` was
already in its denominator and already counted as "declared, not implemented" — indistinguishable
from the 400 others nobody has needed. What made it a finding was a real map asking for it and
nothing accounting for the request.

## Postscript: what the DirectX work says about reading a format at all

Three defects in one afternoon — B326, B327, B328 — and all three were the same shape: **a reader
that understood the syntax and stopped one level short of the semantics.**

The VMT parser handled nested blocks correctly. It knew a `Proxies` block was not the material's
keys, and it knew a patch's `replace` block was. What it did not know is that a *third* kind of
depth-two block exists — the conditional and the fallback — and the failure of that omission is
silent by construction: a key that never arrives is indistinguishable from a material that never
declared one.

**The measurement that finds this class of defect is a census of the CONTAINER, not of the
parameters.** "Which parameters do we implement" was answered and re-answered for months while
5,415 materials carried a `$selfillum` nobody could see. The question that found it was "what
structures do the shipped files contain that we do not read", and it took one probe over 30,684
files.

Worth generalising to the other formats this project reads. A `.mdl` has sections nothing here
opens; a BSP has lumps this renderer skips; a `.phy`'s Havok half is not read at all — and only the
last of those is written down as a known gap. The others are unknown unknowns of exactly the shape
`$selfillum` was.
