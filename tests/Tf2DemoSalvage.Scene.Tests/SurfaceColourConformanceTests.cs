using System;
using System.Numerics;

using Tf2DemoSalvage.Content.Bsp;

namespace Tf2DemoSalvage.Scene.Tests;

/// <summary>
/// `GetColorForSurface` (`c_impact_effects.cpp:96`) through `TraceLineMaterialAndLighting` — `R_LightVec`'s walk
/// (`engine.dll` `0x1800d4b00`, `0x1800d3990`) and `GetLowResColorSample` (`materialsystem.dll` `0x180039c10`) (B415).
/// </summary>
/// <remarks>
/// <code>
/// end    = startpos + ( endpos − startpos ) · 1.1
/// diffuse, s, t = R_LightVec( startpos, end ): the face the ray meets, its average light, its texture coordinate
/// base   = its base texture's low-res image at ( s, t ), bilinear and wrapped
/// colour = pow( diffuse, 1 / 2.2 ) · base
/// </code>
/// The wall is the plane x = 0, 512 units square, one luxel per 16 units and one texel per unit of a 64-texel texture.
/// </remarks>
public sealed class SurfaceColourConformanceTests
{
    [Test]
    public void At_AShotIntoTheWall_IsItsLightToTheGammaTimesTheThumbnail()
    {
        // The thumbnail is one texel of 128, 64, 32, so the sample is that wherever it lands; the light is the
        // face's one average, 64 · 2^−2 / 255.
        (float r, float g, float b) = Colour().At(new Vector3(100f, 256f, 256f), new Vector3(0f, 256f, 256f));

        float lit = MathF.Pow(64f * MathF.ScaleB(1f, -2) / 255f, 1f / 2.2f);

        r.ShouldBe(lit * (128f / 255f), 1e-5f);
        g.ShouldBe(lit * (64f / 255f), 1e-5f);
        b.ShouldBe(lit * (32f / 255f), 1e-5f);
    }

    [Test]
    public void At_AFaceThatTakesNoLight_IsBlack()
    {
        Colour(SurfaceProperties.NoLight).At(new Vector3(100f, 256f, 256f), new Vector3(0f, 256f, 256f))
            .ShouldBe((0f, 0f, 0f));
    }

    [Test]
    public void At_APointOutsideTheFacesLightmap_IsBlack()
    {
        // y = 600 is past the face's 32-luxel extent.
        Colour().At(new Vector3(100f, 600f, 256f), new Vector3(0f, 600f, 256f)).ShouldBe((0f, 0f, 0f));
    }

    /// <summary>
    /// `R_LightVec` (`0x1800d4b00`) skips `SURFDRAW_WATERSURFACE` in both passes; a texinfo's `SURF_WARP` stands in for it.
    /// </summary>
    [Test]
    public void At_AShotIntoWater_IsBlack()
    {
        Colour(SurfaceProperties.Warp).At(new Vector3(100f, 256f, 256f), new Vector3(0f, 256f, 256f))
            .ShouldBe((0f, 0f, 0f));
    }

    /// <summary>
    /// `R_LightVec` (`0x1800d40a0`) ray-tests each displacement its leaves list (`0x1800d3640`) through
    /// `CDispCollTree::AABBTree_Ray` (`dispcoll_common.cpp:564`) and, on a hit, adds the luxel under it (`0x1800d37d0`:
    /// `samples + ( dt · smax + ds )`, ds and dt truncated) and takes the texture coordinate bilinear over the base
    /// face's corners (`0x1800bfc90`).
    /// </summary>
    /// <remarks>
    /// A flat 64-unit floor, power 2, five luxels a side. The shot lands at x 50, y 18: 0.78 across the columns (x) and
    /// 0.28 down the rows (y), so luxel ( 3.125, 1.125 ) truncated to ( 3, 1 ) — whose sample is 200 at exponent −3 — and
    /// texture s 0.78125, 0.5625 of the way from the thumbnail's red texel round to its black one.
    /// </remarks>
    [Test]
    public void At_AShotIntoADisplacement_TakesTheLuxelUnderTheHit()
    {
        (float r, float g, float b) = Floor().At(new Vector3(50f, 18f, 50f), new Vector3(50f, 18f, 0f));

        float lit = MathF.Pow(200f * MathF.ScaleB(1f, -3) / 255f, 1f / 2.2f);

        r.ShouldBe(lit * 0.4375f, 1e-5f);
        g.ShouldBe(0f);
        b.ShouldBe(0f);
    }

    /// <summary>`AABBTree_Ray` returns at once for a displacement flagged `SURF_NORAY_COLL` (`dispcoll_common.cpp:569`).</summary>
    [Test]
    public void At_ADisplacementThatTakesNoRays_IsBlack()
    {
        Floor(noRay: true).At(new Vector3(50f, 18f, 50f), new Vector3(50f, 18f, 0f)).ShouldBe((0f, 0f, 0f));
    }

    /// <summary>
    /// `GetColorForSurface` sends a prop hit to `GetStaticPropMaterialColorAndLighting` (`c_impact_effects.cpp:111`), whose
    /// studio branch (`engine.dll` `0x1801c9f10`) gives a flat 0.5 gray base, not the material's.
    /// </summary>
    /// <remarks>A cube whose upper face is 0.25: that to the 1/2.2, times 0.5.</remarks>
    [Test]
    public void OfStaticProp_AnUpwardHit_IsItsLightToTheGammaTimesHalf()
    {
        AmbientCube cube = new(default, default, default, default, (0.25f, 0.25f, 0.25f), default);

        SurfaceColour.OfStaticProp(PointLighting.Bounce(cube), null, Vector3.Zero, Vector3.UnitZ).R
            .ShouldBe(MathF.Pow(0.25f, 1f / 2.2f) * 0.5f, 1e-6f);
    }

    [Test]
    public void Sample_BetweenTexels_IsBilinearAndWraps()
    {
        SurfaceThumbnail two = new([0, 0, 0, 255, 255, 0, 0, 255], 2, 1, 64, 64);

        // s = 0.75 is three quarters of the way across two texels: from texel 1 halfway back round to texel 0.
        SurfaceColour.Sample(two, 0.75f, 0f).R.ShouldBe(0.5f, 1e-6f);

        // A negative coordinate wraps as `s + ( 1 − (int)s )`.
        SurfaceColour.Sample(two, -0.25f, 0f).R.ShouldBe(0.5f, 1e-6f);
    }

    /// <summary>A flat displacement on z = 0, rows along y and columns along x, in the one leaf the tree has.</summary>
    private static SurfaceColour Floor(bool noRay = false)
    {
        Vector3[] corners = [new(0f, 0f, 0f), new(0f, 64f, 0f), new(64f, 64f, 0f), new(64f, 0f, 0f)];
        DisplacementCollisionTree tree = DisplacementCollisionTree.Build(corners, 2, new (Vector3, float)[25]);
        LuxelMapping lighting = new((1f / 16f, 0f, 0f, 0f), (0f, 1f / 16f, 0f, 0f), 0, 0, 5, 5);

        DecalFace face = new(
            0, corners, Vector3.UnitZ, 0f, new Vector4(1f, 0f, 0f, 0f), new Vector4(0f, 1f, 0f, 0f),
            (0, 0), (64, 64), false, true, false, lighting, SurfaceProperties.None, 0);

        DecalWorld world = new([new DecalNode(-1, -1, Vector3.UnitX, 1000f, 0, 0)], [[]], [face])
        {
            RayDisplacements = [new RayDisplacement(0, tree, noRay)],
            LeafDisplacements = [[0]],
        };

        // One style, 25 luxels; luxel ( 3, 1 ) is sample 1 · 5 + 3.
        byte[] lump = new byte[25 * 4];

        lump[(8 * 4) + 0] = 200;
        lump[(8 * 4) + 3] = unchecked((byte)(sbyte)-3);

        BspLightSamples samples = new(lump, [new BspFaceLightLayout(0, (0, 255, 255, 255))]);

        return new SurfaceColour(world, samples, texdata => texdata == 0
            ? new SurfaceThumbnail([0, 0, 0, 255, 255, 0, 0, 255], 2, 1, 64, 64)
            : null);
    }

    private static SurfaceColour Colour(SurfaceProperties flags = SurfaceProperties.None)
    {
        Vector3[] corners = [new(0f, 0f, 0f), new(0f, 512f, 0f), new(0f, 512f, 512f), new(0f, 0f, 512f)];
        LuxelMapping lighting = new((0f, 1f / 16f, 0f, 0f), (0f, 0f, 1f / 16f, 0f), 0, 0, 33, 33);

        DecalFace face = new(
            0, corners, Vector3.UnitX, 0f, new Vector4(0f, 1f, 0f, 0f), new Vector4(0f, 0f, 1f, 0f),
            (0, 0), (512, 512), true, false, false, lighting, flags, 0);

        DecalWorld world = new([new DecalNode(-1, -2, Vector3.UnitX, 0f, 0, 1)], [[], []], [face]);

        // One style, its average 64 at exponent -2, just before the samples at byte 4.
        byte[] lump = new byte[8];

        lump[0] = 64;
        lump[1] = 64;
        lump[2] = 64;
        lump[3] = unchecked((byte)(sbyte)-2);

        BspLightSamples samples = new(lump, [new BspFaceLightLayout(4, (0, 255, 255, 255))]);

        return new SurfaceColour(world, samples, texdata => texdata == 0
            ? new SurfaceThumbnail([128, 64, 32, 255], 1, 1, 64, 64)
            : null);
    }
}
