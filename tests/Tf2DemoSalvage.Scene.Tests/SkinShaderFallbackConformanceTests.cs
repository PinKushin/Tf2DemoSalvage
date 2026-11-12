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
        shading.Generic.ShouldBe(new MapGenericReflection((ModelMaterialShading.GammaToLinear(0.5f), ModelMaterialShading.GammaToLinear(0.5f), ModelMaterialShading.GammaToLinear(0.5f)), 1f, 0.25f, BlackBase: false));
    }

    [Test]
    public void Resolve_SkinMaterialWithNoBaseTexture_IsBlackOnTheGenericPath()
    {
        ResolvedMaterial resolved = Resolve("\"$phong\" 1\n\"$basemapalphaphongmask\" 1\n\"$envmap\" \"env_cubemap\"");

        resolved.Texture.ShouldNotBeNull().Image.Top.ToArray()[..3].ShouldBe(new byte[] { 255, 255, 255 }, "the skin shader's white");
        resolved.LocalReflection.ShouldNotBeNull().Generic.ShouldNotBeNull().BlackBase.ShouldBeTrue();
    }

    [Test]
    public void Resolve_SkinMaterialWhoseEnvmapTheGenericInitDrops_HasNoGenericReflection()
    {
        // A bump map and an `$envmapmask` without `$normalmapalphaenvmapmask`: the generic init undefines the envmap.
        MapEnvmapShading shading = Resolve(
            "\"$basetexture\" \"test/colour\"\n\"$bumpmap\" \"test/normal\"\n\"$phong\" 1\n\"$envmap\" \"env_cubemap\"\n" +
            "\"$envmapmask\" \"test/colour\"")
            .LocalReflection.ShouldNotBeNull();

        shading.SkinMask.ShouldNotBeNull();
        shading.Generic.ShouldBeNull();
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
    /// `InitLightmappedGeneric_DX9` (`worldvertextransition.cpp:135`, `:141`), and LightmappedGeneric_Decal has its own
    /// (`lightmappedgeneric_decal.cpp:57-66`).
    /// </remarks>
    [TestCase("VertexLitGeneric")]
    [TestCase("UnlitGeneric")]
    [TestCase("LightmappedGeneric")]
    [TestCase("WorldVertexTransition")]
    [TestCase("LightmappedGeneric_Decal")]
    public void Resolve_BaseAlphaEnvMapMaskOverABaseWithAlpha_IsMasked(string shader) =>
        ResolveBaseAlphaMask(shader, "test/alpha").ShouldBeTrue();

    /// <remarks>
    /// **Cleared over a base with no alpha flags** (`IsTranslucent()`): `InitVertexLitGeneric_DX9`
    /// (`vertexlitgeneric_dx9_helper.cpp:290-303`), `InitSkin_DX9` (`skin_dx9_helper.cpp:149-160`),
    /// `InitLightmappedGeneric_DX9` (`lightmappedgeneric_dx9_helper.cpp:210-219`), LightmappedGeneric_Decal's
    /// (`lightmappedgeneric_decal.cpp:57-66`).
    /// </remarks>
    [TestCase("VertexLitGeneric")]
    [TestCase("UnlitGeneric")]
    [TestCase("LightmappedGeneric")]
    [TestCase("WorldVertexTransition")]
    [TestCase("LightmappedGeneric_Decal")]
    public void Resolve_BaseAlphaEnvMapMaskOverAnOpaqueBase_IsNotMasked(string shader) =>
        ResolveBaseAlphaMask(shader, "test/colour").ShouldBeFalse();

    /// <remarks>
    /// **Cleared with no base at all**, by every init's params pass: `InitParamsVertexLitGeneric_DX9` (`:160-166`),
    /// `InitParamsSkin_DX9` (`skin_dx9_helper.cpp:73-77`), `InitParamsLightmappedGeneric_DX9`
    /// (`lightmappedgeneric_dx9_helper.cpp:142-147`), LightmappedGeneric_Decal's (`lightmappedgeneric_decal.cpp:36-41`).
    /// </remarks>
    [TestCase("VertexLitGeneric")]
    [TestCase("UnlitGeneric")]
    [TestCase("LightmappedGeneric")]
    [TestCase("WorldVertexTransition")]
    [TestCase("LightmappedGeneric_Decal")]
    public void Resolve_BaseAlphaEnvMapMaskWithNoBase_IsNotMasked(string shader) =>
        ResolveBaseAlphaMask(shader, null).ShouldBeFalse();

    private static bool ResolveBaseAlphaMask(string shader, string? baseTexture) =>
        Resolve(
                (baseTexture is null ? string.Empty : "\"$basetexture\" \"" + baseTexture + "\"\n") +
                "\"$basealphaenvmapmask\" 1\n\"$envmap\" \"env_cubemap\"",
                shader)
            .LocalReflection.ShouldNotBeNull().MaskedByBaseAlpha;
}
