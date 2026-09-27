using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace Tf2DemoSalvage.Scene.Hud;

/// <summary>`C_TFTeam::Get_Localized_Name` after `UpdateTeamName` (c_tf_team.cpp:111-209).</summary>
public static class TfTeamNames
{
    private const int TeamSpectator = 1;
    private const int TeamRed = 2;
    private const int TeamBlue = 3;

    /// <summary>A team's name: a premade party's leader or an event team in matchmaking, a tournament name, else localized.</summary>
    /// <param name="teamNumber">The team.</param>
    /// <param name="state">The game state.</param>
    /// <param name="find">`g_pVGuiLocalize->Find`, the token with its '#'.</param>
    /// <returns>The name.</returns>
    public static string Localized(int teamNumber, HudState state, Func<string, string?> find)
    {
        ArgumentNullException.ThrowIfNull(find);

        if (state.ConVars.GetBool("mp_tournament") && teamNumber is TeamRed or TeamBlue)
        {
            if (state.Rules.IsCompetitiveMode)
            {
                if (state.Rules.HasPremadeParties || state.Rules.EventTeamStatus != 0)
                {
                    string format = find("#TF_Team_PartyLeader") ?? "%s";

                    if (state.Rules.EventTeamStatus != 0)
                    {
                        // "INVADERS_ARE_PYRO = 1; INVADERS_ARE_HEAVY = 2;" (:131-137).
                        bool pyro = (teamNumber == TeamBlue) == (state.Rules.EventTeamStatus == 1);

                        return Printf(format, find(pyro ? "#TF_Pyro" : "#TF_HWGuy") ?? string.Empty);
                    }

                    int leader = teamNumber == TeamRed ? state.Rules.PartyLeaderRed : state.Rules.PartyLeaderBlue;

                    if (state.ScoreboardPlayers is { } resource && resource.Any(slot => slot.EntityIndex == leader && slot.Connected))
                    {
                        return Printf(format, SafeName(state.Names?.GetValueOrDefault(leader) ?? string.Empty));
                    }
                }
            }
            else
            {
                string name = state.ConVars.GetString(teamNumber == TeamBlue ? "mp_tournament_blueteamname" : "mp_tournament_redteamname");

                if (name.Length > 0)
                {
                    return name;
                }
            }
        }

        return teamNumber switch
        {
            TeamBlue => find("#TF_BlueTeam_Name") ?? "BLU",
            TeamRed when state.Rules.MannVsMachine => find("#TF_Defenders") ?? "DEFENDERS",
            TeamRed => find("#TF_RedTeam_Name") ?? "RED",
            TeamSpectator => find("#TF_Spectators") ?? "SPECTATORS",
            _ => string.Empty,
        };
    }

    /// <summary>`UTIL_MakeSafeName` (cdll_util.cpp:801): a leading '#' and every '%' become '*', and '&amp;' doubles.</summary>
    /// <param name="name">The player's name.</param>
    /// <returns>The name vgui can show.</returns>
    public static string SafeName(string name)
    {
        ArgumentNullException.ThrowIfNull(name);

        StringBuilder safe = new(name.Length);

        for (int index = 0; index < name.Length; index++)
        {
            char character = name[index];

            if ((index == 0 && character == '#') || character == '%')
            {
                safe.Append('*');
            }
            else if (character == '&')
            {
                safe.Append("&&");
            }
            else
            {
                safe.Append(character);
            }
        }

        return safe.ToString();
    }

    /// <summary>`V_swprintf_safe( out, pFormat, value )` of a format with one `%s` or `%ls`.</summary>
    private static string Printf(string format, string value)
    {
        int at = format.IndexOf("%ls", StringComparison.Ordinal);

        if (at >= 0)
        {
            return string.Concat(format.AsSpan(0, at), value, format.AsSpan(at + 3));
        }

        at = format.IndexOf("%s", StringComparison.Ordinal);

        return at >= 0 ? string.Concat(format.AsSpan(0, at), value, format.AsSpan(at + 2)) : format;
    }
}
