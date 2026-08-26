using System;

using Microsoft.Extensions.Logging.Abstractions;

using Tf2DemoSalvage.Presentation;

namespace Tf2DemoSalvage.Presentation.Tests;

/// <summary>
/// Flying the free camera by however long the last frame took.
/// </summary>
/// <remarks>
/// **This was the second half of <c>MainForm.FlyCamera</c>** (B188, D90), which did two unrelated
/// jobs: it recorded the frame's duration for the meter, and it moved the camera. Only the first is
/// anything to do with a window, and even that is a metric rather than a control.
///
/// **The controller already owned <c>Origin</c> and <c>Angles</c>**, so the form was reading state
/// out of it, doing arithmetic, and writing the result back — which is the shape that leaves two
/// places able to disagree about where the camera is.
/// </remarks>
public sealed class CameraFlightTests
{
    /// <summary>Where the camera sits if it has not been placed yet.</summary>
    private static readonly (float X, float Y, float Z) Unplaced = (100f, 200f, 300f);

    [Test]
    public void Fly_WithNothingHeld_DoesNotMove()
    {
        // The control for every case below. `FlightInput.IsIdle` short-circuits inside `Movement`,
        // and a controller that wrote Origin anyway would look identical from outside until the
        // camera had never been placed — at which point it would silently jump to the fallback.
        FreeCameraController camera = Placed();

        camera.Fly(default, 0.016d, Unplaced).ShouldBeFalse();

        camera.Origin.ShouldBe((0f, 0f, 0f));
    }

    [Test]
    public void Fly_HoldingForward_MovesAlongTheViewDirection()
    {
        // Yaw 0 and pitch 0 is straight down +X in Source's convention, so a forward hold is a
        // change in X alone. Predicting the AXIS rather than just "it moved" is what separates a
        // correct basis from a transposed one.
        FreeCameraController camera = Placed();

        camera.Fly(Forward, 1d, Unplaced).ShouldBeTrue();

        (float X, float Y, float Z) flown = OriginOf(camera);

        flown.X.ShouldBeGreaterThan(0f);
        flown.Y.ShouldBe(0f, 0.001f);
        flown.Z.ShouldBe(0f, 0.001f);
    }

    [Test]
    public void Fly_FromAnUnplacedCamera_StartsFromTheFallback()
    {
        // **The camera has no position until the map has framed it**, and flight can happen on the
        // frame that first places it. Starting from the origin instead would put the viewer at the
        // centre of the world, under the map.
        // **Angles levelled, and Origin deliberately left null.** A fresh controller starts at pitch
        // 35 — it is placed looking DOWN at the map — so a forward hold would also lose height, and
        // the Z assertion below would be measuring the default pitch rather than the fallback. The
        // variable under test is where flight STARTS from; everything else is held still.
        FreeCameraController camera = new(NullLogger.Instance) { Angles = (0f, 0f) };

        camera.Origin.ShouldBeNull("the fixture is only meaningful while unplaced");

        camera.Fly(Forward, 1d, Unplaced).ShouldBeTrue();

        // Moved forward FROM the fallback, so Y and Z are the fallback's and X is beyond it.
        (float X, float Y, float Z) flown = OriginOf(camera);

        flown.X.ShouldBeGreaterThan(Unplaced.X);
        flown.Y.ShouldBe(Unplaced.Y, 0.001f);
        flown.Z.ShouldBe(Unplaced.Z, 0.001f);
    }

    [Test]
    public void Fly_OverAStalledFrame_MovesNoFurtherThanTheClamp()
    {
        // **A stall is not flight time.** A map load or a window drag would otherwise fling the
        // camera across the map when the loop resumes — the same reason a stall is not playback
        // time.
        //
        // The prediction is exact: a ten-second frame and a frame at the clamp must land in the
        // SAME place, which no "it moved less" assertion can distinguish from a smaller step.
        FreeCameraController stalled = Placed();
        FreeCameraController clamped = Placed();

        stalled.Fly(Forward, 10d, Unplaced);
        clamped.Fly(Forward, FreeCameraController.MaximumFrameSeconds, Unplaced);

        stalled.Origin.ShouldBe(clamped.Origin);
    }

    [Test]
    public void Fly_ForNoTime_DoesNotMove()
    {
        // The first frame after a restart has no elapsed time. `Movement` already refuses a
        // non-positive duration; this pins that the controller does not work around it.
        FreeCameraController camera = Placed();

        camera.Fly(Forward, 0d, Unplaced).ShouldBeFalse();

        camera.Origin.ShouldBe((0f, 0f, 0f));
    }

    [Test]
    public void Fly_TurnedAndHoldingForward_FollowsTheNewAngles()
    {
        // The controller owns the angles, so flight must read the CURRENT ones rather than a copy
        // taken when it was constructed. Yaw 90 turns forward from +X to +Y.
        FreeCameraController camera = Placed();

        camera.Angles = (0f, 90f);

        camera.Fly(Forward, 1d, Unplaced).ShouldBeTrue();

        (float X, float Y, float Z) flown = OriginOf(camera);

        flown.X.ShouldBe(0f, 0.001f);
        flown.Y.ShouldBeGreaterThan(0f);
    }

    /// <summary>Holding forward and nothing else.</summary>
    private static FlightInput Forward => new(Forward: 1f, Right: 0f, Up: 0f, Fast: false);

    /// <summary>A controller already placed at the world origin, facing down +X and level.</summary>
    private static FreeCameraController Placed() =>
        new(NullLogger.Instance) { Origin = (0f, 0f, 0f), Angles = (0f, 0f) };

    /// <summary>Where a controller is, asserting it has been placed at all.</summary>
    private static (float X, float Y, float Z) OriginOf(FreeCameraController camera)
    {
        camera.Origin.ShouldNotBeNull("flight must place the camera, not leave it unplaced");

        return camera.Origin.Value;
    }
}
