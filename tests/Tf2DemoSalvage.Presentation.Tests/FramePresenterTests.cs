using Microsoft.Extensions.Logging.Abstractions;

using Tf2DemoSalvage.Audio;
using Tf2DemoSalvage.Core.Scene;
using Tf2DemoSalvage.Core.Tests;
using Tf2DemoSalvage.Scene;
using Tf2DemoSalvage.Scene.Hud;

namespace Tf2DemoSalvage.Presentation.Tests;

/// <summary>
/// The presenter that owns a frame's parts (D92, D214) [no engine counterpart: the composition is this viewer's], over a
/// device a test can read and with no map loaded.
/// </summary>
/// <remarks>
/// **What a frame with no map still does, and what a seek asks of the parts.** Each part has its own suite over data a
/// test wrote; this holds what only the owner does.
///
/// **Not here, and it is the larger half:** the ORDER the parts run in with a map loaded — the decals' landing sound
/// before the client's own, the bursts' whiz after — because a `LoadedMap` cannot be built by a test. The paused frames
/// and the sound log hold that order, byte for byte.
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

        return new FramePresenter(models, moment, moments, sound, NullLogger.Instance, NullLogger.Instance);
    }

    private static ViewFrame Frame(DemoTimeline? timeline = null, int tick = 4321) =>
        new(timeline, null, null, tick, tick, false, false, null, 1280, 720, 0.016f, 0d, true, true, true, 2048, 50);

    [Test]
    public void DrawOver_WithNoMap_StillHandsTheDeviceTheFramesParticlesOnce()
    {
        FramePresenter frames = Presenter();
        FakeFrameDevice device = new();

        frames.DrawOver(Frame(), AlongX, device);

        device.Particles.ShouldHaveSingleItem().ShouldBeEmpty("no map: no trails, no bursts, no impacts");
        frames.ParticleCounts.ShouldBe((0, 0, 0));
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
