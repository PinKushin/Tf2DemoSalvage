using Microsoft.Extensions.Logging.Abstractions;

namespace Tf2DemoSalvage.Presentation.Tests;

/// <summary>Turning the free camera, and reading a viewpoint out of the environment.</summary>
/// <remarks>
/// **These tests exist because the ones that looked like them tested nothing that ran** (B206).
/// `FreeLookState` carried a `Drag` with eleven tests and no production caller; the drag the viewer
/// actually performed was written out longhand inside `MainForm.OnViewportMouseMove` and had none.
///
/// The drag cases below are ported from that file — the reasoning was sound, it was simply pointed
/// at the wrong object.
/// </remarks>
public sealed class FreeCameraControllerTests
{
    [Test]
    public void Drag_Downward_PitchesDown()
    {
        // Positive pitch is DOWNWARD in Source, and dragging the mouse down looks down — so the two
        // agree in sign. Getting this pair wrong inverts the vertical axis, which is the first thing
        // anyone notices and the last thing a test usually checks.
        FreeCameraController camera = Camera();
        float before = camera.Angles.Pitch;

        camera.Drag(deltaX: 0f, deltaY: 100f);

        camera.Angles.Pitch.ShouldBe(
            before + (100f * FreeCameraController.DegreesPerPixel), 0.001);
    }

    [Test]
    public void Drag_Rightward_TurnsLeft()
    {
        // **The world moves with the pointer**, as though the mouse were dragging the scene rather
        // than the head — the convention every first-person game uses. Inverting it is the single
        // most noticeable thing a camera can get wrong.
        FreeCameraController camera = Camera();
        float before = camera.Angles.Yaw;

        camera.Drag(deltaX: 100f, deltaY: 0f);

        camera.Angles.Yaw.ShouldBe(
            before - (100f * FreeCameraController.DegreesPerPixel), 0.001);
    }

    [Test]
    public void Drag_FarBeyondVertical_ClampsPitchAtTheEngineLimit()
    {
        // **±89, matching what the engine clamps a player to.** At exactly 90 the forward vector is
        // parallel to the world's up axis and the basis has no right vector — and this project has
        // already paid for that once: the flight guard tested a cancelled vector for exactly zero,
        // and at pitch 90 the residue from cos(90°) normalised up to full speed (D65).
        FreeCameraController camera = Camera();

        camera.Drag(0f, 100_000f);
        camera.Angles.Pitch.ShouldBe(FreeCameraController.PitchLimit);

        camera.Drag(0f, -1_000_000f);
        camera.Angles.Pitch.ShouldBe(-FreeCameraController.PitchLimit);
    }

    [Test]
    public void Drag_Yaw_IsNotClamped()
    {
        // **The control for the clamp above**, and the asymmetry is deliberate rather than an
        // oversight. Yaw wraps around and every value is a legal heading, so bounding it would
        // introduce a discontinuity where the geometry has none — a camera that stops turning after
        // enough drags in one direction.
        FreeCameraController camera = Camera();
        float before = camera.Angles.Yaw;

        for (int drag = 0; drag < 40; drag++)
        {
            camera.Drag(deltaX: 100f, deltaY: 0f);
        }

        camera.Angles.Yaw.ShouldBe(before - 1000f, 0.01, "forty drags of 25 degrees, unbounded");
    }

    [Test]
    public void Parse_APitchBeyondVertical_ClampsItRatherThanTakingItLiterally()
    {
        // **The defect D65 fixed, pinned on the type that actually runs.** `TF2VIEW_CAMERA` exists
        // so a viewpoint can be copied out of the game's own `ang` readout, and 90 is an ordinary
        // thing to copy. The original path applied it unclamped, putting the camera in exactly the
        // state the drag had always been careful to avoid.
        //
        // This assertion previously existed only against `FreeLookState.PlaceAt`, which nothing
        // called — so D65's guard was, in test terms, unwatched on the live path.
        ((float X, float Y, float Z) Origin, float Pitch, float Yaw)? parsed =
            FreeCameraController.Parse("10 20 30 90 45");

        parsed.ShouldNotBeNull();
        parsed.Value.Pitch.ShouldBe(FreeCameraController.PitchLimit);
        parsed.Value.Yaw.ShouldBe(45f, "yaw is untouched");
        parsed.Value.Origin.ShouldBe((10f, 20f, 30f));
    }

    private static FreeCameraController Camera() => new(NullLogger.Instance);
}
