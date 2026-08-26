using Tf2DemoSalvage.Scene;

namespace Tf2DemoSalvage.Presentation.Tests;

/// <summary>Turning the active camera into a pair of ears.</summary>
/// <remarks>
/// **The prediction comes from `AngleVectors`, not from the code under test**
/// (`mathlib/mathlib_base.cpp:936`). Reduced at roll zero, Valve's right vector is
/// `(sin yaw, -cos yaw, 0)`, and these assert that reduction at angles where a wrong formula would
/// disagree.
/// </remarks>
public sealed class SoundListenerTests
{
    /// <summary>How close two floats must be, given sin and cos of a converted angle.</summary>
    private const double Tolerance = 1e-6;

    [Test]
    public void From_LookingDownPositiveX_PutsRightAtNegativeY()
    {
        // **Yaw zero is the decisive case for telling right from forward.** Valve's forward at yaw
        // zero is (1,0,0) and right is (0,-1,0) — so an implementation that returned the forward
        // vector by mistake fails here. A test at 45 degrees could not tell them apart by sign.
        Ears ears = SoundListener.From(Camera(yaw: 0f))!.Value;

        ears.Right.X.ShouldBe(0f, Tolerance);
        ears.Right.Y.ShouldBe(-1f, Tolerance);
        ears.Right.Z.ShouldBe(0f);
    }

    [Test]
    public void From_LookingDownPositiveY_PutsRightAtPositiveX()
    {
        // The quarter turn. Together with the case above this pins the ROTATION DIRECTION: a
        // formula with the sign flipped agrees at neither angle, and one that swapped the
        // components agrees at both only if it also flipped a sign.
        Ears ears = SoundListener.From(Camera(yaw: 90f))!.Value;

        ears.Right.X.ShouldBe(1f, Tolerance);
        ears.Right.Y.ShouldBe(0f, Tolerance);
    }

    [Test]
    public void From_LookingSteeplyDown_LeavesRightUnchanged()
    {
        // **This guards against a plausible future 'fix'.** `right` is the one basis vector pitch
        // does not enter when roll is zero — `sp` appears in Valve's formula only multiplied by
        // `sr`. Someone folding `cos(pitch)` in to make it look symmetric with `forward` would
        // shrink the right vector as the camera looks down, panning every sound toward the centre.
        Ears level = SoundListener.From(Camera(yaw: 30f, pitch: 0f))!.Value;
        Ears steep = SoundListener.From(Camera(yaw: 30f, pitch: 80f))!.Value;

        steep.Right.X.ShouldBe(level.Right.X, Tolerance);
        steep.Right.Y.ShouldBe(level.Right.Y, Tolerance);
    }

    [Test]
    public void From_AnywhereInTheWorld_CarriesTheOriginThrough()
    {
        // A control on the other half of the record: the position is passed, not derived, and an
        // asymmetric point catches a transposition that (0,0,0) would hide.
        Ears ears = SoundListener.From(
            new FreeCamera { Origin = (128f, -64f, 32f), Angles = (0f, 0f, 0f) })!.Value;

        ears.Origin.ShouldBe((128f, -64f, 32f));
    }

    [Test]
    public void From_WithNoCamera_HasNoEars()
    {
        // Null rather than a listener at the world origin — which would attenuate every sound by
        // its distance from (0,0,0) and be indistinguishable from a broken falloff curve.
        SoundListener.From(camera: null).ShouldBeNull();
    }

    private static FreeCamera Camera(float yaw, float pitch = 0f) =>
        new() { Origin = (0f, 0f, 0f), Angles = (pitch, yaw, 0f) };
}
