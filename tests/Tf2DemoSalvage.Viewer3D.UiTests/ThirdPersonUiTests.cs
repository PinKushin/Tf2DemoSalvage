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

        // **Taken BEFORE arriving, because arriving is the event being waited for** (B266). The
        // viewmodel pass reports a CHANGE the moment it happens and throttles only an unchanged
        // repeat, so entering third person writes the skip line at once. Sampling the baseline
        // after arrival consumed that line and left the test waiting a full throttle interval for
        // the next one — a second, for evidence that had already been written.
        //
        // The cycle passes through first person, where the pass DRAWS, so no skip line can arrive
        // between here and third person: the one this waits for is the one arrival produced.
        int skips = Viewer.Count("viewmodel pass skipped");

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

        // **Synchronised on frames HAPPENING, not on a count failing to change.** The claim is that
        // the pass keeps refusing, so it needs several frames to have gone by — and the viewer says
        // when they have: the skip line is written every time the pass declines. Waiting for the
        // drawing count to stay put would be a negative retry, which cannot succeed early and
        // therefore always costs its whole timeout.
        // One skip line is the evidence the assertion needs: the pass ran after the camera reached
        // third person, and declined. Its baseline was taken before `Reach` — see above.
        Retry.WhileFalse(
            () => Viewer.Count("viewmodel pass skipped") > skips,
            TimeSpan.FromSeconds(5),
            ViewerSession.PollInterval,
            throwOnTimeout: true,
            timeoutMessage: "The viewmodel pass never reported skipping, so nothing was observed.");

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
        // Against a baseline for the same reason the teardown in `FirstPersonUiTests` is: these
        // counts accumulate for the life of the shared viewer.
        int free = Viewer.Count(BackToFree);

        for (int press = 0; press < 3 && Viewer.Count(BackToFree) == free; press++)
        {
            Switch();
        }

        Switch();
        Switch();
    }

    /// <summary>Presses the camera-mode key and waits for the viewer to say it moved.</summary>
    /// <remarks>
    /// **Waits on ANY mode transition, never on a particular one, and that is not a detail.** Every
    /// press logs exactly one of three lines, so their sum always rises — a condition that arrives
    /// in milliseconds. Waiting for a SPECIFIC line instead means two presses in three are waiting
    /// for something that will not happen, and each burns the whole timeout: a negative retry is a
    /// sleep wearing a synchronisation's clothes
    /// (`docs/memory/a-negative-retry-is-a-sleep.md`). That mistake cost this suite about forty
    /// seconds a run and the owner noticed it before the tests did.
    /// </remarks>
    private static void Switch()
    {
        int before = Transitions();

        ViewerSession.PressSwitchCameraMode();

        Retry.WhileFalse(
            () => Transitions() > before,
            TimeSpan.FromSeconds(5),
            ViewerSession.PollInterval,
            throwOnTimeout: true,
            timeoutMessage: "Pressing the camera-mode key logged no mode change at all.");
    }

    /// <summary>How many camera-mode changes the viewer has announced.</summary>
    private static int Transitions() =>
        Viewer.Count(FirstPerson) + Viewer.Count(ThirdPerson) + Viewer.Count(BackToFree);

    private const string FirstPerson = "first person on,";
}
