using System;
using System.Collections.Generic;

using Tf2DemoSalvage.Core.Scene;
using Tf2DemoSalvage.Presentation;

namespace Tf2DemoSalvage.Presentation.Tests;

/// <summary>
/// Playback: what the transport controls mean, and where the demo has got to.
/// </summary>
/// <remarks>
/// **Every rule tested here was written from a bug and none of them had a test**, because the logic
/// lived in `MainForm` entangled with a WinForms control and a real `Stopwatch`. Reaching it needed
/// a form, a message pump and the desktop lock; so it was never reached.
///
/// That is the concrete claim D54 made when MVP was chosen over MVVM, and this file is it being
/// collected: no window, no STA thread, no `run-exclusive.ps1`, and the whole suite runs in
/// milliseconds on any machine including the Linux measurement boxes.
/// </remarks>
public sealed class PlaybackPresenterTests
{
    /// <summary>A clock over a demo with a known length.</summary>
    /// <remarks>
    /// 0.015 s per tick is <c>DEFAULT_TICK_INTERVAL</c> from Valve's <c>const.h</c>, so a second of
    /// real time is 66.67 ticks and the arithmetic below is the engine's rather than invented.
    /// </remarks>
    private static PlaybackClock Clock(int lastTick = 1000) =>
        new(PlaybackClock.DefaultIntervalPerTick, lastTick);

    [Test]
    public void Advance_WhilePaused_DoesNothing()
    {
        // The host calls Advance every frame without asking whether anything is playing, so this is
        // the common case rather than an edge one.
        (PlaybackPresenter presenter, FakePlaybackView view, FakeElapsedTime time) = Wired();

        time.Seconds = 1.0;
        presenter.Advance();

        view.ShownTicks.ShouldBeEmpty();
    }

    [Test]
    public void Advance_WhilePlaying_MovesTheClockByTheElapsedTime()
    {
        (PlaybackPresenter presenter, FakePlaybackView view, FakeElapsedTime time) = Wired();

        view.PressPlayPause(true);
        time.Seconds = 0.015;    // exactly one tick
        presenter.Advance();

        view.LastShownTick.ShouldBe(1);
    }

    [Test]
    public void Advance_AStallLongerThanTheCap_IsClampedRatherThanTeleporting()
    {
        // **A stall is not elapsed playback time.** Loading a map or dragging the window by its
        // title bar stops the loop; handing that whole gap to the clock jumps the demo by however
        // long the hitch was. Ten seconds of stall must advance 100 ms of playback, not ten
        // seconds' worth.
        (PlaybackPresenter presenter, FakePlaybackView view, FakeElapsedTime time) = Wired();

        view.PressPlayPause(true);
        time.Seconds = 10.0;
        presenter.Advance();

        int capped = (int)(PlaybackPresenter.MaximumFrameSeconds / PlaybackClock.DefaultIntervalPerTick);

        view.LastShownTick.ShouldBe(capped, "100 ms at 0.015 s a tick is 6 ticks");
        view.LastShownTick.ShouldBeLessThan(
            100, "and nowhere near the 666 ticks ten seconds would have been");
    }

    [Test]
    public void Advance_ZeroElapsed_DoesNotRestartTheClock()
    {
        // Restarting on a zero-length frame would discard the fraction accumulating toward the next
        // tick, so a fast enough loop would never advance at all — the position would be reset just
        // before it crossed.
        (PlaybackPresenter presenter, FakePlaybackView view, FakeElapsedTime time) = Wired();

        view.PressPlayPause(true);
        int restartsAfterPlay = time.Restarts;

        time.Seconds = 0;
        presenter.Advance();

        time.Restarts.ShouldBe(restartsAfterPlay);
        view.ShownTicks.ShouldBeEmpty();
    }

    [Test]
    public void PlayPause_Starting_RestartsTheElapsedClock()
    {
        // **Real time passed while paused is not playback time.** Without this the first frame
        // after resuming feeds the whole pause into the clock and the demo jumps forward by however
        // long the user spent reading the map.
        (_, FakePlaybackView view, FakeElapsedTime time) = Wired();

        time.Seconds = 45.0;    // the user was paused for three quarters of a minute
        view.PressPlayPause(true);

        time.Restarts.ShouldBe(1);
        time.Seconds.ShouldBe(0, "the pause is discarded rather than carried in");
    }

    [Test]
    public void PlayPause_Stopping_ResetsTheElapsedClock()
    {
        (_, FakePlaybackView view, FakeElapsedTime time) = Wired();

        view.PressPlayPause(true);
        view.PressPlayPause(false);

        time.Resets.ShouldBeGreaterThan(0);
    }

    [Test]
    public void Speed_ChangedWhilePlaying_RestartsSoTheStraddlingFrameIsNotRescaled()
    {
        // The frame in progress was measured at the old speed; counting it at the new one applies a
        // rate to time that never elapsed under it.
        (_, FakePlaybackView view, FakeElapsedTime time) = Wired();

        view.PressPlayPause(true);
        int before = time.Restarts;

        view.ChooseSpeed(2f);

        time.Restarts.ShouldBe(before + 1);
    }

    [Test]
    public void Speed_ChangedWhilePaused_DoesNotRestart()
    {
        // **The control for the test above**, and without it "restarts on speed change" and
        // "restarts on everything" are the same observation. Nothing is elapsing while paused, so
        // there is nothing to discard.
        (_, FakePlaybackView view, FakeElapsedTime time) = Wired();

        int before = time.Restarts;
        view.ChooseSpeed(2f);

        time.Restarts.ShouldBe(before);
    }

    [Test]
    public void Speed_IsAppliedToElapsedTimeRatherThanToTheTickRate()
    {
        // Valve's own replay editor multiplies elapsed by host_timescale
        // (replayperformanceeditor.cpp). Scaling the RATE instead would move the current position
        // the instant the speed changed, because the position is measured in ticks.
        (PlaybackPresenter presenter, FakePlaybackView view, FakeElapsedTime time) = Wired();

        view.PressPlayPause(true);
        view.ChooseSpeed(2f);

        view.ShownTicks.ShouldBeEmpty("changing speed alone must not move the position");

        time.Seconds = 0.015;
        presenter.Advance();

        view.LastShownTick.ShouldBe(2, "one tick of real time at double speed is two ticks");
    }

    [Test]
    public void Advance_ReachingTheEnd_StopsPlaying()
    {
        (PlaybackPresenter presenter, FakePlaybackView view, FakeElapsedTime time) = Wired(lastTick: 10);

        view.PressPlayPause(true);

        for (int frame = 0; frame < 20; frame++)
        {
            time.Seconds = PlaybackPresenter.MaximumFrameSeconds;
            presenter.Advance();
        }

        view.Playing.ShouldBeFalse();
        view.LastShownTick.ShouldBe(10);
    }

    [Test]
    public void Advance_ReachingTheStartInReverse_AlsoStopsPlaying()
    {
        // **Stopping only at the end leaves reverse playback spinning against tick zero**, still
        // claiming to play. The forward case alone cannot catch that, which is why both directions
        // are tested rather than one.
        (PlaybackPresenter presenter, FakePlaybackView view, FakeElapsedTime time) = Wired(lastTick: 100);

        view.Scrub(50);
        view.PressPlayPause(true);
        view.ChooseSpeed(-1f);

        for (int frame = 0; frame < 40; frame++)
        {
            time.Seconds = PlaybackPresenter.MaximumFrameSeconds;
            presenter.Advance();
        }

        view.Playing.ShouldBeFalse();
        view.LastShownTick.ShouldBe(0);
    }

    [Test]
    public void Scrub_SeeksTheClockAndReportsTheMomentWithoutPlaying()
    {
        (PlaybackPresenter presenter, FakePlaybackView view, _) = Wired();

        List<double> moments = [];
        presenter.MomentChanged += (_, e) => moments.Add(e.Position);

        view.Scrub(400);

        moments.ShouldBe([400.0]);
        view.Playing.ShouldBeFalse("scrubbing does not start playback");
    }

    [Test]
    public void MomentChanged_WhilePlaying_CarriesTheFractionNotTheWholeTick()
    {
        // **Truncating here snaps every pose to the last packet** and makes the interpolation layer
        // a no-op that still passes all of its own tests. Half a tick must arrive as 0.5, not 0.
        (PlaybackPresenter presenter, FakePlaybackView view, FakeElapsedTime time) = Wired();

        List<double> moments = [];
        presenter.MomentChanged += (_, e) => moments.Add(e.Position);

        view.PressPlayPause(true);
        time.Seconds = PlaybackClock.DefaultIntervalPerTick / 2;
        presenter.Advance();

        moments.Count.ShouldBe(1);
        moments[0].ShouldBe(0.5, 0.0001);
        ((int)moments[0]).ShouldBe(0, "and the whole tick is still zero, so a truncating reader sees nothing");
    }

    [Test]
    public void Advance_WithNoDemoLoaded_DoesNothing()
    {
        FakePlaybackView view = new();
        FakeElapsedTime time = new();
        PlaybackPresenter presenter = new(view, time);

        presenter.HasDemo.ShouldBeFalse();

        view.PressPlayPause(true);
        time.Seconds = 1.0;
        presenter.Advance();

        view.ShownTicks.ShouldBeEmpty();
    }

    [Test]
    public void Load_ANewDemo_StopsPlaybackAndShowsItsFirstTick()
    {
        (_, FakePlaybackView view, FakeElapsedTime time) = Wired();

        view.PressPlayPause(true);
        view.ShownTicks.Clear();

        PlaybackPresenter presenter = new(view, time);
        presenter.Load(Clock());

        view.Playing.ShouldBeFalse("a freshly loaded demo is not running");
        view.ShownTicks.ShouldBe([0]);
    }

    [Test]
    public void Constructor_ANullArgument_Throws()
    {
        Should.Throw<ArgumentNullException>(() => new PlaybackPresenter(null!, new FakeElapsedTime()));
        Should.Throw<ArgumentNullException>(() => new PlaybackPresenter(new FakePlaybackView(), null!));
    }

    /// <summary>A presenter wired to a fake view and a controllable clock, with a demo loaded.</summary>
    /// <summary>
    /// Setting the view's flag does not start playback; only telling the presenter does.
    /// </summary>
    /// <remarks>
    /// **The regression this pins shipped and the owner lived with it:** *"the ui says its playing
    /// but the demo is not actually playing, no ticks go by, i have to 'pause' which does nothing
    /// then hit play again to get it started"*.
    ///
    /// The cause is a correct decision meeting a caller that did not know about it.
    /// `TransportBar.Playing`'s setter deliberately does NOT raise `PlayPauseToggled`, because the
    /// presenter assigns it when playback reaches an end and a raising setter made the presenter
    /// re-enter its own handler. So assigning the property is not "start playing" — it is "show
    /// that we are playing" — and the elapsed clock, which only `OnPlayPauseToggled` starts, stayed
    /// stopped. `Advance` then returns on a non-positive elapsed for ever.
    ///
    /// **The first half of this test is the state the bug produced** and must keep failing to
    /// advance: a caller that only sets the flag gets nothing, which is the contract the setter's
    /// own remarks describe. The second half is what a caller is supposed to do instead.
    ///
    /// The comment in `MainForm` says this exact fault was found and fixed once before — *"the
    /// button showed playing while no time was fed to a clock that did not exist, and the demo sat
    /// still until the user paused and played again"* — and it came back, because nothing tested it.
    /// The owner: *"idk when it regressed, we dont actually check playback in the ui tests"*.
    /// </remarks>
    [Test]
    public void Play_RatherThanAssigningTheFlag_IsWhatStartsTheClock()
    {
        (PlaybackPresenter presenter, FakePlaybackView view, FakeElapsedTime time) = Wired();

        // What the autoplay path used to do: set the flag, which relabels the button and tells the
        // presenter nothing.
        view.Playing = true;
        time.Seconds = 0.5d;

        presenter.Advance();

        view.LastShownTick.ShouldBe(
            -1,
            "assigning the flag is a display change, so nothing has been fed to the clock");

        // What it must do instead.
        presenter.Play();

        time.Seconds = 0.5d;
        presenter.Advance();

        view.Playing.ShouldBeTrue();
        view.LastShownTick.ShouldBeGreaterThan(0, "half a second of a demo is some ticks");
    }

    /// <summary>Playing a presenter with no demo loaded does nothing rather than throwing.</summary>
    /// <remarks>
    /// The startup path calls this before it is certain a timeline was decoded — a file with no
    /// schema still opens and still has a length — so "no clock" is an ordinary state here rather
    /// than a caller's mistake.
    /// </remarks>
    [Test]
    public void Play_WithNoDemoLoaded_DoesNothing()
    {
        FakePlaybackView view = new();
        FakeElapsedTime time = new();
        PlaybackPresenter presenter = new(view, time);

        Should.NotThrow(presenter.Play);

        view.Playing.ShouldBeFalse("there is nothing to play");
        time.Restarts.ShouldBe(0);
    }

    private static (PlaybackPresenter Presenter, FakePlaybackView View, FakeElapsedTime Time) Wired(
        int lastTick = 1000)
    {
        FakePlaybackView view = new();
        FakeElapsedTime time = new();
        PlaybackPresenter presenter = new(view, time);

        presenter.Load(Clock(lastTick));
        view.ShownTicks.Clear();

        return (presenter, view, time);
    }
}
