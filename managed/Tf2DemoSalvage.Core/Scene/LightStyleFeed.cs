using System;
using System.Collections.Generic;
using System.Text;

using Tf2DemoSalvage.Core.Net;

namespace Tf2DemoSalvage.Core.Scene;

/// <summary>Every light style's pattern over a demo, from the <c>lightstyles</c> string table.</summary>
/// <remarks>
/// **The pattern is the entry's user data**, its text only the style's number. `R_AnimateLight` (`engine.dll`
/// `0x1800d3ec0`) reads it through `GetStringUserData` and takes one off the length for the terminating null. A switchable
/// light flips its style between `"m"` and `"a"` as it turns on and off — `koth_harvest_event` does so 80 times — so the
/// table is kept as a history by tick, which a seek can ask at any point.
/// </remarks>
public sealed class LightStyleFeed
{
    /// <summary>The string table.</summary>
    public const string TableName = "lightstyles";

    private readonly Dictionary<int, List<(int Tick, string Pattern)>> _changes = [];

    /// <summary>Records a create or update message's entries.</summary>
    /// <param name="entries">The entries.</param>
    /// <param name="tick">The update's tick; null for the table's creation, which holds from the start.</param>
    /// <exception cref="ArgumentNullException"><paramref name="entries"/> is null.</exception>
    public void Apply(IReadOnlyList<StringTableEntry> entries, int? tick)
    {
        ArgumentNullException.ThrowIfNull(entries);

        foreach (StringTableEntry entry in entries)
        {
            string pattern = Encoding.ASCII.GetString([.. entry.UserData]).TrimEnd('\0');

            if (!_changes.TryGetValue(entry.Index, out List<(int Tick, string Pattern)>? history))
            {
                _changes[entry.Index] = history = [];
            }

            history.Add((tick ?? int.MinValue, pattern));
        }
    }

    /// <summary>A style's pattern at a tick.</summary>
    /// <param name="style">The style.</param>
    /// <param name="tick">The tick.</param>
    /// <returns>The pattern the table held then; empty when it held none.</returns>
    public string PatternAt(int style, int tick)
    {
        string pattern = string.Empty;

        if (_changes.TryGetValue(style, out List<(int Tick, string Pattern)>? history))
        {
            foreach ((int at, string held) in history)
            {
                if (at > tick)
                {
                    break;
                }

                pattern = held;
            }
        }

        return pattern;
    }
}
