using System.Collections.Generic;

using Tf2DemoSalvage.Core.Net;

namespace Tf2DemoSalvage.Core.Scene;

/// <summary>A string table read as names by index — what every precache table is to the client.</summary>
/// <remarks>
/// **One copy of the rule every precache shares**, which three tables had each written for themselves (models, dynamic
/// models, scenes) before decals made a fourth:
///
/// - **By the entry's own index, not its position in the message.** An update carries only the entries that changed,
///   each stating where it belongs, so numbering them from zero would rewrite the front of the table.
/// - **An entry with no text is skipped.** It is a payload-only update to one already named, and the name does not
///   change; an empty name at index zero is that table's placeholder for "none".
/// </remarks>
public sealed class NameTable
{
    private readonly Dictionary<int, string> _names = [];

    /// <summary>How many entries have a name.</summary>
    public int Count => _names.Count;

    /// <summary>Records a create or update message's entries; later ones replace earlier ones.</summary>
    /// <param name="entries">Entries from the message, or null for none.</param>
    public void Apply(IReadOnlyList<StringTableEntry>? entries)
    {
        if (entries is null)
        {
            return;
        }

        foreach (StringTableEntry entry in entries)
        {
            if (entry.Index < 0 || string.IsNullOrEmpty(entry.Text))
            {
                continue;
            }

            _names[entry.Index] = entry.Text;
        }
    }

    /// <summary>The name at an index, or null when the table has none there.</summary>
    /// <param name="index">The index the demo sent.</param>
    /// <returns>The name.</returns>
    public string? Name(int index) => _names.TryGetValue(index, out string? name) ? name : null;
}
