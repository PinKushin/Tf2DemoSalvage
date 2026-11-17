using Tf2DemoSalvage.Core.Schema;
using Tf2DemoSalvage.Core.Scene;

namespace Tf2DemoSalvage.Core.Tests.Scene;

/// <summary>`C_TFPlayer::ShouldDrawSpyAsDisguised`, which decides whether a corpse takes its player's model (B763).</summary>
/// <remarks>
/// <code>
/// // c_tf_player.cpp:10656-10675
/// if ( GetLocalPlayer() &amp;&amp; InCond( TF_COND_DISGUISED ) &amp;&amp; GetEnemyTeam( GetTeamNumber() ) == local team )
///     return !( GetDisguiseClass() == TF_CLASS_SPY &amp;&amp; GetDisguiseTeam() == local team );
/// return false;
/// </code>
/// A BLU spy, disguised as a RED medic, seen by RED: drawn disguised. Each case changes one input.
/// </remarks>
public sealed class DrawnAsDisguiseConformanceTests
{
    private const int Spy = 8;
    private const int Medic = 5;

    [TestCase(SceneTeams.Blu, SceneTeams.Red, true, Medic, SceneTeams.Red, true, TestName = "DrawnAsDisguise_ADisguisedEnemy_IsDrawnDisguised")]
    [TestCase(SceneTeams.Blu, SceneTeams.Blu, true, Medic, SceneTeams.Red, false, TestName = "DrawnAsDisguise_ADisguisedTeammate_IsNot")]
    [TestCase(SceneTeams.Blu, SceneTeams.Red, false, Medic, SceneTeams.Red, false, TestName = "DrawnAsDisguise_AnUndisguisedEnemy_IsNot")]
    [TestCase(SceneTeams.Blu, SceneTeams.Red, true, Spy, SceneTeams.Red, false, TestName = "DrawnAsDisguise_AnEnemyDisguisedAsOurSpy_IsNot")]
    [TestCase(SceneTeams.Red, SceneTeams.Blu, true, Spy, SceneTeams.Blu, false, TestName = "DrawnAsDisguise_ARedEnemyDisguisedAsABluSpySeenByBlu_IsNot")]
    [TestCase(SceneTeams.Blu, SceneTeams.Red, true, Spy, SceneTeams.Blu, true, TestName = "DrawnAsDisguise_AnEnemyDisguisedAsHisOwnSpy_IsDrawnDisguised")]
    [TestCase(SceneTeams.Blu, null, true, Medic, SceneTeams.Red, false, TestName = "DrawnAsDisguise_WithNoLocalPlayer_IsNot")]
    public void DrawnAsDisguise_ASpy_IsDrawnDisguisedAsTheEngineSays(
        int team, int? local, bool disguised, int disguiseClass, int disguiseTeam, bool expected)
    {
        EntityState spy = new(1, 0, 0, "CTFPlayer");

        spy.Set("DT_BaseEntity.m_iTeamNum", PropertyValue.FromInt(team));
        spy.Set("DT_TFPlayerShared.m_nPlayerCond", PropertyValue.FromInt(disguised ? 1 << PlayerConditions.Disguised : 0));
        spy.Set("DT_TFPlayerShared.m_nDisguiseClass", PropertyValue.FromInt(disguiseClass));
        spy.Set("DT_TFPlayerShared.m_nDisguiseTeam", PropertyValue.FromInt(disguiseTeam));

        DemoTimeline.DrawnAsDisguise(spy, local).ShouldBe(expected);
    }
}
