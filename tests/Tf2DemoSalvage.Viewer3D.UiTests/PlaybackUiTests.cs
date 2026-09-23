using System;
using System.Linq;

using FlaUI.Core.AutomationElements;
using FlaUI.Core.Tools;

using Tf2DemoSalvage.Presentation;

namespace Tf2DemoSalvage.Viewer3D.UiTests;

/// <summary>The demo PLAYS in the shared session, at 8x on a real match (B408, B418, D189).</summary>
/// <remarks>
/// **It puts the shared viewer back at the session's opening tick** (<see cref="ViewerSession.OpeningTick"/>). Playing moves the viewer 13,000 ticks into `z1800`, to a moment
/// with far more effects alive than the opening tick the other fixtures were written against. Left there, it made every test
/// after it slow: a paused frame costs about 18 ms of particle and sprite building there against about 3.4 ms at tick 20000,
/// and `Transport_ShuttleIntoReverse_UpdatesTheSpeedReadout` took 1 min 55 s. The owner caught it (*"we are in a different
/// cam, and on a different player than the test was initially made for, that might be why"*), and chose the fix: *"couldnt
/// we have just reset the tick to 20000 after the playback test?"* A reset holds whatever order the suite runs in, where
/// ordering the fixtures fails for the first new one added without an `[Order]`.
///
/// **And it runs last, a second safety:** every other fixture carries an order from <see cref="UiFixtureOrder"/> and this one
/// deliberately does not, since NUnit starts ordered fixtures before unordered ones.
/// </remarks>
[TestFixture]
public sealed class PlaybackUiTests
{
    /// <summary>The one viewer this assembly runs, with its demo already open.</summary>
    private static ViewerApplication _viewer => ViewerSession.App;

    [Test]
    public void Transport_PlayAtEightTimes_AdvancesThroughTwentySecondsOfPlayback()
    {
        // **The only test that PLAYS the demo** (B408). Every other one drives the window with playback stopped, so a
        // physics event that fired forever once playback reached it passed the whole suite for days. The owner: *"the demo
        // doesnt have to be f12, the same thing would have happened on any demo, the UI suite just does no playing of the
        // demo, so we can just add a playing test to the ui suite"*, and *"one session"*: the shared viewer, not a second
        // process. It replaces `build/playback-check.ps1`.
        //
        // **At 8x from the start**, so twenty seconds of playback carry the match 160 seconds forward — players fighting,
        // dying, and leaving corpses to simulate. A hang freezes the tick readout, and the wait below fails naming the tick.
        const int PlaybackSeconds = 20;
        const double Speed = 8d;

        // **The demo loaded, not only the world.** The session waits for the world and its textures; the transport learns the
        // demo's length after that, and run alone this test found the readout at 'tick 0 / 0'.
        Retry.WhileFalse(
            () => DemoPosition.Read(_viewer.Find(TransportBar.TickLabelId).Name)?.LastTick > 0,
            TimeSpan.FromSeconds(180),
            throwOnTimeout: true,
            timeoutMessage: $"the demo never loaded; the readout says '{_viewer.Find(TransportBar.TickLabelId).Name}'.");

        // **Where the session opens, not where this test found it** — the owner: *"the playback is starting on 0, no matter
        // where the app is opening the demo, so it shouldnt look at where it starts"*. The session's own constant, so the two
        // cannot drift.
        int lastTick = DemoPosition.Read(_viewer.Find(TransportBar.TickLabelId).Name)?.LastTick
            ?? throw new InvalidOperationException("the tick readout is not a position.");
        DemoPosition opening = new(ViewerSession.OpeningTick, lastTick);

        _viewer.Find(TransportBar.StartButtonId).AsButton().Invoke();

        // **Set through automation, not the shuttle ladder or a keypress.** The owner: *"the playback test is also testing the
        // speed slider, when those should be seperate tests, and those are whats taking 30 secs or more each time"*, and a
        // keypress needs the viewer in front, which it is not while the owner's own window is. The slider's right end is the
        // fastest speed, which `SpeedSlider_AtEachEnd_ReachesSpeedsTheButtonsCannot` already proves.
        //
        // **The Value pattern, measured**: the slider supports `Value` and `LegacyIAccessible`, not `RangeValue`, and its
        // position runs from −`TimeScale.Positions` to +`TimeScale.Positions`, the right end being the fastest. *Writing the raw
        // position was tried and refused* ("Value does not fall within the expected range"): the accessible value is the
        // position as a percentage of the range, so the right end is 100.
        AutomationElement slider = _viewer.Find(TransportBar.SpeedBarId);

        if (slider.Patterns.Value.PatternOrDefault is not { IsReadOnly.Value: false } value)
        {
            throw new InvalidOperationException(
                "the speed slider has no writable Value pattern; it supports " +
                string.Join(", ", slider.GetSupportedPatterns().Select(pattern => pattern.Name)) + ".");
        }

        string was = value.Value.Value;
        value.SetValue("100");

        Retry.WhileFalse(
            () => _viewer.Find(TransportBar.SpeedLabelId).Name == TimeScale.From(Speed).Description(),
            TimeSpan.FromSeconds(5),
            throwOnTimeout: true,
            timeoutMessage: $"setting the speed slider's value (it read '{was}') did not reach {TimeScale.From(Speed).Label()}; " +
                            $"the readout says '{_viewer.Find(TransportBar.SpeedLabelId).Name}'.");

        // `z1800` is 66 ticks a second.
        const int Ticks = (int)(PlaybackSeconds * Speed * 66);

        int from = 0;
        int until = 0;
        int? reached = null;
        System.Diagnostics.Stopwatch clock = new();

        try
        {
            SetPlaying(true);

            // **The first reading is taken only once the readout is seen MOVING under playback.** Read straight after the
            // Start press, it said tick 0 and then jumped to 20000, and a first draft passed in 9.1 s on that jump. *An upper
            // bound on the step was tried and failed every run*: at 8x the readout moves hundreds of ticks between polls. A jump
            // is caught by the wall-clock control at the end instead.
            int seen = Tick() ?? 0;

            Retry.WhileFalse(
                () => Tick() is { } now && now > seen,
                TimeSpan.FromSeconds(30),
                throwOnTimeout: true,
                timeoutMessage: $"playback never started moving from tick {seen}; the readout says " +
                                $"'{_viewer.Find(TransportBar.TickLabelId).Name}'.");

            from = Tick() ?? 0;
            until = from + Ticks;
            clock.Start();

            Retry.WhileFalse(
                () => Tick() >= until,
                TimeSpan.FromSeconds(PlaybackSeconds * 6),
                throwOnTimeout: true,
                timeoutMessage:
                    $"playback from tick {from} did not reach {until}; the readout stopped at " +
                    $"'{_viewer.Find(TransportBar.TickLabelId).Name}'. A hang in the playback loop reads exactly like this.");
        }
        finally
        {
            clock.Stop();

            // Paused, and the speed left where it is: every speed test sets its own first.
            SetPlaying(false);
            reached = Tick();

            // Back to the session's opening tick, whatever happened above, so no test after this inherits the moment.
            Seek(opening);
        }

        TestContext.Out.WriteLine(
            $"PLAYBACK from tick {from} to {reached} (needed {until}) at {Speed}x in {clock.Elapsed.TotalSeconds:0.0} s");

        (reached ?? 0).ShouldBeGreaterThanOrEqualTo(until, $"{PlaybackSeconds} s of playback at {Speed}x from tick {from}");

        // After the playback's own claims, so a failed seek cannot hide a failed playback. **Within one percent of the demo**,
        // because the scrub bar's accessible value is a whole percentage: 20000 lands on 20142, 35% of 57,551.
        (Tick() ?? 0).ShouldBeInRange(
            opening.Tick - (lastTick / 100) - 1, opening.Tick + (lastTick / 100) + 1,
            "the shared viewer is back at the session's opening tick, to the scrub bar's resolution");

        // **The control on the reading above**: playback at 8x cannot cover twenty seconds of it in less wall time, so a
        // pass faster than that was a seek, not playback.
        clock.Elapsed.TotalSeconds.ShouldBeGreaterThan(
            PlaybackSeconds * 0.9, "the readout reached its target faster than playback can, so it jumped");
    }

    /// <summary>Seeks the shared viewer to a tick through the scrub bar, and waits for the readout to show it.</summary>
    /// <remarks>
    /// The scrub bar is a `TrackBar` from 0 to the last tick, and like the speed slider its accessible value is a percentage of
    /// that range; setting it raises `Scrubbed`, which is a real seek. It fails by timing out rather than asserting, so the
    /// exact-tick claim stays with the test.
    /// </remarks>
    private static void Seek(DemoPosition to)
    {
        if (to.LastTick <= 0 || _viewer.Find(TransportBar.ScrubBarId).Patterns.Value.PatternOrDefault is not { } scrub)
        {
            return;
        }

        scrub.SetValue((to.Tick * 100d / to.LastTick).ToString("R", System.Globalization.CultureInfo.InvariantCulture));

        Retry.WhileFalse(
            () => Tick() is { } now && Math.Abs(now - to.Tick) <= (to.LastTick / 100) + 1,
            TimeSpan.FromSeconds(10),
            throwOnTimeout: false);
    }

    /// <summary>The tick the readout shows, or null when it is not a position.</summary>
    private static int? Tick() => DemoPosition.Read(_viewer.Find(TransportBar.TickLabelId).Name)?.Tick;

    /// <summary>Presses play or pause until the button's accessible name says the transport is in that state.</summary>
    /// <remarks>The name is "Pause" while playing and "Play" while paused — <c>TransportBar.Playing</c> sets both.</remarks>
    private static void SetPlaying(bool playing)
    {
        string wanted = playing ? "Pause" : "Play";

        if (_viewer.Find(TransportBar.PlayButtonId).Name != wanted)
        {
            _viewer.Find(TransportBar.PlayButtonId).AsButton().Invoke();
        }

        Retry.WhileFalse(
            () => _viewer.Find(TransportBar.PlayButtonId).Name == wanted,
            TimeSpan.FromSeconds(5),
            throwOnTimeout: true,
            timeoutMessage: $"the play button never read '{wanted}'.");
    }
}
