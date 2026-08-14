using System;
using System.Collections.Generic;

using Tf2DemoSalvage.Viewer3D;

namespace Tf2DemoSalvage.Viewer3D.Tests;

/// <summary>
/// The camera as a matrix, against the camera as arithmetic.
/// </summary>
/// <remarks>
/// **Two paths project geometry and they must agree.** The map's outline and the player markers are
/// still projected on the processor; the textured world is transformed by this matrix on the GPU.
/// If the two disagree the outline slides off its own surfaces — and by a fraction, which reads as
/// a rounding artifact rather than as a wrong formula.
///
/// So the measurement is differential: run both over the same points and require the same answer.
/// A fixture asserting specific clip coordinates would only restate whichever implementation was
/// written first.
/// </remarks>
public sealed class CameraMatrixTests
{
    [Test]
    public void ToMatrix_AgreesWithProjectAtEveryCorner()
    {
        TopDownCamera camera = TopDownCamera.Fit(
            [(-4096f, -3000f), (5120f, 4800f)], 1600, 900);

        foreach ((float x, float y) in new[]
        {
            (-4096f, -3000f), (5120f, 4800f), (0f, 0f), (512f, -1024f), (-9000f, 12000f),
        })
        {
            (float expectedX, float expectedY) = camera.Project(x, y);
            (float actualX, float actualY) = Transform(camera.ToMatrix(), x, y);

            actualX.ShouldBe(expectedX, 1e-4f, $"x at ({x}, {y})");
            actualY.ShouldBe(expectedY, 1e-4f, $"y at ({x}, {y})");
        }
    }

    [Test]
    public void ToMatrix_WithoutAHeightRange_LeavesDepthAlone()
    {
        // A camera that has not been told the map's height range cannot project height, so it
        // passes the third component through. Callers that have not moved to world Z yet keep
        // working rather than having their geometry silently flattened.
        TopDownCamera camera = TopDownCamera.Fit([(0f, 0f), (1000f, 1000f)], 800, 600);
        float[] matrix = camera.ToMatrix();

        foreach (float depth in new[] { 0f, 0.25f, 1f })
        {
            Depth(matrix, depth).ShouldBe(depth, 1e-6f);
        }
    }

    [Test]
    public void ToMatrix_WithAHeightRange_ProjectsWorldHeightToDepth()
    {
        // **D21: the camera owns the projection, and that includes depth.** Height used to be
        // flattened into the vertices before the matrix ever ran, which is a top-down projection
        // baked into the geometry - fine while the overhead view was the only camera, and exactly
        // what would have to be undone for a free camera.
        //
        // The convention is unchanged so that only the place the arithmetic happens moves: the
        // highest point in the map is nearest at 0, the lowest is furthest at 1.
        TopDownCamera camera = TopDownCamera
            .Fit([(0f, 0f), (1000f, 1000f)], 800, 600)
            .WithHeights(lowest: -200f, highest: 600f);

        float[] matrix = camera.ToMatrix();

        Depth(matrix, 600f).ShouldBe(0f, 1e-5f, "the highest point is nearest");
        Depth(matrix, -200f).ShouldBe(1f, 1e-5f, "the lowest point is furthest");
        Depth(matrix, 200f).ShouldBe(0.5f, 1e-5f, "halfway up is halfway back");
    }

    [Test]
    public void ToMatrix_DepthMatchesTheArithmeticItReplaced()
    {
        // The formula that used to run per vertex on the processor, restated as the check on the
        // matrix that replaced it. A disagreement here sorts the map differently from how it
        // sorted before, which looks like a z-fighting problem rather than a moved calculation.
        const float lowest = -128f;
        const float highest = 1024f;

        float[] matrix = TopDownCamera
            .Fit([(0f, 0f), (1000f, 1000f)], 800, 600)
            .WithHeights(lowest, highest)
            .ToMatrix();

        foreach (float z in new[] { -128f, 0f, 300f, 1024f })
        {
            float expected = 1f - Math.Clamp((z - lowest) / (highest - lowest), 0f, 1f);

            Depth(matrix, z).ShouldBe(expected, 1e-5f);
        }
    }

    [Test]
    public void WithHeights_ZoomingAfterwards_DoesNotChangeDepth()
    {
        // Depth must not depend on how far the view is zoomed, which was the point the old test
        // was making and is still true - it is only computed somewhere else now.
        TopDownCamera camera = TopDownCamera
            .Fit([(0f, 0f), (1000f, 1000f)], 800, 600)
            .WithHeights(0f, 1000f);

        float[] before = camera.ToMatrix();
        float[] after = camera.WithZoom(4f).ToMatrix();

        Depth(after, 250f).ShouldBe(Depth(before, 250f), 1e-6f);
    }

    [Test]
    public void ToMatrix_KeepsTheHomogeneousDivideAtOne()
    {
        // An orthographic view has no perspective divide. A w other than one would scale the whole
        // map by an amount that depends on nothing.
        float[] matrix = TopDownCamera.Fit([(0f, 0f), (10f, 10f)], 100, 100).ToMatrix();

        matrix[3].ShouldBe(0f);
        matrix[7].ShouldBe(0f);
        matrix[11].ShouldBe(0f);
        matrix[15].ShouldBe(1f);
    }

    /// <summary>The depth the shader would compute for a world height.</summary>
    private static float Depth(float[] matrix, float z) =>
        (0f * matrix[2]) + (0f * matrix[6]) + (z * matrix[10]) + matrix[14];

    /// <summary>Row-vector multiply, exactly as the vertex shader does it.</summary>
    private static (float X, float Y) Transform(float[] matrix, float x, float y) =>
        ((x * matrix[0]) + (y * matrix[4]) + matrix[12],
         (x * matrix[1]) + (y * matrix[5]) + matrix[13]);
}
