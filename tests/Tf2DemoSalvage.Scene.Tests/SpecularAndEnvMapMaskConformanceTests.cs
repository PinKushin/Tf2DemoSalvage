namespace Tf2DemoSalvage.Scene.Tests;

/// <summary>
/// Which materials <c>mat_specular 0</c> takes the envmap from, and which sample <c>$envmapmask</c>, per setting (B720 part
/// 5), through the production <c>Resolve</c> over a hand-built pakfile and no game.
/// </summary>
/// <remarks>
/// **`mat_specular 0`, read from published source and confirmed in TF2's shipped bytecode.** Two params passes undefine
/// `$envmap` under it, each only where `$basetexture` is defined: `InitParamsVertexLitGeneric_DX9`
/// (`vertexlitgeneric_dx9_helper.cpp:214-218`; TF2 x64 `stdshader_dx9.dll` 0x180054994-0x1800549e1, config +0x34 bit 7)
/// and `InitParamsLightmappedGeneric_DX9` (`lightmappedgeneric_dx9_helper.cpp:166-170`; 0x18002feca-0x18002ff12). Those
/// are the only two tests of that bit in the DLL. UnlitGeneric runs the first (`unlitgeneric_dx9.cpp:166`),
/// WorldVertexTransition the second (`worldvertextransition.cpp:135`); `InitParamsSkin_DX9` makes no such test, so a
/// `$phong` + `$basemapalphaphongmask 1` material keeps its envmap while `mat_phong` routes it there (`:105-116`).
///
/// **`$envmapmask`**: loaded only by `InitVertexLitGeneric_DX9` (`:349-352`), never by `InitSkin_DX9`, and undefined by
/// the generic params pass beside a bump map (`:204-206`) or a kept `$normalmapalphaenvmapmask` (`:190-193`). Only the
/// non-bump shader samples it (`ENVMAPMASK`, `:816`), so a light warp (`:611`) reads none.
/// </remarks>
public sealed class SpecularAndEnvMapMaskConformanceTests
{
    private const string Envmap = "\"$envmap\" \"env_cubemap\"\n";
    private const string Base = "\"$basetexture\" \"test/colour\"\n";
    private const string Mask = "\"$envmapmask\" \"test/alpha\"\n";

    private static MapEnvmapShading Shading(string body, string shader = "VertexLitGeneric") =>
        SkinShaderFallbackConformanceTests.Resolve(body, shader).LocalReflection.ShouldNotBeNull();

    [TestCase("VertexLitGeneric")]
    [TestCase("UnlitGeneric")]
    [TestCase("LightmappedGeneric")]
    [TestCase("WorldVertexTransition")]
    public void Resolve_AnEnvmapBesideABaseTexture_IsUndefinedWithoutSpecular(string shader)
    {
        Shading(Envmap, shader).UndefinedWithoutSpecular.ShouldBeFalse("the control: with no base the envmap stands");
        Shading(Base + Envmap, shader).UndefinedWithoutSpecular.ShouldBeTrue();
    }

    /// <remarks>A shader whose params pass makes no `UseSpecular()` test keeps its envmap: Refract (`refract_dx9_helper.cpp`).</remarks>
    [Test]
    public void Resolve_AnEnvmapOnAnotherShader_StandsWithoutSpecular() =>
        Shading(Base + Envmap, "Refract").UndefinedWithoutSpecular.ShouldBeFalse();

    /// <remarks>
    /// LightmappedGeneric_Decal declares no shader parameter (`lightmappedgeneric_decal.cpp:21-22`) and its draw binds the
    /// base and the bumped lightmap only (`:99-114`): a `$envmap` in its VMT reflects nothing, at any setting.
    /// </remarks>
    [Test]
    public void Resolve_LightmappedGenericDecalWithAnEnvmap_ReflectsNothing()
    {
        SkinShaderFallbackConformanceTests.Resolve(Base + Envmap, "LightmappedGeneric").LocalReflection
            .ShouldNotBeNull("the control: the same VMT on LightmappedGeneric reflects");
        SkinShaderFallbackConformanceTests.Resolve(Base + Envmap, "LightmappedGeneric_Decal").LocalReflection.ShouldBeNull();
    }

    /// <remarks>
    /// A skin material with a bump map runs the generic params pass at every setting (B720 part 4), so `mat_specular 0`
    /// undefines its envmap on the skin shader and on each generic fallback alike.
    /// </remarks>
    [Test]
    public void Resolve_SkinMaterialFromTheGenericParams_IsUndefinedWithoutSpecularAtEverySetting()
    {
        MapEnvmapShading shading = Shading(Base + "\"$bumpmap\" \"test/normal\"\n\"$phong\" 1\n" + Envmap);

        shading.UndefinedWithoutSpecular.ShouldBeTrue();
        shading.Generic.ShouldNotBeNull().UndefinedWithoutSpecular.ShouldBeTrue();
        shading.GenericWithBumpMapOff.ShouldNotBeNull().UndefinedWithoutSpecular.ShouldBeTrue();
        shading.GenericWithBothOff.ShouldNotBeNull().UndefinedWithoutSpecular.ShouldBeTrue();
    }

    /// <remarks>`InitParamsSkin_DX9` runs under `mat_phong 1` and tests no `UseSpecular()`; under `mat_phong 0` the generic pass does.</remarks>
    [Test]
    public void Resolve_BaseMapAlphaPhongMaskMaterial_KeepsItsEnvmapWithoutSpecularWhilePhongIsOn()
    {
        MapEnvmapShading shading = Shading(Base + "\"$phong\" 1\n\"$basemapalphaphongmask\" 1\n" + Envmap);

        shading.UndefinedWithoutSpecular.ShouldBeFalse("mat_phong 1: the skin params pass");
        shading.Generic.ShouldNotBeNull().UndefinedWithoutSpecular.ShouldBeTrue("mat_phong 0");
        shading.GenericWithBumpMapOff.ShouldNotBeNull().UndefinedWithoutSpecular.ShouldBeFalse("mat_bumpmap 0, mat_phong 1");
        shading.GenericWithBothOff.ShouldNotBeNull().UndefinedWithoutSpecular.ShouldBeTrue("both off");
    }

    [TestCase("VertexLitGeneric")]
    [TestCase("UnlitGeneric")]
    public void Resolve_GenericMaterialWithAnEnvMapMask_SamplesIt(string shader)
    {
        Shading(Base + Envmap, shader).MaskedByMaskTexture.ShouldBeFalse("the control: no mask named");

        MapEnvmapShading shading = Shading(Base + Envmap + Mask, shader);

        shading.MaskedByMaskTexture.ShouldBeTrue();
        shading.MaskTexture.ShouldNotBeNull();
    }

    /// <remarks>
    /// **A world material samples its `$envmapmask` too, at a coordinate of its own** (B720): `InitLightmappedGeneric_DX9`
    /// loads it beside an envmap (`lightmappedgeneric_dx9_helper.cpp:269-272`), the draw binds it to sampler 5 with no sRGB
    /// read (`:498-501`, `:848-851`) and multiplies the reflection by its colour (`lightmappedgeneric_ps2_3_x.h:402-405`), at
    /// the base coordinate run through `$envmapmasktransform` — not through the base's own transform
    /// (`lightmappedgeneric_vs20.fxc:196-205`). The model shaders sample at the base's coordinate, so they do not.
    /// </remarks>
    [TestCase("LightmappedGeneric", true)]
    [TestCase("WorldVertexTransition", true)]
    [TestCase("VertexLitGeneric", false)]
    public void Resolve_AWorldMaterialWithAnEnvMapMask_SamplesItAtItsOwnCoordinate(string shader, bool ownCoordinate)
    {
        Shading(Base + Envmap, shader).MaskedByMaskTexture.ShouldBeFalse("the control: no mask named");

        MapEnvmapShading shading = Shading(Base + Envmap + Mask, shader);

        shading.MaskedByMaskTexture.ShouldBeTrue();
        shading.MaskTexture.ShouldNotBeNull();
        shading.MaskAtItsOwnCoordinate.ShouldBe(ownCoordinate);
    }

    /// <remarks>The lightmapped params pass undefines the mask beside a bump map (`lightmappedgeneric_dx9_helper.cpp:149-152`).</remarks>
    [Test]
    public void Resolve_AWorldEnvMapMaskBesideABumpMap_IsNotSampled()
    {
        MapEnvmapShading shading = Shading(Base + Envmap + Mask + "\"$bumpmap\" \"test/normal\"\n", "LightmappedGeneric");

        shading.MaskedByMaskTexture.ShouldBeFalse();
        shading.MaskTexture.ShouldBeNull();
    }

    /// <remarks>A light warp draws the bump shader (`:611`), which has no envmap mask sampler.</remarks>
    [Test]
    public void Resolve_EnvMapMaskBesideALightWarp_IsNotSampled() =>
        Shading(Base + Envmap + Mask + "\"$lightwarptexture\" \"test/colour\"\n").MaskedByMaskTexture.ShouldBeFalse();

    /// <remarks>
    /// The `$phong` material `WantsSkinShader` refuses keeps `$phong` through the params pass, so `InitSkin_DX9` runs and
    /// loads no mask, although every draw is the generic one.
    /// </remarks>
    [Test]
    public void Resolve_EnvMapMaskOnAPhongMaterialTheSkinShaderRefuses_IsNotSampled() =>
        Shading(Base + Envmap + Mask + "\"$phong\" 1\n").MaskedByMaskTexture.ShouldBeFalse();

    /// <remarks>A `$normalmapalphaenvmapmask` kept by a light warp's tangent space undefines the mask (`:181-193`).</remarks>
    [Test]
    public void Resolve_EnvMapMaskBesideAKeptNormalMapAlphaFlag_IsNotLoaded() =>
        Shading(Base + Envmap + Mask + "\"$lightwarptexture\" \"test/colour\"\n\"$normalmapalphaenvmapmask\" 1\n")
            .MaskTexture.ShouldBeNull();

    /// <remarks>
    /// `$phong` + `$basemapalphaphongmask 1` with no bump map or warp: the skin init (no mask) while `mat_phong` is on; under
    /// `mat_phong 0` the generic params pass clears `$phong` (`:105-109`), `InitVertexLitGeneric_DX9` loads the mask and the
    /// non-bump shader samples it.
    /// </remarks>
    [Test]
    public void Resolve_BaseMapAlphaPhongMaskMaterialWithAnEnvMapMask_SamplesItUnderPhongOffOnly()
    {
        MapEnvmapShading shading = Shading(Base + Envmap + Mask + "\"$phong\" 1\n\"$basemapalphaphongmask\" 1\n");

        shading.MaskedByMaskTexture.ShouldBeFalse("the skin shader");
        shading.MaskTexture.ShouldNotBeNull();
        shading.Generic.ShouldNotBeNull().MaskedByMaskTexture.ShouldBeTrue("mat_phong 0");
        shading.GenericWithBumpMapOff.ShouldNotBeNull().MaskedByMaskTexture.ShouldBeFalse("mat_bumpmap 0: the skin init loaded none");
        shading.GenericWithBothOff.ShouldNotBeNull().MaskedByMaskTexture.ShouldBeTrue("both off");
    }
}
