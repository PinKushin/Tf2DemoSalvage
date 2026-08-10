using System.Collections.Generic;
using System.Globalization;

namespace Tf2DemoSalvage.Core.Net;

/// <summary>
/// Accumulates the match roster from the <c>userinfo</c> string table.
/// </summary>
/// <remarks>
/// **A roster is built from the create message *and* every later update.** `userinfo` is created
/// once, during signon, so reading only the create message yields the roster as it stood at the
/// start — and players dropping and rejoining mid-match is routine in TF2. Anyone who connects
/// afterwards arrives as an `svc_UpdateStringTable`, which was ignored outright (RISKS B22).
///
/// The defect stayed invisible because it produces a *plausible* answer: a roster short by one,
/// or naming the previous occupant of a reused slot, passes every check that asks whether names
/// are non-empty and counts are sane.
///
/// **The entity index is the entry index.** In a create message each entry also carries its
/// index as decimal text, and the two always agree — verified across the whole corpus. An update
/// carries no text at all, so the index is the only source available, and using it for both
/// paths is what lets them share this code rather than diverge.
/// </remarks>
public static class RosterBuilder
{
    /// <summary>The table this reads. Updates name their table only by id, not by name.</summary>
    public const string TableName = "userinfo";

    /// <summary>Applies a table's entries to a roster, keyed by entity index.</summary>
    /// <param name="entries">Entries from a create or update message.</param>
    /// <param name="players">Roster to update in place. Later records replace earlier ones.</param>
    /// <remarks>
    /// Entries carrying no user data are skipped rather than removed. They mark a slot being
    /// vacated, and the question this answers is "who played in this match", not "who is
    /// connected right now" — a player who left is still someone the demo should name. A slot
    /// later reused by someone else overwrites the record, which is the correct outcome for
    /// both questions.
    /// </remarks>
    public static void Apply(
        IReadOnlyList<StringTableEntry> entries, IDictionary<int, PlayerInfo> players)
    {
        if (entries is null || players is null)
        {
            return;
        }

        foreach (StringTableEntry entry in entries)
        {
            if (entry.UserData.Count < PlayerInfo.RecordBytes)
            {
                continue;
            }

            // Where an entry carries text it is the index in decimal, and disagreement would
            // mean one of the two readings is wrong. Preferring the index and ignoring the text
            // silently would hide that; skipping the entry makes it visible as a missing player
            // rather than a wrong one.
            if (entry.Text is not null &&
                (!int.TryParse(entry.Text, NumberStyles.Integer, CultureInfo.InvariantCulture,
                    out int named) || named != entry.Index))
            {
                continue;
            }

            players[entry.Index] = PlayerInfo.Parse([.. entry.UserData], entry.Index);
        }
    }
}
