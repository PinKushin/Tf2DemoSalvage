using System;

using Tf2DemoSalvage.Presentation;

namespace Tf2DemoSalvage.Presentation.Tests;

/// <summary>
/// Free-camera movement: pure trigonometry, tested directly for the first time.
/// </summary>
/// <remarks>
/// **This geometry existed for weeks with no direct tests, and the reason was the signature.**
/// `FreeFlight.Movement` took an `IReadOnlySet&lt;Keys&gt;`, so exercising the trigonometry meant
/// constructing WinForms key sets — which put a function of sines and cosines behind a UI
/// dependency it had no need of.
///
/// Splitting the key mapping from the movement (D65) left this half taking four numbers and a
/// boolean, and it turns out to have several claims worth pinning: the axis convention, the pitch
/// sign, and diagonal normalisation.
/// </remarks>
public sealed class FreeFlightPathTests
{
    /// <summary>Half a second, so the expected distance is a round number.</summary>
    private const double Half = 0.5;

    /// <summary>Distance covered in <see cref="Half"/> a second at normal speed.</summary>
    private const float Travel = FreeFlightPath.SpeedPerSecond * (float)Half;

    [Test]
    public void Movement_ForwardAtZeroPitchAndYaw_TravelsAlongPositiveX()
    {
        // **Valve's convention: X forward, Y left, Z up.** A camera at yaw 0 and pitch 0 faces +X,
        // so forward must move along it and nowhere else.
        (float x, float y, float z) = FreeFlightPath.Movement(
            new FlightInput(Forward: 1f, Right: 0f, Up: 0f, Walk: false), Half, pitch: 0f, yaw: 0f);

        x.ShouldBe(Travel, 0.01);
        y.ShouldBe(0f, 0.01);
        z.ShouldBe(0f, 0.01);
    }

    [Test]
    public void Movement_APositivePitch_LooksAndTravelsDOWN()
    {
        // **The trap in Source's convention, and it is a sign.** Positive pitch is DOWNWARD, so
        // forward at pitch +90 must go to negative Z. Getting it the other way round makes the
        // camera fly into the floor while the user looks at the sky — which reads as an inverted
        // mouse setting rather than as an error.
        (float x, float _, float z) = FreeFlightPath.Movement(
            new FlightInput(1f, 0f, 0f, false), Half, pitch: 90f, yaw: 0f);

        z.ShouldBe(-Travel, 0.01, "positive pitch is downward in Source");
        x.ShouldBe(0f, 0.01, "and looking straight down leaves no horizontal component");
    }

    [Test]
    public void Movement_StrafingRightAtYawZero_TravelsAlongNegativeY()
    {
        // Y is LEFT in Valve's axes, so the right-hand vector is -Y. A reader assuming Y-is-right
        // gets a camera that strafes the wrong way, which is instantly obvious to a user and
        // invisible to any assertion that only checks the magnitude.
        (float x, float y, float _) = FreeFlightPath.Movement(
            new FlightInput(0f, 1f, 0f, false), Half, pitch: 0f, yaw: 0f);

        y.ShouldBe(-Travel, 0.01);
        x.ShouldBe(0f, 0.01);
    }

    [Test]
    public void Movement_Diagonal_CoversTheSameDistanceAsStraight()
    {
        // **The classic diagonal-speed bug.** Without normalising before the scale, holding forward
        // and right covers 1.41 times the distance — which feels like the camera being inconsistent
        // rather than like a defect anyone can name.
        (float x, float y, float z) = FreeFlightPath.Movement(
            new FlightInput(1f, 1f, 0f, false), Half, pitch: 0f, yaw: 0f);

        float distance = MathF.Sqrt((x * x) + (y * y) + (z * z));

        distance.ShouldBe(Travel, 0.01, "diagonal travel is the same speed as straight");
    }

    [Test]
    public void Movement_Walking_ScalesTheDistanceAndNothingElse()
    {
        // **`+speed` SLOWS the camera** — see `FreeCameraConformanceTests` for the engine reading.
        // This test only pins that the modifier scales distance and leaves the direction alone; the
        // magnitude is the conformance suite's to predict.
        (float x, float y, float z) = FreeFlightPath.Movement(
            new FlightInput(1f, 0f, 0f, Walk: true), Half, pitch: 0f, yaw: 0f);

        x.ShouldBe(Travel * FreeFlightPath.WalkMultiplier, 0.1);
        x.ShouldBeLessThan(Travel, "walking is slower than not walking");
        y.ShouldBe(0f, 0.01, "and the direction is unchanged");
        z.ShouldBe(0f, 0.01);
    }

    [Test]
    public void Movement_OpposedAxes_AreIdleRatherThanAnError()
    {
        // Holding W and S together is a person resting two fingers, not a fault to report.
        new FlightInput(0f, 0f, 0f, false).IsIdle.ShouldBeTrue();

        FreeFlightPath.Movement(FlightInput.None, Half, 0f, 0f).ShouldBe((0f, 0f, 0f));
    }

    [Test]
    public void Movement_LookingStraightDownWhileFlyingForwardAndUp_Cancels()
    {
        // **The case the idle check cannot catch**, because the input is not idle: forward and up
        // are both pressed. At pitch 90 the forward vector is straight down, so it cancels the up
        // axis exactly after projection — and a naive divide by the resulting zero length yields
        // NaN, which moves the camera to nowhere and never comes back.
        (float x, float y, float z) = FreeFlightPath.Movement(
            new FlightInput(Forward: 1f, Right: 0f, Up: 1f, Walk: false), Half, pitch: 90f, yaw: 0f);

        float.IsNaN(x).ShouldBeFalse();
        (x, y, z).ShouldBe((0f, 0f, 0f));
    }

    [Test]
    public void Movement_ZeroOrNegativeElapsed_DoesNotMove()
    {
        FreeFlightPath.Movement(new FlightInput(1f, 0f, 0f, false), 0, 0f, 0f)
            .ShouldBe((0f, 0f, 0f));

        FreeFlightPath.Movement(new FlightInput(1f, 0f, 0f, false), -1, 0f, 0f)
            .ShouldBe((0f, 0f, 0f));
    }

    [Test]
    public void Movement_AtYawNinety_FacesPositiveY()
    {
        // A second yaw, because one angle cannot distinguish a correct rotation from a constant.
        (float x, float y, float _) = FreeFlightPath.Movement(
            new FlightInput(1f, 0f, 0f, false), Half, pitch: 0f, yaw: 90f);

        y.ShouldBe(Travel, 0.01);
        x.ShouldBe(0f, 0.01);
    }

    [Test]
    public void Movement_UpIsWorldUp_RegardlessOfWhereTheCameraLooks()
    {
        // Up is not the camera's up vector; it is the world's. A camera pitched over must still
        // rise vertically when the user asks to go up, which is what every flying camera does and
        // what makes it usable at all.
        foreach (float pitch in new[] { -60f, 0f, 45f })
        {
            (float x, float y, float z) = FreeFlightPath.Movement(
                new FlightInput(0f, 0f, 1f, false), Half, pitch, yaw: 30f);

            z.ShouldBe(Travel, 0.01, $"at pitch {pitch}");
            x.ShouldBe(0f, 0.01, $"at pitch {pitch}");
            y.ShouldBe(0f, 0.01, $"at pitch {pitch}");
        }
    }
}
