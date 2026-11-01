using System.Collections.Generic;
using System.Linq;

using Tf2DemoSalvage.Content.Bsp;

namespace Tf2DemoSalvage.Rendering.Tests;

/// <summary>
/// A lit studio decal is lit by its model's cube and lamps, never by the lightmap's white texel (B642). Hand-built
/// materials and light, no game install.
/// </summary>
/// <remarks>
/// **Valve, read from the binaries** (`docs/measurements/2026-11-02-prop-decal-light-plan.md`). studiorender's decal path
/// lights a decal with the draw's ambient cube and up to four local lights (`studiorender.dll` `0x18000a880`), or under the
/// lighting state already set. `engine.dll` sets that state, for a static prop's decals, to the prop's full lighting at its
/// lighting origin (`0x1800f1bd0`, `0x1800eb5e0`). Its vertices are white (`0x1800096c0`), so no colour mesh reaches it.
/// A VertexLitGeneric decal (`decals/bigshot1model`, `break1model`, `bproof1model`, `beersplashmodel`) is therefore shaded
/// like the model it sits on.
///
/// **Arithmetic, at tone-map scale 1:** a texel of 100 is 0.1274 linear. Under a cube face of 0.25 it draws
/// sRGB(0.0319) = 50; under a face of 1.0 it draws its own 100. The white texel the decals took before drew it at 138.
///
/// **The control:** a DecalModulate decal under the same dark cube still draws Valve's gamma product, 188 · 2 · 32/255 =
/// 47, because a modulating material reads no light (B639).
/// </remarks>
[NonParallelizable]
public sealed class ModelDecalLightConformanceTests
{
    [Test]
    public void DrawModelDecals_LitDecalFacingADarkCubeFace_TakesThatFace()
    {
        (int, int, int) drawn = DrawDecal(LitGrey, normal: (0f, -1f, 0f)) ?? Skip();

        drawn.Item2.ShouldBeInRange(48, 52, $"a lit decal of texel 100 facing a cube face of 0.25 drew {drawn}; Valve's 50");
    }

    [Test]
    public void DrawModelDecals_LitDecalTurnedToABrightCubeFace_TakesThatFace()
    {
        (int, int, int) drawn = DrawDecal(LitGrey, normal: (1f, 0f, 0f)) ?? Skip();

        drawn.Item2.ShouldBeInRange(98, 102, $"a lit decal of texel 100 facing a cube face of 1.0 drew {drawn}; Valve's 100");
    }

    [Test]
    public void DrawModelDecals_DecalModulateUnderADarkCube_IsValvesGammaProduct()
    {
        (int, int, int) drawn = DrawDecal(ModulateDark, normal: (0f, -1f, 0f)) ?? Skip();

        drawn.Item2.ShouldBeInRange(46, 48, $"a DecalModulate texel of 32 under a dark cube drew {drawn}; Valve's 47 over 188");
    }

    private const int LitGrey = 0;
    private const int ModulateDark = 1;

    /// <summary>A VertexLitGeneric decal of texel 100, and a DecalModulate decal of texel 32.</summary>
    private static readonly MapAssets Assets = MapAssets.ForMaterials(
        [
            ModulateToneMapConformanceTests.Grey(100, decal: true),
            ModulateToneMapConformanceTests.Grey(32, modulate: true, twice: true, decal: true),
        ],
        ["VertexLitGeneric", "DecalModulate"]);

    /// <summary>A cube dark on its −Y face, which the decal faces, and full on +X.</summary>
    private static readonly AmbientCube Cube = new(
        PositiveX: (1f, 1f, 1f),
        NegativeX: (0.5f, 0.5f, 0.5f),
        PositiveY: (0.5f, 0.5f, 0.5f),
        NegativeY: (0.25f, 0.25f, 0.25f),
        PositiveZ: (0.5f, 0.5f, 0.5f),
        NegativeZ: (0.5f, 0.5f, 0.5f));

    /// <summary>
    /// A model drawn off to the side, then one decal quad of <paramref name="material"/> over the cleared 188, lit by
    /// <see cref="Cube"/> through the model decal draw.
    /// </summary>
    private static (int, int, int)? DrawDecal(int material, (float X, float Y, float Z) normal)
    {
        using OffscreenTarget? target = OffscreenTarget.TryCreate(16, 16);

        if (target is null)
        {
            return null;
        }

        float surface = ModulateToneMapConformanceTests.Surface;

        target.Clear(surface, surface, surface);

        // The model the decals ride, 1000 units aside so the read pixel sees only the decal.
        target.DrawModelPose(
            ModulateToneMapConformanceTests.Quad(1f, offset: 1000f),
            [new WorldBatch(LitGrey, 0, 6)],
            ModulateToneMapConformanceTests.Camera(),
            ModulateToneMapConformanceTests.Identity,
            Assets,
            bothSides: true,
            toneMapScale: 1f);

        List<WorldVertex> decal = [.. ModulateToneMapConformanceTests.Quad(1f)
            .Select(corner => corner with { NormalX = normal.X, NormalY = normal.Y, NormalZ = normal.Z })];

        target.DrawPosedModelDecals(decal, [new WorldBatch(material, 0, 6)], Cube);

        return target.PixelAt(8, 8);
    }

    private static (int, int, int) Skip()
    {
        Assert.Ignore("no Direct3D device on this machine");
        return default;
    }
}
