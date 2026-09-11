using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;

using Tf2DemoSalvage.Core.Net;
using Tf2DemoSalvage.Core.Scene;

namespace Tf2DemoSalvage.Probe.Probes;

/// <summary>
/// Every player's real name against their entity index and user id.
/// </summary>
/// <remarks>
/// **Written because a screenshot's nametag and an entity index kept being conflated.** A red
/// nameplate visible in one player's view names whoever is ON SCREEN — often an enemy the recorder
/// is looking at — and has no necessary relation to whose eyes the camera is using or who owns a
/// given entity. `--spectate` takes a name, `carried` and `props` report only entity indices, and
/// nothing printed both together — so an hour was spent guessing which real player "entity 7" was
/// from what happened to be visible in a picture, guessing wrong more than once.
///
/// <code>
///   roster z1800
///   roster z1800 abelll
/// </code>
///
/// The optional second argument filters by a case-insensitive substring of the name.
/// </remarks>
public sealed class RosterProbe : IProbe
{
    /// <inheritdoc/>
    public string Name => "roster";

    /// <inheritdoc/>
    public string Summary =>
        "every player's real name against their entity index and user id: roster <demo> [name substring]";

    /// <inheritdoc/>
    public void Run(TextWriter output, IReadOnlyList<string> arguments)
    {
        ArgumentNullException.ThrowIfNull(output);
        ArgumentNullException.ThrowIfNull(arguments);

        if (arguments.Count == 0)
        {
            output.WriteLine("roster <demo> [name substring]");
            return;
        }

        if (DemoCorpus.Find(arguments[0], output) is not { } path)
        {
            output.WriteLine($"No demo named '{arguments[0]}'.");
            return;
        }

        string filter = arguments.Count > 1 ? arguments[1] : string.Empty;

        DemoTimeline timeline = DemoTimeline.Build(File.ReadAllBytes(path));

        output.WriteLine(string.Create(
            CultureInfo.InvariantCulture,
            $"{Path.GetFileName(path)}: {timeline.Roster.Count} roster entries"
            + $"{(timeline.RecorderEntityIndex is { } recorder ? $", recorder entity {recorder}" : string.Empty)}"));

        if (timeline.Roster.Count == 0)
        {
            output.WriteLine("  none — the demo carried no player_info string table at all.");
            return;
        }

        foreach (PlayerInfo player in timeline.Roster.Values
            .Where(player => filter.Length == 0 ||
                player.Name.Contains(filter, StringComparison.OrdinalIgnoreCase))
            .OrderBy(player => player.EntityIndex))
        {
            output.WriteLine(string.Create(
                CultureInfo.InvariantCulture,
                $"  entity {player.EntityIndex,4}  user id {player.UserId,6}  "
                + $"{(player.IsBot ? "BOT  " : "     ")}{(player.IsSourceTv ? "STV  " : "     ")}"
                + $"'{player.Name}'"));
        }
    }
}
