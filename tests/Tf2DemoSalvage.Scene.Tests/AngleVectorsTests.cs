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
}
