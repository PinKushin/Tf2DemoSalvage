using Tf2DemoSalvage.Content.Assets;

namespace Tf2DemoSalvage.Content.Tests.Assets;

/// <summary>
/// That a <c>$phong</c> material's reflection is masked the way the skin shader masks it (B521).
/// </summary>
/// <remarks>
/// *Read from published source*, <c>skin_ps20b.fxc:228-242</c> — there is no unmasked reflection on this path:
///
/// <code>
/// // Mask is either normal map alpha or base map alpha
/// #if ( SELFILLUMFRESNEL == 1 ) // This is to match the 2.0 version of vertexlitgeneric
///     float fEnvMapMask = lerp( baseColor.a, g_fInvertPhongMask, g_EnvmapTint_ShadowTweaks.w );
/// #else
///     float fEnvMapMask = lerp( baseColor.a, fSpecMask, g_EnvmapTint_ShadowTweaks.w );
/// #endif
///     envMapColor = (ENV_MAP_SCALE * lerp(1, fFresnelRanges, g_EnvMapFresnel.x) *
///                     lerp(fEnvMapMask, 1-fEnvMapMask, g_fInvertPhongMask)) * texCUBE( EnvmapSampler, vReflect ) * g_EnvmapTint_ShadowTweaks.xyz;
/// </code>
///
/// <c>w</c> is <c>$normalmapalphaenvmapmask</c> (<c>skin_dx9_helper.cpp:789</c>), and
/// <c>fSpecMask = lerp( normalTexel.a, baseColor.a, g_fBaseMapAlphaPhongMask )</c> (<c>skin_ps20b.fxc:200</c>).
/// cp_process_f12's <c>models/props_hydro/water_pipes</c> is the case that found it: <c>$phong</c>, <c>$envmap</c>,
/// neither mask key the shader knows — drawn unmasked, where the engine multiplies by the base map's alpha.
/// </remarks>
public sealed class SkinEnvMapMaskConformanceTests
{
    [Test]
    public void SkinEnvMapMask_WithNoMaskKeys_IsTheBaseAlpha() =>
        ModelMaterialShading.SkinEnvMapMask(false, false, false, false).ShouldBe(EnvMapMask.BaseAlpha);

    [Test]
    public void SkinEnvMapMask_WithInvertPhongMask_IsTheBaseAlphaInverted() =>
        ModelMaterialShading.SkinEnvMapMask(false, false, invertPhongMask: true, false).ShouldBe(EnvMapMask.InvertedBaseAlpha);

    [Test]
    public void SkinEnvMapMask_WithNormalMapAlphaEnvMapMask_IsTheBumpAlpha() =>
        ModelMaterialShading.SkinEnvMapMask(normalMapAlphaEnvMapMask: true, false, false, false).ShouldBe(EnvMapMask.BumpAlpha);

    [Test]
    public void SkinEnvMapMask_WithNormalMapAlphaEnvMapMaskAndInvertPhongMask_IsTheBumpAlphaInverted() =>
        ModelMaterialShading.SkinEnvMapMask(true, false, invertPhongMask: true, false).ShouldBe(EnvMapMask.InvertedBumpAlpha);

    [Test]
    public void SkinEnvMapMask_WithNormalMapAlphaEnvMapMaskAndBaseMapAlphaPhongMask_IsTheBaseAlpha() =>
        ModelMaterialShading.SkinEnvMapMask(true, baseMapAlphaPhongMask: true, false, false).ShouldBe(EnvMapMask.BaseAlpha);

    [TestCase(false)]
    [TestCase(true)]
    public void SkinEnvMapMask_WithNormalMapAlphaEnvMapMaskAndSelfIllumFresnel_ReflectsNothing(bool invertPhongMask) =>
        // lerp( m, 1 - m, invert ) with m = invert is 0 either way.
        ModelMaterialShading.SkinEnvMapMask(true, false, invertPhongMask, selfIllumFresnel: true).ShouldBe(EnvMapMask.Nothing);

    [Test]
    public void SkinEnvMapMask_WithSelfIllumFresnelAlone_IsTheBaseAlpha() =>
        ModelMaterialShading.SkinEnvMapMask(false, false, false, selfIllumFresnel: true).ShouldBe(EnvMapMask.BaseAlpha);

    /// <remarks>
    /// `$envmapfresnel`: "Degree to which Fresnel should be applied to env map" (`vertexlitgeneric_dx9.cpp:61`),
    /// zero when unset (`skin_dx9_helper.cpp:114-117`), sent as `g_EnvMapFresnel.x` (`:791-792`) and used as
    /// <c>lerp(1, fFresnelRanges, g_EnvMapFresnel.x)</c> (`skin_ps20b.fxc:238`).
    /// </remarks>
    [TestCase(0f, 0.3f, 1f)]
    [TestCase(1f, 0.3f, 0.3f)]
    [TestCase(0.5f, 0.3f, 0.65f)]
    [TestCase(-0.34f, 0.3f, 1.238f)]
    public void SkinEnvMapFresnel_ByDegree_BlendsFromOneToThePhongFresnelTerm(float degree, float fresnel, float expected) =>
        ModelMaterialShading.SkinEnvMapFresnel(degree, fresnel).ShouldBe(expected, 1e-5f);

    [Test]
    public void EnvMapFresnel_WhenTheMaterialDeclaresNone_IsZero() =>
        Parse("\"$phong\" \"1\"").EnvMapFresnel.ShouldBe(0f);

    /// <remarks>Three shipped materials write a NEGATIVE degree (`vmt-param $envmapfresnel`: -0.34), and it is not clamped.</remarks>
    [Test]
    public void EnvMapFresnel_WrittenNegative_IsKept() =>
        Parse("\"$envmapfresnel\" \"-0.340000\"").EnvMapFresnel.ShouldBe(-0.34f, 1e-6f);

    private static VmtMaterial Parse(string line) =>
        VmtMaterial.Parse(System.Text.Encoding.UTF8.GetBytes("\"VertexLitGeneric\"\n{\n" + line + "\n}\n"));
}
