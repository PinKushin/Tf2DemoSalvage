using System;

using Tf2DemoSalvage.Content.Assets;

namespace Tf2DemoSalvage.Content.Tests.Assets;

/// <summary>
/// That <c>$envmaptint</c> reaches the pixel shader in the colour space its shader sends it in (B521).
/// </summary>
/// <remarks>
/// *Read from published source.* The three generic shaders hand the same VMT key to the pixel shader three ways:
///
/// <code>
/// // vertexlitgeneric_dx9_helper.cpp:1094-1101 -- VertexLitGeneric without $phong, and UnlitGeneric
/// pContextData->m_SemiStaticCmdsOut.SetEnvMapTintPixelShaderDynamicStateGammaToLinear( 0, info.m_nEnvmapTint, fTintReplaceFactor );
///
/// // commandbuilder.h:279-287
/// Param( tintVar)->GetLinearVecValue( color, 3 );
///
/// // skin_dx9_helper.cpp:785-786 -- VertexLitGeneric with $phong (InitVertexLitGeneric_DX9 routes to InitSkin_DX9)
/// params[info.m_nEnvmapTint]->GetVecValue(vEnvMapTint_MaskControl, 3);
///
/// // lightmappedgeneric_dx9_helper.cpp:677
/// pContextData->m_SemiStaticCmdsOut.SetEnvMapTintPixelShaderDynamicState( 0, info.m_nEnvmapTint );
/// </code>
///
/// *Interpolated:* that <c>GetLinearVecValue</c> is <c>GammaToLinear</c> per component — <c>cmaterialvar.cpp</c> is
/// not published. *Measured* on cp_process_f12's ceiling lamps (<c>groundlight001_sheet</c>, tint <c>[.2 .2 .2]</c>):
/// TF2's reflection is roughly a fifth of the raw-tint one, and 0.2^2.2 / 0.2 is 0.145.
/// </remarks>
public sealed class ModelEnvMapTintConformanceTests
{
    private static readonly (float Red, float Green, float Blue) Fifth = (0.2f, 0.2f, 0.2f);

    private static readonly float FifthLinear = MathF.Pow(51f / 255f, 2.2f);

    [Test]
    public void EnvMapTint_OnVertexLitGenericWithoutPhong_IsGammaToLinear()
    {
        (float red, float green, float blue) = ModelMaterialShading.EnvMapTint("VertexLitGeneric", phong: false, Fifth);

        red.ShouldBe(FifthLinear, 1e-6f);
        green.ShouldBe(FifthLinear, 1e-6f);
        blue.ShouldBe(FifthLinear, 1e-6f);
    }

    [Test]
    public void EnvMapTint_OnUnlitGeneric_IsGammaToLinear() =>
        ModelMaterialShading.EnvMapTint("UnlitGeneric", phong: false, Fifth).Red.ShouldBe(FifthLinear, 1e-6f);

    [Test]
    public void EnvMapTint_OnVertexLitGenericWithPhong_IsAsWritten() =>
        ModelMaterialShading.EnvMapTint("VertexLitGeneric", phong: true, (1.5f, 1.2f, 0.2f)).ShouldBe((1.5f, 1.2f, 0.2f));

    [Test]
    public void EnvMapTint_OnLightmappedGeneric_IsAsWritten() =>
        ModelMaterialShading.EnvMapTint("LightmappedGeneric", phong: false, Fifth).ShouldBe(Fifth);

    [Test]
    public void EnvMapTint_AboveOneOnVertexLitGeneric_IsOne()
    {
        // GammaToLinear returns 1 from 0.95 up (mathlib/color_conversion.cpp:282-288); unlike the modulation
        // colour there is no `> 1 ? c :` pass-through in front of it.
        (float red, float green, float blue) = ModelMaterialShading.EnvMapTint("VertexLitGeneric", phong: false, (1.5f, 0.96f, -1f));

        red.ShouldBe(1f);
        green.ShouldBe(1f);
        blue.ShouldBe(0f);
    }
}
