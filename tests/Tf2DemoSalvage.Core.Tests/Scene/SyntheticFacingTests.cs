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
