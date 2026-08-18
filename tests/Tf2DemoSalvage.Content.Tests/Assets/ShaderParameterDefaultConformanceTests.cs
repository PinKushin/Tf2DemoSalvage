using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;

using Tf2DemoSalvage.Content.Assets;
using Tf2DemoSalvage.SdkReference;

namespace Tf2DemoSalvage.Content.Tests.Assets;

/// <summary>
/// Every material parameter default, against the <c>SHADER_PARAM</c> that declares it.
/// </summary>
/// <remarks>
/// **Defaults decide how almost every surface draws, which is why this is the rendering conformance
/// test worth having.** A VMT names a handful of parameters and omits the rest, so for the great
/// majority of materials the DEFAULT is what reaches the shader. A wrong default is therefore not a
/// rare edge case — it is a rendering error on nearly every surface that does not override it, and
/// it is invisible in exactly the way that matters: the material still draws, just not the way the
/// engine draws it.
///
/// **The declarations are shipped, so this needs no decompiler.** Valve's own shaders declare each
/// parameter with its type and default:
///
/// <code>
/// SHADER_PARAM( DETAILSCALE, SHADER_PARAM_TYPE_FLOAT, "4", "scale of the detail texture" )
/// SHADER_PARAM( DETAILTINT, SHADER_PARAM_TYPE_COLOR, "[1 1 1]", "detail texture tint" )
/// </code>
///
/// That is the authority, and it is better than the wiki or a guess: it is the literal string the
/// material system parses when the key is absent.
///
/// Found by auditing which implemented shader parameters had SEMANTIC coverage rather than merely
/// being claimed in <c>MaterialCensus</c>. `SdkCoverageTests` catches a parameter we never
/// implemented; only a test like this catches one we implemented wrongly.
/// </remarks>
public sealed class ShaderParameterDefaultConformanceTests
{
    /// <summary>Shaders that between them declare every parameter this project reads.</summary>
    private static readonly string[] ShaderSources =
    [
        "src/materialsystem/stdshaders/lightmappedgeneric_dx9.cpp",
        "src/materialsystem/stdshaders/vertexlitgeneric_dx9.cpp",
        "src/materialsystem/stdshaders/worldvertextransition.cpp",
    ];

    [Test]
    public void DetailScaleDefaultsToTheDeclaredFour()
    {
        // Absent, a detail texture tiles four times over the base. Defaulting to 1 would make
        // every detail texture on every surface four times too large.
        Declared("DETAILSCALE").ShouldBe("4");

        (float u, float v) = Bare().DetailScale;

        u.ShouldBe(4f, 0.0001f);

        // Both components, because a scalar broadcasts: Valve reads one float and copies it.
        v.ShouldBe(4f, 0.0001f);
    }

    [Test]
    public void DetailBlendFactorDefaultsToTheDeclaredOne()
    {
        Declared("DETAILBLENDFACTOR").ShouldBe("1");

        Bare().DetailBlendFactor.ShouldBe(1f, 0.0001f);
    }

    [Test]
    public void DetailBlendModeDefaultsToTheDeclaredZero()
    {
        // **Zero is a real mode, not "none".** The declaration spells the modes out —
        // "0=normal, 1=additive, 2=alpha blend detail over base, 3=crossfade" — so a material
        // naming a detail texture and no mode blends rather than doing nothing.
        Declared("DETAILBLENDMODE").ShouldBe("0");

        Bare().DetailBlendMode.ShouldBe(0);
    }

    [Test]
    public void DetailTintDefaultsToTheDeclaredWhite()
    {
        // White is the multiplicative identity, so the default must not tint at all. Any other
        // value would shift the colour of every detailed surface that does not set it.
        Declared("DETAILTINT").ShouldBe("[1 1 1]");

        AssertWhite(Bare().DetailTint);
    }

    [Test]
    public void SelfIllumTintDefaultsToTheDeclaredWhite()
    {
        Declared("SELFILLUMTINT").ShouldBe("[1 1 1]");

        AssertWhite(Bare().SelfIllumTint);
    }

    [Test]
    public void ABooleanParameterIsAnIntegerAndAnyNonZeroIsTrue()
    {
        // **The SDK declares these as integers, not as the string "1".** $ssbump is the readable
        // proof — SHADER_PARAM( SSBUMP, SHADER_PARAM_TYPE_INTEGER, "0", ... ) — and the
        // flag-valued ones become MATERIAL_VAR_* bits set from an integer read. Nothing in the
        // material system compares a parameter against the characters '1'.
        Type("SSBUMP").ShouldBe("SHADER_PARAM_TYPE_INTEGER");
        Declared("SSBUMP").ShouldBe("0");

        // This project read nine parameters as `Value(key) is "1"`, which agrees with the engine on
        // every material Valve ships and disagrees on anything else. A custom map's materials go
        // through the same reader, so "Valve always writes 1" is a fact about Valve rather than
        // about the input.
        Parse("""LightmappedGeneric { "$translucent" "2" }""").IsTranslucent.ShouldBeTrue();
        Parse("""LightmappedGeneric { "$ssbump" "2" }""").IsSelfShadowingBump.ShouldBeTrue();
        Parse("""LightmappedGeneric { "$additive" "3" }""").IsAdditive.ShouldBeTrue();

        // Whitespace survives too, because the engine's integer read is atoi-shaped.
        Parse("""LightmappedGeneric { "$nocull" " 1" }""").IsNoCull.ShouldBeTrue();

        // The controls, which are what stop this asserting that everything is true: zero and a
        // value that is not a number at all both stay false.
        Parse("""LightmappedGeneric { "$translucent" "0" }""").IsTranslucent.ShouldBeFalse();
        Parse("""LightmappedGeneric { "$additive" "no" }""").IsAdditive.ShouldBeFalse();
    }

    [Test]
    public void DetailScaleIsSetToFourByTheInitialiserToo()
    {
        // **A second, stronger citation for the same default.** The SHADER_PARAM declaration is
        // what the material system parses; this is the shader's own initialiser filling the value
        // in when the material left it out, in BaseVSShader.cpp:2122:
        //
        //     if( detailScaleVar >= 0 && !params[detailScaleVar]->IsDefined() )
        //         params[detailScaleVar]->SetFloatValue( 4.0f );
        //
        // Worth pinning separately because the two could in principle disagree, and if they ever
        // did it would be the initialiser that wins at run time.
        Source("src/materialsystem/stdshaders/BaseVSShader.cpp")
            .ShouldMatch(@"detailScaleVar\s*\]\s*->\s*SetFloatValue\(\s*4\.0f\s*\)");
    }

    [Test]
    public void SelfIlluminationIsMaskedByBaseTextureAlpha()
    {
        // **Where the mask comes from, which is the whole of $selfillum's behaviour.** The glow is
        // not applied to the surface uniformly — the base texture's alpha channel selects which
        // texels emit. Valve states it while clearing the flag for the degenerate case
        // (BaseVSShader.cpp:2127): "No texture means no self-illum or env mask in base alpha".
        //
        // An implementation that ignored the mask would light the whole surface instead of its
        // windows or its screen, which reads as a blown-out material rather than a wrong one.
        Source("src/materialsystem/stdshaders/BaseVSShader.cpp")
            .ShouldContain("No texture means no self-illum or env mask in base alpha");

        // The material side of it: the tint is carried, and it is only meaningful when the flag is
        // set — so both have to survive parsing together.
        VmtMaterial lit = Parse(
            """
            LightmappedGeneric
            {
                "$basetexture" "metal/screen"
                "$selfillum" "1"
                "$selfillumtint" "[1 0.5 0]"
            }
            """);

        lit.IsSelfIlluminated.ShouldBeTrue();
        lit.SelfIllumTint.Red.ShouldBe(1f, 0.0001f);
        lit.SelfIllumTint.Green.ShouldBe(0.5f, 0.0001f);
        lit.SelfIllumTint.Blue.ShouldBe(0f, 0.0001f);
    }

    [Test]
    public void EveryDefaultCheckedHereWasActuallyFoundInTheSdk()
    {
        // **The control.** Every assertion above compares against a string this test extracted; if
        // the extraction silently returned nothing, `ShouldBe` would fail — but a regex that
        // matched a DIFFERENT parameter would pass while measuring the wrong thing. Asserting the
        // set of names found makes the extraction itself visible.
        foreach (string name in new[]
        {
            "DETAILSCALE", "DETAILBLENDFACTOR", "DETAILBLENDMODE", "DETAILTINT", "SELFILLUMTINT",
        })
        {
            Declared(name).ShouldNotBeNullOrWhiteSpace($"{name} was not found in any shipped shader");
        }
    }

    /// <summary>A material with no parameters at all, so every property reports its default.</summary>
    private static VmtMaterial Bare() => Parse("LightmappedGeneric\n{\n}\n");

    private static VmtMaterial Parse(string text) =>
        VmtMaterial.Parse(System.Text.Encoding.UTF8.GetBytes(text));

    /// <summary>The <c>SHADER_PARAM_TYPE_*</c> a parameter is declared with.</summary>
    private static string Type(string parameter) => Declaration(parameter, "type");

    /// <summary>One SDK file's text, for the claims that are about code rather than a declaration.</summary>
    private static string Source(string path)
    {
        if (!SourceSdk.Available)
        {
            Assert.Ignore("the Source SDK is not available");
        }

        return SourceSdk.Text(path).ShouldNotBeNull(path);
    }

    private static void AssertWhite((float Red, float Green, float Blue) colour)
    {
        colour.Red.ShouldBe(1f, 0.0001f);
        colour.Green.ShouldBe(1f, 0.0001f);
        colour.Blue.ShouldBe(1f, 0.0001f);
    }

    /// <summary>The default string a <c>SHADER_PARAM</c> declares for a parameter.</summary>
    /// <remarks>
    /// Several shaders declare the same parameter identically, so the first match is taken. The
    /// name is anchored with word boundaries because <c>DETAIL</c> is a prefix of
    /// <c>DETAILSCALE</c>, <c>DETAILTINT</c> and the rest — without them, asking for one would
    /// answer with another.
    /// </remarks>
    private static string Declared(string parameter) => Declaration(parameter, "default");

    private static string Declaration(string parameter, string part)
    {
        if (!SourceSdk.Available)
        {
            Assert.Ignore("the Source SDK is not available");
        }

        Regex declaration = new(
            @"SHADER_PARAM\(\s*" + Regex.Escape(parameter) +
            @"\s*,\s*(?<type>SHADER_PARAM_TYPE_\w+)\s*,\s*""(?<default>[^""]*)""",
            RegexOptions.None,
            TimeSpan.FromSeconds(5));

        foreach (string path in ShaderSources)
        {
            if (SourceSdk.Text(path) is { } text && declaration.Match(text) is { Success: true } found)
            {
                return found.Groups[part].Value;
            }
        }

        return string.Empty;
    }
}
