using System;
using System.Collections.Generic;

namespace Tf2DemoSalvage.Core.Net;

/// <summary>
/// One of the server's precache string tables, read as an index-to-name map.
/// </summary>
/// <remarks>
/// **Three tables of this exact shape ship in every demo** — `soundprecache`, `modelprecache` and
/// `EffectDispatch` — and each is a list of names that some later message refers to by index. The
/// third was written as a third copy (B305) with the extraction recorded as owed; this is it.
///
/// **What is shared is not just the dictionary.** Three rules travel with it, and each was learned
/// once and would have to be learned again per copy:
///
/// - **Keyed on the entry's own INDEX, not its position.** The two agree in a freshly created table
///   and stop agreeing the moment an update arrives out of order, and it is the index a message
///   refers to.
/// - **Updates count as well as the create message.** A late precache arrives as
///   <c>svc_UpdateStringTable</c>, so a reader built only from the create goes stale part way
///   through a demo.
/// - **An entry with no text is a real entry carrying only user data.** Skipped rather than stored,
///   or it would resolve to a name of <c>""</c> and blank one that had already arrived.
///
/// **`ModelPrecache` is deliberately NOT built on this.** It looks like a fourth, and it is not: a
/// model index is packed with the protocol's own bit layout and needs unpacking before it means
/// anything (`LastPackedIndexProtocol`), and it carries a second table for dynamic models. Forcing
/// it through here would hide both. The shape being similar is not the same as the behaviour being
/// shared — see `docs/memory/extraction-without-adoption-is-not-dry.md`, which is about the
/// opposite failure and applies to this one in the mirror.
/// </remarks>
public sealed class PrecacheTable
{
    private readonly Dictionary<int, string> _byIndex = [];

    /// <summary>Reads the named table.</summary>
    /// <param name="tableName">Which string table this holds, exactly as the server names it.</param>
    /// <exception cref="ArgumentNullException"><paramref name="tableName"/> is null.</exception>
    public PrecacheTable(string tableName)
    {
        ArgumentNullException.ThrowIfNull(tableName);

        TableName = tableName;
    }

    /// <summary>The string table this reads.</summary>
    public string TableName { get; }

    /// <summary>How many resolvable names it holds.</summary>
    public int Count => _byIndex.Count;

    /// <summary>Every name held, by index.</summary>
    public IReadOnlyDictionary<int, string> Entries => _byIndex;

    /// <summary>Takes a created string table's entries, if it is this one.</summary>
    /// <param name="table">Any created string table; others are ignored.</param>
    /// <exception cref="ArgumentNullException"><paramref name="table"/> is null.</exception>
    public void Add(CreateStringTableMessage table)
    {
        ArgumentNullException.ThrowIfNull(table);

        if (string.Equals(table.Name, TableName, StringComparison.Ordinal))
        {
            Take(table.Entries);
        }
    }

    /// <summary>Applies an update, if it targets this table.</summary>
    /// <param name="update">The update.</param>
    /// <param name="tableName">
    /// The name the update's table id resolved to, or null when it could not be resolved. **An
    /// update carries only an ID**, so the caller resolves it from its own state — and passing the
    /// wrong name writes another table's entries into this one.
    /// </param>
    /// <exception cref="ArgumentNullException"><paramref name="update"/> is null.</exception>
    public void Add(UpdateStringTableMessage update, string? tableName)
    {
        ArgumentNullException.ThrowIfNull(update);

        if (string.Equals(tableName, TableName, StringComparison.Ordinal))
        {
            Take(update.Entries);
        }
    }

    /// <summary>The name at an index, or null when it is not held.</summary>
    /// <param name="index">The index a message referred to.</param>
    /// <returns>The precached name, or null.</returns>
    /// <remarks>
    /// **Null rather than a placeholder.** A caller still prints the number, so an unresolved index
    /// loses nothing, while inventing a name would put a wrong one into a trace that reads like a
    /// decoded one.
    /// </remarks>
    public string? Resolve(int index) =>
        _byIndex.TryGetValue(index, out string? name) ? name : null;

    private void Take(IReadOnlyList<StringTableEntry> entries)
    {
        for (int at = 0; at < entries.Count; at++)
        {
            StringTableEntry entry = entries[at];

            if (entry.Text is { Length: > 0 } name)
            {
                _byIndex[entry.Index] = name;
            }
        }
    }
}
