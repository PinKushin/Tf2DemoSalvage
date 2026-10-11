using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;

using Tf2DemoSalvage.Content.Bsp;
using Tf2DemoSalvage.SdkReference;

namespace Tf2DemoSalvage.Rendering.Tests;

/// <summary>
/// The light the renderer samples at a point on a real wall, against that wall's own luxels (B521) — the output-level
/// half of `LuxelCoordinateConformanceTests`.
/// </summary>
/// <remarks>
/// **The point is the one the golden comparison disagreed at.** cp_process_f12's face 8882 is the wall under the three
/// indoor lamps, 19 luxels by 5, and (320, −1938.1, 808.1) on it is pixel (600, 420) of the indoor capture
/// (`pixel-face`, 2026-10-11) — in the steep edge of a spotlight's pool, where the four luxels about it read 0.201,
/// 0.336, 0.633 and 1.109 in green. TF2's pixel there implies a light of about 0.52; the viewer's implied 0.19, which
/// is these luxels sampled at (9.62, 1.99) instead of (10.15, 2.49): the coordinate times 18/19 and 4/5.
///
/// **Two routes to one number.** The expected light is the face's own luxels from the lump, blended at the luxel
/// coordinate the texinfo row gives — vrad's statement of where each luxel is (`utils/vrad/lightmap.cpp:847-866`).
/// The sampled light is the atlas the renderer uploads, read at the atlas coordinate the renderer's own mapping gives
/// that point. They share the lump and the texinfo row and nothing else. Sabotaged with the old divisor, the atlas
/// reads 0.177 here — the 0.19 the viewer's own capture implied — and this goes red.
/// </remarks>
public sealed class LightmapSamplingOnF12Tests
{
    private const string Map = "cp_process_f12";

    private const int Wall = 8882;

    [Test]
    [Category(Tf2DemoSalvage.SdkReference.TestCategories.MutationHeavy)]
    public void Sample_TheWallUnderTheLampsAtTheComparisonsPixel_IsItsOwnLuxelsBlendedAtItsLuxelCoordinate()
    {
        MapAssets assets = Skip.Unless(MapCache.TryLoad(Map, maximumTextureSize: 256), "cp_process_f12 or the game is not on this machine");
        byte[] bytes = MapCache.Bytes(Map);

        IReadOnlyList<BspSurface> surfaces = BspSurfaces.Read(bytes);
        int index = Enumerable.Range(0, surfaces.Count).Single(at => surfaces[at].FaceIndex == Wall);
        BspSurface face = surfaces[index];

        (face.LuxelWidth, face.LuxelHeight).ShouldBe((19, 5), "the instrument: the wall this test was written against");

        Vector3 point = new(320f, -1938.1f, 808.1f);
        LuxelMapping lighting = face.Lighting;

        // Where the point is in luxels, straight from the texinfo row: the dot, the offset, less the face's mins.
        float across = (lighting.Across.X * point.X) + (lighting.Across.Y * point.Y) + (lighting.Across.Z * point.Z) + lighting.Across.Offset - lighting.MinU;
        float down = (lighting.Down.X * point.X) + (lighting.Down.Y * point.Y) + (lighting.Down.Z * point.Z) + lighting.Down.Offset - lighting.MinV;

        across.ShouldBe(10.15f, 0.02f);
        down.ShouldBe(2.49f, 0.02f);

        BspLightmap own = BspLightmaps.ReadAll(bytes)[index].Flat;
        float expected = Bilinear(own.Pixels.Span, own.Width, across, down);

        expected.ShouldBe(0.459f, 0.005f, "the face's own luxels at (10.15, 2.49), as the probe measured them");

        // What the renderer reads: the atlas, at the coordinate its own mapping gives the point. A texel's centre is
        // half a texel into it, so texel-space is the atlas coordinate times the size, less a half.
        LightmapAtlas atlas = assets.Lightmaps;
        AtlasRect rectangle = atlas.Rectangles[face.FaceIndex];
        (float u, float v) = lighting.Project(point.X, point.Y, point.Z);
        float texelX = ((rectangle.U + (u * rectangle.Width)) * atlas.Width) - 0.5f;
        float texelY = ((rectangle.V + (v * rectangle.Height)) * atlas.Height) - 0.5f;

        float sampled = Bilinear(atlas.Pixels, atlas.Width, texelX, texelY);

        TestContext.Out.WriteLine($"WALL expected {expected:0.000}, the atlas at ({texelX:0.00}, {texelY:0.00}) reads {sampled:0.000}");

        sampled.ShouldBe(expected, 0.003f);
    }

    /// <summary>Green, bilinear between the four texels about a texel-space point; 1 is a light of one.</summary>
    private static float Bilinear(ReadOnlySpan<byte> texels, int width, float x, float y)
    {
        int left = (int)MathF.Floor(x);
        int top = (int)MathF.Floor(y);
        float fx = x - left;
        float fy = y - top;

        float upper = float.Lerp(Green(texels, width, left, top), Green(texels, width, left + 1, top), fx);
        float lower = float.Lerp(Green(texels, width, left, top + 1), Green(texels, width, left + 1, top + 1), fx);

        return float.Lerp(upper, lower, fy);
    }

    private static float Green(ReadOnlySpan<byte> texels, int width, int x, int y) =>
        BspLightmaps.Load(texels.Slice(((y * width) + x) * 4, 4)).Green / 255f;
}
