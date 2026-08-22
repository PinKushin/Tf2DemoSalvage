using System;
using System.Collections.Generic;
using System.Linq;

namespace Tf2DemoSalvage.Core.Net;

/// <summary>
/// Resolves a <c>svc_Sounds</c> sound index to the file the server precached for it.
/// </summary>
/// <remarks>
/// **A sound index is meaningless outside the demo that carries it.** The wire format never names
/// a sound — it sends an index into the <c>soundprecache</c> string table, which is built per
/// server and per map. Index 4440 is <c>vo/announcer_am_lastmanalive01.mp3</c> in one recording
/// and something else entirely in the next, so a trace printing the number alone reports the only
/// part of the sound that does not travel.
///
/// The table itself is large — 3,500 to 6,800 entries across the corpus — and arrives once in the
/// signon stream, sometimes LZSS- or Snappy-compressed. Nothing here decodes it; that already
/// happens in <see cref="StringTableCodec"/>. This type only keeps what came out.
///
/// **Updates are applied as well as the initial table**, because a map change or a late precache
/// sends <c>svc_UpdateStringTable</c> rather than a fresh <c>svc_CreateStringTable</c>, and a
/// resolver built only from the create message goes stale mid-demo.
/// </remarks>
public sealed class SoundNames
{
    /// <summary>The string table sound indices address.</summary>
    public const string TableName = "soundprecache";

    private readonly Dictionary<int, string> _byIndex = [];

    /// <summary>Number of resolvable names held.</summary>
    public int Count => _byIndex.Count;

    /// <summary>Every precached name held, in no particular order.</summary>
    /// <remarks>
    /// **For the callers that need the whole table rather than one index.** The mixer prefetches,
    /// and a test that checks every precached sound can be opened has to be able to ask for all of
    /// them. Collecting them at the call site instead would mean re-implementing the
    /// <see cref="TableName"/> filter above, so the caller would be measuring its own copy of the
    /// rule rather than this one.
    /// </remarks>
    public IEnumerable<string> Names => _byIndex.Values;

    /// <summary>Takes the entries of a created string table, if it is the sound table.</summary>
    /// <param name="table">Any created string table; non-sound tables are ignored.</param>
    /// <exception cref="ArgumentNullException"><paramref name="table"/> is null.</exception>
    public void Add(CreateStringTableMessage table)
    {
        ArgumentNullException.ThrowIfNull(table);

        if (!string.Equals(table.Name, TableName, StringComparison.Ordinal))
        {
            return;
        }

        Add(table.Entries);
    }

    /// <summary>Applies a string table update, if it targets the sound table.</summary>
    /// <param name="update">The update.</param>
    /// <param name="tableName">
    /// Name the update's table id resolved to, or <c>null</c> when it could not be resolved.
    /// </param>
    /// <exception cref="ArgumentNullException"><paramref name="update"/> is null.</exception>
    public void Add(UpdateStringTableMessage update, string? tableName)
    {
        ArgumentNullException.ThrowIfNull(update);

        if (!string.Equals(tableName, TableName, StringComparison.Ordinal))
        {
            return;
        }

        Add(update.Entries);
    }

    /// <summary>The precached path for a sound index, or <c>null</c> if it is not known.</summary>
    /// <param name="soundIndex">Index as <c>svc_Sounds</c> or <c>svc_Prefetch</c> carries it.</param>
    /// <returns>The path, or <c>null</c>.</returns>
    /// <remarks>
    /// Null rather than a placeholder: the caller still prints the number, so an unresolved
    /// index loses nothing, while inventing a name would put a wrong path in a trace that reads
    /// like a decoded one.
    /// </remarks>
    public string? Resolve(int soundIndex) =>
        _byIndex.TryGetValue(soundIndex, out string? name) ? name : null;

    private void Add(IReadOnlyList<StringTableEntry> entries)
    {
        // Keyed on the entry's own index, not its position in the list. The two agree in a
        // freshly created table and stop agreeing the moment an update arrives out of order, and
        // it is the entry's index that a sound message refers to.
        //
        // An entry with no text is a real entry carrying only user data - skipped rather than
        // stored, so it cannot resolve to a sound named "".
        foreach (StringTableEntry entry in entries.Where(e => !string.IsNullOrEmpty(e.Text)))
        {
            _byIndex[entry.Index] = entry.Text!;
        }
    }
}
