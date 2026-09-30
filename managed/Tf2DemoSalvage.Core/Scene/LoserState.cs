namespace Tf2DemoSalvage.Core.Scene;

/// <summary>
/// <c>CTFPlayerShared::IsLoser</c> and <c>IsLoserStateStunned</c>, from the values the client reads.
/// </summary>
/// <remarks>
/// **Two readers, one rule.** The HUD asks it of the local player each frame (<c>CanPickupBuilding</c>, B112's
/// neighbour) and the gesture context asks it of whoever raised an event, at the moment the event arrives — the
/// double jump plays <c>ACT_MP_DOUBLEJUMP_LOSERSTATE</c> for a loser (<c>tf_playeranimstate.cpp:1196</c>). The
/// inputs come from different places, a sampled <c>ScenePlayer</c> and the live entity table, so the rule takes
/// them as values and neither caller keeps a copy of it.
///
/// <code>
/// if ( tf_always_loser.GetBool() )                   return true;          // tf_player_shared.cpp:13656
/// if ( !TFGameRules() )                              return false;         // :13659
/// if ( TFGameRules()->IsMatchTypeCompetitive() )     return false;         // :13663
/// if ( State_Get() != GR_STATE_TEAM_WIN )            return IsLoserStateStunned();   // :13666-13672
/// bLoser = GetWinningTeam() != GetTeamNumber();                            // :13674
/// if ( bLoser &amp;&amp; spy &amp;&amp; InCond( TF_COND_DISGUISED ) &amp;&amp; GetDisguiseTeam() == GetWinningTeam() )
///     bLoser = false;                                                      // :13679-13684
/// </code>
///
/// **A stalemate is a win for nobody**: the round ends in <c>GR_STATE_TEAM_WIN</c> with no winning team, every
/// team differs from it, and both sides are losers — which is what TF2 shows.
/// </remarks>
public static class LoserState
{
    /// <summary>The ConVar that makes everybody a loser: <c>tf_always_loser</c>, replicated and a cheat.</summary>
    public const string AlwaysLoserConVar = "tf_always_loser";

    /// <summary><c>GR_STATE_TEAM_WIN</c> (<c>teamplayroundbased_gamerules.h:63</c>): someone has won the round.</summary>
    public const int TeamWin = 5;

    /// <summary><c>TF_STUN_LOSER_STATE</c> (<c>tf_shareddefs.h:1339</c>): a stun that plays the loser's animations.</summary>
    public const int LoserStateStun = 1 << 6;

    /// <summary><c>TF_CLASS_SPY</c> (<c>tf_shareddefs.h:214</c>).</summary>
    private const int Spy = 8;

    /// <summary><c>CTFPlayerShared::IsLoser</c> (<c>tf_player_shared.cpp:13654</c>).</summary>
    /// <param name="alwaysLoser"><c>tf_always_loser.GetBool()</c>.</param>
    /// <param name="matchTypeCompetitive"><c>TFGameRules()-&gt;IsMatchTypeCompetitive()</c>.</param>
    /// <param name="roundState">
    /// <c>State_Get()</c>, <c>m_iRoundState</c> — or null when there is no game rules entity, which is the engine's
    /// null <c>TFGameRules()</c>. The era specimens before 2009 carry none.
    /// </param>
    /// <param name="winningTeam"><c>GetWinningTeam()</c>, <c>m_iWinningTeam</c>.</param>
    /// <param name="team">The player's <c>GetTeamNumber()</c>.</param>
    /// <param name="playerClass">The player's <c>GetPlayerClass()-&gt;GetClassIndex()</c>.</param>
    /// <param name="conditions">The player's condition bits.</param>
    /// <param name="disguiseTeam">The player's <c>GetDisguiseTeam()</c>.</param>
    /// <param name="stunIndex">The player's <c>m_iStunIndex</c>.</param>
    /// <param name="stunFlags">The player's <c>m_iStunFlags</c>.</param>
    /// <returns>Whether the player is in the loser state.</returns>
    public static bool IsLoser(
        bool alwaysLoser,
        bool matchTypeCompetitive,
        int? roundState,
        int? winningTeam,
        int? team,
        int? playerClass,
        PlayerConditions conditions,
        int? disguiseTeam,
        int? stunIndex,
        int? stunFlags)
    {
        if (alwaysLoser)
        {
            return true;
        }

        if (roundState is not { } state || matchTypeCompetitive)
        {
            return false;
        }

        if (state != TeamWin)
        {
            return IsLoserStateStunned(conditions, stunIndex, stunFlags);
        }

        bool loser = winningTeam != team;

        // "don't reveal disguised spies" (:13678) — the loser's animations would give the disguise away.
        if (loser && playerClass == Spy && conditions.Has(PlayerConditions.Disguised) && disguiseTeam == winningTeam)
        {
            loser = false;
        }

        return loser;
    }

    /// <summary><c>CTFPlayerShared::IsLoserStateStunned</c> (<c>tf_player_shared.cpp:9966</c>).</summary>
    /// <param name="conditions">The player's condition bits.</param>
    /// <param name="stunIndex">
    /// <c>m_iStunIndex</c>. On the client <c>GetActiveStunInfo()</c> is non-null exactly when it is at least zero
    /// (<c>:7475</c>), and its flags are <c>m_iStunFlags</c> verbatim (<c>:7463</c>) — the client keeps no stun list
    /// of its own.
    /// </param>
    /// <param name="stunFlags"><c>m_iStunFlags</c>.</param>
    /// <returns>Whether an active stun carries <c>TF_STUN_LOSER_STATE</c> under <c>TF_COND_STUNNED</c>.</returns>
    public static bool IsLoserStateStunned(PlayerConditions conditions, int? stunIndex, int? stunFlags) =>
        stunIndex is >= 0 &&
        conditions.Has(PlayerConditions.Stunned) &&
        ((stunFlags ?? 0) & LoserStateStun) != 0;
}
