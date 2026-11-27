using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;

using Tf2DemoSalvage.Core.Scene;

namespace Tf2DemoSalvage.Probe.Probes;

/// <summary>
/// Every change of each player's team, and of whether he is the recorder's enemy, as the timeline reports them (B65).
/// </summary>
/// <remarks>
/// **The team the skin is drawn from**: `C_TFPlayer::GetSkin` switches on `GetTeamNumber()` (c_tf_player.cpp:7795),
/// the entity's own `m_iTeamNum`. Printing the timeline's answer per change, rather than recomputing it, is what lets a
/// run before and after a change to the team's source be diffed line for line.
/// </remarks>
public sealed class PlayerTeamsProbe : IProbe
{
    /// <inheritdoc/>
    public string Name => "player-teams";

    /// <inheritdoc/>
    public string Summary => "each player's team and enemy flag as the timeline reports them, per change (B65): player-teams <demo>";

    /// <inheritdoc/>
    public void Run(TextWriter output, IReadOnlyList<string> arguments)
    {
        ArgumentNullException.ThrowIfNull(output);
        ArgumentNullException.ThrowIfNull(arguments);

        if (arguments.Count == 0 || DemoCorpus.Find(arguments[0], output) is not { } path)
        {
            output.WriteLine("player-teams <demo>");
            return;
        }

        DemoTimeline timeline = ProbeInstall.Timeline(File.ReadAllBytes(path), output);
        Dictionary<int, (int? Team, bool IsEnemy)> last = [];
        int changes = 0;

        foreach (TimelineFrame frame in timeline.Frames)
        {
            foreach (ScenePlayer player in frame.Players)
            {
                (int? Team, bool IsEnemy) now = (player.Team, player.IsEnemy);

                if (last.TryGetValue(player.EntityIndex, out (int? Team, bool IsEnemy) before) && before == now)
                {
                    continue;
                }

                last[player.EntityIndex] = now;
                changes++;
                output.WriteLine(string.Create(
                    CultureInfo.InvariantCulture,
                    $"tick {frame.Tick,6} entity {player.EntityIndex,3} team {now.Team?.ToString(CultureInfo.InvariantCulture) ?? "-"} enemy {now.IsEnemy}"));
            }
        }

        output.WriteLine(string.Create(CultureInfo.InvariantCulture, $"{Path.GetFileName(path)}: {last.Count} players, {changes} changes"));
    }
}
