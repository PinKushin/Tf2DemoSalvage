using System.Collections.Generic;

using Tf2DemoSalvage.SdkReference;

namespace Tf2DemoSalvage.Rendering.Tests;

/// <summary>
/// `r_lightmap_bicubic 1` changes the lit picture, and `0` leaves it as it was (B679).
/// </summary>
/// <remarks>
/// **The shader half of the cvar.** `LightmappedGeneric` reads its lightmap through `LightMapSample`
/// (`common_lightmappedgeneric_fxc.h:153-224`), which takes four bilinear taps weighted by the cubic B-spline when
/// the cvar is on (`lightmappedgeneric_dx9_helper.cpp:932`) and one tap when it is off. A component test cannot see
/// whether the constant reaches the shader or whether the shader compiled, so this draws a wall magnified until a
/// handful of luxels cover it, once each way, and reads the row across it.
///
/// **The control is the same draw twice with the cvar off**: if those differ, the difference measured below is noise,
/// not the filter.
/// </remarks>
public sealed class BicubicLightmapRenderTests
{
    private const int Size = 64;

    [Test]
    [Category(Tf2DemoSalvage.SdkReference.TestCategories.MutationHeavy)]
    public void DrawWorld_WithBicubicLightmapOn_ChangesTheLitRowAndOffIsRepeatable()
    {
        using OffscreenTarget? target = OffscreenTarget.TryCreate(Size, Size);

        if (target is null)
        {
            Assert.Ignore("no Direct3D on this machine");
            return;
        }

        if (MapCache.TryLoad("cp_process_f12", maximumTextureSize: 256) is not { } assets)
        {
            Assert.Ignore("the map or the game is not installed");
            return;
        }

        int[] Row(bool bicubic)
        {
            (List<WorldVertex> wall, WorldBatch batch) = MagnifiedWall();

            target.Clear(0f, 0f, 0f);
            target.DrawWorld(wall, [batch], Identity, assets, bicubicLightmap: bicubic);

            int[] row = new int[Size];

            for (int x = 0; x < Size; x++)
            {
                (int red, int green, int blue) = target.PixelAt(x, Size / 2);
                row[x] = red + green + blue;
            }

            return row;
        }

        int[] off = Row(false);
        int[] again = Row(false);
        int[] on = Row(true);

        int Difference(int[] a, int[] b)
        {
            int total = 0;

            for (int x = 0; x < Size; x++)
            {
                total += System.Math.Abs(a[x] - b[x]);
            }

            return total;
        }

        TestContext.Out.WriteLine($"BICUBIC off/off {Difference(off, again)} off/on {Difference(off, on)}");

        Difference(off, again).ShouldBe(0, "the control: the same draw twice is the same picture");
        Difference(off, on).ShouldBeGreaterThan(0, "r_lightmap_bicubic 1 must reach the shader and change the filter");
    }

    /// <summary>A full-screen wall whose lightmap coordinate spans a sliver of the atlas, so each luxel covers many pixels.</summary>
    private static (List<WorldVertex> Vertices, WorldBatch Batch) MagnifiedWall()
    {
        const float From = 0.30f;
        const float To = 0.31f;
        const float Down = 0.5f;

        List<WorldVertex> vertices =
        [
            new(-1f, -1f, 0.9f, 0f, 0f, From, Down, 0f, 1f, 1f, 1f),
            new(1f, 1f, 0.9f, 1f, 1f, To, Down, 0f, 1f, 1f, 1f),
            new(1f, -1f, 0.9f, 1f, 0f, To, Down, 0f, 1f, 1f, 1f),
            new(-1f, -1f, 0.9f, 0f, 0f, From, Down, 0f, 1f, 1f, 1f),
            new(-1f, 1f, 0.9f, 0f, 1f, From, Down, 0f, 1f, 1f, 1f),
            new(1f, 1f, 0.9f, 1f, 1f, To, Down, 0f, 1f, 1f, 1f),
        ];

        return (vertices, new WorldBatch(0, 0, vertices.Count));
    }

    private static float[] Identity =>
    [
        1f, 0f, 0f, 0f,
        0f, 1f, 0f, 0f,
        0f, 0f, 1f, 0f,
        0f, 0f, 0f, 1f,
    ];
}
