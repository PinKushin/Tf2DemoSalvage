using System.Collections.Generic;

using Tf2DemoSalvage.Core.Scene;

namespace Tf2DemoSalvage.Core.Tests.Scene;

/// <summary>Another player's eye angles interpolate like his origin and angles (B749).</summary>
/// <remarks>
/// **The engine registers them in the same latch group.** `C_TFPlayer`'s constructor:
///
/// <code>
///   AddVar( &amp;m_angEyeAngles, &amp;m_iv_angEyeAngles, LATCH_SIMULATION_VAR );   // c_tf_player.cpp:3874
/// </code>
///
/// so every simulation latch appends an eye-angle entry with `GetSimulationTime()` as its changetime, beside the
/// origin's and `m_angRotation`'s (`c_baseentity.cpp:2806`; `NoteChanged` appends unconditionally). They are drawn
/// from the same pair, `cl_interp` behind, and a `QAngle` is slerped: `Lerp_Hermite&lt;QAngle&gt;` refuses hermite and
/// `Lerp&lt;QAngle&gt;` goes through quaternions (`mathlib.h:661`).
/// </remarks>
public sealed class EyeAnglesInterpolationConformanceTests
{
    private const int UpdateGap = 4;

    [Test]
    public void At_EyeYawHalfwayBetweenTwoUpdates_IsSlerpedTheShortWayRound()
    {
        ScenePropTrack track = Turning((-10f, 0f), (10f, 0f));

        track.At(Between(track, 0.5))!.Value.EyeYaw.ShouldNotBeNull().ShouldBe(0f, 0.01f);
    }

    [Test]
    public void At_EyePitchHalfwayBetweenTwoUpdates_IsHalfwayAtTheSameYaw()
    {
        ScenePropTrack track = Turning((30f, 0f), (30f, 40f));

        ScenePose pose = track.At(Between(track, 0.5))!.Value;

        pose.EyePitch.ShouldNotBeNull().ShouldBe(20f, 0.01f);
        pose.EyeYaw.ShouldNotBeNull().ShouldBe(30f, 0.01f);
    }

    [Test]
    public void At_AnUpdateArrivedButNotYetDrawn_KeepsTheOlderEyeAngles()
    {
        // A quarter of the way, the second update has ARRIVED (state follows arrival, B383) but the drawn moment is a
        // quarter of the way to it: the interpolated variables are `cl_interp` behind, the eye angles with them.
        ScenePropTrack track = Turning((0f, 0f), (40f, 0f));

        track.At(Between(track, 0.25))!.Value.EyeYaw.ShouldNotBeNull().ShouldBe(10f, 0.01f);
    }

    [Test]
    public void At_AnEntityWithNoEyeAngles_HasNone()
    {
        // The control for the null: a prop has no `m_angEyeAngles`, so no interpolated value may be invented for it.
        ScenePropTrack track = new(entityIndex: 3, "models/props/door.mdl");
        track.Add(100, new ScenePose { X = 0f });
        track.Add(100 + UpdateGap, new ScenePose { X = 4f });

        ScenePose pose = track.At(Between(track, 0.5))!.Value;

        pose.EyeYaw.ShouldBeNull();
        pose.EyePitch.ShouldBeNull();
    }

    [Test]
    public void PlayersAt_BetweenTwoUpdates_CarriesTheInterpolatedEyeYaw()
    {
        // The wiring: the player the cameras follow and the body is posed from carries the track's eye angles.
        DemoTimeline timeline = DemoTimeline.Build(SyntheticPlayer.DemoOfEyesOverTicks(
            0.015f, (100, 0f, 0f), (100 + UpdateGap, 4f, 40f), (100 + (2 * UpdateGap), 8f, 40f)));
        int delay = timeline.TrackFor(1).ShouldNotBeNull().InterpolationDelayTicks;
        List<ScenePlayer> players = [];

        timeline.PlayersAt(100 + delay + (UpdateGap / 2d), players);

        // The fixture's eye yaw is 12 bits over 360 degrees, so 40 is said to within half a step.
        players.ShouldHaveSingleItem().EyeYaw.ShouldNotBeNull().ShouldBe(20f, 0.1f);
    }

    /// <summary>A player track turning from one (yaw, pitch) to another over one update gap, moving so each update latches.</summary>
    private static ScenePropTrack Turning((float Yaw, float Pitch) from, (float Yaw, float Pitch) to)
    {
        ScenePropTrack track = new(entityIndex: 1, "models/player/scout.mdl");
        track.Add(100, new ScenePose { X = 0f, EyeYaw = from.Yaw, EyePitch = from.Pitch });
        track.Add(100 + UpdateGap, new ScenePose { X = 4f, EyeYaw = to.Yaw, EyePitch = to.Pitch });

        return track;
    }

    private static double Between(ScenePropTrack track, double fraction) =>
        100 + track.InterpolationDelayTicks + (fraction * UpdateGap);
}
