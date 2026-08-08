using System;
using System.Collections.Generic;
using System.Linq;
using Tf2DemoSalvage.Core.Net;

namespace Tf2DemoSalvage.Core.Schema;

/// <summary>
/// The entity schema a demo carries in its <c>dem_datatables</c> command.
/// </summary>
/// <param name="Tables">Every SendTable, in wire order.</param>
/// <param name="ServerClasses">Class ids paired with the table that describes them.</param>
/// <remarks>
/// This is the project premise made concrete: the file describes its own entity layout, so a
/// parser never has to agree with any particular TF2 build. It is also why the live client
/// rejects old demos and a standalone parser does not — the client validates these tables
/// against its own compiled-in definitions and gives up on a mismatch.
///
/// Deliberately *not* flattened. Entity deltas index into a flattened property list, built by
/// merging nested tables, applying exclusions, then sorting <c>SPROP_CHANGES_OFTEN</c>
/// properties forward. That is a separate step, and the place silent wrongness will live — see
/// <c>RISKS.md</c> B4.
/// </remarks>
public sealed record DemoSchema(
    IReadOnlyList<SendTable> Tables,
    IReadOnlyList<ServerClass> ServerClasses)
{
    /// <summary>Finds a table by name, or <c>null</c> if the schema has no such table.</summary>
    /// <param name="name">Table name, e.g. <c>DT_TFPlayer</c>.</param>
    /// <returns>The table, or <c>null</c>.</returns>
    public SendTable? FindTable(string name) =>
        Tables.FirstOrDefault(t => string.Equals(t.Name, name, StringComparison.Ordinal));
}
