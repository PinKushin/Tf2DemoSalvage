using System.Collections.Generic;

using Tf2DemoSalvage.Core.Schema;
using Tf2DemoSalvage.Core.Scene;

namespace Tf2DemoSalvage.Core.Tests.Scene;

/// <summary>
/// `GetLastDamageTimeMvMOnly()` (c_tf_player.h:378), which `C_TFPlayer::GetSkin` reads for a hidden-unless-damaged
/// übercharge (c_tf_player.cpp:7829, B776). The client receives `m_flMvMLastDamageTime` under two names:
/// `RecvPropFloat( RECVINFO( m_flMvMLastDamageTime ) )` and `RECVINFO_NAME( m_flMvMLastDamageTime, "m_flLastDamageTime" )
/// // Renamed` (c_tf_player.cpp:3803-3804).
/// </summary>
public sealed class ScenePlayerLastDamageTimeTests
{
    [TestCase("m_flMvMLastDamageTime", TestName = "PlayersAt_TheMvMLastDamageTimeSent_ReachesTheScenePlayer")]
    [TestCase("m_flLastDamageTime", TestName = "PlayersAt_TheOlderLastDamageTimeName_ReachesTheScenePlayer")]
    public void PlayersAt_ALastDamageTimeSent_ReachesTheScenePlayer(string name)
    {
        DemoTimeline timeline = DemoTimeline.Build(SyntheticPlayer.Demo(
            new Dictionary<string, PropertyValue>
            {
                ["m_vecOrigin"] = PropertyValue.FromVectorXY(0f, 0f),
                ["m_vecOrigin[2]"] = PropertyValue.FromFloat(0f),
                ["m_iTeamNum"] = PropertyValue.FromInt(SceneTeams.Blu),
                ["m_lifeState"] = PropertyValue.FromInt(0),
                [name] = PropertyValue.FromFloat(12.5f),
            }));

        timeline.PlayersAt(66).ShouldHaveSingleItem().LastDamageTimeMvMOnly.ShouldNotBeNull().ShouldBe(12.5f, 0.01f);
    }

    [Test]
    public void PlayersAt_NoLastDamageTimeSent_IsNull() =>
        DemoTimeline.Build(SyntheticPlayer.Demo(
                new Dictionary<string, PropertyValue>
                {
                    ["m_vecOrigin"] = PropertyValue.FromVectorXY(0f, 0f),
                    ["m_vecOrigin[2]"] = PropertyValue.FromFloat(0f),
                    ["m_iTeamNum"] = PropertyValue.FromInt(SceneTeams.Blu),
                    ["m_lifeState"] = PropertyValue.FromInt(0),
                }))
            .PlayersAt(66).ShouldHaveSingleItem().LastDamageTimeMvMOnly.ShouldBeNull();

    /// <remarks>
    /// `GetSkin` returns `m_nForcedSkin` only while `m_bForcedSkin` is set (c_tf_player.cpp:7791-7792); both are received
    /// (:3818-3819). The control is the same skin number without the flag, which forces nothing.
    /// </remarks>
    [TestCase(1, 7, TestName = "PlayersAt_AForcedSkinWithItsFlag_IsTheForcedSkin")]
    [TestCase(0, null, TestName = "PlayersAt_AForcedSkinNumberWithoutItsFlag_ForcesNothing")]
    public void PlayersAt_AForcedSkin_IsReadOnlyUnderItsFlag(int flag, int? expected) =>
        DemoTimeline.Build(SyntheticPlayer.Demo(
                new Dictionary<string, PropertyValue>
                {
                    ["m_vecOrigin"] = PropertyValue.FromVectorXY(0f, 0f),
                    ["m_vecOrigin[2]"] = PropertyValue.FromFloat(0f),
                    ["m_iTeamNum"] = PropertyValue.FromInt(SceneTeams.Blu),
                    ["m_lifeState"] = PropertyValue.FromInt(0),
                    ["m_bForcedSkin"] = PropertyValue.FromInt(flag),
                    ["m_nForcedSkin"] = PropertyValue.FromInt(7),
                }))
            .PlayersAt(66).ShouldHaveSingleItem().ForcedSkin.ShouldBe(expected);
}
