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

    /// <summary>
    /// `R_LightVec` itself (`engine.dll` `0x1800d40a0`), as `ComputeAmbientFromSphericalSamples` (`0x1801b74f0`) calls it:
    /// the face and its light, no texture coordinate, the ray as given (no 1.1).
    /// </summary>
    [Test]
    public void LightVec_ARayIntoTheWall_IsThatFaceAndItsAverageLight()
    {
        (DecalFace? face, Vector3 light) = Colour().LightVec(new Vector3(100f, 256f, 256f), new Vector3(-100f, 256f, 256f), styles: false);

        face.ShouldNotBeNull().Index.ShouldBe(0);
        light.X.ShouldBe(64f * MathF.ScaleB(1f, -2) / 255f, 1e-7f);
    }

    [Test]
    public void LightVec_ARayThatStopsShortOfTheWall_IsNoFace()
    {
        Colour().LightVec(new Vector3(100f, 256f, 256f), new Vector3(50f, 256f, 256f), styles: false).Face.ShouldBeNull();
    }

    /// <remarks>
    /// **A sky face is kept aside, not tested.** *Read in disassembly*, the walk `0x1800d4b00`: on a node the ray
    /// crosses, a face with `SURFDRAW_SKY` (`flags &amp; 4`) is stored in the walk (`+0x78`) without asking whether the
    /// ray passes through it, and `0x1800d40a0` returns it only when no face and no displacement took the ray. It adds
    /// no light. The ray here crosses the node 344 units outside the face's 512-unit square.
    /// </remarks>
    [Test]
    public void LightVec_ARayAcrossANodeHoldingASkyFace_ReturnsTheSkyFaceWithNoLight()
    {
        (DecalFace? face, Vector3 light) = Colour(SurfaceProperties.Sky).LightVec(
            new Vector3(100f, 600f, 256f), new Vector3(-100f, 600f, 256f), styles: false);

        (face.ShouldNotBeNull().Flags & SurfaceProperties.Sky).ShouldNotBe(SurfaceProperties.None);
        light.ShouldBe(Vector3.Zero);
    }

    /// <remarks>
    /// `bUseLightStyles` (the third argument, 0 from `0x1801b74f0`): the face test `0x1800d3990` then adds the first
    /// style map's average alone. The face has two, 64 and 32 at exponent −2; with styles it is their sum.
    /// </remarks>
    [Test]
    public void LightVec_WithoutLightStyles_AddsTheFirstStyleMapAlone()
    {
        SurfaceColour two = Colour(second: 32);
        Vector3 from = new(100f, 256f, 256f);
        Vector3 to = new(-100f, 256f, 256f);

        two.LightVec(from, to, styles: false).Light.X.ShouldBe(64f * MathF.ScaleB(1f, -2) / 255f, 1e-7f);
        two.LightVec(from, to, styles: true).Light.X.ShouldBe(96f * MathF.ScaleB(1f, -2) / 255f, 1e-7f);
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

    private static SurfaceColour Colour(SurfaceProperties flags = SurfaceProperties.None, byte second = 0)
    {
        Vector3[] corners = [new(0f, 0f, 0f), new(0f, 512f, 0f), new(0f, 512f, 512f), new(0f, 0f, 512f)];
        LuxelMapping lighting = new((0f, 1f / 16f, 0f, 0f), (0f, 0f, 1f / 16f, 0f), 0, 0, 33, 33);

        DecalFace face = new(
            0, corners, Vector3.UnitX, 0f, new Vector4(0f, 1f, 0f, 0f), new Vector4(0f, 0f, 1f, 0f),
            (0, 0), (512, 512), true, false, false, lighting, flags, 0);

        DecalWorld world = new([new DecalNode(-1, -2, Vector3.UnitX, 0f, 0, 1)], [[], []], [face]);

        // Its averages just before the samples at byte 8, in reverse: style slot 0's — 64 at exponent -2 — at byte 4,
        // and a second slot's (`second`, when given, on style 32) at byte 0.
        byte[] lump = new byte[12];

        lump[4] = 64;
        lump[5] = 64;
        lump[6] = 64;
        lump[7] = unchecked((byte)(sbyte)-2);
        lump[0] = second;
        lump[3] = unchecked((byte)(sbyte)-2);

        BspLightSamples samples = new(lump, [new BspFaceLightLayout(8, (0, second == 0 ? (byte)255 : (byte)32, 255, 255))]);

        return new SurfaceColour(world, samples, texdata => texdata == 0
            ? new SurfaceThumbnail([128, 64, 32, 255], 1, 1, 64, 64)
            : null);
    }
}
