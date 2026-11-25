using Microsoft.Extensions.Logging.Abstractions;

using Tf2DemoSalvage.Audio;
using Tf2DemoSalvage.Core.Scene;
using Tf2DemoSalvage.Core.Tests;
using Tf2DemoSalvage.Scene;
using Tf2DemoSalvage.Scene.Hud;

namespace Tf2DemoSalvage.Presentation.Tests;

/// <summary>
/// The presenter that owns a frame's parts (D92, D214) [no engine counterpart: the composition is this viewer's], over a
/// device a test can read, with no map loaded and with one a test wrote.
/// </summary>
/// <remarks>
/// **What a frame with no map still does, and what a seek asks of the parts.** Each part has its own suite over data a
/// test wrote; this holds what only the owner does.
///
/// **The ORDER the parts run in with a map loaded is held as far as a map with no install reaches**
/// (<see cref="SyntheticMap"/>): the light styles' lightmap, then the decals, then the frame's particles, each seen at
/// the device. **Still not here:** the decals' landing sound before the client's own and the bursts' whiz after — a
/// landing needs a decal material and the bursts need a particle file, and both come out of an install. The paused
/// frames and the sound log hold that part of the order, byte for byte.
/// </remarks>
public sealed class FramePresenterTests
{
    private static readonly FreeCamera AlongX = new() { Origin = (0f, 0f, 0f), Angles = (0f, 0f, 0f) };

    private static FramePresenter Presenter()
    {
        EntityModelSet models = new();
        MomentScene moment = new(models, new ViewmodelScene(), NullLogger.Instance);
        MomentPresenter moments = new(moment, new FrameLedger(), NullLogger.Instance);
        SoundPresenter sound = new(
            new SoundscapeSystem(new ActiveLoops(), static _ => null, NullLogger.Instance),
            new ActiveLoops(),
            static _ => null,
            NullLogger.Instance);

        return new FramePresenter(models, moment, moments, sound, NullLogger.Instance, NullLogger.Instance, NullLogger.Instance);
    }

    private static ViewFrame Frame(DemoTimeline? timeline = null, int tick = 4321) =>
        new(timeline, null, null, tick, tick, false, false, null, 1280, 720, 0.016f, 0d, true, true, true, 2048, 50);

    /// <remarks>
    /// **The econ effects are decided at the pose, not the draw** (B69 g): the presenter hands the moment its decision and
    /// its clears, so the pose reads a clear made this frame (cdll_client_int.cpp:2193-2239). With no map nothing is decided.
    /// </remarks>
    [Test]
    public void Constructor_TheMoment_DecidesEconEffectsAtItsPose()
    {
        EntityModelSet models = new();
        MomentScene moment = new(models, new ViewmodelScene(), NullLogger.Instance);
        SoundPresenter sound = new(
            new SoundscapeSystem(new ActiveLoops(), static _ => null, NullLogger.Instance), new ActiveLoops(), static _ => null, NullLogger.Instance);

        _ = new FramePresenter(
            models, moment, new MomentPresenter(moment, new FrameLedger(), NullLogger.Instance), sound,
            NullLogger.Instance, NullLogger.Instance, NullLogger.Instance);

        (moment.DecideEconParticles is not null, moment.FastCullClearedAt is not null).ShouldBe((true, true));
        Should.NotThrow(() => moment.DecideEconParticles!(new MomentInfo(1d, 1, false, null, null, 0.015f, 54f)));
    }

    /// <remarks>
    /// **A pose that decides nothing leaves nothing decided** (B69 review): a skip's frames, a seek's replay and a map with no
    /// particle systems return before deciding, and the draw offers what the last decision left. Kept, that was a decision
    /// from before the seek, offered again by the next draw. The control: the decision made by hand is there before the pose.
    /// </remarks>
    [Test]
    public void DecideEconParticles_APoseThatDecidesNothing_LeavesNothingToOffer()
    {
        EntityModelSet models = new();
        MomentScene moment = new(models, new ViewmodelScene(), NullLogger.Instance);
        SoundPresenter sound = new(
            new SoundscapeSystem(new ActiveLoops(), static _ => null, NullLogger.Instance), new ActiveLoops(), static _ => null, NullLogger.Instance);
        FramePresenter presenter = new(
            models, moment, new MomentPresenter(moment, new FrameLedger(), NullLogger.Instance), sound,
            NullLogger.Instance, NullLogger.Instance, NullLogger.Instance);
        EconParticleHolder worn = new(true, true, false, false, false, true, false, false, null);
        EconParticleItem hat = new(
            40, Wearable: true, Received: null, Dormant: false, EconParticleState.Visible,
            [new EconParticle("superrare_burning1", 0, false, false, false, [null])], worn);

        presenter.Bursts.DecideEconParticles([hat], 1000, landed: true, static _ => true);
        presenter.Bursts.EconDecided.ShouldHaveSingleItem("the control");

        moment.DecideEconParticles!(new MomentInfo(1001d, 1001, false, null, null, 0.015f, 54f));

        presenter.Bursts.EconDecided.ShouldBeEmpty();
    }

    [Test]
    public void DrawOver_WithNoMap_StillHandsTheDeviceTheFramesParticlesOnce()
    {
        FramePresenter frames = Presenter();
        FakeFrameDevice device = new();

        frames.DrawOver(Frame(), AlongX, device);

        device.Particles.ShouldHaveSingleItem().ShouldBeEmpty("no map: no trails, no bursts, no impacts");
        frames.ParticleCounts.ShouldBe((0, 0, 0));
    }

    /// <remarks>
    /// `RenderPlayerSprites` (viewrender.cpp:2140) is every frame's, a skip's included (B549): the device is told once a frame,
    /// empty with no map to hold the materials.
    /// </remarks>
    [Test]
    public void DrawOver_EachFrame_HandsTheDeviceThePlayerSpritesOnce()
    {
        FramePresenter frames = Presenter();
        FakeFrameDevice device = new();

        frames.DrawOver(Frame() with { Speaking = new System.Collections.Generic.HashSet<int> { 3 } }, AlongX, device);
        frames.DrawOver(Frame(), AlongX, device);

        device.PlayerSprites.Count.ShouldBe(2);
        device.PlayerSprites.ShouldAllBe(sprites => sprites.Length == 0, "no map: no speech material");
    }

    /// <remarks>
    /// **The voice icons are gated by the server's ConVars as of the frame's tick** (B843 review 1): `mp_tournament` was
    /// set to 1 at tick 10 and 2 at 20, so a frame at 15 reads 1 where the recording's last value is 2, and one at 25
    /// reads 2, which is not the default either (review 2, Sonnet). Drawing with no demo then lets the timeline go (Fable).
    /// </remarks>
    [TestCase(15, "1")]
    [TestCase(25, "2")]
    public void DrawOver_AFrameBetweenTwoSets_GatesTheVoiceIconsByItsTicksValues(int tick, string expected)
    {
        FramePresenter frames = Presenter();
        DemoTimeline timeline = DemoTimeline.Build(SyntheticPlayer.DemoWithMessagesAt(
            (10, [new Core.Net.SetConVarMessage([new("mp_tournament", "1")])]),
            (20, [new Core.Net.SetConVarMessage([new("mp_tournament", "2")])])));

        frames.DrawOver(Frame(timeline, tick), AlongX, new FakeFrameDevice());
        string? gated = frames.VoiceIconConVars.GetString("mp_tournament");
        frames.DrawOver(Frame(), AlongX, new FakeFrameDevice());

        (gated, frames.VoiceIconConVars.FindAt).ShouldBe((expected, null));
    }

    /// <remarks>
    /// `cl_drawmonitors` reaches the client-side effects through the device, both ways (B736): with it off no monitor runs
    /// `DrawEffects`, so a camera in play ages nothing twice.
    /// </remarks>
    [Test]
    public void DrawOver_MonitorsOffThenOn_HandsTheImpactsTheDevicesSetting()
    {
        FramePresenter frames = Presenter();
        FakeFrameDevice device = new() { DrawMonitors = false };

        frames.DrawOver(Frame(), AlongX, device);
        bool off = frames.ImpactsDrawMonitors;
        device.DrawMonitors = true;
        frames.DrawOver(Frame(), AlongX, device);

        (off, frames.ImpactsDrawMonitors).ShouldBe((false, true));
    }

    /// <remarks>`r_threaded_particles` reaches the particle fork through the frame, both ways, so a user's 0 is honoured.</remarks>
    [Test]
    public void DrawOver_ThreadedParticlesOffThenOn_SwitchesTheParticleFork()
    {
        FramePresenter frames = Presenter();
        FakeFrameDevice device = new();

        frames.DrawOver(Frame() with { ThreadedParticles = false }, AlongX, device);
        bool off = frames.ParticlesThreaded;
        frames.DrawOver(Frame(), AlongX, device);

        (off, frames.ParticlesThreaded).ShouldBe((false, true));
    }

    [Test]
    public void DrawOver_WithAMapLoaded_UploadsTheLightmapThenTheDecalsThenTheParticles()
    {
        DemoTimeline timeline = SyntheticEffects.TimelineWrittenWith(PlayerFlagLayout.Current);
        LoadedMap map = SyntheticMap.Read(timeline);
        FramePresenter frames = Presenter();
        FakeFrameDevice device = new();

        frames.LevelInit(map, doPixelVisibility: true);
        frames.DrawOver(Frame(timeline) with { Loaded = map, Game = SyntheticMap.NoInstall() }, AlongX, device);

        device.Calls.ShouldBe(
            [nameof(FakeFrameDevice.UpdateLightmap), nameof(FakeFrameDevice.UploadShotDecals), nameof(FakeFrameDevice.SetParticles)],
            "light styles first, because the decals and the particles are lit by them; the particles last, with the impacts joined");
    }

    /// <remarks>
    /// **The seek is the frame's one decision, and a frame that runs no effects keeps it** (D221): with no trail
    /// definition — every CI run — no burst steps, and the seek waits for the frame that does.
    /// </remarks>
    [Test]
    public void DrawOver_ASeekOnFramesWithNoTrailDefinition_HoldsTheSeekForTheEffects()
    {
        FramePresenter frames = Presenter();
        FakeFrameDevice device = new();

        frames.ShowMomentOver(Frame(tick: 100), 100d, null, static tick => Frame(tick: (int)tick), null);
        frames.Skip.ShouldNotBeNull().Skipped.ShouldBeTrue("the control: the first move is a skip");

        frames.DrawOver(Frame(tick: 100), AlongX, device);
        frames.DrawOver(Frame(tick: 100), AlongX, device);

        frames.SeekPending.ShouldBeTrue();
    }

    /// <remarks>A new demo's first move is a skip: it does not move from the last demo's tick (D221).</remarks>
    [Test]
    public void ShowMomentOver_ANewDemoAtTheSameTickAndJumpCount_IsASkip()
    {
        FramePresenter frames = Presenter();
        DemoTimeline next = SyntheticEffects.TimelineWrittenWith(PlayerFlagLayout.Current);

        frames.ShowMomentOver(Frame(tick: 500), 500d, null, static tick => Frame(tick: (int)tick), null, jumps: 1);
        int skips = frames.Skip.ShouldNotBeNull().Skips;

        frames.ShowMomentOver(Frame(next, 500), 500d, null, tick => Frame(next, (int)tick), null, jumps: 1);

        frames.Skip.ShouldNotBeNull().Skips.ShouldBe(skips + 1);
    }

    [Test]
    public void Take_OneMove_AnswersEachReaderOnce()
    {
        FrameSkip move = new(100, Generation: 3, Skips: 1) { Skipped = true };
        (int, int) particles = default;
        (int, int) sound = default;

        (move.Take(ref particles), move.Take(ref particles), move.Take(ref sound)).ShouldBe((true, null, true));
    }

    /// <remarks>
    /// **A reader sees a skip it missed**: the opening state's jump and the frame's played step both come before the
    /// sound runs. Measured: the sound read only the played step, called the 20,000-tick jump playing, and started
    /// every sound since tick 0 — 2,159 ms over the run's first three frames at 8x, against 23 ms on main.
    /// </remarks>
    [Test]
    public void ShowMomentOver_AJumpThenAPlayedStepBeforeTheReader_StillReadsTheJump()
    {
        FramePresenter frames = Presenter();
        (int, int) sound = default;

        frames.ShowMomentOver(Frame(tick: 400), 400d, null, static tick => Frame(tick: (int)tick), null, jumps: 1);
        frames.Skip!.Take(ref sound);
        frames.ShowMomentOver(Frame(tick: 20000), 20000d, null, static tick => Frame(tick: (int)tick), null, jumps: 2);
        frames.ShowMomentOver(Frame(tick: 20053), 20053d, null, static tick => Frame(tick: (int)tick), null, jumps: 2);

        FrameSkip last = frames.Skip.ShouldNotBeNull();

        last.Skipped.ShouldBeFalse("the control: the last move was played");
        last.Take(ref sound).ShouldBe(true);
    }

    [Test]
    public void Pick_OfAFrame_IsAskedWithTheFramesTickAndSize()
    {
        FramePresenter frames = Presenter();

        frames.Pick("640 360", Frame(tick: 777), AlongX).ShouldNotBeNull().ShouldStartWith("pick (640 360) at tick 777 from (0 0 0) ");
        frames.Pick("640 360", Frame(), null).ShouldBeNull("no frame has been drawn");
        frames.Pick(null, Frame(), AlongX).ShouldBeNull();
    }

    [Test]
    public void WithIdTargets_WithNoMap_ClearsTheTargetAndNeverAsksForTheEye()
    {
        FramePresenter frames = Presenter();
        HudState looking = new(InGame: true, HasLocalPlayer: true, HideHud: 0, Health: 125, Alive: true) { IdTarget = 9 };
        int asked = 0;

        HudState state = frames.WithIdTargets(
            looking,
            100,
            Frame(SyntheticEffects.TimelineWrittenWith(PlayerFlagLayout.Current)),
            () =>
            {
                asked++;

                return AlongX;
            });

        state.IdTarget.ShouldBeNull();
        asked.ShouldBe(0);
    }
}
