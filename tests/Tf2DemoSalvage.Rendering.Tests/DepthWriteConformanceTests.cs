using System;
using System.Text;

using Tf2DemoSalvage.Content.Assets;
using Tf2DemoSalvage.SdkReference;

namespace Tf2DemoSalvage.Rendering.Tests;

/// <summary>
/// Which materials write depth — our rule against the shader clauses that state Valve's.
/// </summary>
/// <remarks>
/// **This file exists because B137 was filed against a count, and the count measured the wrong
/// population.** The claim was that this project uses "one blanket rule where Valve decides per
/// shader", evidenced by 18 of 60 published shaders enabling blending without disabling depth
/// writes — 30%, presented as a divergence to fix.
///
/// **Thirteen of those eighteen are dx6/dx7/dx8 fallbacks or full-screen post effects**, and the
/// remaining five are Portal and HL2 shaders and a dx8-era overlay path. Not one is a shader TF2
/// uses for a world or model material. The number was real and answered a question nobody had
/// asked.
///
/// The owner's question is what found the actual rule:
///
/// > "if a blanket rule is wrong there has to be a way to tell when to use what like a flag"
///
/// **There is, and it is the flag.** Valve's own shaders tie blending and depth writes together in
/// one condition:
///
/// <code>
/// // cable_dx9.cpp:55
/// if ( IS_FLAG_SET( MATERIAL_VAR_TRANSLUCENT ) )
/// {
///     pShaderShadow->EnableDepthWrites( false );
///     pShaderShadow->EnableBlending( true );
///     pShaderShadow->BlendFunc( SHADER_BLEND_SRC_ALPHA, SHADER_BLEND_ONE_MINUS_SRC_ALPHA );
/// }
///
/// // cloud_dx9.cpp:52 — writes off first, the flag only picks the blend function
/// pShaderShadow->EnableDepthWrites( false );
/// pShaderShadow->EnableBlending( true );
/// if ( IS_FLAG_SET( MATERIAL_VAR_ADDITIVE ) ) { BlendFunc( ONE, ONE ); }
/// else                                        { BlendFunc( SRC_ALPHA, INV_SRC_ALPHA ); }
/// </code>
///
/// So the rule is one flag test per kind, and this project's is the same rule. What the tests below
/// pin is each clause against ours, and — the half that actually decides pictures — the clause that
/// is ABSENT.
/// </remarks>
public sealed class DepthWriteConformanceTests
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
    public void Translucent_ValvesCableShader_TiesBlendingAndDepthWritesInOneCondition()
    {
        string cable = Sdk("src/materialsystem/stdshaders/cable_dx9.cpp");

        // The condition, and both consequences inside it. Asserted together because the claim is
        // that they are ONE decision — finding the two calls anywhere in the file would not say
        // that.
        int test = cable.IndexOf("IS_FLAG_SET( MATERIAL_VAR_TRANSLUCENT )", StringComparison.Ordinal);

        test.ShouldBeGreaterThanOrEqualTo(0, "cable_dx9.cpp no longer tests the translucent flag");

        string block = cable[test..Math.Min(test + 300, cable.Length)];

        block.ShouldContain("EnableDepthWrites( false )", Case.Sensitive);
        block.ShouldContain("EnableBlending( true )", Case.Sensitive);

        // **Ours.** A translucent material blends, so it writes no depth.
        WorldRenderer.Blends(marks: false, translucent: true, additive: false, modulate: false)
            .ShouldBeTrue("$translucent must take the no-depth-write state, as cable_dx9.cpp does");
    }

    [Test]
    public void Additive_ValvesCloudShader_DisablesDepthWritesBeforeChoosingTheBlendFunction()
    {
        string cloud = Sdk("src/materialsystem/stdshaders/cloud_dx9.cpp");

        int state = cloud.IndexOf("EnableDepthWrites( false )", StringComparison.Ordinal);

        state.ShouldBeGreaterThanOrEqualTo(0, "cloud_dx9.cpp no longer disables depth writes");

        string block = cloud[state..Math.Min(state + 400, cloud.Length)];

        // The ORDER is the evidence: writes go off unconditionally and the flag only selects
        // between ONE/ONE and SRC_ALPHA/INV_SRC_ALPHA afterwards. So additive and translucent get
        // the same depth treatment and differ only in colour.
        block.ShouldContain("EnableBlending( true )", Case.Sensitive);
        block.ShouldContain("IS_FLAG_SET( MATERIAL_VAR_ADDITIVE )", Case.Sensitive);
        block.ShouldContain("SHADER_BLEND_ONE, SHADER_BLEND_ONE", Case.Sensitive);

        WorldRenderer.Blends(marks: false, translucent: false, additive: true, modulate: false)
            .ShouldBeTrue("$additive must take the no-depth-write state, as cloud_dx9.cpp does");
    }

    [Test]
    public void AlphaTest_IsExcludedFromTranslucency_SoItKeepsItsDepthWrites()
    {
        // **The clause that is absent, and the one that decides pictures.** A grate or a fence is
        // alpha-TESTED, not translucent: the fragment is either drawn or discarded, so it is opaque
        // where it is drawn and must write depth like any wall. Treating it as blending leaves
        // everything behind a fence testing against nothing.
        //
        // Valve states the exclusion in EvaluateBlendRequirements (BaseVSShader.cpp:1580) — texture
        // alpha only makes a material translucent when the alpha-test flag is NOT set:
        //
        //     isTranslucent = isTranslucent || ( TextureIsTranslucent( textureVar, isBaseTexture ) &&
        //                                        !(CurrentMaterialVarFlags() & MATERIAL_VAR_ALPHATEST ) );
        string helper = Sdk("src/materialsystem/stdshaders/BaseVSShader.cpp");

        helper.ShouldContain(
            "!(CurrentMaterialVarFlags() & MATERIAL_VAR_ALPHATEST )",
            Case.Sensitive,
            "the exclusion, in Valve's own translucency query");

        // **Ours, measured through the material reader rather than asserted about it.** A material
        // declaring both keys must come out NOT translucent, which is what keeps it writing depth.
        VmtMaterial both = Vmt("$translucent 1\n\t$alphatest 1");

        both.IsAlphaTested.ShouldBeTrue("the fixture declares $alphatest");

        both.IsTranslucent.ShouldBeFalse(
            "alpha test wins over translucency, as EvaluateBlendRequirements has it — otherwise a "
            + "fence stops writing depth and everything behind it draws through");

        WorldRenderer.Blends(
            marks: false,
            translucent: both.IsTranslucent,
            additive: both.IsAdditive,
            modulate: false)
            .ShouldBeFalse("so an alpha-tested material keeps the opaque, depth-writing state");

        // The control: with the alpha-test key removed the SAME material is translucent, so the
        // assertion above is about the exclusion and not about the fixture failing to parse.
        Vmt("$translucent 1").IsTranslucent.ShouldBeTrue();
    }

    [Test]
    public void OpaqueMaterial_WithNoBlendKeyAtAll_KeepsItsDepthWrites()
    {
        // The base case, and the reason it is worth an assertion: every test above shows something
        // taking the no-write state, and a predicate that returned true unconditionally would pass
        // all of them.
        VmtMaterial wall = Vmt("$basetexture \"concrete/concretewall001a\"");

        wall.IsTranslucent.ShouldBeFalse();
        wall.IsAdditive.ShouldBeFalse();
        wall.IsDecal.ShouldBeFalse();

        WorldRenderer.Blends(marks: false, translucent: false, additive: false, modulate: false)
            .ShouldBeFalse("an ordinary world surface writes depth");

        // And LightmappedGeneric — what that wall really is — does not disable depth writes in
        // Valve's shader either, which is the contrast the whole rule rests on.
        Sdk("src/materialsystem/stdshaders/lightmappedgeneric_dx9_helper.cpp").ShouldNotContain(
            "EnableDepthWrites( false )",
            Case.Sensitive);
    }

    /// <summary>Reads an SDK file, or fails loudly.</summary>
    private static string Sdk(string path) =>
        SourceSdk.Text(path) ?? throw new InvalidOperationException($"{path} is missing from the SDK");

    /// <summary>A LightmappedGeneric material with the given body.</summary>
    private static VmtMaterial Vmt(string body) =>
        VmtMaterial.Parse(
            Encoding.UTF8.GetBytes($"\"LightmappedGeneric\"\n{{\n\t{body}\n}}\n"));
}
