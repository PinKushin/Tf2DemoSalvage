using System.Collections.Generic;
using System.Linq;

using Tf2DemoSalvage.Core.Scene;
using Tf2DemoSalvage.Core.Schema;

namespace Tf2DemoSalvage.Core.Tests.Scene;

/// <summary>
/// Which way a player faces, and that it reaches the scene rather than stopping at the track.
/// </summary>
/// <remarks>
/// **Converted from <c>PlayerFacingTests</c>, which cost about 25 seconds of the corpus suite.**
/// Its two assertions were a plumbing check against the track's own number, and a control
/// demanding "more than eight distinct yaws somewhere in the corpus" — the second existing because
/// yaw reported as a constant is indistinguishable from yaw plumbed correctly when every player
/// happens to face the same way.
///
/// A written demo removes the hedge. Two players are given deliberately different eye angles, so
/// "they do not all face the same way" becomes "this one faces here and that one faces there".
/// </remarks>
public sealed class SyntheticFacingTests
{
    /// <summary>The tick B442 was measured at.</summary>
    private const int B442Tick = 5541;

    /// <summary>The recorder's eye yaw at <see cref="B442Tick"/>: 209.384, in the fixture's −180..180.</summary>
    private const float LookingYaw = 209.384f - 360f;

    /// <summary>Half a step of the fixture's 12-bit eye yaw over 360 degrees, which is as close as it can say.</summary>
    private const float EyeYawQuantum = 0.05f;

    [Test]
    public void PlayersAt_TheYaw_IsWhatTheTrackHolds()
    {
        // **Plumbing rather than decode**, which is what the corpus version measured too: whatever
        // the pose says, the player must report. The number itself comes from FeetYaw, which lags
        // the eyes deliberately, so predicting it would be testing that class rather than this
        // path — the track is the right oracle here.
        DemoTimeline timeline = DemoTimeline.Build(SyntheticPlayer.Demo(
            SyntheticPlayer.OriginTable.NonLocal,
            tick: 66,
            (1, Facing(eyeYaw: 45f))));

        ScenePlayer player = timeline.PlayersAt(66).ShouldHaveSingleItem();

        ScenePropTrack track = timeline.TrackFor(1).ShouldNotBeNull();
        ScenePose pose = track.At(66.0).ShouldNotBeNull();

        player.Yaw.ShouldBe(pose.Yaw);
    }

    [Test]
    public void PlayersAt_TwoPlayersFacingDifferentWays_ReportDifferentYaws()
    {
        // **The control, and the whole reason the corpus version needed a corpus.** A yaw hard-wired
        // to a constant satisfies the plumbing test above, because the track would hold that same
        // constant. Only two players looking different ways can separate them — and on found data
        // that meant hoping a recording contained it, asserted as "more than eight distinct values
        // somewhere".
        //
        // Here the two are put at opposite headings by construction.
        DemoTimeline timeline = DemoTimeline.Build(SyntheticPlayer.Demo(
            SyntheticPlayer.OriginTable.NonLocal,
            tick: 66,
            (1, Facing(eyeYaw: 0f)),
            (2, Facing(eyeYaw: 90f))));

        float[] yaws =
        [
            .. timeline.PlayersAt(66)
                .OrderBy(player => player.EntityIndex)
                .Select(player => player.Yaw),
        ];

        yaws.Length.ShouldBe(2);
        yaws[0].ShouldNotBe(yaws[1], "two players given different eye angles report one yaw");
    }

    [Test]
    public void PlayersAt_TheEyePitch_IsCarriedSeparatelyFromTheYaw()
    {
        // Pitch and yaw arrive as two elements of the same array — m_angEyeAngles[0] and [1] — so
        // a reader taking the wrong element gets a plausible angle from the wrong axis. A player
        // looking sharply up with zero yaw separates them: the pitch is large and the yaw is not.
        DemoTimeline timeline = DemoTimeline.Build(SyntheticPlayer.Demo(
            SyntheticPlayer.OriginTable.NonLocal,
            tick: 66,
            (1, Facing(eyeYaw: 0f, eyePitch: -60f))));

        ScenePlayer player = timeline.PlayersAt(66).ShouldHaveSingleItem();

        player.EyePitch.ShouldNotBeNull().ShouldBe(-60f, 1f);
        player.EyeYaw.ShouldNotBeNull().ShouldBe(0f, 1f);
    }

    [Test]
    public void PlayersAt_ARecorderLookingAwayFromItsEnter_FacesItsLocalEyeYaw()
    {
        // **The drawn body, and B442's cause.** The recorder's ENTER stated 336.422 in both exclusive tables;
        // every update since said 209.384 in the local one. The client holds one m_angEyeAngles and the last
        // write is what it holds (c_tf_player.cpp:3745-3746, :3764-3765); this reported 336.422 for the whole
        // demo, so the recorder's body faced the way they spawned.
        ScenePlayer recorder = DemoTimeline.Build(B442Recorder()).PlayersAt(B442Tick).ShouldHaveSingleItem();

        recorder.EyeYaw.ShouldNotBeNull().ShouldBe(LookingYaw, EyeYawQuantum);
    }

    [Test]
    public void PlayersAt_ARecorderRunningWhereItLooks_DrivesMoveXForward()
    {
        // **Tick 5541 of movement-test-pov-cp_process, input for input** (B442): travel of (-3.093, -1.843) a
        // tick (240 units a second, its networked m_vecVelocity), 209.384 in the local table since tick 5540,
        // and 336.422 left in the non-local one by the tick-0 ENTER. ComputePoseParam_MoveYaw
        // (multiplayer_animstate.cpp:1566-1620), with the estimate yaw atan2(-1.843, -3.093) = -149.211 and the
        // eye yaw 209.384 - 360 = -150.616:
        //
        //     flYaw = AngleNormalize( -( -150.616 - -149.211 ) ) = 1.405
        //     x =  cos 1.405 = 0.99970,  y = -sin 1.405 = -0.02452,  pushed out by 0.99970: (1.000, -0.0245)
        //
        // Read from the stale 336.422 instead, flYaw is -125.63 and the answer is (-0.717, 1.000) — the
        // backward half of the grid, which is what the corpus reported (-0.674 there, from its own heading).
        List<ScenePlayer> players = [];
        DemoTimeline.Build(B442Recorder()).PlayersAt((double)B442Tick, players);

        ScenePlayer recorder = players.ShouldHaveSingleItem();

        recorder.MoveX.ShouldBe(1f, 1e-3f);
        recorder.MoveY.ShouldBe(-0.0245f, 1e-3f);
    }

    /// <summary>The recorder of <see cref="PlayersAt_ARecorderRunningWhereItLooks_DrivesMoveXForward"/>.</summary>
    /// <remarks>
    /// One hundred ticks of the same travel, ENTERing at tick 5441 where that travel puts them at 5541's
    /// recorded origin — the heading window and the interpolation delay both sit well inside the run.
    /// </remarks>
    private static byte[] B442Recorder() => SyntheticPlayer.DemoOfARecorder(
        intervalPerTick: 0.015f,
        ticks: (B442Tick - 100, B442Tick),
        enter: (-1481.491f + (100 * 3.093f), -2408.251f + (100 * 1.843f), 336.422f - 360f),
        perTick: (-3.093f, -1.843f),
        eyeYaw: LookingYaw);

    /// <summary>A positioned player looking in a chosen direction.</summary>
    private static Dictionary<string, PropertyValue> Facing(
        float eyeYaw, float eyePitch = 0f) => new()
        {
            ["m_vecOrigin"] = PropertyValue.FromVectorXY(0f, 0f),
            ["m_vecOrigin[2]"] = PropertyValue.FromFloat(0f),
            ["m_iTeamNum"] = PropertyValue.FromInt(SceneTeams.Red),
            ["m_lifeState"] = PropertyValue.FromInt(0),
            ["m_angEyeAngles[0]"] = PropertyValue.FromFloat(eyePitch),
            ["m_angEyeAngles[1]"] = PropertyValue.FromFloat(eyeYaw),
        };
}
