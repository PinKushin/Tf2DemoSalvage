using System;
using System.Collections.Generic;
using System.Linq;

using Tf2DemoSalvage.Core.Scene;
using Tf2DemoSalvage.Core.Tests;

namespace Tf2DemoSalvage.Scene.Tests;

/// <summary>
/// The recorder's feet turned per frame through the production chain — <see cref="TimelineMoments"/> over a
/// <see cref="DemoPlayer"/> — on an authored point-of-view demo (B450).
/// </summary>
/// <remarks>
/// The engine converges the feet by <c>gpGlobals-&gt;frametime</c> from <c>EyeAngles()</c>
/// (<c>multiplayer_animstate.cpp:1759</c>, <c>c_tf_player.cpp:4279-4284</c>), and for the local player those are his
/// <c>pl.v_angle</c> — the recorded <c>democmdinfo_t.localViewAngles</c>. Moving, the goal is the eye
/// (<c>:1716-1720</c>); a goal over 60 degrees away saturates the rate at 720 a second (<c>:1804-1805</c>), so half a
/// tick at 0.015 turns them 5.4 degrees, as <c>SyntheticRecorderViewTests</c> derives one layer down.
///
/// This replaces the question <c>CorpusRecorderFeetTests</c> asked of the 2013 badlands POV, whose recorder ran
/// about while turning; that recording left gcor on 2026-10-29 and the scripted specimens never turn.
/// </remarks>
public sealed class TimelineMomentsRecorderFeetTests
{
    /// <summary>A frame a quarter of the way into a tick of the fixture's 100-110 run.</summary>
    private const double Start = 105.25;

    /// <summary>Half a tick — 0.0075 seconds — later, still inside the same tick.</summary>
    private const double Later = 105.75;

    /// <summary>The recorder's entity: <c>svc_ServerInfo</c> names slot 0.</summary>
    private const int Recorder = 1;

    [Test]
    public void PlayersAt_TwoFramesInsideATickWhileTurning_TurnsTheDrawnFeetBetweenThem()
    {
        // 20 degrees a tick from zero at tick 100: his local yaw is past 100 here, far beyond the 60 that saturates.
        (ScenePlayer start, ScenePlayer later) = TwoFrames(localYawPerTick: 20f);

        (later.Yaw - start.Yaw).ShouldBe(5.4f, 1e-3f);
        later.AimYaw.ShouldNotBeNull().ShouldBe(-Normalize(later.EyeYaw.ShouldNotBeNull() - later.Yaw), 1e-3f);
    }

    [Test]
    public void PlayersAt_TwoFramesInsideATickWithoutTurning_LeavesTheFeetWhereTheyWere()
    {
        // The control: the same demo with his local yaw held at zero, where the feet already are — within the
        // fixture's 12-bit eye yaw step (360 / 4096 = 0.088) — so there is nothing to turn toward.
        (ScenePlayer start, ScenePlayer later) = TwoFrames(localYawPerTick: 0f);

        (later.Yaw - start.Yaw).ShouldBe(0f, 0.088f);
    }

    private static (ScenePlayer Start, ScenePlayer Later) TwoFrames(float localYawPerTick)
    {
        DemoTimeline timeline = DemoTimeline.Build(
            SyntheticPlayer.DemoOfARecorderAndABystander(viewed: true, localYawPerTick));
        TimelineMoments moments = new(timeline) { Player = new DemoPlayer(timeline) };

        return (At(moments, Start), At(moments, Later));
    }

    private static ScenePlayer At(TimelineMoments moments, double tick)
    {
        List<ScenePlayer> players = [];
        moments.PlayersAt(tick, players);

        return players.Single(player => player.EntityIndex == Recorder);
    }

    private static float Normalize(float degrees) => MathF.IEEERemainder(degrees, 360f);
}
