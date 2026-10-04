using System;

using FlaUI.Core.Tools;

namespace Tf2DemoSalvage.Viewer3D.UiTests;

/// <summary>
/// A paused demo makes no new animation-event sounds.
/// </summary>
/// <remarks>
/// **`C_BaseAnimating::DoAnimationEvents` fires an event once, on the frame the cycle crosses it**, and returns at
/// once when the cycle has not moved (<c>c_baseanimating.cpp:3550</c>). A paused demo stops the clock, so it crosses
/// nothing. The viewer handed the sound pass the same frame's events again on every frame the time held still, so
/// the shared session — paused at the opening tick — played two players' footsteps about once a frame for the whole
/// run: 27,882 footstep starts at tick 20000 in one UI session's log.
///
/// **The control is that footsteps sounded while the demo opened.** Without it, "no new footsteps" would also hold for
/// a viewer that plays none at all, which is every run without a TF2 install.
/// </remarks>
public sealed class PausedSoundUiTests
{
    /// <summary>The audio log line every footstep start writes.</summary>
    private const string Footstep = "player/footsteps/";

    /// <summary>The once-a-second frame-rate report, which the viewer writes paused or not.</summary>
    private const string RateReport = "frames a second";

    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(30);

    private static ViewerApplication Viewer => ViewerSession.App;

    [Test]
    public void Footsteps_WhilePaused_AreNotPlayedAgain()
    {
        ViewerSession.RequireTheGame();

        Retry.WhileFalse(() => Viewer.Count("opening state applied") > 0, Timeout, ViewerSession.PollInterval, throwOnTimeout: true);
        Retry.WhileFalse(() => Viewer.Count(Footstep) > 0, Timeout, ViewerSession.PollInterval)
            .Success.ShouldBeTrue("no footstep sounded at the opening tick, so an unchanged count below would prove nothing");

        // Two whole reports after the first footstep, so the opening frame's batch has finished arriving.
        AfterTwoReports();
        int heard = Viewer.Count(Footstep);

        // Two more seconds of paused frames.
        AfterTwoReports();

        Viewer.Count(Footstep).ShouldBe(heard, "a paused demo crosses no animation event, so no footstep starts again");
    }

    /// <summary>Waits until the viewer has written two more frame-rate reports.</summary>
    private static void AfterTwoReports()
    {
        int reports = Viewer.Count(RateReport);

        Retry.WhileFalse(() => Viewer.Count(RateReport) >= reports + 2, Timeout, ViewerSession.PollInterval, throwOnTimeout: true);
    }
}
