using System;

using Tf2DemoSalvage.Content.Assets;
using Tf2DemoSalvage.Core;

namespace Tf2DemoSalvage.Content.Tests.Assets;

/// <summary>The two places <see cref="StudioBones"/> wrote .NET's <c>float.Epsilon</c> for C's <c>FLT_EPSILON</c> (B623 review).</summary>
/// <remarks>
/// <c>float.Epsilon</c> is 2⁻¹⁴⁹ and <c>1f + float.Epsilon</c> is one; <c>FLT_EPSILON</c> is 2⁻²³. Each subject is chosen
/// where the two give different floats, and the expectation is the engine's line written out.
/// </remarks>
public sealed class FltEpsilonConformanceTests
{
    /// <remarks>
    /// **<c>QuaternionScale</c>, <c>t = sinsom / (sinom + FLT_EPSILON)</c>** (<c>mathlib_base.cpp:1782</c>). A rotation of
    /// sine 0.001, where the epsilon is a ten-thousandth of it: half the angle, x scaled by the engine's own quotient.
    /// </remarks>
    [Test]
    public void Scale_ASmallRotation_DividesBySineplusFltEpsilon()
    {
        (float X, float Y, float Z, float W) small = (0.001f, 0f, 0f, MathF.Sqrt(1f - (0.001f * 0.001f)));

        float sinom = MathF.Min(MathF.Sqrt(small.X * small.X), 1f);
        float sinsom = MathF.Sin(MathF.Asin(sinom) * 0.5f);
        float expected = small.X * (sinsom / (sinom + ValveFloat.FltEpsilon));

        StudioBones.Scale(small, 0.5f).X.ShouldBe(expected);
        expected.ShouldNotBe(small.X * (sinsom / sinom), "the control: the epsilon moves the answer at this size");
    }

    /// <remarks>
    /// **<c>MatrixAngles</c> to a quaternion, <c>if( trace &gt; 1.0f + FLT_EPSILON )</c>** (<c>mathlib_base.cpp:157</c>). A
    /// trace exactly one float step above one is not past it, so the engine takes the largest-diagonal branch (here x,
    /// <c>:165-172</c>); against <c>float.Epsilon</c> the trace branch ran instead.
    /// </remarks>
    [Test]
    public void ToQuaternion_ATraceOneFloatStepAboveOne_TakesTheDiagonalBranch()
    {
        float step = MathF.BitIncrement(1f) - 1f;
        float[] matrix = [step, 0.3f, 0.2f, 0f, 0.1f, 0f, 0.6f, 0f, 0.4f, 0.5f, 0f, 0f];

        (matrix[0] + matrix[5] + matrix[10] + 1f).ShouldBe(MathF.BitIncrement(1f), "the control: the trace is one step above one");

        float x = 1f + matrix[0] - matrix[5] - matrix[10];
        float y = matrix[4] + matrix[1];
        float z = matrix[2] + matrix[8];
        float w = matrix[9] - matrix[6];

        // `QuaternionNormalize`: the reciprocal of the root of the sum of squares, multiplied in.
        float inverse = 1f / MathF.Sqrt((x * x) + (y * y) + (z * z) + (w * w));

        StudioBones.ToQuaternion(matrix).ShouldBe((x * inverse, y * inverse, z * inverse, w * inverse));
    }
}
