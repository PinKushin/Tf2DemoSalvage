using System;
using System.Collections.Generic;
using System.Linq;
using Tf2DemoSalvage.Core.Net;

namespace Tf2DemoSalvage.Core.Schema;

/// <summary>
/// One property in a class's flattened list, with the context needed to decode and diagnose it.
/// </summary>
/// <param name="Property">The property definition.</param>
/// <param name="OwnerTable">The table that contributed it, for diagnostics.</param>
/// <param name="ArrayElement">
/// For an array, the element template describing how each element is encoded. Null otherwise.
/// </param>
public readonly record struct FlatProperty(
    SendProperty Property,
    string OwnerTable,
    SendProperty? ArrayElement);

/// <summary>
/// Flattens a SendTable hierarchy into the ordered list that entity deltas index into.
/// </summary>
/// <remarks>
/// **The highest-risk code in this project** (<c>RISKS.md</c> B4). Entity updates address
/// properties by *position* in this list. A wrong order does not throw and does not look
/// wrong — it reads real values into the wrong fields, and the demo silently describes a
/// different match.
///
/// Three rules produce that order, and each is easy to get backwards:
///
/// 1. **Exclusions are gathered first**, over the whole reachable hierarchy, before any
///    property is emitted. A derived table can exclude a property from a table it has not
///    referenced yet.
/// 2. **Collapsible children inline where they are referenced; non-collapsible children do
///    not.** A non-collapsible child's properties are appended as a group *before* the
///    referencing table's own properties, not at the point of reference.
/// 3. **`SPROP_CHANGES_OFTEN` properties move to the front by a stable partition**, not a
///    sort. Relative order within each group is part of the contract.
/// </remarks>
public static class SchemaFlattener
{
    /// <summary>Marks a nested table whose properties inline into the parent.</summary>
    private const int CollapsibleFlag = 1 << 12;

    /// <summary>Marks an array's element template, which is attached rather than emitted.</summary>
    private const int InsideArrayFlag = 1 << 8;

    /// <summary>Flattens the property list for a table.</summary>
    /// <param name="schema">The demo's schema.</param>
    /// <param name="tableName">Table to flatten, e.g. <c>DT_TFPlayer</c>.</param>
    /// <returns>Properties in the order entity deltas index them, or empty if unknown.</returns>
    public static IReadOnlyList<FlatProperty> Flatten(DemoSchema schema, string tableName)
    {
        ArgumentNullException.ThrowIfNull(schema);

        SendTable? table = schema.FindTable(tableName);
        if (table is null)
        {
            return [];
        }

        HashSet<(string Table, string Property)> excludes = GatherExcludes(schema, table);
        List<FlatProperty> flat = [];
        Collect(schema, table, excludes, flat, []);

        // A stable partition, not a sort: properties that change often move to the front while
        // relative order inside each group is preserved. An unstable sort would satisfy
        // "changes-often first" and still corrupt the addressing.
        return
        [
            .. flat.Where(f => f.Property.ChangesOften),
            .. flat.Where(f => !f.Property.ChangesOften),
        ];
    }

    /// <summary>Flattens the property list for a server class.</summary>
    /// <param name="schema">The demo's schema.</param>
    /// <param name="serverClass">The class whose table should be flattened.</param>
    /// <returns>Properties in the order entity deltas index them.</returns>
    public static IReadOnlyList<FlatProperty> Flatten(DemoSchema schema, ServerClass serverClass)
    {
        // Stryker disable once Statement: removing this guard changes nothing observable - the
        // call below guards the same argument and throws the same exception. Kept because it
        // fails at the boundary the caller actually touched. Equivalent mutant.
        ArgumentNullException.ThrowIfNull(schema);

        return Flatten(schema, serverClass.TableName);
    }

    /// <summary>
    /// Collects every exclusion reachable from a table, before any property is emitted.
    /// </summary>
    /// <remarks>
    /// Done as a separate pass because a table may exclude a property from a table it has not
    /// referenced yet — resolving exclusions lazily would apply some of them too late.
    /// </remarks>
    private static HashSet<(string Table, string Property)> GatherExcludes(
        DemoSchema schema, SendTable table)
    {
        HashSet<(string, string)> excludes = [];
        HashSet<string> visited = [];
        Walk(table);
        return excludes;

        void Walk(SendTable current)
        {
            if (!visited.Add(current.Name))
            {
                return;
            }

            foreach (SendProperty property in current.Properties)
            {
                if (property.IsExcluded)
                {
                    excludes.Add((property.ReferencedTable, property.Name));
                }
                else if (property.Type == SendPropType.DataTable)
                {
                    SendTable? child = schema.FindTable(property.ReferencedTable);
                    if (child is not null)
                    {
                        Walk(child);
                    }
                }
            }
        }
    }

    /// <summary>
    /// Emits one table's properties, appending non-collapsible children's groups first.
    /// </summary>
    private static void Collect(
        DemoSchema schema,
        SendTable table,
        HashSet<(string Table, string Property)> excludes,
        List<FlatProperty> output,
        HashSet<string> stack)
    {
        if (!stack.Add(table.Name))
        {
            // Malformed schemas can reference in a cycle. Without this the failure mode is a
            // hang, which reads as slowness rather than as a bug.
            return;
        }

        List<FlatProperty> local = [];
        Iterate(schema, table, excludes, local, output, stack);
        output.AddRange(local);

        stack.Remove(table.Name);
    }

    private static void Iterate(
        DemoSchema schema,
        SendTable table,
        HashSet<(string Table, string Property)> excludes,
        List<FlatProperty> local,
        List<FlatProperty> output,
        HashSet<string> stack)
    {
        IReadOnlyList<SendProperty> properties = table.Properties;

        for (int i = 0; i < properties.Count; i++)
        {
            SendProperty property = properties[i];

            // Exclusion markers describe a removal; they are never data themselves. Element
            // templates belong to the array that follows them.
            if (property.IsExcluded || (property.Flags & InsideArrayFlag) != 0)
            {
                continue;
            }

            if (excludes.Contains((table.Name, property.Name)))
            {
                continue;
            }

            if (property.Type == SendPropType.DataTable)
            {
                SendTable? child = schema.FindTable(property.ReferencedTable);
                if (child is null)
                {
                    continue;
                }

                if ((property.Flags & CollapsibleFlag) != 0)
                {
                    // Inline: contributes at the point of reference, into this same list.
                    if (stack.Add(child.Name))
                    {
                        Iterate(schema, child, excludes, local, output, stack);
                        stack.Remove(child.Name);
                    }
                }
                else
                {
                    // Separate: the child's whole list lands before this table's own.
                    Collect(schema, child, excludes, output, stack);
                }

                continue;
            }

            // An array's element template is the property immediately before it.
            SendProperty? element = null;
            if (property.Type == SendPropType.Array && i > 0 &&
                (properties[i - 1].Flags & InsideArrayFlag) != 0)
            {
                element = properties[i - 1];
            }

            local.Add(new FlatProperty(property, table.Name, element));
        }
    }
}
