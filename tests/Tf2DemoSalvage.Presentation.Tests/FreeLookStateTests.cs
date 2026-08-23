using System;

using Tf2DemoSalvage.Presentation;

namespace Tf2DemoSalvage.Presentation.Tests;

/// <summary>
/// The free camera's orientation and position, and what the mouse and keyboard do to them.
/// </summary>
/// <remarks>
/// Three fields and two event handlers in `MainForm` until D66. Pure state and arithmetic, so the
/// only thing that ever made it untestable was the company it kept.
/// </remarks>
public sealed class FreeLookStateTests
{
    [Test]
    public void Drag_Downward_PitchesDown()
    {
        // Positive pitch is DOWNWARD in Source, and dragging the mouse down looks down — so the two
        // agree in sign. Getting this pair wrong inverts the vertical axis, which is the first thing
        // anyone notices and the last thing a test usually checks.
        FreeLookState state = new();
        float before = state.Angles.Pitch;

        state.Drag(deltaX: 0f, deltaY: 100f);

        state.Angles.Pitch.ShouldBe(before + (100f * FreeLookState.DegreesPerPixel), 0.001);
    }

    [Test]
    public void Drag_Rightward_TurnsLeft()
    {
        // **The world moves with the pointer**, as though the mouse were dragging the scene rather
        // than the head — the convention every first-person game uses. Inverting it is the single
        // most noticeable thing a camera can get wrong.
        FreeLookState state = new();

        state.Drag(deltaX: 100f, deltaY: 0f);

        state.Angles.Yaw.ShouldBe(-100f * FreeLookState.DegreesPerPixel, 0.001);
    }

    [Test]
    public void Drag_FarBeyondVertical_ClampsPitchAtTheEngineLimit()
    {
        // **±89, matching what the engine clamps a player to.** At exactly 90 the forward vector is
        // parallel to the world's up axis and the basis has no right vector — and this project has
        // already paid for that once: the flight guard tested a cancelled vector for exactly zero,
        // and at pitch 90 the residue from cos(90°) normalised up to full speed (D65).
        FreeLookState state = new();

        state.Drag(0f, 100_000f);
        state.Angles.Pitch.ShouldBe(FreeLookState.PitchLimit);

        state.Drag(0f, -1_000_000f);
        state.Angles.Pitch.ShouldBe(-FreeLookState.PitchLimit);
    }

    [Test]
    public void Drag_Yaw_IsNotClamped()
    {
        // **The control for the clamp above**, and the asymmetry is deliberate rather than an
        // oversight. Yaw wraps around and every value is a legal heading, so bounding it would
        // introduce a discontinuity where the geometry has none — a camera that stops turning after
        // enough drags in one direction.
        FreeLookState state = new();

        for (int drag = 0; drag < 40; drag++)
        {
            state.Drag(deltaX: 100f, deltaY: 0f);
        }

        state.Angles.Yaw.ShouldBe(-1000f, 0.01, "forty drags of 25 degrees, unbounded");
    }

    [Test]
    public void PlaceAt_APitchBeyondVertical_IsClampedToo()
    {
        // **The defect D65 fixed, pinned.** TF2DEMOSALVAGE_CAMERA exists so a viewpoint can be
        // copied out of the game's own `ang` readout, and 90 is an ordinary thing to copy. The
        // original path applied it unclamped, putting the camera in exactly the state the drag had
        // always been careful to avoid.
        FreeLookState state = new();

        state.PlaceAt((10f, 20f, 30f), pitch: 90f, yaw: 45f);

        state.Angles.Pitch.ShouldBe(FreeLookState.PitchLimit, "clamped, not taken literally");
        state.Angles.Yaw.ShouldBe(45f, "yaw is untouched");
        state.Origin.ShouldBe((10f, 20f, 30f));
    }

    [Test]
    public void Fly_BeforeTheCameraIsPlaced_DoesNothing()
    {
        // **Placing on first use is the design, so flying before it must not define the origin.**
        // Treating "not placed yet" as (0,0,0) would drop the camera in the corner of the map, and
        // it would look like the camera having been moved rather than never positioned.
        FreeLookState state = new();

        state.IsPlaced.ShouldBeFalse();
        state.Fly(new FlightInput(1f, 0f, 0f, false), 0.5).ShouldBeFalse();
        state.Origin.ShouldBeNull();
    }

    [Test]
    public void Fly_Forward_MovesAlongTheLookDirection()
    {
        FreeLookState state = new();
        state.PlaceAt((0f, 0f, 0f), pitch: 0f, yaw: 0f);

        state.Fly(new FlightInput(1f, 0f, 0f, false), 0.5).ShouldBeTrue();

        state.Origin.ShouldNotBeNull();
        // Half a second at 600 units a second, facing +X.
        state.Origin.GetValueOrDefault().X.ShouldBe(300f, 0.1);
        state.Origin.GetValueOrDefault().Y.ShouldBe(0f, 0.1);
    }

    [Test]
    public void Fly_Accumulates_AcrossFrames()
    {
        // Position is integrated rather than recomputed, so two frames must travel twice as far.
        // A presenter that recomputed from a base each frame would hold still, which reads as the
        // keys not registering.
        FreeLookState state = new();
        state.PlaceAt((0f, 0f, 0f), 0f, 0f);

        state.Fly(new FlightInput(1f, 0f, 0f, false), 0.5);
        state.Fly(new FlightInput(1f, 0f, 0f, false), 0.5);

        state.Origin.GetValueOrDefault().X.ShouldBe(600f, 0.1);
    }

    [Test]
    public void Fly_AnIdleFrame_ReportsNoChangeSoTheHostCanSkipTheRedraw()
    {
        // The keys are up most of the time, so this is the common case. Repainting for it is the
        // mistake that made the transport buttons sluggish once already — paint messages queued
        // faster than the pump could drain them.
        FreeLookState state = new();
        state.PlaceAt((5f, 5f, 5f), 0f, 0f);

        state.Fly(FlightInput.None, 0.5).ShouldBeFalse();
        state.Origin.ShouldBe((5f, 5f, 5f));
    }

    [Test]
    public void Fly_AtTheClampedPitchWithForwardAndUp_DoesNotJump()
    {
        // **The two halves of D65 together.** The clamp keeps pitch off 90, and the epsilon guard
        // catches any residue that survives — so forward-and-up at the steepest legal angle moves
        // sanely rather than flinging the camera sideways at full speed.
        FreeLookState state = new();
        state.PlaceAt((0f, 0f, 0f), pitch: 1000f, yaw: 0f);

        state.Angles.Pitch.ShouldBe(FreeLookState.PitchLimit);

        state.Fly(new FlightInput(Forward: 1f, Right: 0f, Up: 1f, Fast: false), 0.5);

        (float x, float y, float z) = state.Origin.GetValueOrDefault();

        float distance = MathF.Sqrt((x * x) + (y * y) + (z * z));

        float.IsNaN(distance).ShouldBeFalse();
        distance.ShouldBeLessThanOrEqualTo(301f, "one frame of travel, not a jump");
    }

    [Test]
    public void Unplace_ForgetsThePosition_SoANewMapPlacesAfresh()
    {
        // The previous position is somewhere in a world that no longer exists; keeping it drops the
        // camera outside the new map.
        FreeLookState state = new();
        state.PlaceAt((100f, 200f, 300f), 10f, 20f);

        state.Unplace();

        state.IsPlaced.ShouldBeFalse();
        state.Angles.Pitch.ShouldBe(10f, "but where it was LOOKING is still meaningful");
    }
}
