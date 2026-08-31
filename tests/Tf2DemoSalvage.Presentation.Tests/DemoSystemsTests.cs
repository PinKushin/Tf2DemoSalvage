using System;
using System.Collections.Generic;

using Microsoft.Extensions.Logging.Abstractions;

using Tf2DemoSalvage.Audio;
using Tf2DemoSalvage.Core.Container;
using Tf2DemoSalvage.Core.Scene;
using Tf2DemoSalvage.Scene;

namespace Tf2DemoSalvage.Presentation.Tests;

/// <summary>
/// Telling every system about a newly-opened demo.
/// </summary>
/// <remarks>
/// **This is the method that broke three times** (B193). `MomentScene.Viewmodels`,
/// `MomentScene.Upload` and `SpectatorView.Eyes` were each an assignment written inline in
/// `MainForm.Apply`, and each became a property nobody set. Two shipped, with 620 viewer tests
/// green — because every one of those tests exercised the components and none exercised the WIRING.
///
/// **So these tests assert the wiring**, which is the only thing that could have caught them.
/// </remarks>
public sealed class DemoSystemsTests
{
    [Test]
    public void Open_WithNoTimeline_ClearsEverySourceRatherThanLeavingThem()
    {
        // **The null path is the one that matters, and it is easy to write as an `if` that only
        // assigns when there is something to assign.** A demo whose schema failed to decode still
        // has to clear the PREVIOUS demo's eyes and viewmodels — leaving them is how a closed demo
        // goes on being spectated.
        SpectatorView spectator = new(NullLogger.Instance);
        MomentScene moment = Scene();
        MomentPresenter moments = Moments(moment);
        PlayerAppearances appearances = Appearances();
        SoundPresenter sound = Sound();

        // **Each one set to something first, or the assertions below cannot fail.** A source that was
        // never assigned is already null, so a test that only checks for null afterwards passes
        // against an `Open` that touches nothing at all — the control this whole class needs.
        spectator.Eyes = new StubEyes();
        moment.Viewmodels = new StubViewmodels();
        moments.Source = new StubMoments();

        // **`appearances.Timeline` is deliberately NOT asserted here, and that is a gap worth
        // naming rather than papering over.** It is a `DemoTimeline?`, whose constructor is private,
        // so a test cannot put a non-null value there — and asserting it is null after `Open` when
        // it was already null before is the precondition-equals-assertion shape that made the rest
        // of this test worthless until it was fixed (`docs/memory/set-the-opposite-state-first.md`).
        //
        // The third level covers it: an unset timeline gives `DemoAppearance.None` for ever, which
        // `MomentScene` reports as "no player appearance" and `WiringUiTests` asserts against a
        // running viewer.

        // **Asserted through the presenter rather than a returned clock** (2026-08-26). `Open`
        // used to hand one back, and once `MainForm` stopped keeping a copy the return value's
        // only reader was this line — so it asks the thing that owns the clock instead.
        PlaybackPresenter playback = new(new FakePlaybackView(), new StopwatchTime());

        Systems(spectator, moment, moments, appearances, sound, playback)
            .Open(timeline: null, lastTick: 100, audio: null, autoPlay: false, autoPlayReason: "X");

        playback.HasDemo.ShouldBeFalse("there is no timeline to run a clock over");
        spectator.Eyes.ShouldBeNull();
        moment.Viewmodels.ShouldBeNull();
        moments.Source.ShouldBeNull("a closed demo goes on being SAMPLED, which draws its players");
        sound.Schedule.ShouldBeNull();
    }

    [Test]
    public void Open_WithNoTimeline_StillForgetsTheAppearance()
    {
        // The appearance is FORGOTTEN rather than rebuilt, on both paths: the archives open later
        // than this, so building now caches nothing for the life of the demo.
        MomentScene moment = Scene();

        moment.Appearance = new GameAppearance(Classes: null, Roles: null);

        Systems(moment: moment)
            .Open(timeline: null, lastTick: 0, audio: null, autoPlay: false, autoPlayReason: "X");

        moment.Appearance.ShouldBeSameAs(DemoAppearance.None);
    }

    [Test]
    public void Open_WithNoTimeline_ForgetsThePreviousDemosClock()
    {
        // **The clock was the one source `Open` did NOT clear**, and the test above could not see
        // it: it asserts `HasDemo` is false on a presenter that was never loaded, so the
        // precondition already equals the assertion — the exact shape that class's own comments
        // warn about for `appearances.Timeline`. Loading one FIRST is what makes the claim
        // falsifiable (`docs/memory/set-the-opposite-state-first.md`).
        //
        // What it cost: opening a demo whose schema fails to decode, after one that decoded, left
        // the presenter holding the PREVIOUS demo's clock while every other source was nulled —
        // `HasDemo` true, `Position` answering from a closed demo. It was masked because
        // `TransportBar.SetDemoLength` used to switch playback off as a side effect, so nothing
        // ever advanced that stale clock. Removing that side effect (D55's rule about the View not
        // deciding business state) is what made this reachable.
        PlaybackPresenter playback = new(new FakePlaybackView(), new StopwatchTime());

        playback.Load(new PlaybackClock(PlaybackClock.DefaultIntervalPerTick, 1000));
        playback.HasDemo.ShouldBeTrue("the precondition: there is a demo to forget");

        Systems(playback: playback)
            .Open(timeline: null, lastTick: 500, audio: null, autoPlay: false, autoPlayReason: "X");

        playback.HasDemo.ShouldBeFalse(
            "a demo that carried no schema must not leave the previous one's clock loaded");
    }

    [Test]
    public void Open_WithNoTimeline_StillSizesTheTransport()
    {
        // **A demo has a LENGTH even when it has no clock, and this is the case that proves the
        // length was not folded into `Load`.** A recording whose schema failed to decode returns
        // from `Open` before any clock is built — but its header still says how many ticks it has,
        // and its scrub bar has to come alive. Folding the two together would leave exactly those
        // demos unscrubbable, which is the ordinary case while decoding is still being finished.
        //
        // This is also the only level that can reach `Open`'s length at all: `DemoTimeline`'s
        // constructor is private, so every test in this class passes `timeline: null`. The autoplay
        // half needs a real demo and lives in `LaunchOptionWiringTests` (B223, D118).
        FakePlaybackView view = new();

        Systems(playback: new PlaybackPresenter(view, new StopwatchTime()))
            .Open(timeline: null, lastTick: 4242, audio: null, autoPlay: false, autoPlayReason: "X");

        view.DemoLength.ShouldBe(4242);
    }

    [Test]
    public void Open_WithNoDemoOpen_SaysThereIsNoAudioDevice()
    {
        // The sound line is a capability statement, and it has to distinguish "no device" from
        // "device open, nothing to play" — otherwise a silent viewer has two indistinguishable
        // causes.
        RecordingLogger log = new();

        MomentScene scene = Scene();

        new DemoSystems(
            new SpectatorView(NullLogger.Instance), scene, Moments(scene), Appearances(), Sound(),
            new PlaybackPresenter(new FakePlaybackView(), new StopwatchTime()), new ActiveLoops(),
            new StubLoggerFactory(log))
            .Open(timeline: null, lastTick: 0, audio: null, autoPlay: false, autoPlayReason: "X");

        log.Count("no audio device, so none will play").ShouldBe(1);
    }

    [Test]
    public void Construct_WithoutACollaborator_Refuses()
    {
        // A null collaborator is a system that silently stops being told — the exact failure this
        // type exists to prevent, refused where the caller still has a stack that names it.
        Should.Throw<ArgumentNullException>(() => new DemoSystems(
            null!, Scene(), Moments(Scene()), Appearances(), Sound(),
            new PlaybackPresenter(new FakePlaybackView(), new StopwatchTime()), new ActiveLoops(),
            NullLoggerFactory.Instance));

        Should.Throw<ArgumentNullException>(() => new DemoSystems(
            new SpectatorView(NullLogger.Instance), Scene(), moments: null!, Appearances(), Sound(),
            new PlaybackPresenter(new FakePlaybackView(), new StopwatchTime()), new ActiveLoops(),
            NullLoggerFactory.Instance));

        Should.Throw<ArgumentNullException>(() => new DemoSystems(
            new SpectatorView(NullLogger.Instance), Scene(), Moments(Scene()), Appearances(), Sound(),
            new PlaybackPresenter(new FakePlaybackView(), new StopwatchTime()), loops: null!,
            NullLoggerFactory.Instance));

        Should.Throw<ArgumentNullException>(() => new DemoSystems(
            new SpectatorView(NullLogger.Instance), Scene(), Moments(Scene()), appearances: null!,
            Sound(), new PlaybackPresenter(new FakePlaybackView(), new StopwatchTime()),
            new ActiveLoops(), NullLoggerFactory.Instance));
    }

    private static DemoSystems Systems(
        SpectatorView? spectator = null,
        MomentScene? moment = null,
        MomentPresenter? moments = null,
        PlayerAppearances? appearances = null,
        SoundPresenter? sound = null,
        PlaybackPresenter? playback = null)
    {
        MomentScene scene = moment ?? Scene();

        return new DemoSystems(
            spectator ?? new SpectatorView(NullLogger.Instance),
            scene,
            moments ?? Moments(scene),
            appearances ?? Appearances(),
            sound ?? Sound(),
            playback ?? new PlaybackPresenter(new FakePlaybackView(), new StopwatchTime()),
            new ActiveLoops(),
            NullLoggerFactory.Instance);
    }

    /// <summary>A fresh appearance holder.</summary>
    private static PlayerAppearances Appearances() => new(NullLogger.Instance);

    private static MomentScene Scene() =>
        new(new EntityModelSet(), new ViewmodelScene(), NullLogger.Instance);

    private static MomentPresenter Moments(MomentScene scene) =>
        new(scene, new FrameLedger(), NullLogger.Instance);

    // **Three do-nothing sources, and their whole job is to be NOT NULL.** `Open` clearing a source
    // is only observable if something was there to clear; the previous version of the null-path test
    // set each one to null first, so it held identically against an `Open` with an empty body.
    //
    // They answer nothing because nothing calls them — `Open` assigns and does not sample.

    private sealed class StubEyes : IEyeSource
    {
        public int? RecorderEntityIndex => null;

        public RecordedView? RecordedViewAt(int tick) => null;

        public IReadOnlyList<ScenePlayer> PlayersAt(int tick) => [];
    }

    private sealed class StubViewmodels : IViewmodelSource
    {
        public SceneViewmodel? MainHandAt(int tick, int player) => null;

        public SceneViewmodel? OffHandAt(int tick, int player) => null;
    }

    private sealed class StubMoments : IMomentSource
    {
        public float IntervalPerTick => 0.015f;

        public void PlayersAt(double tick, ICollection<ScenePlayer> into) => into.Clear();

        public void PropsAt(double tick, ICollection<SceneProp> into) => into.Clear();

        // Null rather than a state: this stub carries no recording, and "the demo did not say" is
        // the honest answer for one.
        public int? RoundStateAt(double tick) => null;
    }

    private static SoundPresenter Sound() =>
        new(new SoundscapeSystem(new ActiveLoops(), _ => null, NullLogger.Instance),
            new ActiveLoops(), _ => null, NullLogger.Instance);

    /// <summary>A logger factory that hands every category the same recorder.</summary>
    private sealed class StubLoggerFactory(RecordingLogger log) : Microsoft.Extensions.Logging.ILoggerFactory
    {
        public void AddProvider(Microsoft.Extensions.Logging.ILoggerProvider provider)
        {
        }

        public Microsoft.Extensions.Logging.ILogger CreateLogger(string categoryName) => log;

        public void Dispose()
        {
        }
    }
}
