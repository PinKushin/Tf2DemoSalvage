using System;

using Microsoft.Extensions.Logging.Abstractions;

using Tf2DemoSalvage.Audio;
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
        SoundPresenter sound = Sound();

        spectator.Eyes = null;
        moment.Viewmodels = null;

        // **Asserted through the presenter rather than a returned clock** (2026-08-26). `Open`
        // used to hand one back, and once `MainForm` stopped keeping a copy the return value's
        // only reader was this line — so it asks the thing that owns the clock instead.
        PlaybackPresenter playback = new(new FakePlaybackView(), new StopwatchTime());

        Systems(spectator, moment, sound, playback)
            .Open(timeline: null, lastTick: 100, audio: null, autoPlay: null, autoPlayName: "X");

        playback.HasDemo.ShouldBeFalse("there is no timeline to run a clock over");
        spectator.Eyes.ShouldBeNull();
        moment.Viewmodels.ShouldBeNull();
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
            .Open(timeline: null, lastTick: 0, audio: null, autoPlay: null, autoPlayName: "X");

        moment.Appearance.ShouldBeSameAs(DemoAppearance.None);
    }

    [Test]
    public void Open_WithNoDemoOpen_SaysThereIsNoAudioDevice()
    {
        // The sound line is a capability statement, and it has to distinguish "no device" from
        // "device open, nothing to play" — otherwise a silent viewer has two indistinguishable
        // causes.
        RecordingLogger log = new();

        new DemoSystems(
            new SpectatorView(NullLogger.Instance), Scene(), Sound(),
            new PlaybackPresenter(new FakePlaybackView(), new StopwatchTime()), new ActiveLoops(),
            new StubLoggerFactory(log))
            .Open(timeline: null, lastTick: 0, audio: null, autoPlay: null, autoPlayName: "X");

        log.Count("no audio device, so none will play").ShouldBe(1);
    }

    [Test]
    public void Construct_WithoutACollaborator_Refuses()
    {
        // A null collaborator is a system that silently stops being told — the exact failure this
        // type exists to prevent, refused where the caller still has a stack that names it.
        Should.Throw<ArgumentNullException>(() => new DemoSystems(
            null!, Scene(), Sound(),
            new PlaybackPresenter(new FakePlaybackView(), new StopwatchTime()), new ActiveLoops(),
            NullLoggerFactory.Instance));

        Should.Throw<ArgumentNullException>(() => new DemoSystems(
            new SpectatorView(NullLogger.Instance), Scene(), Sound(),
            new PlaybackPresenter(new FakePlaybackView(), new StopwatchTime()), loops: null!,
            NullLoggerFactory.Instance));
    }

    private static DemoSystems Systems(
        SpectatorView? spectator = null,
        MomentScene? moment = null,
        SoundPresenter? sound = null,
        PlaybackPresenter? playback = null) =>
        new(spectator ?? new SpectatorView(NullLogger.Instance),
            moment ?? Scene(),
            sound ?? Sound(),
            playback ?? new PlaybackPresenter(new FakePlaybackView(), new StopwatchTime()),
            new ActiveLoops(),
            NullLoggerFactory.Instance);

    private static MomentScene Scene() =>
        new(new EntityModelSet(), new ViewmodelScene(), NullLogger.Instance);

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
