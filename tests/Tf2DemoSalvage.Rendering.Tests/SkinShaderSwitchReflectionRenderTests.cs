using System.Collections.Generic;
using System.Linq;

using Tf2DemoSalvage.Content.Assets;
using Tf2DemoSalvage.Content.Bsp;

namespace Tf2DemoSalvage.Rendering.Tests;

/// <summary>
/// A model's reflection and base under <c>mat_phong 0</c> and <c>mat_bumpmap 0</c>, drawn on a real device from hand-built
/// materials (B720's remainders).
/// </summary>
/// <remarks>
/// **What the engine does, read from published source and disassembly.** Either switch takes a skin material to
/// `DrawVertexLitGeneric_DX9_Internal` (`vertexlitgeneric_dx9_helper.cpp:1515`), whose reflection is masked by the
/// generic combos (`:717`, `:816-817`) rather than by the skin's `lerp( baseColor.a, fSpecMask, w )`, and whose base,
/// with no `$basetexture` and an envmap, is `TEXTURE_BLACK` (`:938-941`) where the skin binds white
/// (`skin_dx9_helper.cpp:527-530`). With no bump map loaded the bump shader samples `TEXTURE_NORMALMAP_FLAT` (`:1077`),
/// whose texel is BGRX `ff 7f 7f` (`materialsystem.dll` `FUN_18000fea0`, 0x18001057f), so `specularFactor =
/// normalTexel.a` (`vertexlit_and_unlit_generic_bump_ps2x.fxc:165-170`) is 1; without a warp it is the non-bump shader,
/// which has no normal-map mask at all.
///
/// **Synthetic** (D38): uniform cube faces and an ambient-only light, so neither depends on the normal, and each
/// prediction is an equality with a control material drawn the generic way from the start.
/// </remarks>
public sealed class SkinShaderSwitchReflectionRenderTests
{
    [Test]
    public void DrawModelPose_NormalMapAlphaMaskWithBumpMapOff_ReflectsUnmasked()
    {
        using OffscreenTarget? target = OffscreenTarget.TryCreate(64, 64);

        if (target is null)
        {
            Assert.Ignore("no Direct3D on this machine");
            return;
        }

        MapEnvmapShading shading = new((1f, 1f, 1f), 0f, 1f, MaskedByBaseAlpha: false, 1f, MaskedByNormalMapAlpha: true);

        int clearOn = Sum(Draw(target, Material(Grey(255), shading, bumpAlpha: 0), phong: true, bumpMap: true));
        int opaqueOn = Sum(Draw(target, Material(Grey(255), shading, bumpAlpha: 255), phong: true, bumpMap: true));
        int clearOff = Sum(Draw(target, Material(Grey(255), shading, bumpAlpha: 0), phong: true, bumpMap: false));

        TestContext.Out.WriteLine($"bump on: alpha 0 {clearOn}, alpha 255 {opaqueOn}; bump off, alpha 0: {clearOff}");

        opaqueOn.ShouldBeGreaterThan(clearOn + 8, "the control: with the map read, its alpha masks the reflection");
        clearOff.ShouldBe(opaqueOn, "with no bump map loaded the flat normal map's alpha is 1, so nothing is masked");
    }

    [TestCase(false, true)]
    [TestCase(true, false)]
    public void DrawModelPose_SkinMaskedReflectionWithTheSkinShaderOff_TakesTheGenericMask(bool phong, bool bumpMap)
    {
        using OffscreenTarget? target = OffscreenTarget.TryCreate(64, 64);

        if (target is null)
        {
            Assert.Ignore("no Direct3D on this machine");
            return;
        }

        // Base alpha 0: the skin's mask (`EnvMapMask.BaseAlpha`) hides the reflection; the generic path, asked for no
        // mask by the material's flags, shows all of it.
        MapEnvmapShading generic = new((1f, 1f, 1f), 0f, 1f, MaskedByBaseAlpha: false, 1f, MaskedByNormalMapAlpha: false);
        MapEnvmapShading skin = generic with
        {
            SkinMask = EnvMapMask.BaseAlpha,
            Generic = new MapGenericReflection((1f, 1f, 1f), 0f, 1f, BlackBase: false),
        };

        int skinOn = Sum(Draw(target, Material(Grey(0), skin, skinShader: true), phong: true, bumpMap: true));
        int control = Sum(Draw(target, Material(Grey(0), generic), phong: true, bumpMap: true));
        int skinOff = Sum(Draw(target, Material(Grey(0), skin, skinShader: true), phong, bumpMap));

        TestContext.Out.WriteLine($"skin {skinOn}, generic control {control}, skin with mat_phong {phong} mat_bumpmap {bumpMap}: {skinOff}");

        control.ShouldBeGreaterThan(skinOn + 8, "the control: the skin's base-alpha mask hides the reflection");
        skinOff.ShouldBe(control, "without the skin shader the generic path masks by the material's own flags");
    }

    [TestCase(false, true)]
    [TestCase(true, false)]
    public void DrawModelPose_SkinMaterialWithNoBaseWithTheSkinShaderOff_DrawsABlackBase(bool phong, bool bumpMap)
    {
        using OffscreenTarget? target = OffscreenTarget.TryCreate(64, 64);

        if (target is null)
        {
            Assert.Ignore("no Direct3D on this machine");
            return;
        }

        // The skin's white base (alpha 1, so its mask passes all of the reflection) against the generic black one.
        MapEnvmapShading generic = new((1f, 1f, 1f), 0f, 1f, MaskedByBaseAlpha: false, 1f, MaskedByNormalMapAlpha: false);
        MapEnvmapShading skin = generic with
        {
            SkinMask = EnvMapMask.BaseAlpha,
            Generic = new MapGenericReflection((1f, 1f, 1f), 0f, 1f, BlackBase: true),
        };

        byte[] white = [255, 255, 255, 255];
        byte[] black = [0, 0, 0, 255];

        int skinOn = Sum(Draw(target, Material(white, skin, skinShader: true, size: 1), phong: true, bumpMap: true));
        int control = Sum(Draw(target, Material(black, generic, size: 1), phong: true, bumpMap: true));
        int skinOff = Sum(Draw(target, Material(white, skin, skinShader: true, size: 1), phong, bumpMap));

        TestContext.Out.WriteLine($"skin {skinOn}, black control {control}, skin with mat_phong {phong} mat_bumpmap {bumpMap}: {skinOff}");

        skinOn.ShouldBeGreaterThan(control + 8, "the control: the skin's white base is lit by the ambient light");
        skinOff.ShouldBe(control, "the generic path binds TEXTURE_BLACK for an envmap with no base texture");
    }

    /// <summary>A switch turned off and back on draws the skin material as it was: the generic constants go into the bind's copy.</summary>
    [TestCase(false, true)]
    [TestCase(true, false)]
    public void DrawModelPose_SkinMaterialAfterASwitchOffAndOnAgain_DrawsAsBefore(bool phong, bool bumpMap)
    {
        using OffscreenTarget? target = OffscreenTarget.TryCreate(64, 64);

        if (target is null)
        {
            Assert.Ignore("no Direct3D on this machine");
            return;
        }

        MapEnvmapShading generic = new((1f, 1f, 1f), 0f, 1f, MaskedByBaseAlpha: false, 1f, MaskedByNormalMapAlpha: false);
        MapEnvmapShading skin = generic with
        {
            SkinMask = EnvMapMask.BaseAlpha,
            Generic = new MapGenericReflection((1f, 1f, 1f), 0f, 1f, BlackBase: false),
        };

        int control = Sum(Draw(target, Material(Grey(0), generic), phong: true, bumpMap: true));

        // One MapAssets for all three, so the renderer keeps the material it uploaded the first time.
        MapAssets assets = Material(Grey(0), skin, skinShader: true);
        int first = Sum(Draw(target, assets, phong: true, bumpMap: true));
        int off = Sum(Draw(target, assets, phong, bumpMap));
        int again = Sum(Draw(target, assets, phong: true, bumpMap: true));

        TestContext.Out.WriteLine($"skin {first}, off {off} (control {control}), on again {again}");

        off.ShouldBe(control, "the control: the switch takes the generic reflection");
        again.ShouldBe(first, "turning the switch back on restores the skin's reflection");
    }

    /// <remarks>
    /// **Two switches, two answers for one skin material masked by its normal map's alpha.** `mat_bumpmap 0` loads no bump
    /// map, so the generic path reads the flat one, alpha 1; `mat_phong 0` keeps it loaded, and the generic bump shader masks
    /// by it (`vertexlit_and_unlit_generic_bump_ps2x.fxc:169-170`).
    /// </remarks>
    [Test]
    public void DrawModelPose_SkinNormalMapAlphaMaskWithEachSwitchOff_MasksOnlyWhileTheBumpMapIsLoaded()
    {
        using OffscreenTarget? target = OffscreenTarget.TryCreate(64, 64);

        if (target is null)
        {
            Assert.Ignore("no Direct3D on this machine");
            return;
        }

        MapEnvmapShading generic = new((1f, 1f, 1f), 0f, 1f, MaskedByBaseAlpha: false, 1f, MaskedByNormalMapAlpha: true);
        MapEnvmapShading skin = generic with
        {
            SkinMask = EnvMapMask.BumpAlpha,
            Generic = new MapGenericReflection((1f, 1f, 1f), 0f, 1f, BlackBase: false),
        };

        int masked = Sum(Draw(target, Material(Grey(255), generic, bumpAlpha: 0), phong: true, bumpMap: true));
        int unmasked = Sum(Draw(target, Material(Grey(255), generic, bumpAlpha: 255), phong: true, bumpMap: true));
        int phongOff = Sum(Draw(target, Material(Grey(255), skin, bumpAlpha: 0, skinShader: true), phong: false, bumpMap: true));
        int bumpOff = Sum(Draw(target, Material(Grey(255), skin, bumpAlpha: 0, skinShader: true), phong: true, bumpMap: false));

        TestContext.Out.WriteLine($"generic masked {masked}, unmasked {unmasked}; skin mat_phong 0 {phongOff}, mat_bumpmap 0 {bumpOff}");

        unmasked.ShouldBeGreaterThan(masked + 8, "the control: the bump alpha masks the reflection");
        phongOff.ShouldBe(masked, "mat_phong 0 keeps the bump map loaded, and its alpha masks");
        bumpOff.ShouldBe(unmasked, "mat_bumpmap 0 reads the flat normal map, whose alpha is 1");
    }

    /// <summary>Sixteen grey texels with one alpha.</summary>
    private static byte[] Grey(byte alpha) => [.. Enumerable.Range(0, 16).SelectMany(_ => new byte[] { 128, 128, 128, alpha })];

    /// <summary>One VertexLitGeneric material over a uniform cube, with a flat normal map when an alpha is given.</summary>
    private static MapAssets Material(byte[] texels, MapEnvmapShading shading, byte? bumpAlpha = null, bool skinShader = false, int size = 4)
    {
        byte[] face = [.. Enumerable.Range(0, 16).SelectMany(static _ => new byte[] { 100, 100, 100, 255 })];
        MapTexture[] faces = [.. Enumerable.Range(0, 6).Select(_ => new MapTexture(4, 4, 4, 4, TextureImage.Rgba(face), IsTransparent: false))];

        MapBump? bump = null;

        if (bumpAlpha is { } alpha)
        {
            byte[] flat = [.. Enumerable.Range(0, 16).SelectMany(_ => new byte[] { 128, 128, 255, alpha })];
            bump = new MapBump(new MapTexture(4, 4, 4, 4, TextureImage.Rgba(flat), IsTransparent: false), false);
        }

        return MapAssets.ForMaterials(
            [new MapTexture(size, size, size, size, TextureImage.Rgba(texels), IsTransparent: false, IsSkinShader: skinShader)],
            ["VertexLitGeneric"],
            bumps: [bump],
            cubemaps: [new MapCubemap(faces, shading)]);
    }

    /// <summary>A quad facing the camera with a tangent frame, drawn as a model under an ambient light, centre pixel read.</summary>
    private static (int R, int G, int B) Draw(OffscreenTarget target, MapAssets assets, bool phong, bool bumpMap)
    {
        static WorldVertex Corner(float x, float z, float u, float v) =>
            new(x, 0f, z, u, v, 0f, 0f, 0f)
            {
                NormalX = 0f,
                NormalY = -1f,
                NormalZ = 0f,
                TangentX = 1f,
                TangentY = 0f,
                TangentZ = 0f,
                TangentW = 1f,
            };

        List<WorldVertex> vertices =
        [
            Corner(-64f, -64f, 0f, 0f),
            Corner(64f, -64f, 1f, 0f),
            Corner(64f, 64f, 1f, 1f),
            Corner(-64f, -64f, 0f, 0f),
            Corner(64f, 64f, 1f, 1f),
            Corner(-64f, 64f, 0f, 1f),
        ];

        float[] identity =
        [
            1f, 0f, 0f, 0f,
            0f, 1f, 0f, 0f,
            0f, 0f, 1f, 0f,
            0f, 0f, 0f, 1f,
        ];

        float[] camera = new FreeCamera { Origin = (0f, -300f, 0f), Angles = (0f, 90f, 0f), Aspect = 1f }.ToMatrix();

        target.Clear(0f, 0f, 0f);
        target.DrawModelPose(
            vertices,
            [new WorldBatch(0, 0, vertices.Count)],
            camera,
            identity,
            assets,
            light: new AmbientCube((0.2f, 0.2f, 0.2f), (0.2f, 0.2f, 0.2f), (0.2f, 0.2f, 0.2f), (0.2f, 0.2f, 0.2f), (0.2f, 0.2f, 0.2f), (0.2f, 0.2f, 0.2f)),
            bothSides: true,
            phong: phong,
            bumpMap: bumpMap);

        return target.PixelAt(32, 32);
    }

    private static int Sum((int R, int G, int B) pixel) => pixel.R + pixel.G + pixel.B;
}
