using Tf2DemoSalvage.Core.Container;
using Tf2DemoSalvage.Core.Scene;
using Tf2DemoSalvage.Viewer3D;

namespace Tf2DemoSalvage.Viewer3D.Tests;

/// <summary>
/// Placing the camera where the recorder's eyes were.
/// </summary>
/// <remarks>
/// **The demo gives the feet and the angles; the eye height is this project's to add.** Both halves
/// were established before any of this was written — the recorded view is <c>GetAbsOrigin()</c>
/// (measured across the corpus, <c>docs/findings/01-container.md</c>) and the offset is per class
/// (<c>tf_gamerules.cpp:1326</c>, transcribed in <see cref="PlayerEye"/>).
///
/// What is left is arithmetic, and it is worth testing separately from the rendering because it is
/// the half that can be checked without looking at anything. Whether the resulting picture is
/// RIGHT is a question for a person with the viewer open; whether the camera is 68 units above the
/// player rather than 68 units north of them is not.
/// </remarks>
public sealed class PointOfViewCameraTests
{
    /// <summary>A soldier: 68 units, per Valve's table.</summary>
    private const int Soldier = 3;

    /// <summary>A scout: 65, the shortest in the roster.</summary>
    private const int Scout = 1;

    [Test]
    public void AtEye_TheCamera_SitsTheClassesEyeHeightAboveTheRecordedOrigin()
    {
        // Straight up in Z and nowhere else. A camera offset along the wrong axis is the mistake
        // that looks like a camera standing beside the player rather than inside them, and on a
        // top-down map it is nearly invisible.
        FreeCamera camera = FreeCamera.AtEye(
            new RecordedView((100f, -200f, 300f), (10f, 20f, 0f), IsCut: false),
            Soldier,
            ducking: false,
            alive: true,
            aspect: 16f / 9f);

        camera.Origin.X.ShouldBe(100f, 0.001f);
        camera.Origin.Y.ShouldBe(-200f, 0.001f);
        camera.Origin.Z.ShouldBe(368f, 0.001f);
    }

    [Test]
    public void AtEye_TwoClasses_SitAtDifferentHeightsFromTheSameOrigin()
    {
        // **The control that says the class is actually consulted.** A camera that added a
        // constant would satisfy the test above; only a comparison between classes catches it,
        // and scout-to-soldier is three units — small on screen and exactly the kind of thing
        // nobody would find by looking.
        RecordedView view = new((0f, 0f, 0f), (0f, 0f, 0f), IsCut: false);

        FreeCamera scout = FreeCamera.AtEye(view, Scout, false, true, 1f);
        FreeCamera soldier = FreeCamera.AtEye(view, Soldier, false, true, 1f);

        scout.Origin.Z.ShouldBe(65f, 0.001f);
        soldier.Origin.Z.ShouldBe(68f, 0.001f);
    }

    [Test]
    public void AtEye_ADuckedPlayer_DropsToTheDuckHeightWhateverTheirClass()
    {
        // VEC_DUCK_VIEW is flat across the roster, so a crouched scout and a crouched sniper share
        // a height. Both are asserted, because "flat" is the claim.
        RecordedView view = new((0f, 0f, 0f), (0f, 0f, 0f), IsCut: false);

        FreeCamera.AtEye(view, Scout, ducking: true, alive: true, aspect: 1f)
            .Origin.Z.ShouldBe(45f, 0.001f);

        FreeCamera.AtEye(view, Soldier, ducking: true, alive: true, aspect: 1f)
            .Origin.Z.ShouldBe(45f, 0.001f);
    }

    [Test]
    public void AtEye_ADeadPlayer_DropsToTheGroundLevelViewHeight()
    {
        // VEC_DEAD_VIEWHEIGHT. Death beats ducking: a player who died crouched is dead, not
        // crouched, and the engine's own view drops to the floor either way.
        RecordedView view = new((0f, 0f, 0f), (0f, 0f, 0f), IsCut: false);

        FreeCamera.AtEye(view, Soldier, ducking: true, alive: false, aspect: 1f)
            .Origin.Z.ShouldBe(14f, 0.001f);
    }

    [Test]
    public void AtEye_TheAngles_AreTheRecordedOnesUnchanged()
    {
        // **The recorded angles are the answer, not a starting point.** They are what the recorder
        // was actually looking at, already clamped by the engine that wrote them, so anything this
        // code does to them is a change to the recording.
        FreeCamera camera = FreeCamera.AtEye(
            new RecordedView((0f, 0f, 0f), (-12.5f, 175.25f, 0f), IsCut: false),
            Soldier,
            false,
            true,
            1f);

        camera.Angles.Pitch.ShouldBe(-12.5f, 0.001f);
        camera.Angles.Yaw.ShouldBe(175.25f, 0.001f);
        camera.Angles.Roll.ShouldBe(0f, 0.001f);
    }

    [Test]
    public void AtEye_TheAspect_IsTheViewportsRatherThanTheDefault()
    {
        // The camera's default aspect is 16:9 and a window is whatever the user dragged it to. A
        // factory that dropped the argument would stretch the picture in a way that looks like a
        // projection bug rather than a plumbing one.
        FreeCamera.AtEye(
            new RecordedView((0f, 0f, 0f), (0f, 0f, 0f), IsCut: false),
            Soldier,
            false,
            true,
            aspect: 4f / 3f)
            .Aspect.ShouldBe(4f / 3f, 0.001f);
    }
}
