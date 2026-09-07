using System;

using Tf2DemoSalvage.Animation.Animating;

namespace Tf2DemoSalvage.Animation.Tests;

/// <summary>
/// The three quantities a ragdoll joint limits, measured from its two bodies (B58, D142, D146).
/// </summary>
/// <remarks>
/// **`FUN_180038620` measures three DIFFERENT kinds of thing, which is the finding these tests
/// exist to hold onto** — see `docs/findings/51-vphysics-is-ivp-and-it-is-readable.md`. Only one of
/// them is an angle:
///
/// - the **twist**, `−atan2(q·p, q·(p×m))` about the bisector of the two bodies' primary axes;
/// - a **swing**, the dot of body B's primary axis with body A's wider-swing axis — a SINE;
/// - a **cone**, the dot of the two primary axes with each other — a COSINE.
///
/// **The bind pose is the control that makes the rest of it checkable.** `constraintToReference` and
/// `constraintToAttached` exist precisely so that each constraint axis maps to the same world vector
/// through either body at rest, so a test can predict `0`, `0` and `1` exactly and any sign or index
/// error shows up immediately.
/// </remarks>
public sealed class IvpRagdollConstraintConformanceTests
{
    private const float Close = 1e-5f;

    /// <summary>A joint whose frames are the identity — the two bodies agree at rest.</summary>
    /// <remarks>
    /// **Identity is what a TF2 ragdoll's reference frame actually is.**
    /// `ragdoll_shared.cpp:245` sets `constraintToReference` to identity outright; the attached
    /// frame is the bone-to-bone transform, which is identity too whenever the test puts both
    /// bodies at the same orientation.
    /// </remarks>
    private static IvpRagdollConstraint Joint() => new()
    {
        FrameA = IvpConstraintFrame.Identity,
        FrameB = IvpConstraintFrame.Identity,
    };

    /// <summary>A rotation of <paramref name="radians"/> about a unit axis.</summary>
    private static (float X, float Y, float Z, float W) About(
        (float X, float Y, float Z) axis, double radians)
    {
        double half = radians / 2d;
        double sine = Math.Sin(half);

        return (
            (float)(axis.X * sine),
            (float)(axis.Y * sine),
            (float)(axis.Z * sine),
            (float)Math.Cos(half));
    }

    /// <remarks>
    /// **The control, and the reason every other prediction here can be exact.** With both bodies
    /// unrotated and both frames the identity, `m` is the primary axis, `p` is perpendicular to it,
    /// and `q` lies along `p × m` — so the twist's numerator is zero and its denominator is one.
    /// </remarks>
    [Test]
    public void Measure_AtTheBindPose_ReadsZeroTwistZeroSwingAndAUnitCone()
    {
        IvpRagdollConstraint joint = Joint();

        joint.Measure(new IvpRigidBody(), new IvpRigidBody());

        joint.Twist.Angle.ShouldBe(0f, Close, "the joint is not twisted");
        joint.Swing.Angle.ShouldBe(0f, Close, "nor swung — this one is a sine");
        joint.Cone.Angle.ShouldBe(1f, Close, "and the cone measure is a COSINE, so it reads one");
    }

    /// <remarks>
    /// **A pure twist moves only the twist.** Rotating body B about the primary axis leaves both
    /// primary axes parallel, so the cone's cosine stays at one and the swing's sine at zero — which
    /// is what separates a correct index assignment from one that has the three axes shuffled.
    /// </remarks>
    [Test]
    public void Measure_AfterAPureTwist_ReadsTheAngleAndLeavesTheOthersAtRest()
    {
        IvpRagdollConstraint joint = Joint();

        IvpRigidBody b = new() { Orientation = About((1f, 0f, 0f), 0.4d) };

        joint.Measure(new IvpRigidBody(), b);

        Math.Abs(joint.Twist.Angle).ShouldBe(0.4f, 1e-4f);
        joint.Swing.Angle.ShouldBe(0f, Close, "a twist is not a swing");
        joint.Cone.Angle.ShouldBe(1f, Close, "and the primary axes are still parallel");
    }

    /// <remarks>
    /// **A swing moves the cone AND the swing, and that is not a defect in the measurement.** The
    /// cone is the angle between the two primary axes however it was reached, so any deflection
    /// shows up in it — which is exactly what makes it a cone rather than a fourth axis.
    ///
    /// Rotating body B about the NARROWER swing axis by θ tips its primary axis by θ, so the cone
    /// reads `cos θ` and the swing — the dot of B's primary with A's wider-swing axis — reads
    /// `±sin θ`.
    /// </remarks>
    [Test]
    public void Measure_AfterASwing_ReadsASineAndDropsTheConeToItsCosine()
    {
        IvpRagdollConstraint joint = Joint();

        IvpRigidBody b = new() { Orientation = About((0f, 1f, 0f), 0.3d) };

        joint.Measure(new IvpRigidBody(), b);

        joint.Cone.Angle.ShouldBe((float)Math.Cos(0.3d), 1e-4f);
        Math.Abs(joint.Swing.Angle).ShouldBe((float)Math.Sin(0.3d), 1e-4f);
    }

    /// <remarks>
    /// **The bounds are assigned to the three blocks DIFFERENTLY, and each block takes them from a
    /// different axis than the one it measures** — which reads like a bug and is what
    /// `FUN_1800393d0` does:
    ///
    /// - twist ← `−hi`, `−lo` of the PRIMARY axis, negated and swapped to match its negated angle;
    /// - cone ← `±half the range` of the WIDER swing;
    /// - swing ← `lo`, `hi` of the NARROWER swing, straight through.
    ///
    /// The degrees come from the model's `.phy`; the conversion is `±0.017453292`, dumped.
    /// </remarks>
    [Test]
    public void FromDegrees_WithThreeAxisRanges_AssignsEachBlockTheBoundsTheEngineGivesIt()
    {
        // Primary −30..15, narrower −25..25, wider −79..57 — the demoman's first joint.
        IvpRagdollConstraint joint = IvpRagdollConstraint.FromDegrees(
            primary: (-30f, 15f), narrower: (-25f, 25f), wider: (-79f, 57f));

        const float Radian = 0.017453292f;

        joint.Twist.Lower.ShouldBe(-15f * Radian, Close, "negated hi");
        joint.Twist.Upper.ShouldBe(30f * Radian, Close, "negated lo");

        joint.Swing.Lower.ShouldBe(-25f * Radian, Close);
        joint.Swing.Upper.ShouldBe(25f * Radian, Close);

        joint.Cone.Lower.ShouldBe(-136f * Radian / 2f, 1e-4f, "half the WIDER range, negative");
        joint.Cone.Upper.ShouldBe(136f * Radian / 2f, 1e-4f);
    }

    /// <remarks>
    /// **An axis whose range covers a full turn is disabled rather than clamped**, and the test uses
    /// a range that is outside its bounds so a solver ignoring the flag would be caught.
    /// `FUN_180037890` clears the enable when `upper − lower >= 2π`.
    /// </remarks>
    [Test]
    public void FromDegrees_WithAnAxisFreeThroughAFullTurn_DisablesThatLimit()
    {
        IvpRagdollConstraint joint = IvpRagdollConstraint.FromDegrees(
            primary: (-180f, 180f), narrower: (-25f, 25f), wider: (-79f, 57f));

        joint.Twist.Limited.ShouldBeFalse("360 degrees is not a limit");
        joint.Swing.Limited.ShouldBeTrue("but its neighbours still are");
    }
}
