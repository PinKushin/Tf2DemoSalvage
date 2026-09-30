using Tf2DemoSalvage.Core.Scene;
using Tf2DemoSalvage.SdkReference;

namespace Tf2DemoSalvage.Core.Tests.Scene;

/// <summary>
/// `CTFPlayerShared::IsLoser` and `IsLoserStateStunned`, against <c>tf_player_shared.cpp</c> (B112).
/// </summary>
/// <remarks>
/// **Where the double jump's loser form comes from.** <c>DoAnimationEvent</c> asks
/// <c>pPlayer-&gt;m_Shared.IsLoser()</c> at the moment the event fires and plays
/// <c>ACT_MP_DOUBLEJUMP_LOSERSTATE</c> when it answers yes (<c>tf_playeranimstate.cpp:1196</c>), and the
/// answer is this (<c>tf_player_shared.cpp:13654</c>):
///
/// <code>
/// if ( tf_always_loser.GetBool() )                   return true;
/// if ( !TFGameRules() )                              return false;
/// if ( TFGameRules()->IsMatchTypeCompetitive() )     return false;   // "No loser mode in competitive"
/// if ( State_Get() != GR_STATE_TEAM_WIN )            return IsLoserStateStunned();
/// bLoser = GetWinningTeam() != GetTeamNumber();
/// if ( bLoser &amp;&amp; spy &amp;&amp; InCond( TF_COND_DISGUISED ) &amp;&amp; GetDisguiseTeam() == GetWinningTeam() ) bLoser = false;
/// </code>
///
/// and <c>IsLoserStateStunned</c> (<c>:9966</c>) is an active stun — on the client
/// <c>m_iStunIndex &gt;= 0</c> (<c>:7475</c>) — under <c>TF_COND_STUNNED</c> whose flags carry
/// <c>TF_STUN_LOSER_STATE</c>. That is the half that works outside humiliation: a Halloween scare.
///
/// **A stalemate is a win for nobody.** It ends in <c>GR_STATE_TEAM_WIN</c> with no winning team, so
/// every team differs from it and both sides lose — which is what TF2 shows.
///
/// **No game rules entity is the engine's null <c>TFGameRules()</c>**, and the port reads it as a round
/// state it was never told: the era specimens before 2009 carry no <c>m_iRoundState</c> at all.
/// </remarks>
public sealed class LoserStateConformanceTests
{
    private const string TfPlayerShared = "src/game/shared/tf/tf_player_shared.cpp";

    private const int Red = SceneTeams.Red;
    private const int Blu = SceneTeams.Blu;
    private const int Scout = 1;
    private const int Spy = 8;

    /// <summary>`GR_STATE_TEAM_WIN` (teamplayroundbased_gamerules.h:63).</summary>
    private const int TeamWin = 5;

    /// <summary>`GR_STATE_RND_RUNNING` (:60).</summary>
    private const int RoundRunning = 4;

    /// <summary>`TF_STUN_LOSER_STATE` (tf_shareddefs.h:1339).</summary>
    private const int LoserStateStun = 1 << 6;

    /// <summary>`TF_STUN_CONTROLS` (tf_shareddefs.h:1334) — a stun, but not the loser's.</summary>
    private const int ControlsStun = 1 << 1;

    /// <summary>`m_nPlayerCond` with no condition set.</summary>
    private const int NoCondition = 0;

    /// <summary>`m_nPlayerCond` with only `TF_COND_STUNNED` (tf_shareddefs.h:705).</summary>
    private const int Stunned = 1 << 15;

    /// <summary>`m_nPlayerCond` with only `TF_COND_DISGUISED` (tf_shareddefs.h:693).</summary>
    private const int Disguised = 1 << 3;

    [Test]
    public void IsLoser_TheSdksBody_IsTheShapeThePortReads()
    {
        Text(TfPlayerShared).ShouldMatch(
            @"(?s)bool\s+CTFPlayerShared::IsLoser\(\s*void\s*\)\s*\{\s*if\s*\(\s*tf_always_loser\.GetBool\(\)\s*\)\s*return\s+true;" +
            @"\s*if\s*\(\s*!TFGameRules\(\)\s*\)\s*return\s+false;" +
            @".{0,80}?if\s*\(\s*TFGameRules\(\)->IsMatchTypeCompetitive\(\)\s*\)\s*return\s+false;" +
            @"\s*if\s*\(\s*TFGameRules\(\)->State_Get\(\)\s*!=\s*GR_STATE_TEAM_WIN\s*\)\s*\{\s*if\s*\(\s*IsLoserStateStunned\(\)\s*\)\s*return\s+true;" +
            @".{0,80}?bool\s+bLoser\s*=\s*TFGameRules\(\)->GetWinningTeam\(\)\s*!=\s*m_pOuter->GetTeamNumber\(\);" +
            @".{0,200}?if\s*\(\s*bLoser\s*&&\s*iClass\s*==\s*TF_CLASS_SPY\s*\)" +
            @".{0,40}?if\s*\(\s*InCond\(\s*TF_COND_DISGUISED\s*\)\s*&&\s*GetDisguiseTeam\(\)\s*==\s*TFGameRules\(\)->GetWinningTeam\(\)\s*\)" +
            @"\s*\{\s*bLoser\s*=\s*false;");
    }

    [Test]
    public void IsLoserStateStunned_TheSdksBody_IsAnActiveStunCarryingTheLoserFlag()
    {
        Text(TfPlayerShared).ShouldMatch(
            @"(?s)bool\s+CTFPlayerShared::IsLoserStateStunned\(\s*void\s*\)\s*const\s*\{\s*if\s*\(\s*GetActiveStunInfo\(\)\s*\)" +
            @"\s*\{\s*if\s*\(\s*InCond\(\s*TF_COND_STUNNED\s*\)\s*&&\s*\(\s*m_iStunFlags\s*&\s*TF_STUN_LOSER_STATE\s*\)\s*\)\s*return\s+true;");

        // The client's half of GetActiveStunInfo: an index of -1 means no stun, whatever the flags say.
        Text(TfPlayerShared).ShouldMatch(
            @"return\s*\(\s*m_iStunIndex\s*>=\s*0\s*\)\s*\?\s*const_cast<stun_struct_t\*>\(\s*&m_ActiveStunInfo\s*\)\s*:\s*NULL;");
    }

    // One row per branch, the expectation read off the lines named. The columns are the engine's inputs
    // in IsLoser's own order: tf_always_loser, IsMatchTypeCompetitive, State_Get, GetWinningTeam, then the
    // player's team, class, m_nPlayerCond, disguise team, m_iStunIndex and m_iStunFlags.
    [TestCase(false, false, TeamWin, Blu, Red, Scout, NoCondition, null, null, null, true, TestName = "IsLoser_OnTheLosingTeamDuringHumiliation_IsTrue", Description = ":13674")]
    [TestCase(false, false, TeamWin, Blu, Blu, Scout, NoCondition, null, null, null, false, TestName = "IsLoser_OnTheWinningTeam_IsFalse", Description = ":13674")]
    [TestCase(false, false, TeamWin, SceneTeams.Unassigned, Blu, Scout, NoCondition, null, null, null, true, TestName = "IsLoser_AfterAStalemate_EveryTeamLoses", Description = ":13674")]
    [TestCase(false, false, RoundRunning, Blu, Red, Scout, NoCondition, null, null, null, false, TestName = "IsLoser_WhileTheRoundRuns_IsFalse", Description = ":13666-13671")]
    [TestCase(false, false, RoundRunning, null, Red, Scout, Stunned, null, 0, LoserStateStun, true, TestName = "IsLoser_LoserStateStunnedWhileTheRoundRuns_IsTrue", Description = ":13668, :9970")]
    [TestCase(false, false, RoundRunning, null, Red, Scout, Stunned, null, 0, ControlsStun, false, TestName = "IsLoser_StunnedWithoutTheLoserFlag_IsFalse", Description = ":9970")]
    [TestCase(false, false, RoundRunning, null, Red, Scout, Stunned, null, -1, LoserStateStun, false, TestName = "IsLoser_TheLoserFlagWithNoActiveStun_IsFalse", Description = ":7475")]
    [TestCase(false, false, RoundRunning, null, Red, Scout, NoCondition, null, 0, LoserStateStun, false, TestName = "IsLoser_TheLoserFlagOutsideTheStunnedCondition_IsFalse", Description = ":9970")]
    [TestCase(false, true, TeamWin, Blu, Red, Scout, NoCondition, null, null, null, false, TestName = "IsLoser_InACompetitiveMatch_IsFalse", Description = ":13663")]
    [TestCase(true, false, TeamWin, Blu, Blu, Scout, NoCondition, null, null, null, true, TestName = "IsLoser_ForcedByTheConVar_IsTrueForTheWinnerToo", Description = ":13656")]
    [TestCase(true, true, null, null, Red, Scout, NoCondition, null, null, null, true, TestName = "IsLoser_ForcedByTheConVar_OutranksEverythingBelowIt", Description = ":13656")]
    [TestCase(false, false, null, null, Red, Scout, Stunned, null, 0, LoserStateStun, false, TestName = "IsLoser_WithNoGameRules_IsFalse", Description = ":13659")]
    [TestCase(false, false, TeamWin, Blu, Red, Spy, Disguised, Blu, null, null, false, TestName = "IsLoser_ASpyDisguisedAsTheWinners_IsHidden", Description = ":13679-13683")]
    [TestCase(false, false, TeamWin, Blu, Red, Spy, Disguised, Red, null, null, true, TestName = "IsLoser_ASpyDisguisedAsHisOwnTeam_Loses", Description = ":13681")]
    [TestCase(false, false, TeamWin, Blu, Red, Spy, NoCondition, Blu, null, null, true, TestName = "IsLoser_ASpyUndisguisedWithAStaleDisguiseTeam_Loses", Description = ":13681 InCond")]
    [TestCase(false, false, TeamWin, Blu, Red, Scout, Disguised, Blu, null, null, true, TestName = "IsLoser_ANonSpyWithTheDisguisedCondition_Loses", Description = ":13679 TF_CLASS_SPY")]
    public void IsLoser_EachBranch_AnswersAsTheEngineDoes(
        bool alwaysLoser,
        bool matchTypeCompetitive,
        int? roundState,
        int? winningTeam,
        int team,
        int playerClass,
        int playerCond,
        int? disguiseTeam,
        int? stunIndex,
        int? stunFlags,
        bool expected)
    {
        LoserState.IsLoser(
            alwaysLoser: alwaysLoser,
            matchTypeCompetitive: matchTypeCompetitive,
            roundState: roundState,
            winningTeam: winningTeam,
            team: team,
            playerClass: playerClass,
            conditions: new PlayerConditions(playerCond, 0, 0, 0, 0),
            disguiseTeam: disguiseTeam,
            stunIndex: stunIndex,
            stunFlags: stunFlags)
            .ShouldBe(expected);
    }

    [Test]
    public void IsMatchTypeCompetitive_OfEachRegisteredGroup_IsTheLadderAndThePlaceholderOnly()
    {
        // tf_gamerules.cpp:2237 through the match descriptions: ladder 6v6 (2) and the event placeholder
        // (8) are MATCH_TYPE_COMPETITIVE, casual 12v12 (7) is MATCH_TYPE_CASUAL, and -1 is no match group.
        // One rule, now read from two places — the HUD's rules and the gesture's — so it is one method.
        SceneGameRules.MatchTypeCompetitive(2).ShouldBeTrue();
        SceneGameRules.MatchTypeCompetitive(8).ShouldBeTrue();
        SceneGameRules.MatchTypeCompetitive(7).ShouldBeFalse();
        SceneGameRules.MatchTypeCompetitive(-1).ShouldBeFalse();
        new SceneGameRules(false, 0, false) { MatchGroup = 2 }.IsMatchTypeCompetitive.ShouldBeTrue(
            "the instance property must be the same rule, not a second copy of it");
    }

    private static string Text(string path)
    {
        if (!SourceSdk.Available)
        {
            Assert.Ignore(SourceSdk.Missing);
        }

        return SourceSdk.Text(path).ShouldNotBeNull(path);
    }
}
