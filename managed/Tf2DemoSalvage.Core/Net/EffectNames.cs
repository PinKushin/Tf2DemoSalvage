using System;
using System.Collections.Generic;

namespace Tf2DemoSalvage.Core.Net;

/// <summary>
/// Resolves an effect index to the name the server precached — the <c>EffectDispatch</c> table.
/// </summary>
/// <remarks>
/// **<c>CTEEffectDispatch</c> is a DISPATCHER rather than an effect.** It carries a `CEffectData`
/// whose `m_iEffectName` names one effect out of a precached table, and everything else in the
/// record — origin, normal, surface property, damage type — is that effect's argument list. Without
/// the table the index is a bare number and the record says where something happened but not what.
///
/// **Measured before it was built** (B304, B305). In `z1800`, `m_iEffectName` is sent 1,697 times
/// across SEVEN distinct values:
///
/// <code>
///   474 x 3    427 x 5    427 x 4    253 x 1    64 x 2    48 x 6    4 x 0
/// </code>
///
/// Seven, against the thirty-nine base-game effect CLASSES `SDK-COVERAGE.md` counts — which is why
/// that report's denominator is the wrong one and this table is the right one.
///
/// **Updates as well as the create message**, for the same reason <see cref="SoundNames"/> does: a
/// late precache arrives as <c>svc_UpdateStringTable</c>, and a resolver built only from the create
/// goes stale part way through a demo.
///
/// **A third table of this exact shape**, after `soundprecache` and `modelprecache`. The extraction
/// belongs there rather than in a fourth copy, and B305 says so — kept separate here only because
/// generalising two working readers is a larger change than adding the one that was missing.
/// </remarks>
public sealed class EffectNames
{
    /// <summary>The string table effect indices address.</summary>
    /// <remarks>
    /// Confirmed present in a real demo before this was written: a trace of `z1800` carries one
    /// `EffectDispatch` create message, with `soundprecache` beside it as the control that says the
    /// search itself works.
    /// </remarks>
    public const string TableName = "EffectDispatch";

    private readonly PrecacheTable _table = new(TableName);

    /// <summary>Number of resolvable names held.</summary>
    public int Count => _table.Count;

    /// <summary>Every precached effect name held, by index.</summary>
    public IReadOnlyDictionary<int, string> Names => _table.Entries;

    /// <summary>Takes the entries of a created string table, if it is the effect table.</summary>
    /// <param name="table">Any created string table; others are ignored.</param>
    /// <exception cref="ArgumentNullException"><paramref name="table"/> is null.</exception>
    public void Add(CreateStringTableMessage table) => _table.Add(table);

    /// <summary>Applies a string table update, if it targets the effect table.</summary>
    /// <param name="update">The update.</param>
    /// <param name="tableName">
    /// Name the update's table id resolved to, or null when it could not be resolved.
    /// </param>
    /// <exception cref="ArgumentNullException"><paramref name="update"/> is null.</exception>
    public void Add(UpdateStringTableMessage update, string? tableName) =>
        _table.Add(update, tableName);

    /// <summary>The name at an index, or null when it is not held.</summary>
    /// <param name="index">The <c>m_iEffectName</c> value.</param>
    /// <returns>The precached name, or null.</returns>
    /// <remarks>
    /// **Null rather than a placeholder**, so a caller can say "index 4, unnamed" instead of
    /// printing something that reads like a real effect. An index this does not hold is a table
    /// that did not arrive, not an effect that has no name.
    /// </remarks>
    public string? Name(int index) => _table.Resolve(index);
}
