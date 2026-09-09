using System;
using System.Collections.Generic;
using System.Globalization;

namespace Tf2DemoSalvage.Core.Net;

/// <summary>
/// Turns what a person typed into the entity they meant — a name, a user id or an index (D153).
/// </summary>
/// <remarks>
/// **Three spellings because a person has three, and only one of them is memorable.** The owner
/// asked for *"a player name or id to first person cam a specific player on boot"*; an entity index
/// is what the viewer already took and is a number nobody knows off the top of their head.
///
/// **Its own type so it can be tested without a demo.** The matching is the part that can be subtly
/// wrong — exact versus partial, case, the SourceTV slot — and a synthetic roster HAS ground truth
/// where a corpus demo only has whoever happens to be in it (D38).
/// </remarks>
public static class PlayerLookup
{
    /// <summary>Which entity the text names.</summary>
    /// <param name="roster">Everybody who played.</param>
    /// <param name="who">A player name, a user id, or an entity index.</param>
    /// <returns>The entity index, or null when nobody matches and the text is not a number.</returns>
    /// <remarks>
    /// **Exact before partial, or a name that is a prefix of another can never be chosen.**
    /// `b4nny` would otherwise lose to `b4nnyPog` whenever the latter joined first — and partial
    /// matching cannot simply be dropped, because competitive names carry clan tags and unicode
    /// that nobody will retype.
    ///
    /// **The SourceTV slot is never a match, whatever it is called.** It is not a player, and
    /// spectating it is the fault `docs/findings/29` records: three identical captures of nothing.
    ///
    /// **A number is a user id BEFORE an entity index**, because a user id is what the demo's own
    /// game events carry, and it falls through to the index so the older spelling keeps working.
    /// </remarks>
    public static int? Resolve(IEnumerable<PlayerInfo> roster, string? who)
    {
        ArgumentNullException.ThrowIfNull(roster);

        if (string.IsNullOrWhiteSpace(who))
        {
            return null;
        }

        string wanted = who.Trim();

        List<PlayerInfo> real = [];

        foreach (PlayerInfo player in roster)
        {
            if (!player.IsSourceTv)
            {
                real.Add(player);
            }
        }

        foreach (PlayerInfo player in real)
        {
            if (string.Equals(player.Name, wanted, StringComparison.OrdinalIgnoreCase))
            {
                return player.EntityIndex;
            }
        }

        foreach (PlayerInfo player in real)
        {
            if (player.Name.Contains(wanted, StringComparison.OrdinalIgnoreCase))
            {
                return player.EntityIndex;
            }
        }

        if (!int.TryParse(wanted, NumberStyles.Integer, CultureInfo.InvariantCulture, out int number))
        {
            return null;
        }

        foreach (PlayerInfo player in real)
        {
            if (player.UserId == number)
            {
                return player.EntityIndex;
            }
        }

        return number;
    }
}
