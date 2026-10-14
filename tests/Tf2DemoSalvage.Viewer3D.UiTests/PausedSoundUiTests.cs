using System;
using System.Globalization;

using FlaUI.Core.Tools;

namespace Tf2DemoSalvage.Viewer3D.UiTests;

/// <summary>
/// A paused demo makes no new animation-event sounds.
/// </summary>
/// <remarks>
/// **`C_BaseAnimating::Simulate` walks animation events only while time moves** — `if ( gpGlobals->frametime != 0.0f )
/// DoAnimationEvents(...)` (<c>c_baseanimating.cpp:5162</c>) — and `DoAnimationEvents` fires an event once, on the
/// frame the cycle crosses it (<c>:3550</c>). The viewer once handed the sound pass the same frame's events again on
/// every frame the time held still: 27,882 footstep starts at one paused tick in one UI session's log.
///
/// **The control is footsteps heard while the demo PLAYS, and it used to be footsteps at the paused opening tick.**
/// That control was itself the divergence (B541): the engine fires none on a paused frame, so the shared session,
/// which never plays, now hears none and "no new footsteps" there would hold for a viewer that plays none at all.
///
/// **Its own viewer, never <see cref="ViewerSession"/>'s**, for <see cref="PlaybackUiTests"/>' reason: playing moves
/// the demo off the opening tick every other fixture assumes. **At 8x**, so the last frame before the pause crosses
/// several footsteps rather than perhaps none — it is that frame's events the original defect replays.
/// </remarks>
public sealed class PausedSoundUiTests
{
    /// <summary>The line every footstep that sounds writes.</summary>
    private const string Footstep = "player/footsteps/";

    /// <summary>The once-a-second frame-rate report, which the viewer writes paused or not.</summary>
    private const string RateReport = "frames a second";

    /// <summary>What that report says of a second that ended paused.</summary>
    private const string PausedReport = ", paused;";

    private const string Speed = "8";

    /// <summary>A hang bound: z1800 loads in about 100 s here, and longer on a CI runner.</summary>
    private static readonly TimeSpan Loading = TimeSpan.FromSeconds(300);

    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(30);

    [Test]
    public void Footsteps_HeardWhilePlaying_StopWhenTheDemoIsPaused()
    {
        ViewerSession.RequireTheGame();

        if (!System.IO.File.Exists(ViewerSession.DemoPath))
        {
            Assert.Ignore($"The corpus demo is not present at {ViewerSession.DemoPath}.");
            return;
        }

        using ViewerApplication viewer = ViewerApplication.Launch(
            ViewerSession.DemoPath,
            "--tick", ViewerSession.OpeningTick.ToString(CultureInfo.InvariantCulture),
            "--autoplay",
            "+demo_timescale", Speed);

        Retry.WhileFalse(() => viewer.Count("opening state applied") > 0, Loading, ViewerSession.PollInterval, throwOnTimeout: true);
        Retry.WhileFalse(() => viewer.Count(Footstep) > 0, Timeout, ViewerSession.PollInterval)
            .Success.ShouldBeTrue("no footstep sounded while the demo played, so an unchanged count below would prove nothing");

        // Posted to the button, so it needs neither the foreground nor the mouse (D215).
        viewer.Activate(TransportBar.PlayButtonId);

        Retry.WhileFalse(
            () => viewer.LastLine(RateReport)?.Contains(PausedReport, StringComparison.Ordinal) == true,
            Timeout,
            ViewerSession.PollInterval,
            throwOnTimeout: true);

        // Two whole reports after the one that saw the pause, so the last playing frame's batch has finished arriving.
        AfterTwoReports(viewer);
        int heard = viewer.Count(Footstep);

        // Two more seconds of paused frames.
        AfterTwoReports(viewer);

        viewer.Count(Footstep).ShouldBe(heard, "a paused demo walks no animation event, so no footstep starts");
    }

    /// <summary>Waits until the viewer has written two more frame-rate reports.</summary>
    private static void AfterTwoReports(ViewerApplication viewer)
    {
        int reports = viewer.Count(RateReport);

        Retry.WhileFalse(() => viewer.Count(RateReport) >= reports + 2, Timeout, ViewerSession.PollInterval, throwOnTimeout: true);
    }
}
