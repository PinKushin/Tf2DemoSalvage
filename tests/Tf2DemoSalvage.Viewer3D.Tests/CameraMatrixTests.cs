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
    public void ToMatrix_LeavesDepthAlone()
    {
        // Depth comes from world height and is computed before the camera is involved. A matrix
        // that scaled it would sort the map by how far the view is zoomed.
        TopDownCamera camera = TopDownCamera.Fit([(0f, 0f), (1000f, 1000f)], 800, 600);
        float[] matrix = camera.ToMatrix();

        foreach (float depth in new[] { 0f, 0.25f, 1f })
        {
            float transformed =
                (0f * matrix[2]) + (0f * matrix[6]) + (depth * matrix[10]) + matrix[14];

            transformed.ShouldBe(depth, 1e-6f);
        }
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

    /// <summary>Row-vector multiply, exactly as the vertex shader does it.</summary>
    private static (float X, float Y) Transform(float[] matrix, float x, float y) =>
        ((x * matrix[0]) + (y * matrix[4]) + matrix[12],
         (x * matrix[1]) + (y * matrix[5]) + matrix[13]);
}
