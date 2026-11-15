using System.Collections.Generic;
using System.Numerics;

using Tf2DemoSalvage.Core.Container;
using Tf2DemoSalvage.Core.Primitives;
using Tf2DemoSalvage.Core.Scene;

namespace Tf2DemoSalvage.Scene.Tests;

/// <summary>A point-of-view recorder who is observing is drawn through the engine's observer view (B745).</summary>
/// <remarks>
/// **The wiring half.** <see cref="ObserverCameraConformanceTests"/> holds each view to the engine's arithmetic; this
/// holds that <see cref="SpectatorView.Eye(double, float)"/> hands the recorder's observer mode, target and freeze-cam
/// start to it. The recorder (a soldier, entity 7) has been in freeze cam since the recording began, on a living killer
/// (entity 9) standing 500 units along +X; the recorded viewpoint sits at the world origin.
/// </remarks>
public sealed class SpectatorViewObserverTests
{
    private const int Recorder = 7;

    private const int Killer = 9;

    [Test]
    public void Eye_ARecorderInFreezeCam_StandsTheDrawnDistanceBeforeHisKiller()
    {
        SpectatorView view = View(ObserverModes.FreezeCam);

        FreeCamera eye = view.Eye(tick: 100, aspect: 16f / 9f).ShouldNotBeNull();

        UniformRandomStream random = new();
        float distance = random.RandomFloat(ObserverCamera.FreezeDistanceMin, ObserverCamera.FreezeDistanceMax);
        float offset = random.RandomFloat(-30f, 20f);

        eye.Origin.X.ShouldBe(500f - distance, 0.01f, "the killer's chest, less the distance PostDataUpdate drew");
        eye.Origin.Y.ShouldBe(0f, 0.01f);
        eye.Origin.Z.ShouldBe(offset, 0.01f, "the killer's EyePosition is his feet, plus the drawn height");
        view.LastObserved.ShouldNotBeNull().FreezeFrameStarted.ShouldBeTrue("1.5 s in, the 0.4 s travel is over");
    }

    [Test]
    public void Eye_ARecorderTenTicksIntoHisFreezeCam_IsAThirdOfTheWayFromTheLastView()
    {
        // In freeze cam from tick 90: at tick 100 the travel is 0.15 of its 0.4 s, which SimpleSpline makes 0.3164.
        SpectatorView view = new(new RecordingLogger())
        {
            Eyes = new Demo(ObserverModes.FreezeCam, freezeFrom: 90),
            SecondsPerTick = 0.015f,
        };

        FreeCamera eye = view.Eye(tick: 100, aspect: 16f / 9f).ShouldNotBeNull();

        UniformRandomStream random = new();
        float distance = random.RandomFloat(ObserverCamera.FreezeDistanceMin, ObserverCamera.FreezeDistanceMax);
        float blend = 0.375f * 0.375f * (3f - (2f * 0.375f));

        eye.Origin.X.ShouldBe((500f - distance) * blend, 0.01f, "from the camera's first view, at the origin");
        view.LastObserved.ShouldNotBeNull().FreezeFrameStarted.ShouldBeFalse();
    }

    [Test]
    public void Eye_ARecorderInTheDeathCamASecondAfterHisDeathOnTheServersClock_FacesHisKiller()
    {
        // Demo tick 100 is server tick 1067, just past a second after the server stamped his death at 15 s, so the swing is
        // done; the killer stands 500 along +Y and his own eyes look along +X. On the demo's own clock (1.5 s) the swing
        // would not have begun.
        SpectatorView view = new(new RecordingLogger())
        {
            Eyes = new Demo(ObserverModes.DeathCam, killerAt: (0f, 500f, 0f), deathTime: 1000 * 0.015f),
            SecondsPerTick = 0.015f,
            ServerTickAt = tick => tick + 967,
        };

        FreeCamera eye = view.Eye(tick: 100, aspect: 16f / 9f).ShouldNotBeNull();

        eye.Angles.Yaw.ShouldBe(90f, 0.01f);
    }

    [Test]
    public void Eye_ARecorderNotObserving_IsTheRecordedViewpointAtHisEyeHeight()
    {
        SpectatorView view = View(ObserverModes.None);

        FreeCamera eye = view.Eye(tick: 100, aspect: 16f / 9f).ShouldNotBeNull();

        (eye.Origin.X, eye.Origin.Y, eye.Origin.Z).ShouldBe((0f, 0f, PlayerEye.Standing(3)));
        view.LastObserved.ShouldBeNull();
    }

    private static SpectatorView View(int observerMode) =>
        new(new RecordingLogger())
        {
            Eyes = new Demo(observerMode),
            SecondsPerTick = 0.015f,
        };

    [Test]
    public void Eye_AnotherDemoOpenedAtTheSameTick_IsThatDemosView()
    {
        // The frame cache is keyed on the tick alone, so a demo opened at the tick the last one stopped on must not
        // be handed the last one's camera (B745 review).
        SpectatorView view = View(ObserverModes.FreezeCam);
        view.Eye(tick: 100, aspect: 16f / 9f);

        view.Eyes = new Demo(ObserverModes.None, viewX: 300f);

        view.Eye(tick: 100, aspect: 16f / 9f).ShouldNotBeNull().Origin.X.ShouldBe(300f);
    }

    [Test]
    public void Eye_AFreezeCamLeftStraightForPlay_EndsTheFreezeFrame()
    {
        // Freeze cam until tick 110, then playing: `PostDataUpdate` calls `FreezeFrame( 0 )` whatever mode it leaves for.
        SpectatorView view = new(new RecordingLogger())
        {
            Eyes = new Demo(ObserverModes.FreezeCam, playFrom: 110),
            SecondsPerTick = 0.015f,
        };

        view.Eye(tick: 100, aspect: 16f / 9f);
        view.Eye(tick: 110, aspect: 16f / 9f);

        view.LastObserved.ShouldNotBeNull().FreezeFrameEnded.ShouldBeTrue();
    }

    [Test]
    public void Eye_ARecorderRoamingOnHimself_IsHisRecordedEye()
    {
        // His own m_hObserverTarget is the engine's `this`: the recorded origin and his own view offset, not his entity.
        SpectatorView view = new(new RecordingLogger())
        {
            Eyes = new Demo(ObserverModes.Roaming, target: Recorder, viewX: 40f),
            SecondsPerTick = 0.015f,
        };

        FreeCamera eye = view.Eye(tick: 100, aspect: 16f / 9f).ShouldNotBeNull();

        (eye.Origin.X, eye.Origin.Z).ShouldBe((40f, PlayerEye.Standing(3)));
    }

    [Test]
    public void Eye_InEyeOnAMovingTargetBetweenTicks_FollowsHimAsDrawn()
    {
        // The drawn sample puts him 50 units on at half a tick; the whole tick's would leave him at 500 (B747).
        SpectatorView view = new(new RecordingLogger())
        {
            Eyes = new Demo(ObserverModes.InEye, moving: true),
            SecondsPerTick = 0.015f,
        };

        view.Eye(tick: 100.5, aspect: 16f / 9f).ShouldNotBeNull().Origin.X.ShouldBe(550f, 0.01f);
    }

    /// <summary>The recorder and his killer, the same at every tick unless told otherwise.</summary>
    private sealed class Demo(
        int observerMode,
        (float X, float Y, float Z)? killerAt = null,
        float deathTime = 0f,
        int freezeFrom = 0,
        int playFrom = int.MaxValue,
        int target = Killer,
        float viewX = 0f,
        bool moving = false) : IEyeSource
    {
        private readonly (float X, float Y, float Z) _killer = killerAt ?? (500f, 0f, 0f);

        public bool HasRecordedView => true;

        public int? RecorderEntityIndex => Recorder;

        public RecordedView? RecordedViewAt(int tick) => new RecordedView { ViewOrigin = (viewX, 0f, 0f) };

        public IReadOnlyList<ScenePlayer> PlayersAt(int tick) => At(tick, 0f);

        public void PlayersAt(double tick, ICollection<ScenePlayer> into)
        {
            foreach (ScenePlayer player in At((int)System.Math.Floor(tick), moving ? (float)(tick % 1d) * 100f : 0f))
            {
                into.Add(player);
            }
        }

        private List<ScenePlayer> At(int tick, float killerAhead)
        {
            int mode = tick >= freezeFrom ? observerMode : ObserverModes.DeathCam;

            if (tick >= playFrom)
            {
                mode = ObserverModes.None;
            }

            return
            [
                new(Recorder, viewX, 0f, 0f, SceneTeams.Red, 0, 3, LifeState: mode == ObserverModes.None ? null : 2)
                {
                    ObserverMode = mode,
                    ObserverTarget = target,
                    DeathTime = deathTime,
                },
                new(Killer, _killer.X + killerAhead, _killer.Y, _killer.Z, SceneTeams.Red, 125, 2),
            ];
        }
    }
}
