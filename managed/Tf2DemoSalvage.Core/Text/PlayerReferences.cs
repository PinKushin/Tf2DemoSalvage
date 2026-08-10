using System;
using System.Collections.Generic;
using System.Globalization;

using Tf2DemoSalvage.Core.Net;

namespace Tf2DemoSalvage.Core.Text;

/// <summary>
/// Turns a game event's numeric player reference into a name, for any output that wants one.
/// </summary>
/// <remarks>
/// Shared rather than duplicated per writer, because the interesting part is the allowlist and a
/// second copy of it would drift. The summary and the trace should not disagree about who a kill
/// belongs to.
/// </remarks>
internal static class PlayerReferences
{
    /// <summary>
    /// Event fields whose value is a user id, and therefore a player.
    /// </summary>
    /// <remarks>
    /// **An allowlist, because the alternative was demonstrably wrong.** Resolving every numeric
    /// field produced <c>damageamount=Ardaddy Ultrasex(14)</c> on a real demo — 14 damage collided
    /// with user id 14 — and turned <c>inflictor_entindex</c> into a player when the inflictor is a
    /// weapon entity. Falling back on unknown ids does not help there: the value was known, it
    /// simply was not a player.
    ///
    /// So a field earns resolution by being named, not by being a small integer. Entity-index
    /// fields are deliberately absent: they address entities, and most of the ones events carry
    /// are weapons and projectiles rather than players.
    /// </remarks>
    private static readonly HashSet<string> PlayerIdFields = new(StringComparer.Ordinal)
    {
        "userid", "attacker", "assister", "patient", "healer", "player",
    };

    /// <summary>
    /// Value at or above which a player reference means "nobody". TF2 sends this rather than a
    /// null or a negative for an unassisted kill.
    /// </summary>
    private const int NoPlayerSentinel = 16384;

    /// <summary>Renders one event field, naming the player when the field refers to one.</summary>
    /// <param name="field">The field name and its decoded value.</param>
    /// <param name="byUserId">Players keyed by user id.</param>
    /// <returns>
    /// <c>Name(id)</c> for a known player, <c>none</c> for the absent-player sentinel, and the
    /// raw value otherwise.
    /// </returns>
    public static string Render(
        KeyValuePair<string, object?> field,
        IReadOnlyDictionary<int, PlayerInfo> byUserId)
    {
        ArgumentNullException.ThrowIfNull(byUserId);

        string raw = Convert.ToString(field.Value, CultureInfo.InvariantCulture) ?? string.Empty;

        if (!PlayerIdFields.Contains(field.Key) ||
            !int.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out int id))
        {
            return raw;
        }

        // An absent assister is transmitted as a large sentinel rather than a null, so a player
        // who was never involved would otherwise be looked up and reported as absent anyway -
        // this says so explicitly instead.
        if (id >= NoPlayerSentinel)
        {
            return "none";
        }

        return byUserId.TryGetValue(id, out PlayerInfo player)
            ? string.Create(CultureInfo.InvariantCulture, $"{player.Name}({id})")
            : raw;
    }
}
