using System;
using System.Collections.Generic;

using Tf2DemoSalvage.Core.Scene;

namespace Tf2DemoSalvage.Scene;

/// <summary>
/// The team wall across a spawn doorway, which its own team does not see.
/// </summary>
/// <remarks>
/// **`C_FuncRespawnRoomVisualizer::DrawModel`, `c_func_respawnroom.cpp:47`**, whose comment is the
/// whole rule — *"Don't draw for friendly players"*:
///
/// <code>
///   int C_FuncRespawnRoomVisualizer::DrawModel( int flags )
///   {
///       if ( TFGameRules()-&gt;State_Get() == GR_STATE_TEAM_WIN )
///           return 1;                                   // nobody sees it once the round is won
///
///       C_BasePlayer *pLocalPlayer = C_BasePlayer::GetLocalPlayer();
///       if ( pLocalPlayer &amp;&amp; pLocalPlayer-&gt;GetTeamNumber() == GetTeamNumber() )
///           return 1;                                   // your own team's wall is invisible to you
///
///       return BaseClass::DrawModel( flags );
///   }
/// </code>
///
/// `return 1` is "handled, drew nothing" rather than "drew".
///
/// **Measured on `cp_fulgur` at tick 900: nine of these are in the draw list**, three of them
/// standing inside the stage-one setup gates at (5416 -2168 512), (5568 -2552 384) and
/// (5720 -3248 464) — the exact doorway the owner reported as *"the wrong grates"* with the frame
/// missing. This project drew every one of them to everybody, so a BLU player in BLU spawn had a
/// team-coloured wall between them and their own gate.
///
/// **`pLocalPlayer &amp;&amp;` is load-bearing and is why this asks "on the recorder's team" rather than
/// "is an enemy".** A SourceTV recording has no local player, so the engine falls straight through
/// and draws every visualizer. Asking about enmity would answer false there too and hide them all.
/// </remarks>
public static class RespawnRoomVisibility
{
    /// <summary>The class the map's spawn walls are.</summary>
    private const string Visualizer = "CFuncRespawnRoomVisualizer";

    /// <summary>The spawn-door force field, which shares only the team-win rule (B359).</summary>
    private const string ForceField = "CFuncForceField";

    /// <summary><c>GR_STATE_TEAM_WIN</c>, <c>teamplayroundbased_gamerules.h:63</c>.</summary>
    /// <remarks>
    /// **Counted from the enum rather than remembered**, because it has no explicit values past the
    /// first: `GR_STATE_INIT = 0`, then PREGAME, STARTGAME, PREROUND, RND_RUNNING, and TEAM_WIN is
    /// the sixth — **5, not 4**. A first draft here said 4, which is `GR_STATE_RND_RUNNING`: the
    /// state a round spends almost all of its time in, so that mistake would have hidden every
    /// spawn wall for the whole match instead of for the seconds after a win.
    /// </remarks>
    public const int TeamWin = 5;

    /// <summary>Which props survive the spawn wall's own rule.</summary>
    /// <param name="drawn">Everything this moment would draw.</param>
    /// <param name="roundState">
    /// <c>m_iRoundState</c> from the game rules, or <c>null</c> when the demo does not say.
    /// </param>
    /// <returns>The props that survive, in the shape <c>DrawList.KeepOnly</c> takes.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="drawn"/> is null.</exception>
    public static IReadOnlyList<SceneProp> Visible(IReadOnlyList<SceneProp> drawn, int? roundState)
    {
        ArgumentNullException.ThrowIfNull(drawn);

        List<SceneProp>? kept = null;

        for (int index = 0; index < drawn.Count; index++)
        {
            SceneProp prop = drawn[index];

            if (Keep(prop, roundState))
            {
                kept?.Add(prop);
                continue;
            }

            // **Allocated only once something is actually removed**, which on most maps and most
            // frames is never. The overwhelmingly common answer is the list handed in.
            if (kept is null)
            {
                kept = new List<SceneProp>(drawn.Count);

                for (int earlier = 0; earlier < index; earlier++)
                {
                    kept.Add(drawn[earlier]);
                }
            }
        }

        return kept ?? drawn;
    }

    private static bool Keep(SceneProp prop, int? roundState)
    {
        bool visualizer = string.Equals(prop.ClassName, Visualizer, StringComparison.Ordinal);

        // **The force field obeys the FIRST of the visualizer's two rules and not the second**
        // (B359). `C_FuncForceField::DrawModel` (`c_func_forcefield.cpp:28`) is two lines:
        //
        //   // Don't draw for anyone during a team win
        //   if ( TFGameRules()->State_Get() == GR_STATE_TEAM_WIN )
        //       return 1;
        //   return BaseClass::DrawModel( flags );
        //
        // No local-player test at all — you SEE your own team's field, which is how you watch
        // enemies fail to walk through it. Giving it the wall's own-team rule as well would delete
        // every field the recorder's team owns.
        //
        // **Handled here rather than in a class of its own because the round-state rule is one
        // rule.** Two files would be two places to change when the state enum moves, and the whole
        // of the difference between the entities is the `visualizer` test below.
        if (!visualizer && !string.Equals(prop.ClassName, ForceField, StringComparison.Ordinal))
        {
            return true;
        }

        // **Nobody sees it once the round is won**, so the losing team can be chased into their
        // own spawn. Null means the demo did not tell us the round state, and drawing is then the
        // engine's answer for every state but one.
        if (roundState == TeamWin)
        {
            return false;
        }

        return !visualizer || !prop.OfRecordersTeam;
    }
}
