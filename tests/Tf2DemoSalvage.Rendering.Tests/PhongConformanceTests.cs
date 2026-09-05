using System;

using Tf2DemoSalvage.Content.Assets;
using Tf2DemoSalvage.SdkReference;

namespace Tf2DemoSalvage.Rendering.Tests;

/// <summary>
/// <c>$phong</c> — the model specular term, specified from the SDK before any of it is built.
/// </summary>
/// <remarks>
/// **The largest remaining rendering gap by the map's own count: 330 materials.** Every model in
/// this viewer reads dull, and this is why — TF2's characters and weapons get most of their
/// definition from a specular highlight that responds to the light, and none of it is drawn.
///
/// **Written before the implementation.** Every assertion here is a quotation from published source
/// or arithmetic on one. The reason is in `docs/CONFORMANCE.md`: *"Read `phong_dx9_helper` before
/// starting — the mask channel is chosen by `$basemapalphaphongmask` versus the normal map's alpha,
/// and picking the wrong one produces a plausible sheen in the wrong places."* A sheen in the wrong
/// places is not a visible error; it looks like art.
///
/// **Three things here invert the obvious reading**, which is the reason to write them down:
///
/// - The reflection vector is the **EYE** reflected through the normal, dotted with the light —
///   `L·R`, not the more familiar `R·L` with the light reflected. Symmetric, but the code is
///   explicit and the Fresnel beside it is not symmetric at all.
/// - `$phongfresnelranges` is **pre-encoded on the CPU** as `((mid-min)*2, mid, (max-mid)*2)`, so
///   the shader's three constants are not the three numbers in the VMT.
/// - `$phongalbedotint` does nothing without `$phongexponenttexture`, because the tint is read from
///   that texture's green channel.
/// </remarks>
public sealed class PhongConformanceTests
{
    [SetUp]
    public void RequireTheSdk()
    {
        if (!SourceSdk.Available)
        {
            Assert.Ignore(SourceSdk.Missing);
        }
    }

    [Test]
    public void Phong_TheSpecularTerm_ReflectsTheEyeAndDotsTheLight()
    {
        // `SpecularAndRimTerms`, common_vertexlitgeneric_dx9.h:167 — the whole term in four lines:
        //
        //     float3 vReflect = 2 * vWorldNormal * dot( vWorldNormal , vEyeDir ) - vEyeDir;
        //     float LdotR = saturate(dot( vReflect, vLightDir ));
        //     specularLighting = pow( LdotR, fSpecularExponent );
        //     specularLighting *= saturate(dot( vWorldNormal, vLightDir ));   // Mask with N.L
        //     specularLighting *= color;                                      // light colour
        //
        // **The N·L mask is the half that is easy to drop**, and dropping it lights the side of a
        // model that faces away from the light — a highlight in a place no light reaches, which
        // reads as a material property rather than as a bug.
        //
        // Note the reflection is of the EYE, not the light. Valve left the equivalent
        // `reflect( -vEyeDir, vWorldNormal )` commented out on the line above, which is the same
        // vector written the other way.
        string source = Sdk("src/materialsystem/stdshaders/common_vertexlitgeneric_dx9.h");

        source.ShouldContain(
            "float3 vReflect = 2 * vWorldNormal * dot( vWorldNormal , vEyeDir ) - vEyeDir;",
            Case.Sensitive,
            "the eye is reflected through the normal");

        source.ShouldContain("float LdotR = saturate(dot( vReflect, vLightDir ));", Case.Sensitive);
        source.ShouldContain("specularLighting = pow( LdotR, fSpecularExponent );", Case.Sensitive);

        source.ShouldContain(
            "specularLighting *= saturate(dot( vWorldNormal, vLightDir ));",
            Case.Sensitive,
            "masked by N.L, so a surface facing away from the light gets none");
    }

    [Test]
    public void Phong_TheMaskChannel_IsChosenByBaseMapAlphaPhongMask()
    {
        // **The trap docs/CONFORMANCE.md names, and it is a lerp rather than a branch**
        // (skin_ps20b.fxc:199):
        //
        //     tangentSpaceNormal = lerp( 2.0f * normalTexel.xyz - 1.0f, float3(0, 0, 1), g_fBaseMapAlphaPhongMask );
        //     fSpecMask = lerp( normalTexel.a, baseColor.a, g_fBaseMapAlphaPhongMask );
        //
        // So the flag picks BOTH the mask channel and whether the normal map is used at all — which
        // its own declaration says outright: "indicates that there is no normal map and that the
        // phong mask is in base alpha".
        //
        // Reading the wrong channel produces a plausible sheen in the wrong places. Reading base
        // alpha where the normal map's alpha was meant is worse than a missing highlight, because a
        // base texture's alpha is usually opacity or a self-illum mask — so the model shines in
        // whatever pattern that channel happens to hold.
        string shader = Sdk("src/materialsystem/stdshaders/skin_ps20b.fxc");
        string declaration = Sdk("src/materialsystem/stdshaders/vertexlitgeneric_dx9.cpp");

        shader.ShouldContain(
            "fSpecMask = lerp( normalTexel.a, baseColor.a, g_fBaseMapAlphaPhongMask );",
            Case.Sensitive,
            "normal-map alpha by default, base alpha when the flag is set");

        declaration.ShouldContain(
            "\"indicates that there is no normal map and that the phong mask is in base alpha\"",
            Case.Sensitive,
            "the flag means more than which channel: it means there is no normal map");
    }

    [Test]
    public void Phong_TheMaskIsScaledByFresnelAndBoost()
    {
        // skin_ps20b.fxc:312 and :315 — the last two things done to the term:
        //
        //     fSpecMask *= fFresnelRanges;
        //     specularLighting *= fSpecMask * g_SpecularBoost;
        //
        // `g_SpecularBoost` is `$phongboost`, whose declaration says the mask is authored to
        // account for it — so boost and mask are one calibration and applying only one of them is
        // not "half right", it is a different material.
        string shader = Sdk("src/materialsystem/stdshaders/skin_ps20b.fxc");
        string declaration = Sdk("src/materialsystem/stdshaders/vertexlitgeneric_dx9.cpp");

        shader.ShouldContain("fSpecMask *= fFresnelRanges;", Case.Sensitive);
        shader.ShouldContain("specularLighting *= fSpecMask * g_SpecularBoost;", Case.Sensitive);

        declaration.ShouldContain(
            "SHADER_PARAM( PHONGBOOST, SHADER_PARAM_TYPE_FLOAT, \"1.0\"",
            Case.Sensitive,
            "boost defaults to 1");

        declaration.ShouldContain(
            "SHADER_PARAM( PHONGEXPONENT, SHADER_PARAM_TYPE_FLOAT, \"5.0\"",
            Case.Sensitive,
            "the exponent's DECLARED default is 5 — and nothing reads it; see below");

        // **Ours, parsed from those same declarations rather than restated.** A default reaches the
        // shader for every material that omits the key, which is most of them — so a wrong default
        // is a rendering error on almost every model rather than an edge case.
        VmtMaterial bare = Vmt("$phong 1");

        bare.PhongBoost.ShouldBe(Declared(declaration, "PHONGBOOST"), 1e-5f);

        // **The exponent is the exception, and this assertion USED to read the same way** (B334).
        // It asserted `bare.PhongExponent == Declared(…, "PHONGEXPONENT")` — 5 — on the reasoning
        // in the paragraph above, which is correct for `$phongboost` and false for this one. The
        // helper never consults the declared default:
        //
        //     vEyePos_SpecExponent[3] = -1.f;
        //     if ( … params[info.m_nPhongExponent]->IsDefined() )
        //         if ( fValue > 0.f ) vEyePos_SpecExponent[3] = fValue;
        //
        // (`skin_dx9_helper.cpp:815-828`) and, with no `$phongexponenttexture`, binds
        // `TEXTURE_WHITE` to that sampler (`:560-567`) — so the shader takes its other branch and
        // computes `1 + 149 * 1`. **150, not 5**, and a factor of thirty is a tight point against a
        // broad wash.
        //
        // **The declaration is still asserted above**, because the point is not that 5 is wrong: it
        // is that a declared default and an effective default are different facts, and this file
        // had been treating them as one.
        shader.ShouldContain(
            "fSpecExp = (g_EyePos_SpecExponent.w >= 0.0) ? g_EyePos_SpecExponent.w : (1.0f + 149.0f * vSpecExpMap.r);",
            Case.Sensitive,
            "the branch that makes the effective default 150 rather than the declared 5");

        bare.PhongExponent.ShouldBe(150f, 1e-5f, "1 + 149 x white, which is what the engine binds");

        // The control: a material that STATES a value must override the default, or the two
        // assertions above are satisfied by an accessor that ignores the VMT entirely.
        Vmt("$phongboost 12").PhongBoost.ShouldBe(12f, 1e-5f);
        Vmt("$phong 1\n\t$phongexponent 20").PhongExponent.ShouldBe(20f, 1e-5f);
    }

    [Test]
    public void Phong_TheFresnelRanges_ArePreEncodedBeforeTheShaderSeesThem()
    {
        // **The three numbers in the VMT are not the three constants the shader reads**, and Valve
        // says so in a comment beside the code (common_vertexlitgeneric_dx9.h:229):
        //
        //     // note: vRanges is now encoded as ((mid-min)*2, mid, (max-mid)*2) to optimize math
        //     float f = saturate( 1 - dot( vNormal, vEyeDir ) );
        //     f = f*f - 0.5;
        //     return vRanges.y + (f >= 0.0 ? vRanges.z : vRanges.x) * f;
        //
        // The commented-out original above it is the readable form: a piecewise blend from low to
        // mid over the first half of the traditional Fresnel and mid to high over the second.
        //
        // Feeding the raw [low mid high] into the optimised expression is silently wrong rather
        // than obviously wrong: at the default [0 0.5 1] it returns 0.5 ± 0.5·f instead of the
        // full 0..1 sweep, so every model is uniformly half-lit and nothing looks broken.
        string source = Sdk("src/materialsystem/stdshaders/common_vertexlitgeneric_dx9.h");
        string declaration = Sdk("src/materialsystem/stdshaders/vertexlitgeneric_dx9.cpp");

        source.ShouldContain(
            "vRanges is now encoded as ((mid-min)*2, mid, (max-mid)*2)",
            Case.Sensitive,
            "the encoding, stated by Valve rather than inferred");

        source.ShouldContain(
            "return vRanges.y + (f >= 0.0 ? vRanges.z : vRanges.x) * f;",
            Case.Sensitive);

        declaration.ShouldContain(
            "SHADER_PARAM( PHONGFRESNELRANGES, SHADER_PARAM_TYPE_VEC3, \"[0  0.5  1]\"",
            Case.Sensitive,
            "and the default the encoding is applied to");

        // **OURS does the encoding, and this is where that is checked.** The local `Encode` helper
        // below used to compute the triple and the test compared it against itself — Valve's
        // formula transcribed twice, agreeing with itself, while `VmtMaterial.PhongFresnelRanges`
        // (the code that actually feeds the shader) was never called.
        //
        // Two materials: one stating the ranges explicitly, one omitting them, because the default
        // path and the parse path are different code and the trap applies to both.
        (float Low, float Mid, float High) encoded = Vmt("$phongfresnelranges \"[0 0.5 1]\"")
            .PhongFresnelRanges;

        encoded.Low.ShouldBe(1f);
        encoded.Mid.ShouldBe(0.5f);
        encoded.High.ShouldBe(1f);

        // Absent, the declared default is the same triple — so the encoded result must match.
        Vmt("$phong 1").PhongFresnelRanges.ShouldBe(encoded);

        // And a non-default triple, because [0 0.5 1] is symmetric: (mid-min)*2 and (max-mid)*2 are
        // both 1 there, so it cannot tell the two halves apart. [0.1 0.4 1.0] gives 0.6 and 1.2,
        // which a swapped or duplicated term would fail.
        (float Low, float Mid, float High) asymmetric =
            Vmt("$phongfresnelranges \"[0.1 0.4 1.0]\"").PhongFresnelRanges;

        asymmetric.Low.ShouldBe(0.6f, 1e-5f);
        asymmetric.Mid.ShouldBe(0.4f, 1e-5f);
        asymmetric.High.ShouldBe(1.2f, 1e-5f);

        Ranged(1f, encoded).ShouldBe(0f, 1e-6f);    // head-on:  dot = 1, f = 0
        Ranged(0f, encoded).ShouldBe(1f, 1e-6f);    // grazing:  dot = 0, f = 1

        // **And the condition that shows why the encoding cannot be skipped, which is the HEAD-ON
        // one alone.** Fed the raw triple the low term becomes 0, so the whole downward half of the
        // curve collapses: a surface facing the eye returns 0.5 where it should return 0, and the
        // highlight never fades out. Grazing returns 1 either way, so that row would discriminate
        // nothing and is not asserted — the first draft of this test predicted 0.25 and 0.75 for
        // these two and was simply wrong about both.
        (float Low, float Mid, float High) raw = (0f, 0.5f, 1f);

        Ranged(1f, raw).ShouldBe(0.5f, 1e-6f);

        Ranged(1f, raw).ShouldNotBe(
            Ranged(1f, encoded), "the encoding is what makes a head-on surface unlit");

        // `Encode` used to live here and is gone: VmtMaterial.PhongFresnelRanges does the encoding
        // now and is what the assertions above call. `Ranged` stays, because it is Valve's SHADER
        // expression rather than ours — the encoded triple has to be fed through it to show what
        // the encoding buys, and no C# of ours evaluates that.
        static float Ranged(float dot, (float Low, float Mid, float High) ranges)
        {
            float f = Math.Clamp(1f - dot, 0f, 1f);
            f = (f * f) - 0.5f;

            return ranges.Mid + ((f >= 0f ? ranges.High : ranges.Low) * f);
        }
    }

    [Test]
    public void Phong_TheAlbedoTint_NeedsAnExponentTexture()
    {
        // **`$phongalbedotint` alone does nothing**, which is worth an assertion because its name
        // reads like a switch that tints by the albedo and it is not:
        //
        //     bool bHasPhongTintMap = bHasSpecularExponentTexture &&
        //         (info.m_nPhongAlbedoTint != -1) && ( params[info.m_nPhongAlbedoTint]->GetIntValue() != 0 );
        //
        // (skin_dx9_helper.cpp:252). The tint is read from the exponent texture's GREEN channel —
        // `vSpecularTint = lerp( float3(1,1,1), baseColor.rgb, vSpecExpMap.g )` — so without
        // `$phongexponenttexture` there is no map to read and the term stays white.
        //
        // An implementation that honours the boolean on its own tints every phong highlight by the
        // base texture, which is a large and entirely invented effect.
        string helper = Sdk("src/materialsystem/stdshaders/skin_dx9_helper.cpp");
        string shader = Sdk("src/materialsystem/stdshaders/skin_ps20b.fxc");

        helper.ShouldContain(
            "bool bHasPhongTintMap = bHasSpecularExponentTexture &&",
            Case.Sensitive,
            "the tint needs the exponent texture, not just the flag");

        shader.ShouldContain(
            "vSpecularTint = lerp( float3(1.0f, 1.0f, 1.0f), baseColor.rgb, vSpecExpMap.g );",
            Case.Sensitive,
            "and it comes from that texture's green channel");
    }

    [Test]
    public void Phong_TheResult_IsAddedToTheDiffuse()
    {
        // The same composition as $envmap, and for the same reason: a highlight makes a surface
        // brighter. skin_ps20b.fxc:365 —
        //
        //     float3 result = specularLighting*vSpecularTint + envMapColor + diffuseComponent;
        //
        // Addition is also what makes "no phong" correct rather than merely absent: the term starts
        // black, so a material without it adds nothing.
        Sdk("src/materialsystem/stdshaders/skin_ps20b.fxc").ShouldContain(
            "float3 result = specularLighting*vSpecularTint + envMapColor + diffuseComponent;",
            Case.Sensitive);
    }

    [Test]
    public void RimLight_IsFoldedIntoTheSpecularByMaxRatherThanAdded()
    {
        // **The composition, and it is the surprise.** Rim light is NOT another additive term —
        // Valve folds it into the specular with `max`, and says why (skin_ps20b.fxc:359):
        //
        //     // Fold rim lighting into specular term by using the max so that we don't really add light twice...
        //     specularLighting = max( specularLighting, rimLighting );
        //
        // Adding instead double-counts wherever a highlight and a rim overlap, which is the
        // silhouette of anything shiny — precisely where both are strongest. It reads as a blown
        // edge rather than as a wrong operator.
        //
        // **Then a second term IS added, and it is not driven by the light at all**:
        //
        //     specularLighting += (vRimAmbientCubeColor * g_fRimBoost) * saturate(fRimMultiply * worldSpaceNormal.z);
        //
        // `vRimAmbientCubeColor` is the ambient cube sampled along the EYE direction
        // (`PixelShaderAmbientLight(vEyeDir, cAmbientCube)`), so a model picks up its surroundings
        // on the rim even with no direct light on it. The `worldSpaceNormal.z` is an upward bias:
        // the sky end of the cube contributes most on upward-facing edges.
        //
        // That second term matters more here than in the engine, because this renderer gives a
        // model one directional light and TF2 gives it several.
        string shader = Sdk("src/materialsystem/stdshaders/skin_ps20b.fxc");

        shader.ShouldContain(
            "specularLighting = max( specularLighting, rimLighting );",
            Case.Sensitive,
            "max, not add — Valve's own comment says why");

        shader.ShouldContain(
            "float3 vRimAmbientCubeColor = PixelShaderAmbientLight(vEyeDir, cAmbientCube);",
            Case.Sensitive,
            "the ambient half is sampled along the eye, not the normal");

        shader.ShouldContain(
            "specularLighting += (vRimAmbientCubeColor * g_fRimBoost) * saturate(fRimMultiply * worldSpaceNormal.z);",
            Case.Sensitive);
    }

    [Test]
    public void RimLight_UsesTheFourthPowerFresnel_NotTheRanges()
    {
        // **Two Fresnels in one shader, and the rim uses the other one.** The specular mask is
        // scaled by `Fresnel( N, V, g_FresnelRanges )` — the piecewise remap — and the rim by
        // `Fresnel4( N, V )`, which is the traditional term squared twice and takes no parameters:
        //
        //     float fresnel = saturate( 1 - dot( vNormal, vEyeDir ) );
        //     fresnel = fresnel * fresnel;   // Square
        //     return fresnel * fresnel;      // Square again for a more subtle look
        //
        // Valve annotates the rim's use of it: "modulated with tint, mask and traditional Fresnel
        // (not using Fresnel ranges)". Using the ranged one for the rim would let a material's
        // $phongfresnelranges widen its silhouette light, which is not a control the artist has.
        //
        // The exponents differ too and both are declared: rim 4.0, phong 5.0.
        string source = Sdk("src/materialsystem/stdshaders/common_vertexlitgeneric_dx9.h");
        string shader = Sdk("src/materialsystem/stdshaders/skin_ps20b.fxc");
        string declaration = Sdk("src/materialsystem/stdshaders/vertexlitgeneric_dx9.cpp");

        source.ShouldContain("float Fresnel4( const float3 vNormal, const float3 vEyeDir )", Case.Sensitive);
        shader.ShouldContain("float fRimFresnel = Fresnel4( worldSpaceNormal, vEyeDir );", Case.Sensitive);

        shader.ShouldContain(
            "not using Fresnel ranges",
            Case.Sensitive,
            "annotated, so this is not an inference from which function was called");

        declaration.ShouldContain(
            "SHADER_PARAM( RIMLIGHTEXPONENT, SHADER_PARAM_TYPE_FLOAT, \"4.0\"", Case.Sensitive);

        declaration.ShouldContain(
            "SHADER_PARAM( RIMLIGHTBOOST, SHADER_PARAM_TYPE_FLOAT, \"1.0\"", Case.Sensitive);

        // **Ours, and the pair is what matters: rim 4 against phong 5.** Reading either alone
        // cannot catch the two being swapped, and swapping them is the plausible mistake — the
        // declarations sit beside each other and differ by one.
        VmtMaterial bare = Vmt("$phong 1\n\t$rimlight 1");

        bare.RimLightExponent.ShouldBe(Declared(declaration, "RIMLIGHTEXPONENT"), 1e-5f);
        bare.RimLightBoost.ShouldBe(Declared(declaration, "RIMLIGHTBOOST"), 1e-5f);

        bare.RimLightExponent.ShouldNotBe(
            bare.PhongExponent, "the two exponents differ, and that is the point of the pair");

        // **And the engine's own dispatch, which is a rule rather than a default.** Rim lighting
        // lives in the Skin shader, which VertexLitGeneric reaches only when $phong is set — so a
        // material asking for a rim and no phong gets neither.
        Vmt("$rimlight 1").HasPhong.ShouldBeFalse();
        Vmt("$phong 1\n\t$rimlight 1").HasPhong.ShouldBeTrue();

        // The fourth power against the square, at a mid angle: the rim's falls off much faster,
        // which is what "more subtle" means and why swapping them is visible.
        Fourth(0.5f).ShouldBe(0.0625f, 1e-6f);
        Square(0.5f).ShouldBe(0.25f, 1e-6f);

        static float Square(float dot)
        {
            float f = Math.Clamp(1f - dot, 0f, 1f);
            return f * f;
        }

        static float Fourth(float dot)
        {
            float f = Square(dot);
            return f * f;
        }
    }

    [Test]
    public void LightWarp_ReplacesTheHalfLambertSquare_AndIsDoubled()
    {
        // **`$lightwarptexture` is not a tint applied afterwards; it REPLACES part of the falloff**,
        // and `DiffuseTerm` (common_vertexlitgeneric_dx9.h:86) shows both halves of that:
        //
        //     if ( bHalfLambert )
        //     {
        //         fResult = saturate(NDotL * 0.5 + 0.5);
        //         if ( !bDoLightingWarp )
        //             fResult *= fResult;          // Square
        //     }
        //     ...
        //     if ( bDoLightingWarp )
        //         fOut = 2.0f * tex1D( lightWarpSampler, fResult );
        //
        // **The square is SKIPPED when a warp is present**, because the warp texture is authored to
        // carry that curve already. Applying both squares the falloff twice, which darkens every
        // shaded side and reads as heavy-handed art rather than as a bug.
        //
        // **And the lookup is DOUBLED.** `2.0f * tex1D(...)`, so a warp of mid-grey is neutral and
        // a warp of white is twice the light. Missing the factor of two halves every model's
        // diffuse — uniformly, so nothing looks wrong, only dim.
        //
        // The dot product going in is UNSATURATED (−1 to 1) before the half-Lambert scale and bias;
        // saturating first would collapse the whole back half of the ramp, which is the half a warp
        // texture exists to shape.
        string source = Sdk("src/materialsystem/stdshaders/common_vertexlitgeneric_dx9.h");

        source.ShouldContain(
            "float NDotL = dot( worldNormal, lightDir );",
            Case.Sensitive,
            "unsaturated, and the comment beside it says so");

        source.ShouldContain(
            "if ( !bDoLightingWarp )",
            Case.Sensitive,
            "the half-Lambert square is conditional on there being NO warp");

        source.ShouldContain(
            "fOut = 2.0f * tex1D( lightWarpSampler, fResult );",
            Case.Sensitive,
            "and the lookup is doubled");

        // **The arithmetic, at the input that separates the two — which is not the obvious one.** A
        // surface turned FULLY away gives 0 either way, squared or not, so it discriminates nothing;
        // the first draft of this asserted 0.25 and 0.5 there and was wrong about both.
        //
        // The terminator, N·L = 0, is where they differ: 0.25 with the square and 0.5 without. A
        // warp read at 0.25 samples a different part of the ramp from one read at 0.5, so keeping
        // the square is not a scale error, it is the wrong texel.
        Warped(0f, squared: true).ShouldBe(0.25f, 1e-6f);
        Warped(0f, squared: false).ShouldBe(0.5f, 1e-6f);

        Warped(-1f, squared: true).ShouldBe(Warped(-1f, squared: false), 1e-6f);

        static float Warped(float dot, bool squared)
        {
            float result = Math.Clamp((dot * 0.5f) + 0.5f, 0f, 1f);

            return squared ? result * result : result;
        }
    }

    /// <summary>Reads an SDK file, or fails loudly.</summary>
    private static string Sdk(string path) =>
        SourceSdk.Text(path) ?? throw new InvalidOperationException($"{path} is missing from the SDK");

    /// <summary>The default a <c>SHADER_PARAM</c> declares, parsed rather than restated.</summary>
    /// <remarks>
    /// Taking the number from Valve's declaration is what makes the comparison meaningful: writing
    /// <c>5f</c> into the test would pin what somebody believed the default was, and the whole
    /// hazard here is that a default reaches the shader for every material that omits the key.
    /// </remarks>
    private static float Declared(string source, string parameter)
    {
        System.Text.RegularExpressions.Match match = System.Text.RegularExpressions.Regex.Match(
            source,
            @"SHADER_PARAM\(\s*" + parameter + @"\s*,\s*SHADER_PARAM_TYPE_FLOAT\s*,\s*""(?<value>[-0-9.]+)""",
            System.Text.RegularExpressions.RegexOptions.None,
            TimeSpan.FromSeconds(5));

        match.Success.ShouldBeTrue($"{parameter} was not declared as a float in the shader");

        return float.Parse(
            match.Groups["value"].Value, System.Globalization.CultureInfo.InvariantCulture);
    }

    /// <summary>A VertexLitGeneric material with the given body.</summary>
    private static VmtMaterial Vmt(string body) =>
        VmtMaterial.Parse(
            System.Text.Encoding.UTF8.GetBytes($"\"VertexLitGeneric\"\n{{\n\t{body}\n}}\n"));
}
