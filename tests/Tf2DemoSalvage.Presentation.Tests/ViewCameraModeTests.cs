using Tf2DemoSalvage.Presentation;
using Tf2DemoSalvage.Scene;

namespace Tf2DemoSalvage.Presentation.Tests;

/// <summary>
/// <see cref="ViewCamera.Active(CameraMode, FreeCamera?, FreeCamera?, FreeCamera)"/> picks one camera.
/// </summary>
/// <remarks>
/// **One answer given once is the whole point of this type.** Its own remarks record why: a frustum
/// built from the free camera while the picture is drawn through a player's eyes culls the geometry
/// the viewer is looking at, and the symptom reads as a culling bug rather than as two cameras.
/// Adding a third mode is exactly the change that could reintroduce that, so each mode is pinned.
///
/// **Every mode falls back to the free camera** (D98). First person on a demo with no recorded eye,
/// and chase with nobody to chase, are ordinary states rather than faults — so the fallbacks are
/// tested as behaviour, not as error handling.
/// </remarks>
public sealed class ViewCameraModeTests
{
    private static FreeCamera At(float x) => new() { Origin = (x, 0f, 0f), Aspect = 1.6f };

    [Test]
    public void Active_InFirstPerson_IsTheEyeCamera()
    {
        ViewCamera.Active(CameraMode.FirstPerson, At(1f), At(2f), At(3f))
            .Origin.X.ShouldBe(1f);
    }

    [Test]
    public void Active_InThirdPerson_IsTheChaseCamera()
    {
        ViewCamera.Active(CameraMode.ThirdPerson, At(1f), At(2f), At(3f))
            .Origin.X.ShouldBe(2f);
    }

    [Test]
    public void Active_InFreeMode_IsTheFreeCameraEvenWhenTheOthersExist()
    {
        // **The control that stops a mode being ignored.** With all three supplied, a selector that
        // simply preferred whichever camera was non-null would pass the two above and fail here.
        ViewCamera.Active(CameraMode.Free, At(1f), At(2f), At(3f))
            .Origin.X.ShouldBe(3f);
    }

    [Test]
    public void Active_InFirstPersonWithNoEye_FallsBackToFree()
    {
        // A demo with no recorded camera and nobody worth following: ordinary, not a fault.
        ViewCamera.Active(CameraMode.FirstPerson, null, At(2f), At(3f))
            .Origin.X.ShouldBe(3f);
    }

    [Test]
    public void Active_InThirdPersonWithNothingToChase_FallsBackToFree()
    {
        ViewCamera.Active(CameraMode.ThirdPerson, At(1f), null, At(3f))
            .Origin.X.ShouldBe(3f);
    }

    [Test]
    public void Active_InFirstPersonWithOnlyAChaseCamera_DoesNotBorrowIt()
    {
        // **The mode is not a hint.** Falling through to the chase camera because it happens to
        // exist would put the viewer in third person while it believes it is in first — and the
        // viewmodel rule keys off exactly that belief, so the weapon would draw over a chase view.
        ViewCamera.Active(CameraMode.FirstPerson, null, At(2f), At(3f))
            .Origin.X.ShouldBe(3f);
    }
}
