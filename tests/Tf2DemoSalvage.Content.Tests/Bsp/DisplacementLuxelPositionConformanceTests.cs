using System;
using System.Collections.Generic;

using Tf2DemoSalvage.Content.Bsp;

namespace Tf2DemoSalvage.Content.Tests.Bsp;

/// <summary>B425: where each luxel of a displacement's lightmap sits in the world, as the engine reckons it for a dlight.</summary>
/// <remarks>
/// **Read from `engine.dll` `0x1800c0600`** (called by the `CDispInfo` dlight adders `0x1800bf8d0`/`0x1800bf9d0`, vtable
/// `0x18038d8b8` slot 0x30): per luxel, row by row, a byte naming a triangle — `255` escapes to `255 + next byte` — then three
/// byte weights times `0.003921569`; the position is the weights on that triangle's three vertices. The triangle's indices
/// come from `CPowerInfo::m_pTriInfos` (`+0x28` of the power info at `+0x268`; `disp_powerinfo.h:164`), built by
/// `InitPowerInfoTriInfos_R` (`disp_powerinfo.cpp:319-370`), and the bytes are `LUMP_DISP_LIGHTMAP_SAMPLE_POSITIONS` (34) at
/// `ddispinfo_t::m_iLightmapSamplePositionStart`, written by vbsp's `CalculateLightmapSamplePositions`
/// (`disp_vbsp.cpp:93-136`): `(unsigned char)(b · 255.9)` for each barycentric weight.
/// </remarks>
public sealed class DisplacementLuxelPositionConformanceTests
{
    /// <summary>
    /// The root of a power-2 grid is (2, 2); its first child is (3, 3) (`g_ChildNodeIndexMul[0]` = (1, 1)), and a leaf node
    /// winds `g_TesselateVerts` from (+1, −1): (4, 2), (3, 2), (2, 2), … each pair closing a triangle on the node. Index =
    /// y · 5 + x (`VertIndex`).
    /// </summary>
    [Test]
    public void SampleTriangles_AtPowerTwo_AreThePowerInfosWinding()
    {
        IReadOnlyList<int> triangles = BspTerrain.SampleTriangles(2);

        triangles.Count.ShouldBe(32 * 3);
        triangles[0].ShouldBe(14);
        triangles[1].ShouldBe(13);
        triangles[2].ShouldBe(18);
        triangles[3].ShouldBe(13);
        triangles[4].ShouldBe(12);
        triangles[5].ShouldBe(18);

        // The second child, (1, 3) (`g_ChildNodeIndexMul[1]` = (−1, 1)), starts at triangle 8: (2, 2), (1, 2), (1, 3).
        triangles[24].ShouldBe(12);
        triangles[25].ShouldBe(11);
        triangles[26].ShouldBe(16);
    }

    [TestCase(3, 128 * 3)]
    [TestCase(4, 512 * 3)]
    public void SampleTriangles_AtEachPower_CoverTheGridTwicePerQuad(int power, int count)
    {
        BspTerrain.SampleTriangles(power).Count.ShouldBe(count);
    }

    [Test]
    public void LuxelPositions_AWholeWeightOnOneCorner_IsThatVertex()
    {
        // Triangle 1 is (13, 12, 18): all of the weight on its second vertex.
        (float X, float Y, float Z)[] at = BspTerrain.LuxelPositions([1, 0, 255, 0], 1, 2, Line(25));

        at[0].X.ShouldBe(12f * (255f * 0.003921569f), 1e-5f);
    }

    [Test]
    public void LuxelPositions_TwoWeights_BlendTheirVerticesByTheEnginesByteScale()
    {
        // Triangle 0 is (14, 13, 18): (128 · 14 + 127 · 13) · 0.003921569 = 13.50196.
        (float X, float Y, float Z)[] at = BspTerrain.LuxelPositions([0, 128, 127, 0], 1, 2, Line(25));

        at[0].X.ShouldBe(13.50196f, 1e-4f);
    }

    [Test]
    public void LuxelPositions_ATriangleEscape_ReadsTwoHundredAndFiftyFivePlusTheNextByte()
    {
        IReadOnlyList<int> triangles = BspTerrain.SampleTriangles(4);

        // 255, 1 → triangle 256, all of its weight on its first vertex; then an ordinary luxel on triangle 3's third.
        (float X, float Y, float Z)[] at = BspTerrain.LuxelPositions([255, 1, 255, 0, 0, 3, 0, 0, 255], 2, 4, Line(289));

        at[0].X.ShouldBe(triangles[256 * 3] * (255f * 0.003921569f), 1e-3f);
        at[1].X.ShouldBe(triangles[(3 * 3) + 2] * (255f * 0.003921569f), 1e-3f);
    }

    [Test]
    public void LuxelPositions_ASampleOutsideEveryTriangle_IsTheZeroWeightedFirstTriangle()
    {
        // vbsp writes 0, 0, 0, 0 when no triangle holds the luxel (`disp_vbsp.cpp:129-132`): every weight zero.
        (float X, float Y, float Z)[] at = BspTerrain.LuxelPositions([0, 0, 0, 0], 1, 2, Line(25));

        at[0].ShouldBe((0f, 0f, 0f));
    }

    /// <summary>A grid whose vertex i sits at (i, 0, 0), so a position reads back as the index it came from.</summary>
    private static (float X, float Y, float Z)[] Line(int count)
    {
        (float X, float Y, float Z)[] grid = new (float, float, float)[count];

        for (int index = 0; index < count; index++)
        {
            grid[index] = (index, 0f, 0f);
        }

        return grid;
    }
}
