using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Text;

using Microsoft.Extensions.Logging.Abstractions;

using Tf2DemoSalvage.Content.Assets;
using Tf2DemoSalvage.Content.Tests.Assets;

namespace Tf2DemoSalvage.Scene.Tests;

/// <summary>
/// What a <c>$phong</c> VertexLitGeneric material resolves to on the skin shader and on the generic path (B720), through
/// the production <c>Resolve</c> over a hand-built pakfile and no game.
/// </summary>
/// <remarks>
/// **Read from published source.** `WantsSkinShader` (`vertexlitgeneric_dx9_helper.cpp:38-59`) is false for `$phong` with
/// no light warp, no `$basemapalphaphongmask 1` and no bump map, and then every draw is
/// `DrawVertexLitGeneric_DX9_Internal` (`:1515`), which has no phong, no rim and the generic reflection. A material the
/// skin shader does draw falls to that path under `mat_phong 0` or `mat_bumpmap 0`, where its envmap tint is converted
/// gamma to linear (`:1096`), its `$envmapcontrast` and `$envmapsaturation` are read, and with no `$basetexture` its base
/// is black (`:938-941`) — unless the generic init has undefined the envmap (`:197-211`).
/// </remarks>
public sealed class SkinShaderFallbackConformanceTests
{
    /// <summary>Resolves one material over three textures: <c>test/colour</c> and <c>test/normal</c> with no alpha flags, <c>test/alpha</c> with <c>EIGHTBITALPHA</c>.</summary>
    internal static ResolvedMaterial Resolve(string body, string shader = "VertexLitGeneric")
    {
        byte[] texture = VtfFixture.Build(VtfFormat.Rgba8888, 2, 2, 1, new byte[16]);
        byte[] alpha = VtfFixture.Build(VtfFormat.Rgba8888, 2, 2, 1, new byte[16]);
        BinaryPrimitives.WriteUInt32LittleEndian(alpha.AsSpan(20), 0x2000);

        Dictionary<string, byte[]> files = new()
        {
            ["materials/test/surface.vmt"] = Encoding.UTF8.GetBytes("\"" + shader + "\"\n{\n" + body + "\n}"),
            ["materials/test/colour.vtf"] = texture,
            ["materials/test/normal.vtf"] = texture,
            ["materials/test/alpha.vtf"] = alpha,
        };

        return MapAssets.Resolve(
            NullLogger.Instance,
            "test/surface",
            PakFile.Read(MaterialFallbackConformanceTests.Zip(files)),
            GameArchives.Open(null),
            0,
            model: true);
    }

    [Test]
    public void Resolve_PhongTheSkinShaderDoesNotWant_DrawsNoHighlight() =>
        Resolve("\"$basetexture\" \"test/colour\"\n\"$phong\" 1").Phong.ShouldBeNull();

    [Test]
    public void Resolve_PhongWithBaseMapAlphaPhongMask_KeepsItsHighlight() =>
        Resolve("\"$basetexture\" \"test/colour\"\n\"$phong\" 1\n\"$basemapalphaphongmask\" 1").Phong.ShouldNotBeNull();

    [Test]
    public void Resolve_PhongTheSkinShaderDoesNotWantWithAnEnvmap_ReflectsTheGenericWay()
    {
        MapEnvmapShading shading = Resolve(
            "\"$basetexture\" \"test/colour\"\n\"$phong\" 1\n\"$envmap\" \"env_cubemap\"\n\"$envmaptint\" \"[.5 .5 .5]\"")
            .LocalReflection.ShouldNotBeNull();

        shading.SkinMask.ShouldBeNull();
        shading.Tint.Red.ShouldBe(ModelMaterialShading.GammaToLinear(0.5f));
    }

    [Test]
    public void Resolve_SkinMaterialWithAnEnvmap_CarriesTheGenericReflection()
    {
        MapEnvmapShading shading = Resolve(
            "\"$basetexture\" \"test/colour\"\n\"$bumpmap\" \"test/normal\"\n\"$phong\" 1\n\"$normalmapalphaenvmapmask\" 1\n" +
            "\"$envmap\" \"env_cubemap\"\n\"$envmaptint\" \"[.5 .5 .5]\"\n\"$envmapcontrast\" 1\n\"$envmapsaturation\" .25")
            .LocalReflection.ShouldNotBeNull();

        shading.SkinMask.ShouldBe(EnvMapMask.BumpAlpha);
        shading.Tint.Red.ShouldBe(0.5f, "the skin shader sends the tint as it is");
        (shading.Contrast, shading.Saturation).ShouldBe((0f, 1f), "the skin shader reads neither");
        shading.MaskedByNormalMapAlpha.ShouldBeTrue();
        shading.Generic.ShouldBe(new MapGenericReflection((ModelMaterialShading.GammaToLinear(0.5f), ModelMaterialShading.GammaToLinear(0.5f), ModelMaterialShading.GammaToLinear(0.5f)), 1f, 0.25f, BlackBase: false, MaskedByNormalMapAlpha: true, UndefinedWithoutSpecular: true));
    }

    [Test]
    public void Resolve_SkinMaterialWithNoBaseTexture_IsBlackOnTheGenericPath()
    {
        ResolvedMaterial resolved = Resolve("\"$phong\" 1\n\"$basemapalphaphongmask\" 1\n\"$envmap\" \"env_cubemap\"");

        resolved.Texture.ShouldNotBeNull().Image.Top.ToArray()[..3].ShouldBe(new byte[] { 255, 255, 255 }, "the skin shader's white");
        resolved.LocalReflection.ShouldNotBeNull().Generic.ShouldNotBeNull().BlackBase.ShouldBeTrue();
    }

    /// <remarks>
    /// **The generic `InitParams` runs for a skin material too, at every setting** (B720 remainder): it asks
    /// `WantsSkinShader` while every texture var is still a string (`materialsystem.dll` `FUN_180007a40` parses, then
    /// `FUN_18004fd60` calls the shader's `InitShaderParams`; `IsTexture()` is the var's type, `imaterialvar.h:143-146`),
    /// so a bump map or warp does not yet count and only `$basemapalphaphongmask 1` routes to `InitParamsSkin_DX9`. A bump
    /// map and an `$envmapmask` without `$normalmapalphaenvmapmask` then undefine the envmap
    /// (`vertexlitgeneric_dx9_helper.cpp:204-212`), and `InitSkin_DX9` loads no cube (`skin_dx9_helper.cpp:205-208`): the
    /// skin shader draws it with none.
    /// </remarks>
    [Test]
    public void Resolve_SkinMaterialWhoseEnvmapTheGenericParamsDrop_ReflectsNothing() =>
        Resolve(
            "\"$basetexture\" \"test/colour\"\n\"$bumpmap\" \"test/normal\"\n\"$phong\" 1\n\"$envmap\" \"env_cubemap\"\n" +
            "\"$envmapmask\" \"test/colour\"")
            .LocalReflection.ShouldBeNull();

    /// <remarks>The same rule on a material with no `$phong`, which the generic path draws at every setting.</remarks>
    [Test]
    public void Resolve_GenericMaterialWithABumpMapAndAnEnvMapMask_ReflectsNothing()
    {
        string common = "\"$basetexture\" \"test/colour\"\n\"$bumpmap\" \"test/normal\"\n\"$envmap\" \"env_cubemap\"\n";

        Resolve(common).LocalReflection.ShouldNotBeNull("the control: without the mask the envmap stands");
        Resolve(common + "\"$envmapmask\" \"test/colour\"").LocalReflection.ShouldBeNull();
    }

    /// <remarks>
    /// **`$normalmapalphaenvmapmask` clears `$basealphaenvmapmask` before the base-alpha check reads it**
    /// (`vertexlitgeneric_dx9_helper.cpp:190-202`), so a bumped material asking for both keeps its envmap.
    /// </remarks>
    [Test]
    public void Resolve_SkinMaterialWithBothMaskFlags_KeepsItsGenericReflection() =>
        Resolve(
            "\"$basetexture\" \"test/colour\"\n\"$bumpmap\" \"test/normal\"\n\"$phong\" 1\n\"$envmap\" \"env_cubemap\"\n" +
            "\"$basealphaenvmapmask\" 1\n\"$normalmapalphaenvmapmask\" 1")
            .LocalReflection.ShouldNotBeNull().Generic.ShouldNotBeNull();

    /// <remarks>
    /// **`$basemapalphaphongmask 1` is the one `$phong` case the params pass routes to the skin init**, and only while
    /// `mat_phong` is on (`vertexlitgeneric_dx9_helper.cpp:105-113`). `InitParamsSkin_DX9` undefines no envmap and
    /// initialises neither `$envmapcontrast` nor `$envmapsaturation`, which the shader system then zero-fills
    /// (`materialsystem.dll` `FUN_18004fd60`, case 7): under `mat_bumpmap 0` the generic draw reflects, greyscale. Under
    /// `mat_phong 0` the generic init ran, and a bump map with `$basealphaenvmapmask` undefines the envmap. 29 shipped
    /// cosmetics take this route (the `envmap-undefined` probe), e.g. `models/player/items/all_class/id_badge_gold`.
    /// </remarks>
    [Test]
    public void Resolve_BaseMapAlphaPhongMaskMaterialTheGenericParamsWouldDrop_ReflectsUnderBumpMapOffOnly()
    {
        MapEnvmapShading shading = Resolve(
            "\"$basetexture\" \"test/alpha\"\n\"$bumpmap\" \"test/normal\"\n\"$phong\" 1\n\"$basemapalphaphongmask\" 1\n" +
            "\"$basealphaenvmapmask\" 1\n\"$envmap\" \"env_cubemap\"")
            .LocalReflection.ShouldNotBeNull("the skin shader's own init keeps the envmap");

        shading.Generic.ShouldBeNull("mat_phong 0: the generic init undefines it");
        shading.GenericWithBothOff.ShouldBeNull("both off: the generic init again");
        MapGenericReflection bumpOff = shading.GenericWithBumpMapOff.ShouldNotBeNull();
        (bumpOff.Contrast, bumpOff.Saturation).ShouldBe((0f, 0f), "neither float initialised, so zero-filled");
    }

    [Test]
    public void Resolve_BaseMapAlphaPhongMaskMaterialNamingItsSaturation_KeepsItUnderBumpMapOff() =>
        Resolve(
            "\"$basetexture\" \"test/alpha\"\n\"$phong\" 1\n\"$basemapalphaphongmask\" 1\n\"$envmap\" \"env_cubemap\"\n" +
            "\"$envmapsaturation\" .25")
            .LocalReflection.ShouldNotBeNull().GenericWithBumpMapOff.ShouldNotBeNull().Saturation.ShouldBe(0.25f);

    /// <remarks>
    /// **`mat_bumpmap 0` withdraws the tangent space from a bump map with no light warp, and with it
    /// `$normalmapalphaenvmapmask`** (`vertexlitgeneric_dx9_helper.cpp:173-187`); an `$envmapmask` beside a bump map then
    /// undefines the envmap (`:191-195`, `:204-212`). Under `mat_phong 0` the bump map keeps the tangent space and the envmap stands.
    /// </remarks>
    [Test]
    public void Resolve_SkinNormalMapAlphaMaskWithAnEnvMapMask_LosesTheEnvmapOnlyUnderBumpMapOff()
    {
        MapEnvmapShading shading = Resolve(
            "\"$basetexture\" \"test/colour\"\n\"$bumpmap\" \"test/normal\"\n\"$phong\" 1\n\"$normalmapalphaenvmapmask\" 1\n" +
            "\"$envmapmask\" \"test/colour\"\n\"$envmap\" \"env_cubemap\"")
            .LocalReflection.ShouldNotBeNull();

        shading.Generic.ShouldNotBeNull("mat_phong 0: the mask flag survives");
        shading.GenericWithBumpMapOff.ShouldBeNull();
        shading.GenericWithBothOff.ShouldBeNull();
    }

    /// <remarks>
    /// **UnlitGeneric has no bump map parameter** (`unlitgeneric_dx9.cpp:103`, `info.m_nBumpmap = -1`), and both envmap
    /// undefinitions test `m_nBumpmap != -1` (`vertexlitgeneric_dx9_helper.cpp:197-198`, `:204`): a stray `$bumpmap` in its
    /// VMT removes nothing.
    /// </remarks>
    [Test]
    public void Resolve_UnlitGenericWithAStrayBumpMapAndAnEnvMapMask_Reflects() =>
        Resolve(
            "\"$basetexture\" \"test/colour\"\n\"$bumpmap\" \"test/normal\"\n\"$envmap\" \"env_cubemap\"\n\"$envmapmask\" \"test/colour\"",
            "UnlitGeneric")
            .LocalReflection.ShouldNotBeNull();

    /// <remarks>
    /// **UnlitGeneric never keeps `$normalmapalphaenvmapmask`**, even beside a stray `$bumpmap`: with no bump map and no warp parameter it has no tangent
    /// space, so the flag is cleared (`:185-188`) before it could clear `$basealphaenvmapmask` (`:190-195`); over an alpha
    /// base the base-alpha mask stands.
    /// </remarks>
    [Test]
    public void Resolve_UnlitGenericWithBothMaskFlags_MasksByTheBaseAlpha()
    {
        MapEnvmapShading shading = Resolve(
            "\"$basetexture\" \"test/alpha\"\n\"$bumpmap\" \"test/normal\"\n\"$basealphaenvmapmask\" 1\n" +
            "\"$normalmapalphaenvmapmask\" 1\n\"$envmap\" \"env_cubemap\"",
            "UnlitGeneric")
            .LocalReflection.ShouldNotBeNull();

        shading.MaskedByBaseAlpha.ShouldBeTrue();
        shading.MaskedByNormalMapAlpha.ShouldBeFalse();
    }

    /// <remarks>
    /// **The mask is per setting, because the flags depend on which params pass ran** (review of part 4).
    /// `InitParamsSkin_DX9` clears `$basealphaenvmapmask` only with no base (`skin_dx9_helper.cpp:73-77`) and keeps
    /// `$normalmapalphaenvmapmask` for any `$phong` (`:85-97`); with the bump map unloaded the generic draw takes the non-bump
    /// shader (`vertexlitgeneric_dx9_helper.cpp:611`, `:772`), whose mask is `BASEALPHAENVMAPMASK` (`:817`;
    /// `vertexlit_and_unlit_generic_ps2x.fxc:339-342`). Under `mat_phong 0` the generic pass keeps the normal-map flag (bump
    /// map loaded), which clears the base-alpha one (`:190-195`), and the bump shader masks by the map (`:717`). Both off,
    /// the flag loses its tangent space and the base-alpha flag beside a bump map undefines the envmap (`:197-202`).
    /// </remarks>
    [Test]
    public void Resolve_BaseMapAlphaPhongMaskWithBothMaskFlags_MasksPerSetting()
    {
        MapEnvmapShading shading = Resolve(
            "\"$basetexture\" \"test/alpha\"\n\"$bumpmap\" \"test/normal\"\n\"$phong\" 1\n\"$basemapalphaphongmask\" 1\n" +
            "\"$basealphaenvmapmask\" 1\n\"$normalmapalphaenvmapmask\" 1\n\"$envmap\" \"env_cubemap\"")
            .LocalReflection.ShouldNotBeNull();

        MapGenericReflection phongOff = shading.Generic.ShouldNotBeNull();
        (phongOff.MaskedByNormalMapAlpha, phongOff.MaskedByBaseAlpha).ShouldBe((true, false), "mat_phong 0: the bump map's alpha");
        MapGenericReflection bumpOff = shading.GenericWithBumpMapOff.ShouldNotBeNull();
        (bumpOff.MaskedByNormalMapAlpha, bumpOff.MaskedByBaseAlpha).ShouldBe((false, true), "mat_bumpmap 0: the base alpha");
        shading.GenericWithBothOff.ShouldBeNull("both off: the generic pass undefines the envmap");
    }

    /// <remarks>
    /// **`$selfillum` with no base survives only the skin params pass** (`skin_dx9_helper.cpp:73-77` clears the base-alpha
    /// flag alone; the generic pass clears both, `vertexlitgeneric_dx9_helper.cpp:160-166`). Under `mat_phong 0` the generic
    /// pass ran, so with no base `$selfillumfresnel` cannot clear the normal-map flag and the bump map masks. Under
    /// `mat_bumpmap 0` the skin pass ran and the snapshot would clear it, but with no bump map loaded there is no mask to
    /// lose; the two cannot be told apart in a picture.
    /// </remarks>
    [Test]
    public void Resolve_SelfIllumFresnelWithNoBaseUnderPhongOff_KeepsTheBumpAlphaMask() =>
        Resolve(
            "\"$bumpmap\" \"test/normal\"\n\"$phong\" 1\n\"$basemapalphaphongmask\" 1\n\"$normalmapalphaenvmapmask\" 1\n" +
            "\"$selfillum\" 1\n\"$selfillumfresnel\" 1\n\"$envmap\" \"env_cubemap\"")
            .LocalReflection.ShouldNotBeNull().Generic.ShouldNotBeNull().MaskedByNormalMapAlpha.ShouldBeTrue();

    /// <remarks>
    /// **The generic snapshot clears `$normalmapalphaenvmapmask` under `$selfillumfresnel`** with no detail texture
    /// (`vertexlitgeneric_dx9_helper.cpp:467`, `:489-493`), over a base, since the params pass clears `$selfillum` without
    /// one (`:160-166`).
    /// </remarks>
    [Test]
    public void Resolve_NormalMapAlphaMaskWithSelfIllumFresnel_IsNotMaskedOnTheGenericPath()
    {
        string common = "\"$basetexture\" \"test/colour\"\n\"$bumpmap\" \"test/normal\"\n\"$normalmapalphaenvmapmask\" 1\n" +
            "\"$envmap\" \"env_cubemap\"\n\"$selfillum\" 1\n";

        Resolve(common).LocalReflection.ShouldNotBeNull().MaskedByNormalMapAlpha.ShouldBeTrue("the control: no Fresnel");
        Resolve(common + "\"$selfillumfresnel\" 1").LocalReflection.ShouldNotBeNull().MaskedByNormalMapAlpha.ShouldBeFalse();
    }

    /// <remarks>
    /// **The sampler is bound only `if ( bHasSpecularExponentTexture &amp;&amp; bHasPhong )`** inside `DrawSkin_DX9`
    /// (`skin_dx9_helper.cpp:560-567`); a `$phong` the skin shader refuses never reaches it.
    /// </remarks>
    [Test]
    public void Resolve_PhongTheSkinShaderDoesNotWantWithAnExponentTexture_LoadsNoExponentMap() =>
        Resolve("\"$basetexture\" \"test/colour\"\n\"$phong\" 1\n\"$phongexponenttexture\" \"test/colour\"")
            .PhongExponentMap.ShouldBeNull();

    [Test]
    public void Resolve_SkinMaterialWithAnExponentTexture_LoadsItsExponentMap() =>
        Resolve("\"$basetexture\" \"test/colour\"\n\"$phong\" 1\n\"$basemapalphaphongmask\" 1\n\"$phongexponenttexture\" \"test/colour\"")
            .PhongExponentMap.ShouldNotBeNull();

    /// <remarks>
    /// **A light warp takes VertexLitGeneric down the bump shader** (`vertexlitgeneric_dx9_helper.cpp:611`, `:692-724`),
    /// which has no `BASEALPHAENVMAPMASK` combo and masks only by the normal map's alpha
    /// (`vertexlit_and_unlit_generic_bump_ps2x.fxc:165-170`). A skin material reaches the same draw only off the skin
    /// shader, whose own mask has no base-alpha combo (`skin_ps20b.fxc:228-242`); that draw is the render tests'.
    /// </remarks>
    [Test]
    public void Resolve_BaseAlphaEnvMapMaskWithALightWarp_IsNotMasked()
    {
        string common = "\"$basetexture\" \"test/alpha\"\n\"$basealphaenvmapmask\" 1\n\"$envmap\" \"env_cubemap\"\n";

        Resolve(common).LocalReflection.ShouldNotBeNull().MaskedByBaseAlpha.ShouldBeTrue("the control: no warp, the base masks");
        Resolve(common + "\"$lightwarptexture\" \"test/colour\"").LocalReflection.ShouldNotBeNull()
            .MaskedByBaseAlpha.ShouldBeFalse();
    }

    /// <remarks>
    /// **Over a base with alpha flags the flag holds**, on every shader whose init was read: VertexLitGeneric and UnlitGeneric
    /// share `InitVertexLitGeneric_DX9` (`unlitgeneric_dx9.cpp:166`, `:182`), WorldVertexTransition shares
    /// `InitLightmappedGeneric_DX9` (`worldvertextransition.cpp:135`, `:141`). LightmappedGeneric_Decal clears the flag by
    /// the same rules (`lightmappedgeneric_decal.cpp:36-41`, `:57-66`) but draws no envmap for it to mask (B720 part 5
    /// review), so it has no case here.
    /// </remarks>
    [TestCase("VertexLitGeneric")]
    [TestCase("UnlitGeneric")]
    [TestCase("LightmappedGeneric")]
    [TestCase("WorldVertexTransition")]
    public void Resolve_BaseAlphaEnvMapMaskOverABaseWithAlpha_IsMasked(string shader) =>
        ResolveBaseAlphaMask(shader, "test/alpha").ShouldBeTrue();

    /// <remarks>
    /// **Cleared over a base with no alpha flags** (`IsTranslucent()`): `InitVertexLitGeneric_DX9`
    /// (`vertexlitgeneric_dx9_helper.cpp:290-303`), `InitSkin_DX9` (`skin_dx9_helper.cpp:149-160`),
    /// `InitLightmappedGeneric_DX9` (`lightmappedgeneric_dx9_helper.cpp:210-219`).
    /// </remarks>
    [TestCase("VertexLitGeneric")]
    [TestCase("UnlitGeneric")]
    [TestCase("LightmappedGeneric")]
    [TestCase("WorldVertexTransition")]
    public void Resolve_BaseAlphaEnvMapMaskOverAnOpaqueBase_IsNotMasked(string shader) =>
        ResolveBaseAlphaMask(shader, "test/colour").ShouldBeFalse();

    /// <remarks>
    /// **Cleared with no base at all**, by every init's params pass: `InitParamsVertexLitGeneric_DX9` (`:160-166`),
    /// `InitParamsSkin_DX9` (`skin_dx9_helper.cpp:73-77`), `InitParamsLightmappedGeneric_DX9`
    /// (`lightmappedgeneric_dx9_helper.cpp:142-147`).
    /// </remarks>
    [TestCase("VertexLitGeneric")]
    [TestCase("UnlitGeneric")]
    [TestCase("LightmappedGeneric")]
    [TestCase("WorldVertexTransition")]
    public void Resolve_BaseAlphaEnvMapMaskWithNoBase_IsNotMasked(string shader) =>
        ResolveBaseAlphaMask(shader, null).ShouldBeFalse();

    private static bool ResolveBaseAlphaMask(string shader, string? baseTexture) =>
        Resolve(
                (baseTexture is null ? string.Empty : "\"$basetexture\" \"" + baseTexture + "\"\n") +
                "\"$basealphaenvmapmask\" 1\n\"$envmap\" \"env_cubemap\"",
                shader)
            .LocalReflection.ShouldNotBeNull().MaskedByBaseAlpha;
}
