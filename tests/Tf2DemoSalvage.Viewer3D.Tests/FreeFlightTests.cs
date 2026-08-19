using System;
using System.Collections.Generic;
using System.Windows.Forms;

using Tf2DemoSalvage.Viewer3D;

namespace Tf2DemoSalvage.Viewer3D.Tests;

/// <summary>
/// Flying the free camera from held keys and a frame's duration.
/// </summary>
/// <remarks>
/// **B97.** The camera moved once per <c>WM_KEYDOWN</c>, so Windows' auto-repeat set its speed:
/// nothing for the repeat delay, then fixed jumps at the repeat rate, and never two directions at
/// once because auto-repeat reports only the last key. Integrating per frame fixes all three, and
/// the movement being a pure function is what makes any of it testable without a window.
/// </remarks>
public sealed class FreeFlightTests
{
    private static HashSet<Keys> Held(params Keys[] keys) => [.. keys];

    /// <summary>Looking along +X, level.</summary>
    private const float Yaw = 0f;

    private const float Pitch = 0f;

    [Test]
    public void NothingHeld_DoesNotMove()
    {
        FreeFlight.Movement(Held(), 0.016, Pitch, Yaw, fast: false).ShouldBe((0f, 0f, 0f));
    }

    [Test]
    public void FreeFlight_Forward_TravelsSpeedTimesDuration()
    {
        // The whole point of the change: distance is speed times time, so it no longer depends on
        // the keyboard's repeat rate or the frame rate. Half a second at 600 units a second is 300.
        (float X, float Y, float Z) moved =
            FreeFlight.Movement(Held(Keys.W), 0.5, Pitch, Yaw, fast: false);

        moved.X.ShouldBe(300f, 0.01f);
        moved.Y.ShouldBe(0f, 0.01f);
        moved.Z.ShouldBe(0f, 0.01f);
    }

    [Test]
    public void FreeFlight_HalfTheFrame_TravelsHalfTheDistance()
    {
        // Frame-rate independence stated as a property rather than a single value.
        (float X, float Y, float Z) longer =
            FreeFlight.Movement(Held(Keys.W), 0.02, Pitch, Yaw, fast: false);

        (float X, float Y, float Z) shorter =
            FreeFlight.Movement(Held(Keys.W), 0.01, Pitch, Yaw, fast: false);

        longer.X.ShouldBe(shorter.X * 2f, 0.001f);
    }

    [Test]
    public void FreeFlight_Shift_QuadruplesTheSpeed()
    {
        float normal = FreeFlight.Movement(Held(Keys.W), 0.1, Pitch, Yaw, fast: false).X;
        float fast = FreeFlight.Movement(Held(Keys.W), 0.1, Pitch, Yaw, fast: true).X;

        fast.ShouldBe(normal * FreeFlight.ShiftMultiplier, 0.01f);
    }

    [Test]
    public void TwoKeysMoveDiagonally_AtTheSameSpeedAsOne()
    {
        // **Auto-repeat could not do this at all**, because it reports one key. And the direction is
        // normalised, so a diagonal is not faster than a straight line — the mistake that makes
        // strafe-running quicker in a lot of homemade cameras.
        (float X, float Y, float Z) straight =
            FreeFlight.Movement(Held(Keys.W), 0.1, Pitch, Yaw, fast: false);

        (float X, float Y, float Z) diagonal =
            FreeFlight.Movement(Held(Keys.W, Keys.D), 0.1, Pitch, Yaw, fast: false);

        float straightLength = MathF.Sqrt(
            (straight.X * straight.X) + (straight.Y * straight.Y) + (straight.Z * straight.Z));

        float diagonalLength = MathF.Sqrt(
            (diagonal.X * diagonal.X) + (diagonal.Y * diagonal.Y) + (diagonal.Z * diagonal.Z));

        diagonalLength.ShouldBe(straightLength, 0.01f);

        // And it genuinely went both ways rather than picking one.
        diagonal.X.ShouldBeGreaterThan(0f);
        MathF.Abs(diagonal.Y).ShouldBeGreaterThan(0f);
    }

    [Test]
    public void FreeFlight_OpposedKeys_Cancel()
    {
        // Holding W and S is a real thing a hand does, and it must not divide by a zero length.
        FreeFlight.Movement(Held(Keys.W, Keys.S), 0.1, Pitch, Yaw, fast: false)
            .ShouldBe((0f, 0f, 0f));
    }

    [Test]
    public void UpAndDownFollowTheWorld_NotTheCamera()
    {
        // **Pitched steeply downward and still rising straight up.** Lifting along the camera's own
        // up axis drifts sideways as soon as the view is pitched, which reads as broken; every
        // editor lifts along the world instead.
        (float X, float Y, float Z) lifted =
            FreeFlight.Movement(Held(Keys.Space), 0.1, pitch: 60f, yaw: 45f, fast: false);

        lifted.Z.ShouldBeGreaterThan(0f);
        lifted.X.ShouldBe(0f, 0.001f);
        lifted.Y.ShouldBe(0f, 0.001f);
    }

    [Test]
    public void FreeFlight_Control_DropsStraightDown()
    {
        // Either Control reports, because Windows sends the left and right keys as distinct codes
        // and a set built from raw key data can hold either.
        FreeFlight.Movement(Held(Keys.LControlKey), 0.1, Pitch, Yaw, fast: false)
            .Z.ShouldBeLessThan(0f);

        FreeFlight.Movement(Held(Keys.RControlKey), 0.1, Pitch, Yaw, fast: false)
            .Z.ShouldBeLessThan(0f);

        FreeFlight.Movement(Held(Keys.ControlKey), 0.1, Pitch, Yaw, fast: false)
            .Z.ShouldBeLessThan(0f);
    }

    [Test]
    public void FreeFlight_Forward_FollowsTheYaw()
    {
        // Turned ninety degrees, forward is +Y rather than +X. Without this the camera would fly
        // where it was first pointed for ever.
        (float X, float Y, float Z) moved =
            FreeFlight.Movement(Held(Keys.W), 0.1, Pitch, yaw: 90f, fast: false);

        moved.Y.ShouldBeGreaterThan(0f);
        moved.X.ShouldBe(0f, 0.01f);
    }

    [Test]
    public void FreeFlight_AZeroLengthFrame_DoesNotMove()
    {
        // The first frame after a stall reports no elapsed time, and multiplying by it must not
        // produce a NaN through the normalisation.
        FreeFlight.Movement(Held(Keys.W), 0d, Pitch, Yaw, fast: false).ShouldBe((0f, 0f, 0f));
    }

    [Test]
    public void FreeFlight_NonFlightKeys_AreNotTracked()
    {
        // The caller keeps a set of held keys; letting every key into it would mean a held F or a
        // held Escape sat in there for ever and Escape is handled elsewhere.
        FreeFlight.IsFlightKey(Keys.W).ShouldBeTrue();
        FreeFlight.IsFlightKey(Keys.Space).ShouldBeTrue();
        FreeFlight.IsFlightKey(Keys.LControlKey).ShouldBeTrue();

        FreeFlight.IsFlightKey(Keys.F).ShouldBeFalse();
        FreeFlight.IsFlightKey(Keys.Escape).ShouldBeFalse();
        FreeFlight.IsFlightKey(Keys.F12).ShouldBeFalse();
    }
}
