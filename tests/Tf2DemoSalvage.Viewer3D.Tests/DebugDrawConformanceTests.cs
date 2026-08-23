using System;

using Tf2DemoSalvage.SdkReference;

namespace Tf2DemoSalvage.Viewer3D.Tests;

/// <summary>
/// Valve's debug visualisations, and what each one actually does to a surface.
/// </summary>
/// <remarks>
/// **Written before the implementation, which is the point of it.** A parity test written afterwards
/// describes what was built; written first it records what the engine does, with its citation, while
/// there is still nothing to bias the answer.
///
/// It caught something immediately. <c>mat_fullbright</c> reads like a boolean and is not — the
/// shaders test <c>GetInt() == 2</c> for a third state — so an implementation begun from the name
/// would have shipped two thirds of the feature and looked complete.
///
/// **These assert against the SDK, and that is a weaker test than this project usually accepts.**
/// The owner's standing objection holds: "us retesting the unchanging sdk is worthless" for anything
/// that can be compared against our own code. So each of these compares a constant we USE against
/// Valve's, exactly as <c>DecalRenderStateConformanceTests</c> does — the SDK read is the
/// denominator, and the assertion is on ours.
/// </remarks>
public sealed class DebugDrawConformanceTests
{
    private const string BaseShader = "src/materialsystem/stdshaders/BaseVSShader.cpp";
    private const string World = "src/materialsystem/stdshaders/lightmappedgeneric_dx9_helper.cpp";
    private const string Dynamic = "src/public/shaderapi/ishaderdynamic.h";
    private const string Material = "src/public/materialsystem/imaterial.h";

    [SetUp]
    public void RequireTheSdk()
    {
        if (!SourceSdk.Available)
        {
            Assert.Ignore(SourceSdk.Missing);
        }
    }

    [Test]
    public void Fullbright_ValveSDeclaration_HasThreeStatesRatherThanTwo()
    {
        // **The finding this test exists for.** `mat_fullbright` is declared "0" and named like a
        // switch, and every shader that consults it tests for TWO. A viewer that implemented it as
        // on/off would be complete by its own lights and missing the more useful half:
        //
        //     bool bLightingOnly = mat_fullbright.GetInt() == 2 && !IS_FLAG_SET( MATERIAL_VAR_NO_DEBUG_OVERRIDE );
        //     if( bLightingOnly )
        //         s_pShaderAPI->BindStandardTexture( SHADER_SAMPLER1, TEXTURE_GREY );
        string shader = Sdk(BaseShader);

        shader.ShouldContain("mat_fullbright.GetInt() == 2", Case.Sensitive);
        shader.ShouldContain("BindStandardTexture( SHADER_SAMPLER1, TEXTURE_GREY )", Case.Sensitive);

        // Ours must agree on how many states there are. A three-valued switch stored as a bool is
        // the same defect as reading it from the name.
        Enum.GetValues<Fullbright>().Length.ShouldBe(
            3, "mat_fullbright is off, no-lighting, or lighting-only — see the citation above");

        ((int)Fullbright.Off).ShouldBe(0, "Valve declares mat_fullbright \"0\"");
        ((int)Fullbright.NoLighting).ShouldBe(1);
        ((int)Fullbright.LightingOnly).ShouldBe(2, "the shaders test == 2 for lighting-only");
    }

    [Test]
    public void Fullbright_TheTwoSubstitutions_AreValvesStandardTextures()
    {
        // **Each state is a texture SUBSTITUTION, not a shader branch**, which is why they compose
        // with everything else the material does. Lighting-only swaps the albedo for grey; no
        // lighting swaps the lightmap for the fullbright one. Both names are in the standard
        // texture list rather than invented per shader.
        string dynamic = Sdk(Dynamic);

        dynamic.ShouldContain("TEXTURE_LIGHTMAP_FULLBRIGHT", Case.Sensitive);
        dynamic.ShouldContain("TEXTURE_GREY", Case.Sensitive);

        // The control: the same enum carries TEXTURE_BLACK, which the world shader binds instead of
        // white when a material has an envmap and no base texture. A reader that matched loosely
        // could not tell these three apart, and picking the wrong one is a picture rather than an
        // error.
        dynamic.ShouldContain("TEXTURE_BLACK", Case.Sensitive);
        Sdk(World).ShouldContain(
            "BindStandardTexture( SHADER_SAMPLER0, TEXTURE_WHITE )", Case.Sensitive);
    }

    [Test]
    public void DebugOverrides_AMaterialCanRefuseThem_ThroughNoDebugOverride()
    {
        // **A debug view is not allowed to override every material**, and the flag that says so is
        // read in the same expression as the mode itself. Skyboxes and UI materials set it, so a
        // fullbright view that ignored it would black out or grey out exactly the surfaces that
        // orient the person looking.
        Sdk(Material).ShouldContain("MATERIAL_VAR_NO_DEBUG_OVERRIDE", Case.Sensitive);
        Sdk(BaseShader).ShouldContain(
            "!IS_FLAG_SET( MATERIAL_VAR_NO_DEBUG_OVERRIDE )", Case.Sensitive);
    }

    [Test]
    public void Wireframe_ValvesDeclaration_IsACheatAndOursIsNot()
    {
        // **A divergence stated rather than absorbed (D75).** Valve gates the debug draws behind
        // sv_cheats because a player could otherwise see through walls. There is no server here and
        // no opponent, so the gate is ceremony — but it is recorded, and this test fails if Valve's
        // side of it ever stops being true.
        Sdk("src/game/client/viewdebug.cpp").ShouldContain(
            "ConVar mat_wireframe( \"mat_wireframe\", \"0\", FCVAR_CHEAT )", Case.Sensitive);

        // And the case that proves the distinction is real rather than convenient: the 3D skybox
        // toggle is NOT a cheat, so "Valve gates debug views" is not a blanket rule we are ignoring.
        Sdk("src/game/client/viewrender.cpp").ShouldContain(
            "ConVar r_3dsky( \"r_3dsky\",\"1\", 0,", Case.Sensitive);
    }

    private static string Sdk(string path) =>
        SourceSdk.Text(path) ?? throw new InvalidOperationException($"{path} is missing from the SDK");
}
