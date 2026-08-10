using System.Collections.Generic;
using System.Globalization;
using Tf2DemoSalvage.Core.Net;

namespace Tf2DemoSalvage.Core.Schema;

/// <summary>
/// Routes <c>instancebaseline</c> string table entries into a decoder.
/// </summary>
/// <remarks>
/// **The class id is the entry's text, not its index** — and that is the reverse of
/// <c>userinfo</c>, where the entity index *is* the entry index (RISKS B22). Verified across the
/// corpus: here the two differ on essentially every entry, with index 0 carrying class 353 and
/// index 1 carrying class 318. Reusing the `userinfo` rule would file every baseline under the
/// wrong class, and a baseline attached to the wrong class still decodes — into the wrong
/// fields, with plausible values.
///
/// Unlike `userinfo`, entries here carry their text on *updates* too, so a baseline written
/// mid-match names its own class and no index-to-class map is needed.
/// </remarks>
public static class BaselineBuilder
{
    /// <summary>The table this reads. Updates name their table only by id, not by name.</summary>
    public const string TableName = "instancebaseline";

    /// <summary>Records every usable baseline in a table's entries.</summary>
    /// <param name="entries">Entries from a create or update message.</param>
    /// <param name="decoder">Decoder to record them in. Later entries replace earlier ones.</param>
    /// <remarks>
    /// Entries without a numeric class id, or without a payload, are skipped rather than
    /// recorded. A cleared entry carries no bits, and storing an empty baseline for its class
    /// would seed entities with nothing while making them look seeded — the difference between
    /// "no baseline" and "an empty baseline" is exactly what
    /// <see cref="EntityDecoder.Baseline"/> returns null to preserve.
    /// </remarks>
    public static void Apply(IReadOnlyList<StringTableEntry> entries, EntityDecoder decoder)
    {
        if (entries is null || decoder is null)
        {
            return;
        }

        foreach (StringTableEntry entry in entries)
        {
            if (entry.UserData.Count == 0 ||
                !int.TryParse(
                    entry.Text, NumberStyles.Integer, CultureInfo.InvariantCulture, out int classId))
            {
                continue;
            }

            decoder.SetBaseline(classId, [.. entry.UserData]);
        }
    }
}
