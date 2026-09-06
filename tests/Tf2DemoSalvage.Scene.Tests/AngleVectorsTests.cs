namespace Tf2DemoSalvage.Scene.Tests;

/// <summary>The basis a Source angle triple describes.</summary>
/// <remarks>
/// **Predicted from `mathlib_base.cpp:906-947`, not from the code under test.** Valve's own values
/// are `forward = (cp*cy, cp*sy, -sp)` and, at roll zero, `right = (sy, -cy, 0)`.
///
/// **The inputs are chosen so a wrong formula disagrees.** Angles of 0 and 90 separate forward from
/// right by sign and by which component is non-zero; 45 degrees would let a transposition through.
/// </remarks>
public sealed class AngleVectorsTests
{
    private const double Tolerance = 1e-6;

    [Test]
    public void Forward_AtZero_PointsDownPositiveX()
    {
        (float X, float Y, float Z) forward = AngleVectors.Forward(pitch: 0f, yaw: 0f);

        forward.X.ShouldBe(1f, Tolerance);
        forward.Y.ShouldBe(0f, Tolerance);
        forward.Z.ShouldBe(0f, Tolerance);
    }

    [Test]
    public void Forward_AtYaw90_PointsDownPositiveY()
    {
        (float X, float Y, float Z) forward = AngleVectors.Forward(pitch: 0f, yaw: 90f);

        forward.X.ShouldBe(0f, Tolerance);
        forward.Y.ShouldBe(1f, Tolerance);
    }

    [Test]
    public void Forward_LookingDown_HasNegativeZ()
    {
        // **The sign of Z is the trap, and Valve's is negative.** `forward->z = -sp`, so a positive
        // pitch looks DOWN. Getting this backwards inverts every mouse-look in the viewer while
        // leaving the horizontal motion perfect, which reads as "the camera is fine but inverted"
        // rather than as a maths error.
        AngleVectors.Forward(pitch: 90f, yaw: 0f).Z.ShouldBe(-1f, Tolerance);
    }

    [Test]
    public void Right_AtZero_PointsDownNegativeY()
    {
        (float X, float Y, float Z) right = AngleVectors.Right(yaw: 0f);

        right.X.ShouldBe(0f, Tolerance);
        right.Y.ShouldBe(-1f, Tolerance);
        right.Z.ShouldBe(0f);
    }

    [Test]
    public void Right_AtYaw90_PointsDownPositiveX()
    {
        AngleVectors.Right(yaw: 90f).X.ShouldBe(1f, Tolerance);
    }

    [Test]
    public void Forward_AndRight_ArePerpendicular()
    {
        // **A property rather than a value, and it holds at every angle.** Two formulas can each
        // look plausible and still not describe one basis; a dot product of zero is the claim that
        // they do. Checked at a pitch as well, since `right` ignores pitch and `forward` does not.
        (float X, float Y, float Z) forward = AngleVectors.Forward(pitch: 30f, yaw: 57f);
        (float X, float Y, float Z) right = AngleVectors.Right(yaw: 57f);

        double dot = (forward.X * right.X) + (forward.Y * right.Y) + (forward.Z * right.Z);

        dot.ShouldBe(0d, 1e-6);
    }

    [Test]
    public void Forward_AtAnyAngle_IsAUnitVector()
    {
        (float X, float Y, float Z) forward = AngleVectors.Forward(pitch: -22f, yaw: 143f);

        double length = System.Math.Sqrt(
            (forward.X * forward.X) + (forward.Y * forward.Y) + (forward.Z * forward.Z));

        length.ShouldBe(1d, 1e-6);
    }

    /// <remarks>
    /// **Roll 90 with nothing else, which is the case a reduced basis cannot produce.** With
    /// `sr = 1` and `cr = 0` Valve's `right->x = (-1*sr*sp*cy + -1*cr*-sy)` collapses to `-sp*cy`
    /// and `right->z` to `-cp`, so at pitch and yaw zero the right vector is `(0, 0, -1)` — pointing
    /// straight DOWN, where the roll-free form returns `(0, -1, 0)` for the same input.
    ///
    /// Detail props are why this matters: 27,686 of 28,699 on `koth_harvest_final` carry a non-zero
    /// roll, because `vbsp` builds a non-upright detail's orientation from the ground's surface
    /// normal (<c>detailobjects.cpp:568-600</c>).
    /// </remarks>
    [Test]
    public void Right_AtRoll90_PointsDownRatherThanSideways()
    {
        (float X, float Y, float Z) right = AngleVectors.Right(pitch: 0f, yaw: 0f, roll: 90f);

        right.X.ShouldBe(0f, Tolerance);
        right.Y.ShouldBe(0f, Tolerance);
        right.Z.ShouldBe(-1f, Tolerance);
    }

    /// <remarks>
    /// **Pitch enters `right` only through `sr`**, so this is the input that separates a
    /// transcription of Valve's three lines from the reduction beside it: at roll 90 and pitch 90
    /// the vector is `(-1, 0, 0)`, and pitch has moved it from `(0, 0, -1)`.
    /// </remarks>
    [Test]
    public void Right_AtRoll90AndPitch90_TakesThePitchTermTheReductionDrops()
    {
        (float X, float Y, float Z) right = AngleVectors.Right(pitch: 90f, yaw: 0f, roll: 90f);

        right.X.ShouldBe(-1f, Tolerance);
        right.Y.ShouldBe(0f, Tolerance);
        right.Z.ShouldBe(0f, Tolerance);
    }

    /// <remarks>
    /// `up->x = (cr*sp*cy + -sr*-sy)` and `up->z = cr*cp`, so at roll 90 the up vector loses its
    /// vertical component entirely and lies along negative Y.
    /// </remarks>
    [Test]
    public void Up_AtRoll90_LiesFlatRatherThanStandingUp()
    {
        (float X, float Y, float Z) up = AngleVectors.Up(pitch: 0f, yaw: 0f, roll: 90f);

        up.X.ShouldBe(0f, Tolerance);
        up.Y.ShouldBe(-1f, Tolerance);
        up.Z.ShouldBe(0f, Tolerance);
    }

    /// <remarks>
    /// **The control.** The camera passes no roll and must keep the vectors it has always had, so
    /// the three-argument form at roll zero has to agree with the reduced two-argument one — for
    /// `right` at a pitch as well, since that is the term the reduction proves cannot reach the
    /// result.
    /// </remarks>
    [Test]
    public void RightAndUp_AtRollZero_AgreeWithTheReducedForms()
    {
        AngleVectors.Right(pitch: 30f, yaw: 57f, roll: 0f)
            .ShouldBe(AngleVectors.Right(yaw: 57f));

        AngleVectors.Up(pitch: 30f, yaw: 57f, roll: 0f)
            .ShouldBe(AngleVectors.Up(pitch: 30f, yaw: 57f));
    }

    /// <remarks>
    /// A basis is orthonormal at any roll, which is the property that catches a sign flipped in one
    /// of the six lines while each component still looks plausible on its own.
    /// </remarks>
    [Test]
    public void ForwardRightAndUp_AtAnArbitraryRoll_AreMutuallyPerpendicular()
    {
        (float X, float Y, float Z) forward = AngleVectors.Forward(pitch: -22f, yaw: 143f);
        (float X, float Y, float Z) right = AngleVectors.Right(pitch: -22f, yaw: 143f, roll: 71f);
        (float X, float Y, float Z) up = AngleVectors.Up(pitch: -22f, yaw: 143f, roll: 71f);

        Dot(forward, right).ShouldBe(0d, 1e-6);
        Dot(forward, up).ShouldBe(0d, 1e-6);
        Dot(right, up).ShouldBe(0d, 1e-6);
    }

    private static double Dot((float X, float Y, float Z) first, (float X, float Y, float Z) second)
        => (first.X * second.X) + (first.Y * second.Y) + (first.Z * second.Z);
}
