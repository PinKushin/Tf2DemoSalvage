using Microsoft.Extensions.Logging.Abstractions;

using Tf2DemoSalvage.Content.Bsp;
using Tf2DemoSalvage.Scene;

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

    [Test]
    public void Dolly_Forward_MovesAlongTheLookDirection()
    {
        // At yaw and pitch zero the camera faces +X, so one notch forward is one notch of +X and
        // nothing else. Any other axis moving means the basis and the travel disagree.
        FreeCameraController camera = Camera();
        camera.Angles = (0f, 0f);
        camera.Origin = (0f, 0f, 0f);

        camera.Dolly(forward: true, ifUnplaced: (0f, 0f, 0f));

        camera.Origin.GetValueOrDefault().X.ShouldBe(FreeCameraController.WheelTravel, 0.01);
        camera.Origin.GetValueOrDefault().Y.ShouldBe(0f, 0.01);
        camera.Origin.GetValueOrDefault().Z.ShouldBe(0f, 0.01);
    }

    [Test]
    public void Dolly_Backward_MovesTheOtherWay()
    {
        // **The control on the direction flag.** Without it, `forward` could be ignored and the
        // forward case would still pass — correct and broken predicting the same observation.
        FreeCameraController camera = Camera();
        camera.Angles = (0f, 0f);
        camera.Origin = (0f, 0f, 0f);

        camera.Dolly(forward: false, ifUnplaced: (0f, 0f, 0f));

        camera.Origin.GetValueOrDefault().X.ShouldBe(-FreeCameraController.WheelTravel, 0.01);
    }

    [Test]
    public void Dolly_LookingDown_DescendsRatherThanTravellingFlat()
    {
        // Pitch has to reach the travel, or the wheel becomes a flat pan that ignores where the
        // camera is pointed — which looks like the camera refusing to go down through a map.
        FreeCameraController camera = Camera();
        camera.Angles = (90f, 0f);
        camera.Origin = (0f, 0f, 0f);

        camera.Dolly(forward: true, ifUnplaced: (0f, 0f, 0f));

        camera.Origin.GetValueOrDefault().Z.ShouldBeLessThan(-1f, "positive pitch looks DOWN");
    }

    [Test]
    public void Dolly_BeforeTheCameraIsPlaced_StartsFromTheFallback()
    {
        // Same contract as Fly: the camera is placed on first use, so a wheel before that must
        // travel from where the view currently is rather than from the corner of the map.
        FreeCameraController camera = Camera();
        camera.Angles = (0f, 0f);

        camera.Dolly(forward: true, ifUnplaced: (100f, 200f, 300f));

        camera.Origin.GetValueOrDefault().X.ShouldBe(100f + FreeCameraController.WheelTravel, 0.01);
        camera.Origin.GetValueOrDefault().Y.ShouldBe(200f, 0.01);
    }

    // **`--look` and `--zoom` reaching the placement, which is the half `Framed`'s own tests cannot
    // see** (B226). Those pin the arithmetic; these pin that `PlaceOverhead` actually calls it. The
    // options were parsed and consumed by nothing at all before 2026-08-29, and that gap was
    // exactly this shape: every piece present, nothing joined up.

    [Test]
    public void Camera_WithNoLookAtOrZoom_FramesTheMapAsBefore()
    {
        // **The control, and the one that protects every existing launch.** A wiring change that
        // reshaped the default view would alter the opening frame of every demo, and no other test
        // in this file looks at where the overhead placement lands.
        FreeCameraController camera = Camera();

        FreeCamera placed = camera.Camera(aspect: 16f / 9f, map: Square(1000f), highest: 0f);

        placed.Origin.X.ShouldBe(0f, 0.01);
        placed.Origin.Y.ShouldBe(0f, 0.01);
    }

    [Test]
    public void Camera_WithALookAt_PlacesItOverThatPointInstead()
    {
        FreeCameraController camera = Camera();

        camera.LookAt = (500f, 200f);

        FreeCamera placed = camera.Camera(aspect: 16f / 9f, map: Square(1000f), highest: 0f);

        placed.Origin.X.ShouldBe(500f, 0.01);
        placed.Origin.Y.ShouldBe(200f, 0.01);
    }

    [Test]
    public void Camera_WithZoomTwo_SitsHalfAsFarAbove()
    {
        // **An exact ratio rather than "lower".** The framing distance is linear in the extent
        // framed, so halving the extent halves the height — a prediction, where "closer than
        // before" would pass against any factor at all including a wrong one.
        //
        // **A big map, and the first version used one a quarter the size and failed at 512 against
        // a predicted 500.** That was not the fix being wrong: `ClearanceAboveGeometry` is 512 and
        // the ZOOMED camera had dropped below it, so the clamp — correctly — held it up. The
        // condition was simply too small for the effect to be visible, which is the "effect size
        // below resolution" case from the testing standards. The answer is a larger map, never a
        // looser assertion.
        FreeCamera wide = Camera().Camera(aspect: 16f / 9f, map: Square(4000f), highest: 0f);

        FreeCameraController zoomed = Camera();

        zoomed.Zoom = 2f;

        FreeCamera close = zoomed.Camera(aspect: 16f / 9f, map: Square(4000f), highest: 0f);

        // **BOTH must clear the floor, not just the wide one.** Checking only `wide` is what let
        // the clamp silently decide the answer above, and a precondition that cannot catch the
        // thing that actually happened is not a precondition.
        wide.Origin.Z.ShouldBeGreaterThan(OverheadPlacement.ClearanceAboveGeometry);
        close.Origin.Z.ShouldBeGreaterThan(OverheadPlacement.ClearanceAboveGeometry);
        close.Origin.Z.ShouldBe(wide.Origin.Z / 2f, 0.01);
    }

    /// <summary>A square map centred on the world origin, out to the given half-extent.</summary>
    private static MapOutline Square(float half) => MapOutline.FromFaces(
    [
        new BspFace(
            [(-half, -half, 0f), (half, -half, 0f), (half, half, 0f), (-half, half, 0f)],
            (0f, 0f, 1f),
            SurfaceProperties.None),
    ]);

    private static FreeCameraController Camera() => new(NullLogger.Instance);
}
