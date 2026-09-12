using System;
using Tf2DemoSalvage.Animation.Animating;

namespace Tf2DemoSalvage.Animation.Tests;

/// <summary>
/// The double-precision transform IVP places a body with inside a step (B369).
/// </summary>
/// <remarks>
/// **Read from `vphysics.dll`** — `docs/findings/51`, *The four routines under the point-plane
/// evaluator*. The time-of-impact evaluators do not rotate through a quaternion: `FUN_1800734e0` builds a
/// 4×4 in doubles with `FUN_180071330`, and `FUN_180070bc0` puts a local point in the world through it:
///
/// <code>
///   m0 = 1 − (z·2z + y·2y)   m1 = x·2y − w·2z       m2 = w·2y + x·2z
///   m4 = w·2z + x·2y         m5 = 1 − (z·2z + x·2x)  m6 = y·2z − w·2x
///   m8 = x·2z − w·2y         m9 = w·2x + y·2z        m10 = 1 − (y·2y + x·2x)
///
///   world[i] = ((p.x·m[i,0] + p.y·m[i,1]) + p.z·m[i,2]) + t[i]
/// </code>
///
/// **That grouping is from the disassembly**, and it corrects an earlier reading taken from the decompiler,
/// which printed the sum as `p.z·m[i,2] + p.x·m[i,0] + p.y·m[i,1]` and was ported as written.
///
/// **Every point here has three different, non-zero components**, so an exchanged axis or a dropped
/// sign cannot land on the same answer.
/// </remarks>
public sealed class IvpMatrixConformanceTests
{
    private const double Tolerance = 1e-6d;

    /// <summary>A quarter turn about Z, as a float quaternion: <c>(0, 0, sin 45°, cos 45°)</c>.</summary>
    private static readonly (float X, float Y, float Z, float W) QuarterTurnAboutZ =
        (0f, 0f, 0.70710677f, 0.70710677f);

    /// <remarks>
    /// **A quarter turn about Z sends X to Y and Y to −X.** With `z = w = sin 45°`: `m1 = −2zw = −1`,
    /// `m4 = 2zw = 1`, `m0 = m5 = 1 − 2z² = 0`, `m10 = 1`, and every other term zero.
    /// </remarks>
    [Test]
    public void FromRotation_AQuarterTurnAboutZ_SendsXToYAndYToMinusX()
    {
        IvpMatrix matrix = IvpMatrix.FromRotation(QuarterTurnAboutZ, (0d, 0d, 0d));

        (double X, double Y, double Z) x = matrix.ToWorld((1d, 0d, 0d));
        (double X, double Y, double Z) y = matrix.ToWorld((0d, 1d, 0d));
        (double X, double Y, double Z) z = matrix.ToWorld((0d, 0d, 1d));

        x.X.ShouldBe(0d, Tolerance);
        x.Y.ShouldBe(1d, Tolerance);
        x.Z.ShouldBe(0d, Tolerance);

        y.X.ShouldBe(-1d, Tolerance);
        y.Y.ShouldBe(0d, Tolerance);
        y.Z.ShouldBe(0d, Tolerance);

        z.X.ShouldBe(0d, Tolerance);
        z.Y.ShouldBe(0d, Tolerance);
        z.Z.ShouldBe(1d, Tolerance);
    }

    /// <remarks>
    /// **Rotation first, then translation.** `(1, 2, 3)` turned a quarter about Z is `(−2, 1, 3)`, and
    /// moved by `(10, 20, 30)` it is `(8, 21, 33)`. Translating first would give `(−22, 11, 33)`.
    /// </remarks>
    [Test]
    public void ToWorld_ARotatedAndTranslatedPoint_RotatesThenTranslates()
    {
        IvpMatrix matrix = IvpMatrix.FromRotation(QuarterTurnAboutZ, (10d, 20d, 30d));

        (double X, double Y, double Z) world = matrix.ToWorld((1d, 2d, 3d));

        world.X.ShouldBe(8d, Tolerance);
        world.Y.ShouldBe(21d, Tolerance);
        world.Z.ShouldBe(33d, Tolerance);
    }

    /// <remarks>
    /// **The identity rotation leaves only the translation**, which is the control for the two cases
    /// above: a matrix that ignored the quaternion would pass this and fail both of them.
    /// </remarks>
    [Test]
    public void ToWorld_TheIdentityRotation_OnlyTranslates()
    {
        IvpMatrix matrix = IvpMatrix.FromRotation((0f, 0f, 0f, 1f), (10d, 20d, 30d));

        (double X, double Y, double Z) world = matrix.ToWorld((1d, 2d, 3d));

        world.X.ShouldBe(11d);
        world.Y.ShouldBe(22d);
        world.Z.ShouldBe(33d);
    }

    /// <remarks>
    /// **`x` and `y` are added before `z`**, which is `FUN_180070bc0`'s instruction order: `ADDSD` of the
    /// `x·m0` and `y·m1` products, then of `z·m2`, then of the translation. With every first-row term one and
    /// the point `(0.1, 0.2, 2.2)`, that is exactly `2.5`; adding `z` first, to either of the others, lands one
    /// ulp above at `2.5000000000000004`.
    /// </remarks>
    [Test]
    public void ToWorld_TermsThatRoundDifferentlyByGrouping_SumsXAndYBeforeZ()
    {
        IvpMatrix matrix = new(
            M0: 1d, M1: 1d, M2: 1d, M4: 0d, M5: 0d, M6: 0d, M8: 0d, M9: 0d, M10: 0d, Translation: (0d, 0d, 0d));

        BitConverter.DoubleToInt64Bits(matrix.ToWorld((0.1d, 0.2d, 2.2d)).X)
            .ShouldBe(BitConverter.DoubleToInt64Bits(2.5d));
    }

    /// <remarks>
    /// **A direction turns and does not move** — how `FUN_1800a3470` carries a face normal into the world.
    /// `(1, 2, 3)` turned a quarter about Z is `(−2, 1, 3)` whatever the translation.
    /// </remarks>
    [Test]
    public void Rotate_ADirectionOnATranslatedBody_TurnsWithoutMoving()
    {
        IvpMatrix matrix = IvpMatrix.FromRotation(QuarterTurnAboutZ, (10d, 20d, 30d));

        (double X, double Y, double Z) turned = matrix.Rotate((1d, 2d, 3d));

        turned.X.ShouldBe(-2d, Tolerance);
        turned.Y.ShouldBe(1d, Tolerance);
        turned.Z.ShouldBe(3d, Tolerance);
    }
}
