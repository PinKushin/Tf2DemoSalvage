# Material modulation — the tint that reached one shader out of many

`$color` and `$alpha` scale everything a material draws. On `cp_process_final` 34 of 410 resolved
materials declare one, including `overlays/dust_gradient01` and `02` — the soft haze that hangs in
the map's lit rooms. Drawn without their modulation they are not haze at all; they are opaque grey
sheets.

This file is about three things: what the engine actually does with those two parameters, a
one-word placement mistake that limited them to a single shader, and a measuring instrument that
accused the code twice in the same shape and was wrong both times.

## What the engine does (evidence: read from published source)

`CBaseVSShader::ColorVarsToVector`, `BaseVSShader.cpp:677-698`, is the published half of the
modulation path:

```cpp
color.Init( 1.0, 1.0, 1.0, 1.0 );
if ( colorVar != -1 )
{
    IMaterialVar* pColorVar = s_ppParams[colorVar];
    if ( pColorVar->GetType() == MATERIAL_VAR_TYPE_VECTOR )
        pColorVar->GetVecValue( color.Base(), 3 );
    else
        color[0] = color[1] = color[2] = pColorVar->GetFloatValue();
}
if ( alphaVar != -1 )
{
    float flAlpha = s_ppParams[alphaVar]->GetFloatValue();
    color[3] = clamp( flAlpha, 0.0f, 1.0f );
}
```

Four behaviours come out of it, and each is wrong under the obvious reading of the parameter name.

**A scalar `$color` is legal and broadcasts.** The branch is on the material var's *type*, and a
value written without brackets is a float var rather than a vector one. `"$color" "0.5"` means half
brightness on all three channels. This project's colour reader accepted only the bracketed triple
and raised `InvalidDataException` on anything else — which would have cost the caller the whole
material rather than the tint.

**Alpha is clamped and colour is not.** There is no matching clamp on the three colour channels, and
that asymmetry is deliberate rather than an oversight. `SetModulationPixelShaderDynamicState_LinearColorSpace`
at line 652 reads:

```cpp
color[0] = color[0] > 1.0f ? color[0] : GammaToLinear( color[0] );
```

A test of `> 1.0f` only has a meaning for a channel allowed to exceed one. Over-bright modulation is
how a material is made to glow, and clamping it caps the glow silently.

**`$color2` multiplies rather than replaces.** `BaseShader.h:271` states the operation on the
declaration of its own helper, which is as unambiguous as this gets:

```cpp
void ApplyColor2Factor( float *pColorOut ) const;    // (*pColorOut) *= COLOR2
```

**Absent means one, on all four channels.** `color.Init( 1.0, 1.0, 1.0, 1.0 )` before anything is
read. That is the same lesson as
[sentinels conflate unknown with answer](../memory/sentinels-conflate-unknown-with-answer.md): on
the wire and in a material file alike, absent means the default, never zero.

`ComputeModulationColor` itself lives in the closed shaderlib, so what is reproduced here is the
published conversion it is built on, not the whole engine path. The render state it feeds — a fading
entity's alpha, a per-instance colour — is not a material property and does not belong in a material
reader.

## The placement bug: `* modulation` inside a ternary

The renderer already had a modulation constant, already uploaded it, and already multiplied by it.
It did so here:

```hlsl
float4 albedo = combine.x > 0.5f
    ? first * second * modulation
    : lerp(first, second, saturate(input.a));
```

`combine.x` selects between UnLitTwoTexture's multiply and a blend material's vertex-alpha mix. The
multiply is the branch Valve's own line describes — `baseColor * baseColor2 * g_DiffuseModulation` —
so the modulation was written where the citation was found, inside that branch.

But `g_DiffuseModulation` is not a two-texture idea. `LightmappedGeneric`, `VertexLitGeneric` and
`UnlitGeneric` all fold it into albedo the same way. Sitting inside the ternary it reached exactly
the materials drawn by one shader and no others, so every ordinary tinted or faded surface had its
colour decoded, uploaded into the constant buffer, and then multiplied by nothing.

The fix is to take it out of the branch:

```hlsl
float4 albedo = combine.x > 0.5f
    ? first * second
    : lerp(first, second, saturate(input.a));

albedo *= modulation;
```

**Why this is worth writing down: the citation was correct and the conclusion drawn from it was
not.** Valve's line genuinely is `baseColor * baseColor2 * g_DiffuseModulation`, and it genuinely is
UnLitTwoTexture's. What it does not say is that the modulation belongs *only* there — a shader's
source states what that shader does, and says nothing about the others. Reading one shader and
generalising the shape of its expression is how a correct quotation becomes a wrong implementation.

There was a second half to the same bug, in the upload rather than the shader: the rest value of the
modulation constant was a hardcoded `1,1,1,1`, with a comment explaining that a material proxy
overwrites it per frame. True, and it meant a material declaring a tint and owning no proxy had its
value overwritten with white on the way in. Both ends had to be right for either to matter.

## The instrument that accused the code twice

`SdkCoverageTests` cross-checks the census's implemented list against the SDK: anything claimed as
implemented that Source never declares is a typo, and a typo there is invisible, because the census
then stops reporting a name no material will ever use.

It failed on `$color`, `$color2` and `$alpha`:

```
these are claimed as implemented but Source declares no such shader parameter: $color, $color2, $alpha
```

The accusation is false, and the header says why. These are not `SHADER_PARAM` declarations. They
are members of `ShaderMaterialVars_t` in `public/shaderlib/BaseShader.h:32` — **standard** vars,
registered once by the material system for every shader, which is precisely why no shader declares
them:

```cpp
// Note: if you add to these, add to s_StandardParams in CBaseShader.cpp
enum ShaderMaterialVars_t
{
    FLAGS = 0,
    ...
    COLOR,
    ALPHA,
    ...
    COLOR2,
    SRGBTINT,
```

**This is the second time this test has made this exact mistake.** Its own comment records the
first: checking against `SHADER_PARAM` alone once reported eight of this project's parameters as
undeclared, and every one of the eight was a `MATERIAL_VAR_*` flag in `imaterial.h` rather than a
shader parameter. Adding the flags axis fixed those eight and left a third category unmodelled. The
engine splits material variables three ways; the inventory knew two.

The comment on the fix now says so in as many words, because the failure mode is that the test
reports a defect in the code with a message that reads like a fact about the engine. Compare
[an uncoverable gap is usually your reader](../memory/an-uncoverable-gap-is-usually-your-reader.md):
an exclusion that sounds like a fact about the format is usually a fact about your parser.

### The names are interpolated, and that is flagged

The enum is read from published source. The mapping from enum member to the string a material writes
— lowercase with a `$` — is **interpolated**, because `s_StandardParams` is in the closed
`CBaseShader.cpp`. Four of the thirteen are confirmed by string in shipped game code, which is
enough to fix the convention and is not a reading of the table:

| Member | Confirmed by |
|---|---|
| `ALPHA` | `FindVar( "$alpha", &foundVar, false )` — `alphamaterialproxy.cpp:42` |
| `COLOR` | `FindVar( "$color", &foundVar, false )` — `thermalmaterialproxy.cpp:50` |
| `COLOR2` | `SetString( "$color2", ... )` — `item_import.cpp:1328` |
| `BASETEXTURE` | every VMT ever written |

### The scrape needed a control before it could be believed

A generated denominator that finds nothing makes the cross-check pass *more* easily — an empty set
can only shrink the list of accusations — so a broken regex there looks exactly like a clean result.
That is [an empty search needs a control](../memory/an-empty-search-needs-a-control.md), which this
project has now been bitten by five times.

The control asserts both halves. It names `COLOR`, `COLOR2` and `ALPHA` individually, because a bare
count would pass on any thirteen capitalised identifiers the regex caught elsewhere in the file. And
it asserts that `BT_NONE` and `BT_BLEND` are **absent** — `BlendType_t` sits directly below
`ShaderMaterialVars_t` in the same header, and without the lookbehind that keeps matching inside the
enum body, the denominator quietly becomes "every capitalised identifier in the file" and never
accuses anything of anything again.

## What the tests measure, and what they cannot

- **`VmtModulationTests`** (16, synthetic, runs on the measurement box) — the arithmetic. Both
  spellings (`{255 128 0}` bytes against `[1 0.5 0]` floats, a factor of 255 apart and both valid),
  the clamp asymmetry from both sides, the multiply, and the identity as a control.
- **`UnimplementedRenderingConformanceTests`** (5, needs the SDK) — the same semantics stated against
  their citations, written before the implementation so they cannot be a description of it.
- **`ModulationWiringTests`** (3, needs TF2 installed) — the only one that can fail when the wiring
  is wrong. It loads `cp_process_final` through `MapAssets`, checks that 34 materials arrive
  carrying a modulation, that the other 376 arrive with none, and that each carried value matches
  its own VMT re-read independently from the archives.

That third one exists because of
[output-level assertion or it is not done](../memory/output-level-assertion-or-it-is-not-done.md).
A unit test proves a component works when the test calls it, and says nothing about whether
production calls it or with what — the gap that has shipped three no-ops in this project with a
green suite.

**The one thing none of them checks is the picture.** The shader change is HLSL; nothing in the
suite compiles or runs it, and a rectangle assertion is evidence about a rectangle. Whether the dust
gradients now read as haze is a question for someone looking at the screen, not a claim this
document is entitled to make.

## Consequences elsewhere

`$alpha` was already read by `VmtMaterial.IsTranslucent` — a value below one makes a material blend
— so the parameter was half-implemented in a way the census could not see: consumed for the
transparency decision, ignored for the colour. That is
[measure the output, not the capability](../memory/measure-the-output-not-the-capability.md) in its
quietest form, where the report is right about a name and wrong about what was done with it.
