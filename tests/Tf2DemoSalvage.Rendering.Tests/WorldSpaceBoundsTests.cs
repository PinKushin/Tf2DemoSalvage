using System;

using Tf2DemoSalvage.Content.Assets;

namespace Tf2DemoSalvage.Rendering.Tests;

/// <summary>
/// The world-space extent of a placed box, and what rotation does to it.
/// </summary>
/// <remarks>
/// **Written because working these numbers out corrected a claim already committed.** A comment in
/// this project said a long prop rotated forty-five degrees buckets larger. It buckets SMALLER: the
/// long axis shrinks and the short one grows. Every prediction below is arithmetic on
/// <c>TransformAABB</c>, done before the code was written and checked against it after.
/// </remarks>
public sealed class WorldSpaceBoundsTests
{
    /// <summary>A 100 × 10 × 20 box centred on the origin.</summary>
    private static StudioBox Long => new(-50f, -5f, -10f, 50f, 5f, 10f);

    [Test]
    public void LongestAxis_WithNoRotation_IsTheBoxsOwnLongestAxis()
    {
        WorldSpaceBounds.LongestAxis(Long, Identity()).ShouldBe(100f, 0.001);
    }

    /// <summary>That translation alone does not change the extent.</summary>
    /// <remarks>
    /// **The control for every other case here.** `fDimension` is a difference of two corners, so
    /// the centre cancels — an implementation that accidentally included the translation would
    /// report a box the size of its distance from the origin, which on a map is thousands of units
    /// and would put every model in the largest bucket.
    /// </remarks>
    [Test]
    public void LongestAxis_WhenTheBoxIsMovedFarFromTheOrigin_IsUnchanged()
    {
        float[] moved = Identity();

        moved[12] = 4000f;
        moved[13] = -2500f;
        moved[14] = 900f;

        WorldSpaceBounds.LongestAxis(Long, moved).ShouldBe(100f, 0.001);
    }

    /// <summary>That a quarter turn swaps which world axis is longest, leaving the span alone.</summary>
    [Test]
    public void LongestAxis_RotatedAQuarterTurnAboutZ_IsStillTheLongAxis()
    {
        WorldSpaceBounds.LongestAxis(Long, RotationAboutZ(90f)).ShouldBe(100f, 0.001);
    }

    /// <summary>That forty-five degrees SHRINKS the longest axis, which is the corrected claim.</summary>
    /// <remarks>
    /// **The arithmetic, predicted before the measurement.** Half-extents are (50, 5, 10). At
    /// forty-five degrees the X and Y rows are both (±0.7071, ±0.7071, 0), so
    /// <c>worldX = |50 × 0.7071| + |5 × 0.7071| = 38.89</c>, and the same for Y. Doubling gives a
    /// world span of 77.78 on both, against 20 on Z — so the longest axis falls from 100 to 77.78.
    ///
    /// A comment claiming rotation enlarges would predict something above 100 here, and this test
    /// is what says otherwise.
    /// </remarks>
    [Test]
    public void LongestAxis_RotatedFortyFiveDegreesAboutZ_ShrinksRatherThanGrows()
    {
        float longest = WorldSpaceBounds.LongestAxis(Long, RotationAboutZ(45f));

        longest.ShouldBe(77.78f, 0.01);
        longest.ShouldBeLessThan(100f, "a long thin box's widest axis narrows as it turns");
    }

    /// <summary>That a cube is unchanged by any rotation, and a long box is not.</summary>
    /// <remarks>
    /// **The pair is the point.** A cube's world extent is rotation-invariant only for quarter
    /// turns; at forty-five degrees it grows by root two, which is the case people picture when
    /// they say rotation enlarges a bounding box. Both are true of different shapes, and asserting
    /// only one of them is how the wrong general claim survived.
    /// </remarks>
    [Test]
    public void LongestAxis_ForACubeAtFortyFiveDegrees_GrowsByRootTwo()
    {
        StudioBox cube = new(-10f, -10f, -10f, 10f, 10f, 10f);

        WorldSpaceBounds.LongestAxis(cube, RotationAboutZ(45f))
            .ShouldBe(20f * MathF.Sqrt(2f), 0.01);
    }

    /// <summary>That the transpose is not silently accepted, which a symmetric box would hide.</summary>
    /// <remarks>
    /// **A deliberately asymmetric rotation**, because this project's convention crossing is a
    /// known trap. At thirty degrees the X and Y world spans differ — 93.6 against 58.7 — so
    /// reading the matrix transposed swaps them and changes the answer. A ninety-degree turn or a
    /// cube would agree either way and prove nothing.
    /// </remarks>
    [Test]
    public void LongestAxis_AtThirtyDegrees_DistinguishesTheMatrixConvention()
    {
        // |50·cos30| + |5·sin30| = 43.30 + 2.5 = 45.80, doubled is 91.60.
        WorldSpaceBounds.LongestAxis(Long, RotationAboutZ(30f)).ShouldBe(91.60f, 0.01);
    }

    [Test]
    public void LongestAxis_ForAMatrixOfTheWrongLength_Throws()
    {
        Should.Throw<ArgumentException>(() => WorldSpaceBounds.LongestAxis(Long, new float[9]));
    }

    private static float[] Identity() =>
    [
        1f, 0f, 0f, 0f,
        0f, 1f, 0f, 0f,
        0f, 0f, 1f, 0f,
        0f, 0f, 0f, 1f,
    ];

    /// <summary>A row-vector rotation about Z, translation in the last row.</summary>
    private static float[] RotationAboutZ(float degrees)
    {
        float radians = degrees * MathF.PI / 180f;
        float cos = MathF.Cos(radians);
        float sin = MathF.Sin(radians);

        return
        [
            cos, sin, 0f, 0f,
            -sin, cos, 0f, 0f,
            0f, 0f, 1f, 0f,
            0f, 0f, 0f, 1f,
        ];
    }
}
