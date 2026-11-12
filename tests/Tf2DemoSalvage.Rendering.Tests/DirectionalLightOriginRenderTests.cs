using System.Collections.Generic;
using System.Linq;

using Tf2DemoSalvage.Content.Assets;
using Tf2DemoSalvage.Content.Bsp;

namespace Tf2DemoSalvage.Rendering.Tests;

/// <summary>A model's directional light, placed 10000 units from the draw's lighting origin, drawn on a real device (B721).</summary>
/// <remarks>
/// **What the engine does, read in TF2's x64 `shaderapidx9.dll`** (`FUN_180015a70`, the pixel-shader light commit): a
/// `D3DLIGHT_DIRECTIONAL` light's `cLightInfo[i].pos` is `lightingOrigin - Direction * 10000.0` (`0x461c4000`), and the
/// model pixel shader lights with `L = normalize( pos - worldPos )` for its diffuse (`PixelShaderDoGeneralDiffuseLight`,
/// `common_vertexlitgeneric_dx9.h:125-137`), and its highlight and rim (`SpecularAndRimTerms`). `lightingOrigin` is the
/// model's lighting origin, which ours carries as the instance's origin (its illumination point).
///
/// **Synthetic** (D38), and the prediction is an identity rather than a direction: at the centre pixel of an odd target
/// the surface point is the world origin, so a light travelling +y with the lighting origin at (10000, 0, 0) arrives as
/// `normalize( (10000, -10000, 0) )` — exactly what a light travelling `(-1, 1, 0) / √2` gives with the origin at the
/// surface. The two draws must be one picture; the infinitely far light gives N·L 1 instead of 0.707.
/// </remarks>
public sealed class DirectionalLightOriginRenderTests
{
    /// <summary>Odd, so the centre pixel lies on the camera's axis and its surface point is (0, 0, 0).</summary>
    private const int Size = 65;

    private const int Centre = 32;

    /// <summary>The engine's distance, `0x461c4000`.</summary>
    private const float Offset = 10000f;

    private static readonly (float X, float Y, float Z) IntoTheSurface = (0f, 1f, 0f);

    private static readonly (float X, float Y, float Z) Diagonal = (-0.70710678f, 0.70710678f, 0f);

    private static readonly (float X, float Y, float Z) AtTheSurface = (0f, 0f, 0f);

    private static readonly (float X, float Y, float Z) Aside = (Offset, 0f, 0f);

    [TestCase(false)]
    [TestCase(true)]
    public void DrawModelPose_SunWithTheLightingOriginAside_ArrivesFrom10000UnitsOff(bool phong)
    {
        using OffscreenTarget? target = OffscreenTarget.TryCreate(Size, Size);

        if (target is null)
        {
            Assert.Ignore("no Direct3D on this machine");
            return;
        }

        MapAssets assets = Material(phong);

        (int R, int G, int B) aside = Draw(target, assets, Aside, Sun(IntoTheSurface), null);
        (int R, int G, int B) diagonal = Draw(target, assets, AtTheSurface, Sun(Diagonal), null);
        (int R, int G, int B) head = Draw(target, assets, AtTheSurface, Sun(IntoTheSurface), null);

        TestContext.Out.WriteLine($"phong {phong}: origin aside {aside}, diagonal at the surface {diagonal}, head-on at the surface {head}");

        // The control: the two directions are told apart, well above one level, or agreement below would mean nothing.
        Sum(Minus(head, diagonal)).ShouldBeGreaterThan(30, "N·L 1 against 0.707 must differ");

        Near(aside, diagonal, 1, "origin - dir*10000 - worldPos, not -dir");
    }

    [TestCase(false)]
    [TestCase(true)]
    public void DrawModelPose_DirectionalLampWithTheLightingOriginAside_ArrivesFrom10000UnitsOff(bool phong)
    {
        // A model panel's second directional light, `MATERIAL_LIGHT_DIRECTIONAL` in a local slot: the same commit writes its
        // position, so the same identity holds for it as for the sun.
        using OffscreenTarget? target = OffscreenTarget.TryCreate(Size, Size);

        if (target is null)
        {
            Assert.Ignore("no Direct3D on this machine");
            return;
        }

        MapAssets assets = Material(phong);

        (int R, int G, int B) aside = Draw(target, assets, Aside, null, [Lamp(IntoTheSurface)]);
        (int R, int G, int B) diagonal = Draw(target, assets, AtTheSurface, null, [Lamp(Diagonal)]);
        (int R, int G, int B) head = Draw(target, assets, AtTheSurface, null, [Lamp(IntoTheSurface)]);

        TestContext.Out.WriteLine($"phong {phong}: origin aside {aside}, diagonal at the surface {diagonal}, head-on at the surface {head}");

        Sum(Minus(head, diagonal)).ShouldBeGreaterThan(30, "N·L 1 against 0.707 must differ");

        Near(aside, diagonal, 1, "origin - dir*10000 - worldPos, not -dir");
    }

    private static SunLight Sun((float X, float Y, float Z) travelling) =>
        new(1f, 1f, 1f, travelling.X, travelling.Y, travelling.Z);

    private static LocalLight Lamp((float X, float Y, float Z) travelling) =>
        new(0f, 0f, 0f, 1f, 1f, 1f, 1f, 0f, 0f, 0f, travelling, Directional: true);

    /// <summary>A grey VertexLitGeneric: plain Lambert, or the skin shader with a flat normal map and a flat-Fresnel phong.</summary>
    private static MapAssets Material(bool phong)
    {
        byte[] grey = [.. Enumerable.Range(0, 16).SelectMany(static _ => new byte[] { 128, 128, 128, 255 })];

        if (!phong)
        {
            return MapAssets.ForMaterials(
                [new MapTexture(4, 4, 4, 4, TextureImage.Rgba(grey), IsTransparent: false)],
                ["VertexLitGeneric"]);
        }

        byte[] flat = [.. Enumerable.Range(0, 16).SelectMany(static _ => new byte[] { 128, 128, 255, 255 })];

        return MapAssets.ForMaterials(
            [new MapTexture(4, 4, 4, 4, TextureImage.Rgba(grey), IsTransparent: false, IsSkinShader: true)],
            ["VertexLitGeneric"],
            bumps: [new MapBump(new MapTexture(4, 4, 4, 4, TextureImage.Rgba(flat), IsTransparent: false), false)],
            phong: [new MapPhong(5f, 1f, (0f, 1f, 0f), (1f, 1f, 1f), MaskedByBaseAlpha: false)]);
    }

    /// <summary>A quad in the y = 0 plane facing the camera, drawn as a model with the given lighting origin, centre pixel read.</summary>
    private static (int R, int G, int B) Draw(
        OffscreenTarget target,
        MapAssets assets,
        (float X, float Y, float Z) origin,
        SunLight? sun,
        IReadOnlyList<LocalLight>? locals)
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

            // Uniform, so it adds the same whatever the normal; a direct light needs a cube to be applied at all.
            light: new AmbientCube((0.1f, 0.1f, 0.1f), (0.1f, 0.1f, 0.1f), (0.1f, 0.1f, 0.1f), (0.1f, 0.1f, 0.1f), (0.1f, 0.1f, 0.1f), (0.1f, 0.1f, 0.1f)),
            bothSides: true,
            sun: sun,
            origin: origin,
            locals: locals);

        return target.PixelAt(Centre, Centre);
    }

    private static int Sum((int R, int G, int B) pixel) => System.Math.Abs(pixel.R) + System.Math.Abs(pixel.G) + System.Math.Abs(pixel.B);

    private static (int R, int G, int B) Minus((int R, int G, int B) a, (int R, int G, int B) b) => (a.R - b.R, a.G - b.G, a.B - b.B);

    private static void Near((int R, int G, int B) actual, (int R, int G, int B) expected, int tolerance, string because)
    {
        System.Math.Abs(actual.R - expected.R).ShouldBeLessThanOrEqualTo(tolerance, because);
        System.Math.Abs(actual.G - expected.G).ShouldBeLessThanOrEqualTo(tolerance, because);
        System.Math.Abs(actual.B - expected.B).ShouldBeLessThanOrEqualTo(tolerance, because);
    }
}
