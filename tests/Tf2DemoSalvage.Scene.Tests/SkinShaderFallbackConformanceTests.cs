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
    private static ResolvedMaterial Resolve(string body)
    {
        byte[] texture = VtfFixture.Build(VtfFormat.Rgba8888, 2, 2, 1, new byte[16]);

        Dictionary<string, byte[]> files = new()
        {
            ["materials/test/surface.vmt"] = Encoding.UTF8.GetBytes("\"VertexLitGeneric\"\n{\n" + body + "\n}"),
            ["materials/test/colour.vtf"] = texture,
            ["materials/test/normal.vtf"] = texture,
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
}
