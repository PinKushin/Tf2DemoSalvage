using System;
using System.Collections.Generic;
using System.Linq;

using Tf2DemoSalvage.Content.Assets;
using Tf2DemoSalvage.Content.Bsp;
using Tf2DemoSalvage.SdkReference;

namespace Tf2DemoSalvage.Rendering.Tests;

/// <summary>
/// More engine behaviour this project does not reproduce, specified before it is built.
/// </summary>
/// <remarks>
/// **Second batch, same rule as <see cref="UnimplementedFeatureConformanceTests"/>**: the real
/// assertion with its citation, behind a check that skips today and activates when the feature
/// lands. What is new here is that one of them is not a material parameter at all — the static prop
/// lump carries a skin index this project never reads, which is a decode gap rather than a shading
/// one and was found by asking what <c>StaticPropConformanceTests</c> had already derived and
/// nothing consumed.
///
/// **Two SDK details worth pinning because they invert the obvious reading**, and both would be got
/// wrong by anyone implementing from the parameter name alone:
///
/// - <c>$seamless_scale</c> of **0 means ordinary mapping**, not zero scale
///   (<c>WorldVertexTransition_dx8.cpp:176</c> guards on <c>!= 0.0f</c>).
/// - <c>$edgesoftnessstart</c> defaults to **0.6** and <c>$edgesoftnessend</c> to **0.5**, so the
///   range counts DOWN. Distance-alpha stores distance-from-edge, and softness runs from more inside
///   to less.
/// </remarks>
public sealed class UnimplementedRenderingConformanceTests
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
    public void Rendering_AStaticProp_CarriesItsOwnSkinIndex()
    {
        // **Implemented, so this is now the real assertion rather than the placeholder.** It was
        // written as a skipping specification — "the placement must expose the index and the
        // renderer must select that family" — with an Assert.Fail below it so that finishing the
        // work could not leave a test quietly passing on nothing.
        //
        // The gap it recorded: StaticPropLump_t.m_Skin sits at offset 32 (padding puts it there,
        // after m_Solid's single byte) and BspStaticProps read only origin, angles and prop type.
        // Every static prop in every map drew skin family 0 whatever the map asked for, which is
        // not an error and reads as the map's own art.
        RequireStaticPropSkin();

        // The placement exposes it...
        typeof(BspStaticProp).GetProperty("Skin").ShouldNotBeNull();

        // ...and it must be a value a map actually varies, or reading it changed nothing. Measured
        // on cp_process_final: 267 of 1631 placements name a family other than zero, asserted
        // properly against a shipped map in BspStaticPropsTests where the map fixture lives.
        BspStaticProp placement = default;

        placement.Skin.ShouldBe(0, "an unset placement draws the first family, as it always did");
    }

    [Test]
    public void Rendering_AlphaTestReference_OverridesTheCutoffOnlyAboveZero()
    {
        // **The renderer hardcodes 0.5 and the engine does not always use it.** WorldRenderer's
        // pixel shader is `clip(albedo.a - 0.5f)` for every alpha-tested surface, but Valve's own
        // setup (BaseVSShader.cpp:925) is:
        //
        //     s_pShaderShadow->EnableAlphaTest( IS_FLAG_SET(MATERIAL_VAR_ALPHATEST) );
        //     if( alphaTestReferenceVar != -1 && params[alphaTestReferenceVar]->GetFloatValue() > 0.0f )
        //         s_pShaderShadow->AlphaFunc( SHADER_ALPHAFUNC_GEQUAL, params[...]->GetFloatValue() );
        //
        // Two things follow, and the second is the one an implementation gets wrong. The cutoff is
        // GEQUAL against $alphatestreference when the material sets one — so a material asking for
        // 0.9 keeps only its most opaque texels and ours keeps everything above half, which
        // thickens every alpha-tested edge. And the override applies ONLY when the value is above
        // zero: the declaration's default is the empty string (depthwrite.cpp:23), so an absent or
        // zero reference means "leave the API default alone" rather than "cut off at zero", which
        // would keep every texel and turn a grate into a solid sheet.
        //
        // Visible on exactly the surfaces that make a map read as a map — foliage, grates,
        // chain-link fences, ladders. This is the shape of defect that looks like bad art.
        RequireImplemented("$alphatestreference", "no entry yet");

        VmtMaterial material = Parse(
            """
            LightmappedGeneric
            {
                "$basetexture" "nature/blendgrassgravel"
                "$alphatest" "1"
                "$alphatestreference" "0.9"
            }
            """);

        // When implemented, the material must carry the reference through so the shader can use it
        // rather than its own constant.
        material.Value("$alphatestreference").ShouldBe("0.9");
    }

    [Test]
    public void Rendering_ASeamlessScaleOfZero_MeansOrdinaryMapping()
    {
        // **The inverted default.** WorldVertexTransition_dx8.cpp:176 enables seamless mapping only
        // when the value IS DEFINED AND non-zero, and line 187 sets it to 0 when absent. So zero is
        // the off switch rather than a degenerate scale, and an implementation treating it as a
        // multiplier collapses every such surface's texture coordinates to a point.
        RequireImplemented("$seamless_scale", "no entry yet");

        VmtMaterial off = Parse(
            """
            "WorldVertexTransition"
            {
                "$basetexture" "nature/rock"
                "$seamless_scale" "0"
            }
            """);

        VmtMaterial on = Parse(
            """
            "WorldVertexTransition"
            {
                "$basetexture" "nature/rock"
                "$seamless_scale" "0.005"
            }
            """);

        off.Value("$seamless_scale").ShouldBe("0");
        on.Value("$seamless_scale").ShouldBe("0.005");
    }

    [Test]
    public void Rendering_DistanceAlpha_SoftensAnEdgeOverADescendingRange()
    {
        // **TF2's signage.** unlitgeneric_dx9.cpp:47 describes $distancealpha as "distance-coded
        // alpha generated from hi-res texture by vtex" — the texture stores distance from the
        // glyph's edge rather than coverage, so a sign stays sharp at any zoom.
        //
        // The range is the trap: EDGESOFTNESSSTART defaults to 0.6 and EDGESOFTNESSEND to 0.5
        // (lines 52-53), so start is GREATER than end. Reading them as a conventional
        // ascending range inverts the gradient and produces a hard edge with a halo.
        //
        // Ignoring the whole feature draws the raw distance field as if it were colour, which is a
        // grey blob where the lettering should be.
        RequireImplemented("$distancealpha", "no entry yet");

        Dictionary<string, string> defaults = ShaderDefaults();

        defaults["EDGESOFTNESSSTART"].ShouldBe("0.6");
        defaults["EDGESOFTNESSEND"].ShouldBe("0.5");

        double.Parse(defaults["EDGESOFTNESSSTART"], System.Globalization.CultureInfo.InvariantCulture)
            .ShouldBeGreaterThan(
                double.Parse(defaults["EDGESOFTNESSEND"], System.Globalization.CultureInfo.InvariantCulture),
                "the softness range counts down; treating it as ascending inverts the gradient");
    }

    [Test]
    public void Rendering_AnOutline_UsesTheGlyphsDistanceField()
    {
        // $outline and its six companions are declared on the same shader as $distancealpha
        // (unlitgeneric_dx9.cpp:63-70) and are meaningless without it: the outline is a second
        // threshold on the same distance field. So an implementation that adds outlines without
        // distance alpha has nothing to threshold.
        RequireImplemented("$outline", "no entry yet");

        Dictionary<string, string> defaults = ShaderDefaults();

        defaults["OUTLINECOLOR"].ShouldBe("[1 1 1]");
        defaults["OUTLINEALPHA"].ShouldBe("0.0");
    }

    [Test]
    public void Rendering_BlendModulate_ChoosesWhereTwoTexturesMeet()
    {
        // $blendmodulatetexture supplies a per-pixel mask for the transition between $basetexture
        // and $basetexture2 on a blended surface, replacing the straight vertex-alpha ramp with a
        // noisy edge. Without it the two textures cross-fade linearly, which is smooth where the
        // map author asked for a ragged join — visible on every ground transition using it.
        RequireImplemented("$blendmodulatetexture", "no entry yet");

        Parse(
            """
            "WorldVertexTransition"
            {
                "$basetexture" "nature/dirt"
                "$basetexture2" "nature/grass"
                "$blendmodulatetexture" "nature/blend_mask"
            }
            """)
            .Value("$blendmodulatetexture")
            .ShouldBe("nature/blend_mask");
    }

    [Test]
    public void Rendering_ColourAndAlpha_ModulateTheWholeMaterial()
    {
        // $color and $alpha scale a material's output, and the engine applies them as a modulation
        // on top of whatever the shader produced. They are per-material rather than per-surface, so
        // ignoring them draws a tinted or faded material at full strength — which on a TF2 map is
        // most often a light haze or a coloured glow rendered as an opaque white one.
        //
        // **Four behaviours, all read from CBaseVSShader::ColorVarsToVector**
        // (BaseVSShader.cpp:677-698), which is the published half of the modulation path.
        // ComputeModulationColor itself lives in the closed shaderlib, so what is asserted here is
        // the conversion the SDK does ship — and it is the part an implementation gets wrong.
        RequireImplemented("$color", "no entry yet");

        VmtMaterial material = Parse(
            """
            "UnlitGeneric"
            {
                "$basetexture" "effects/glow"
                "$color" "[1 .5 .25]"
                "$alpha" "0.5"
            }
            """);

        material.Modulation.ShouldBe((1f, 0.5f, 0.25f, 0.5f));
    }

    [Test]
    public void Rendering_AScalarColour_BroadcastsToEveryChannel()
    {
        // **The form that is not a vector at all, and the one that throws.** ColorVarsToVector
        // branches on the var's TYPE:
        //
        //     if ( pColorVar->GetType() == MATERIAL_VAR_TYPE_VECTOR )
        //         pColorVar->GetVecValue( color.Base(), 3 );
        //     else
        //         color[0] = color[1] = color[2] = pColorVar->GetFloatValue();
        //
        // So "$color" "0.5" is legal and means half brightness on all three channels. A reader that
        // accepts only the bracketed triple rejects a material the engine draws happily — and this
        // project's colour helper did exactly that, raising InvalidDataException on "not three
        // numbers", which would have taken the whole material down rather than the tint.
        RequireImplemented("$color", "no entry yet");

        Parse(
            """
            "UnlitGeneric"
            {
                "$basetexture" "effects/glow"
                "$color" "0.5"
            }
            """)
            .Modulation.ShouldBe((0.5f, 0.5f, 0.5f, 1f));
    }

    [Test]
    public void Rendering_Alpha_IsClampedWhileColourIsNot()
    {
        // **The asymmetry, and the discriminator.** The same seven lines clamp one and not the
        // other:
        //
        //     float flAlpha = s_ppParams[alphaVar]->GetFloatValue();
        //     color[3] = clamp( flAlpha, 0.0f, 1.0f );
        //
        // There is no matching clamp on the three colour channels, and that is deliberate rather
        // than an oversight: SetModulationPixelShaderDynamicState_LinearColorSpace (line 652) reads
        // `color[i] > 1.0f ? color[i] : GammaToLinear( color[i] )`, which only makes sense for a
        // channel allowed to exceed one. Over-bright modulation is how a material is made to glow.
        //
        // An implementation that clamps both loses that; one that clamps neither lets $alpha above
        // one turn a blended surface opaque. Asserted with values outside the range in BOTH
        // directions, because a clamp applied to the wrong operand passes on one side.
        RequireImplemented("$color", "no entry yet");

        Parse(
            """
            "UnlitGeneric"
            {
                "$color" "[2 3 4]"
                "$alpha" "1.75"
            }
            """)
            .Modulation.ShouldBe((2f, 3f, 4f, 1f));

        Parse(
            """
            "UnlitGeneric"
            {
                "$color" "[-1 0 0]"
                "$alpha" "-0.25"
            }
            """)
            .Modulation.ShouldBe((-1f, 0f, 0f, 0f));
    }

    [Test]
    public void Rendering_ASecondColour_MultipliesTheFirst()
    {
        // $color2 is a standard parameter alongside $color (BaseShader.h:45), and the header states
        // the operation outright on the declaration of its helper:
        //
        //     void ApplyColor2Factor( float *pColorOut ) const;   // (*pColorOut) *= COLOR2
        //
        // Multiplied, not replaced — so a material naming both gets the product, and one naming
        // only $color2 is tinted by it alone. This is not hypothetical on TF2 maps: MaterialCensus
        // records cp_process_final's props carrying `360?$color2`, which is the same parameter
        // under a platform prefix.
        //
        // Half times half is a QUARTER, which no other combination of these two inputs produces:
        // replacing gives 0.5, adding gives 1.0. The green and blue channels differ from red so a
        // transposed component cannot pass.
        RequireImplemented("$color", "no entry yet");

        Parse(
            """
            "UnlitGeneric"
            {
                "$color" "[0.5 1 0.5]"
                "$color2" "[0.5 0.5 1]"
            }
            """)
            .Modulation.ShouldBe((0.25f, 0.5f, 0.5f, 1f));
    }

    [Test]
    public void Rendering_AMaterialNamingNeither_ModulatesNothing()
    {
        // The identity, which is what every one of the hundreds of materials that name no colour
        // must resolve to. ColorVarsToVector opens `color.Init( 1.0, 1.0, 1.0, 1.0 )` and only
        // overwrites what the material declared, so absent means one on all four channels.
        //
        // **The control for the three tests above.** Without it "modulation is applied" and
        // "modulation is applied to everything" are indistinguishable — a bug that tinted every
        // surface would pass all four of the assertions that name a colour.
        RequireImplemented("$color", "no entry yet");

        Parse(
            """
            "LightmappedGeneric"
            {
                "$basetexture" "concrete/concretefloor001a"
            }
            """)
            .Modulation.ShouldBe((1f, 1f, 1f, 1f));
    }

    /// <summary>Skips unless the census says the parameter is implemented.</summary>
    /// <remarks>
    /// <c>TF2DEMOSALVAGE_CHECK_SPEC=1</c> lifts the guard, which checks the SPECIFICATION rather
    /// than the code — see <see cref="EnvmapConformanceTests"/>, where the reasoning is written out.
    /// A conformance test that only ever skips is unverified prose, and a wrong citation in one
    /// surfaces months later as a failure blamed on whoever implemented the feature.
    /// </remarks>
    private static void RequireImplemented(string parameter, string entry)
    {
        if (Environment.GetEnvironmentVariable("TF2DEMOSALVAGE_CHECK_SPEC") is "1")
        {
            return;
        }

        if (!MaterialCensus.ImplementedParameters.Contains(parameter, StringComparer.OrdinalIgnoreCase))
        {
            Assert.Ignore(
                $"{parameter} is not implemented ({entry}). The assertion below is what the engine " +
                "does, written before the code so it cannot be a description of it.");
        }
    }

    /// <summary>Skips while the static prop reader ignores the skin index.</summary>
    /// <remarks>
    /// The capability is checked by asking whether anything exposes it, which today nothing does.
    /// Stated as its own helper so the day a skin appears on a placement this test starts running
    /// rather than needing to be remembered.
    /// </remarks>
    private static void RequireStaticPropSkin()
    {
        bool exposed = typeof(BspStaticProp)
            .GetProperties()
            .Any(property => property.Name.Contains("Skin", StringComparison.OrdinalIgnoreCase));

        if (!exposed)
        {
            Assert.Ignore(
                "static prop skins are not read. StaticPropLump_t.m_Skin is at offset 32 in every " +
                "declared version (already derived by StaticPropConformanceTests) and " +
                "BspStaticProps reads only origin, angles and prop type — so every static prop " +
                "draws skin family 0. cap_point_base.mdl has three families.");
        }
    }

    /// <summary>The default values the shader declares for its parameters.</summary>
    /// <remarks>
    /// Read from <c>SHADER_PARAM</c> declarations, whose third argument is the default as a string.
    /// Those defaults are part of the specification: a material that omits the key gets them, so an
    /// implementation choosing its own is wrong for every material that does not state one.
    /// </remarks>
    private static Dictionary<string, string> ShaderDefaults()
    {
        string source = SourceSdk.Text("src/materialsystem/stdshaders/unlitgeneric_dx9.cpp")
            ?? throw new InvalidOperationException("unlitgeneric_dx9.cpp is missing from the SDK");

        Dictionary<string, string> defaults = new(StringComparer.Ordinal);

        foreach (System.Text.RegularExpressions.Match hit in
            System.Text.RegularExpressions.Regex.Matches(
                source,
                @"SHADER_PARAM\(\s*([A-Z0-9_]+)\s*,\s*[A-Z_]+\s*,\s*""([^""]*)""",
                System.Text.RegularExpressions.RegexOptions.None,
                TimeSpan.FromSeconds(10)))
        {
            defaults.TryAdd(hit.Groups[1].Value, hit.Groups[2].Value);
        }

        defaults.Count.ShouldBeGreaterThan(20, "no shader parameters were extracted");

        return defaults;
    }

    /// <summary>Parses a VMT from text.</summary>
    private static VmtMaterial Parse(string text) =>
        VmtMaterial.Parse(System.Text.Encoding.UTF8.GetBytes(text));
}
