using System;
using System.Numerics;

using Tf2DemoSalvage.Content.Bsp;

namespace Tf2DemoSalvage.Content.Tests.Bsp;

/// <summary>
/// A displacement vertex's normal — <c>CCoreDispInfo::GenerateDispSurfNormals</c> and <c>CalcNormalFromEdges</c>
/// (`builddisp.cpp:1732-1913`), which `CDispInfo` lifts a shadow's decal along (B702).
/// </summary>
/// <remarks>
/// **The fixture grid** is laid out as vbsp lays a floor out from a face wound clockwise seen from above: rows along +Y,
/// columns along +X, one unit apart, three by three.
/// </remarks>
public sealed class DisplacementNormalsConformanceTests
{
    private static Vector3[] Grid(Func<int, int, float> height)
    {
        Vector3[] grid = new Vector3[9];

        for (int row = 0; row < 3; row++)
        {
            for (int column = 0; column < 3; column++)
            {
                grid[(row * 3) + column] = new Vector3(column, row, height(row, column));
            }
        }

        return grid;
    }

    /// <remarks>Flat: every triangle's normal is +Z, so every vertex's average is exactly +Z — corners (two triangles),
    /// edges (four) and the centre (eight) alike.</remarks>
    [Test]
    public void NormalsOf_AFlatFloor_IsUpEverywhere() =>
        DisplacementMesh.NormalsOf(Grid((_, _) => 0f), 3).ShouldAllBe(normal => normal == Vector3.UnitZ);

    /// <remarks>
    /// A tent, the centre raised one unit. Of `CalcNormalFromEdges`' eight triangles, worked by hand: quadrant I's second
    /// and III's first do not touch the centre and are flat, (0, 0, 1); I's first and III's second lean along a diagonal,
    /// (1, 1, 1)/√3 and (−1, −1, 1)/√3; II's and IV's four lean 45°, (−1, 0, 1), (0, 1, 1), (0, −1, 1), (1, 0, 1), each
    /// /√2. They cancel sideways, and the average is NOT normalised: z = (2 + 2/√3 + 4/√2) / 8 ≈ 0.748.
    /// </remarks>
    [Test]
    public void NormalsOf_TheTopOfATent_IsTheUnnormalisedAverage()
    {
        Vector3 centre = DisplacementMesh.NormalsOf(Grid((row, column) => row == 1 && column == 1 ? 1f : 0f), 3)[4];

        centre.X.ShouldBe(0f, 1e-6f);
        centre.Y.ShouldBe(0f, 1e-6f);
        centre.Z.ShouldBe((2f + (2f / MathF.Sqrt(3f)) + (4f / MathF.Sqrt(2f))) / 8f, 1e-6f);
    }
}
