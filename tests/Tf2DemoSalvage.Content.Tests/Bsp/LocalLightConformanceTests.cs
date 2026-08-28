using System;
using System.Text.RegularExpressions;

using Tf2DemoSalvage.Content.Bsp;

namespace Tf2DemoSalvage.Content.Tests.Bsp;

/// <summary>
/// How the engine lights a model with local lights — written from the shaders before this project
/// passed any.
/// </summary>
/// <remarks>
/// **The gap this exists to close.** `LocalLights.AddTo` folds a map's nearest world lights into the
/// ambient cube. The engine keeps them separate, and the difference is not brightness — it is that
/// a cube has no direction to shade against. A light folded into a cube arrives from every side at
/// once, so a model takes no N·L falloff from it, casts no highlight from it, and a long wall lit
/// from one end shades evenly rather than falling off along its length.
///
/// **B170 is parked on this.** Measured in `docs/RISKS.md`: the viewmodel's cube is healthy at
/// 0.2344 and reaches the draw intact, our output is arithmetically correct for its inputs, and
/// `sun none` indoors means the phong gate — `sunColour.w > 0.5f` — is shut, so the weapon receives
/// no specular term at all. TF2's weapon metal reads 81–108 in the same room where ours reads 11–28.
/// Phong summed over local lights is the term that is missing; this suite is what says what "summed"
/// means before anything is written.
///
/// **Four shader facts, each cited, and none of them measured from our own data** — per
/// `docs/memory/read-the-spec-before-measuring-our-data.md`, measuring this project first could only
/// have found that our numbers are self-consistent, which they are.
/// </remarks>
public sealed class LocalLightConformanceTests
{
    private static readonly TimeSpan Limit = TimeSpan.FromSeconds(10);

    private const string ModelLighting = "src/materialsystem/stdshaders/common_vertexlitgeneric_dx9.h";
    private const string VertexCommon = "src/materialsystem/stdshaders/common_vs_fxc.h";

    /// <summary>That the engine carries FOUR, which is what this project's cap must match.</summary>
    /// <remarks>
    /// `PixelShaderDoLightingLinear` nests `nNumLights > 0` through `> 3`, and the fourth light is
    /// packed into the `.w` channels of the other three's constants rather than getting a slot of
    /// its own — which is itself the evidence that four is the ceiling and not an arbitrary sample.
    /// </remarks>
    [Test]
    public void Sdk_ModelLighting_SumsAtMostFourLocalLights()
    {
        string source = Sdk(ModelLighting);

        source.ShouldContain("if ( nNumLights > 3 )");
        source.ShouldNotContain("if ( nNumLights > 4 )");

        LocalLights.MaximumLocalLights.ShouldBe(4);
    }

    /// <summary>That each light is a colour times an attenuation times N·L, added.</summary>
    /// <remarks>
    /// `PixelShaderDoGeneralDiffuseLight` ends `return vColor * fAtten * DiffuseTerm(...)`, and the
    /// caller accumulates with `linearColor +=`. **Added, never blended** — which is why a local
    /// light can only ever brighten, and why folding one into a cube cannot be corrected by scaling
    /// the cube down.
    /// </remarks>
    [Test]
    public void Sdk_EachLocalLight_IsColourTimesAttenuationTimesTheDiffuseTerm()
    {
        string source = Flat(Sdk(ModelLighting));

        source.ShouldContain("return vColor * fAtten * DiffuseTerm(");
        source.ShouldContain("linearColor += PixelShaderDoGeneralDiffuseLight(");
    }

    /// <summary>That the diffuse term is a saturated Lambert unless the material asks otherwise.</summary>
    /// <remarks>
    /// `DiffuseTerm` returns `saturate( NDotL )` normally; `$halflambert` replaces it with
    /// `saturate(NDotL * 0.5 + 0.5)` **squared**. The squaring is the part a reimplementation
    /// forgets, and without it a half-Lambert model is far too bright on its dark side.
    /// </remarks>
    [Test]
    public void Sdk_TheDiffuseTerm_IsSaturatedLambertOrSquaredHalfLambert()
    {
        string source = Flat(Sdk(ModelLighting));

        source.ShouldContain("fResult = saturate( NDotL );");
        source.ShouldContain("fResult = saturate(NDotL * 0.5 + 0.5);");
        source.ShouldContain("fResult *= fResult;");
    }

    /// <summary>That attenuation is Valve's constant, linear and quadratic denominator.</summary>
    /// <remarks>
    /// `VertexAttenInternal`: `1.0f / dot( cLightInfo[n].atten.xyz, vDist )` where `vDist` is
    /// `dst(distSquared, 1/dist)` = `(1, d, d²)`. So the denominator is `a0 + a1·d + a2·d²` — the
    /// three terms a `dworldlight_t` carries, which is why this project's
    /// <see cref="LocalLights"/> already computes exactly that shape.
    ///
    /// **A directional light bypasses the whole thing**: `lerp( flAtten, 1.0f, color.w )` selects a
    /// flat 1 when the light is directional, which is why the sun is on its own path here and must
    /// not become a local light.
    /// </remarks>
    [Test]
    public void Sdk_Attenuation_IsOneOverConstantPlusLinearPlusQuadratic()
    {
        string source = Flat(Sdk(VertexCommon));

        source.ShouldContain("float flDistanceAtten = 1.0f / dot( cLightInfo[lightNum].atten.xyz, vDist );");
        source.ShouldContain("vDist = dst( lightDistSquared, ooLightDist );");
        source.ShouldContain("result = lerp( flAtten, 1.0f, cLightInfo[lightNum].color.w );");
    }

    /// <summary>That the specular term is computed per light, with that light's attenuation.</summary>
    /// <remarks>
    /// **This is the one B170 turns on.** `PixelShaderDoSpecularLight` passes
    /// `vLightColor * fAtten` into `SpecularAndRimTerms`, and it is called once per light — so a
    /// model's highlight is a sum over the local lights and not a single term driven by the sun.
    /// A renderer that gates phong on the sun alone produces no highlight indoors, which is exactly
    /// what this project does today and what the measurement in B170 records.
    /// </remarks>
    [Test]
    public void Sdk_TheSpecularTerm_IsSummedPerLightWithThatLightsAttenuation()
    {
        string source = Flat(Sdk(ModelLighting));

        source.ShouldContain("bDoSpecularWarp, specularWarpSampler, fFresnel, vLightColor * fAtten,");

        // The rim term is masked by the same N·L, so it is per-light too rather than ambient.
        source.ShouldContain("rimLighting *= saturate(dot( vWorldNormal, vLightDir ));");
    }

    /// <summary>That the cube and the local lights are ADDED, so neither replaces the other.</summary>
    /// <remarks>
    /// `PixelShaderDoLightingLinear` does `linearColor += ambient` and then `linearColor +=` each
    /// light. **So a light must be in exactly one of the two.** Folding it into the cube AND
    /// passing it separately would double it — which is the specific mistake this suite exists to
    /// stop, because the result looks like a lighting change rather than like a bug.
    /// </remarks>
    [Test]
    public void Sdk_TheAmbientCubeAndTheLocalLights_AreAddedRatherThanBlended()
    {
        string source = Flat(Sdk(ModelLighting));

        source.ShouldContain("linearColor += ambient;");
        source.ShouldContain("float3 linearColor = 0.0f;");
    }

    private static string Sdk(string relativePath) =>
        Skip.Unless(SourceSdk.Text(relativePath), SourceSdk.Missing);

    private static string Flat(string source) =>
        Regex.Replace(source, @"[ \t]+", " ", RegexOptions.None, Limit);
}
