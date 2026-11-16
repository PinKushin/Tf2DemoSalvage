using System.Collections.Generic;

using Tf2DemoSalvage.Core.Scene;

namespace Tf2DemoSalvage.Core.Tests.Scene;

/// <summary>Another player's eye angles interpolate like his origin (B749).</summary>
/// <remarks>
/// **The engine registers them in the same latch group.** `C_TFPlayer`'s constructor:
///
/// <code>
///   AddVar( &amp;m_angEyeAngles, &amp;m_iv_angEyeAngles, LATCH_SIMULATION_VAR );   // c_tf_player.cpp:3874
/// </code>
///
/// so every simulation latch appends an eye-angle entry with `GetSimulationTime()` as its changetime, beside the
/// origin's (`c_baseentity.cpp:2806`). They are drawn `cl_interp` behind, from that pair, and a `QAngle` is slerped:
/// `Lerp_Hermite&lt;QAngle&gt;` refuses hermite and `Lerp&lt;QAngle&gt;` goes through quaternions (`mathlib.h:661`).
///
/// **The poses here are built as `RecordProp` builds a player's**: a player's `m_angRotation` is not networked, so
/// the pose's angles are `m_angEyeAngles` as the wire sends them (yaw 0..360), and its eye fields the same, normalised.
/// </remarks>
public sealed class EyeAnglesInterpolationConformanceTests
{
    private const int UpdateGap = 4;

    [Test]
    public void At_EyeYawHalfwayAcrossTheWrap_TakesTheShortArcThroughSouth()
    {
        // 170 to 190 on the wire, 170 to -170 stored: twenty degrees through due south. A lerp of the stored values
        // turns 340 degrees the other way and lands on 0; the slerp lands on 180.
        ScenePropTrack track = Turning((170f, 0f), (190f, 0f));

        float yaw = track.At(Between(track, 0.5))!.Value.EyeYaw.ShouldNotBeNull();

        System.MathF.Abs(yaw).ShouldBe(180f, 0.01f);
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
    public void At_AQuarterOfTheWayToAnArrivedUpdate_IsAQuarterOfTheWay()
    {
        // The second update has ARRIVED (state follows arrival, B383), but the drawn moment is a quarter of the way to
        // it: the interpolated variables are `cl_interp` behind, the eye angles with them. Taken from the arrived pose,
        // this was 40.
        ScenePropTrack track = Turning((0f, 0f), (40f, 0f));

        track.At(Between(track, 0.25))!.Value.EyeYaw.ShouldNotBeNull().ShouldBe(10f, 0.01f);
    }

    [Test]
    public void At_AnEntityWithNoEyeAngles_HasNone()
    {
        // The control for the null: a prop has no `m_angEyeAngles`, so its slerped angles are not eye angles.
        ScenePropTrack track = new(entityIndex: 3, "models/props/door.mdl");
        track.Add(100, new ScenePose { X = 0f, Yaw = 0f });
        track.Add(100 + UpdateGap, new ScenePose { X = 4f, Yaw = 40f });

        ScenePose pose = track.At(Between(track, 0.5))!.Value;

        pose.EyeYaw.ShouldBeNull();
        pose.EyePitch.ShouldBeNull();
    }

    [Test]
    public void PlayersAt_BetweenTwoUpdates_CarriesTheInterpolatedEyeAngles()
    {
        // The wiring: the player the cameras follow and the body is posed from carries the track's eye angles.
        DemoTimeline timeline = DemoTimeline.Build(SyntheticPlayer.DemoOfEyesOverTicks(
            0.015f, (100, 0f, 0f, 0f), (100 + UpdateGap, 4f, 40f, -40f), (100 + (2 * UpdateGap), 8f, 40f, -40f)));
        ScenePropTrack track = timeline.TrackFor(1).ShouldNotBeNull();
        double moment = 100 + track.InterpolationDelayTicks + (UpdateGap / 2d);
        ScenePose drawn = track.At(moment).ShouldNotBeNull();
        List<ScenePlayer> players = [];

        timeline.PlayersAt(moment, players);

        // The track is the oracle: the conformance tests above pin its numbers. The control: the whole tick's frame
        // states the arrived eyes, 40 and -40, so a player carried from the frame cannot pass.
        ScenePlayer player = players.ShouldHaveSingleItem();
        ScenePlayer stated = timeline.PlayersAt((int)moment).ShouldHaveSingleItem();
        stated.EyeYaw.ShouldNotBeNull().ShouldBe(40f, 0.1f);
        drawn.EyeYaw.ShouldNotBeNull().ShouldBeLessThan(30f, "the track's sample lies between the two updates");

        player.EyeYaw.ShouldBe(drawn.EyeYaw);
        player.EyePitch.ShouldBe(drawn.EyePitch);
    }

    /// <summary>A player track turning from one (yaw, pitch) to another over one update gap, moving so each update latches.</summary>
    private static ScenePropTrack Turning((float Yaw, float Pitch) from, (float Yaw, float Pitch) to)
    {
        ScenePropTrack track = new(entityIndex: 1, "models/player/scout.mdl");
        track.Add(100, Player(0f, from));
        track.Add(100 + UpdateGap, Player(4f, to));

        return track;
    }

    /// <summary>A pose as <c>RecordProp</c> builds a player's: the wire's eye angles in its angles and, normalised, its eyes.</summary>
    private static ScenePose Player(float x, (float Yaw, float Pitch) eyes) => new()
    {
        X = x,
        Pitch = eyes.Pitch,
        Yaw = eyes.Yaw,
        EyePitch = DemoTimeline.NormalizeAngle(eyes.Pitch),
        EyeYaw = DemoTimeline.NormalizeAngle(eyes.Yaw),
    };

    private static double Between(ScenePropTrack track, double fraction) =>
        100 + track.InterpolationDelayTicks + (fraction * UpdateGap);
}
