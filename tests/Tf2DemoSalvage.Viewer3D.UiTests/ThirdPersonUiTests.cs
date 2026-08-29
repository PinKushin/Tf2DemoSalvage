using System;

using FlaUI.Core.Tools;

namespace Tf2DemoSalvage.Viewer3D.UiTests;

/// <summary>
/// The third-person camera, driven through a real window.
/// </summary>
/// <remarks>
/// **Reached with SPACE rather than with its own binding, and that is a harness limit rather than a
/// choice.** `thirdperson` defaults to `CTRL+b`, and driving a modified shortcut from a UI test is
/// unsolved here — B216 records that neither `TypeSimultaneously` nor an explicit press/release
/// produced a keystroke `ProcessCmdKey` saw as `Keys.Control | Keys.B`, established with a control
/// arm that failed identically at the viewport where no guard is involved.
///
/// The cycle reaches the same mode, so the mode itself is still tested; what is not tested here is
/// that the binding resolves, and that gap is stated rather than papered over.
///
/// **The assertion that matters is the absence of a viewmodel.** Everything argued about for a whole
/// session reduces to one engine rule — `CViewRender::ShouldDrawViewModel` (`viewrender.cpp:974`)
/// refuses whenever `C_BasePlayer::ShouldDrawLocalPlayer()` is true, which is exactly "the view is
/// third person". A chase camera that still drew the weapon would be the same class of defect as the
/// one this branch exists to fix, arriving from the opposite direction.
/// </remarks>
public sealed class ThirdPersonUiTests
{
    private static ViewerApplication Viewer => ViewerSession.App;

    private const string ThirdPerson = "third person on, chasing";

    private const string BackToFree = "back to the free camera";

    [Test]
    public void SwitchCameraMode_PressedTwiceFromFree_ReachesThirdPerson()
    {
        ViewerSession.RequireTheGame();

        int before = Viewer.Count(ThirdPerson);

        // Free -> first person -> third person. The cycle is Valve's: a spectator's mode runs
        // through in-eye, chase and roaming.
        Reach();

        Retry.WhileFalse(
            () => Viewer.Count(ThirdPerson) > before,
            TimeSpan.FromSeconds(10),
            throwOnTimeout: true,
            timeoutMessage: "Cycling the camera mode twice did not reach third person.");

        Viewer.Count(ThirdPerson).ShouldBeGreaterThan(before);
    }

    [Test]
    public void ThirdPerson_WhileChasing_DrawsNoViewmodel()
    {
        ViewerSession.RequireTheGame();

        Reach();

        Retry.WhileFalse(
            () => Viewer.Count(ThirdPerson) > 0,
            TimeSpan.FromSeconds(10),
            throwOnTimeout: true,
            timeoutMessage: "Never reached third person, so the viewmodel claim is untested.");

        // **Counted AFTER arriving, so a viewmodel drawn during the first-person leg of the cycle
        // cannot be mistaken for one drawn here.** The cycle passes through first person, where a
        // viewmodel is correct — sampling from before the switch would measure that instead.
        int drewOnArrival = Viewer.Count("viewmodel pass: drawing");

        // Long enough for many frames to pass. A single frame proves nothing: the pass runs every
        // frame, so the claim is that it keeps refusing, not that it refused once.
        Retry.WhileTrue(
            () => Viewer.Count("viewmodel pass: drawing") == drewOnArrival,
            TimeSpan.FromSeconds(2),
            throwOnTimeout: false);

        Viewer.Count("viewmodel pass: drawing").ShouldBe(
            drewOnArrival,
            "no viewmodel may be drawn in third person: ShouldDrawViewModel refuses whenever " +
            "ShouldDrawLocalPlayer() is true, and that is exactly this view");
    }

    /// <summary>Cycles from wherever the camera is to third person.</summary>
    /// <remarks>
    /// **Returns to Free first, so the number of presses is knowable.** A test that pressed twice
    /// from an unknown mode would land somewhere different depending on what ran before it, which is
    /// the ordering dependence that makes a suite pass alone and fail together.
    /// </remarks>
    private static void Reach()
    {
        for (int press = 0; press < 3; press++)
        {
            int free = Viewer.Count(BackToFree);

            ViewerSession.PressSwitchCameraMode();

            if (Retry.WhileFalse(
                    () => Viewer.Count(BackToFree) > free,
                    TimeSpan.FromSeconds(3),
                    throwOnTimeout: false).Success)
            {
                break;
            }
        }

        ViewerSession.PressSwitchCameraMode();
        ViewerSession.PressSwitchCameraMode();
    }
}
