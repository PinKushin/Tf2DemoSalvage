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

    [Test]
    public void Sample_BetweenTexels_IsBilinearAndWraps()
    {
        SurfaceThumbnail two = new([0, 0, 0, 255, 255, 0, 0, 255], 2, 1, 64, 64);

        // s = 0.75 is three quarters of the way across two texels: from texel 1 halfway back round to texel 0.
        SurfaceColour.Sample(two, 0.75f, 0f).R.ShouldBe(0.5f, 1e-6f);

        // A negative coordinate wraps as `s + ( 1 − (int)s )`.
        SurfaceColour.Sample(two, -0.25f, 0f).R.ShouldBe(0.5f, 1e-6f);
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
