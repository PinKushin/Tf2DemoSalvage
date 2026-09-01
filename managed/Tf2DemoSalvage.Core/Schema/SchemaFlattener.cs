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
/// <param name="Path">
/// The dotted chain of datatable member names from the class root down to this property —
/// <c>m_AttributeContainer.m_Item.m_AttributeList.m_Attributes.001.m_iRawValue32</c> — or empty
/// for a property reached through no named hop worth distinguishing.
///
/// **Carried because <c>OwnerTable.Name</c> is LOSSY for repeated sub-tables** (B234).
/// <c>SendPropUtlVectorDataTable</c> references the same element table twenty times under members
/// named <c>000</c>–<c>019</c>, and <c>DT_ScriptCreatedItem</c> embeds <c>DT_AttributeList</c>
/// twice — so fifty thousand attribute properties in one demo share two flat names, and only their
/// paths tell them apart.
/// </param>
/// <param name="ElementScoped">
/// Whether any hop on the path is a vector element (an all-digit member) or the vector's
/// <c>lengthproxy</c> — the properties whose flat name collides by construction, and which state
/// accumulation must therefore key by <see cref="Path"/>.
/// </param>
public readonly record struct FlatProperty(
    SendProperty Property,
    string OwnerTable,
    SendProperty? ArrayElement,
    string Path = "",
    bool ElementScoped = false);

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
        Collect(schema, table, excludes, flat, [], path: "", elementScoped: false);

        // Not a stable partition. An earlier version of this used one, on the reasoning that
        // preserving relative order within each group must be safer - and it produced the right
        // 741 properties for CTFPlayer in the wrong order, which is the one failure mode that
        // does not announce itself (RISKS B4, B12).
        //
        // The engine walks the list swapping each changes-often property with whatever sits at
        // the boundary. Changes-often properties therefore keep their relative order, but the
        // displaced ones land wherever the swap threw them - the tail is deliberately scrambled,
        // and reproducing that scramble exactly is the contract.
        int boundary = 0;

        for (int i = 0; i < flat.Count; i++)
        {
            if (!flat[i].Property.ChangesOften)
            {
                continue;
            }

            if (i != boundary)
            {
                (flat[i], flat[boundary]) = (flat[boundary], flat[i]);
            }

            boundary++;
        }

        return flat;
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
        HashSet<string> stack,
        string path,
        bool elementScoped)
    {
        if (!stack.Add(table.Name))
        {
            // Malformed schemas can reference in a cycle. Without this the failure mode is a
            // hang, which reads as slowness rather than as a bug.
            return;
        }

        List<FlatProperty> local = [];
        Iterate(schema, table, excludes, local, output, stack, path, elementScoped);
        output.AddRange(local);

        stack.Remove(table.Name);
    }

    private static void Iterate(
        DemoSchema schema,
        SendTable table,
        HashSet<(string Table, string Property)> excludes,
        List<FlatProperty> local,
        List<FlatProperty> output,
        HashSet<string> stack,
        string path,
        bool elementScoped)
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

                // **The member NAME is the identity the flat list loses, so it is carried down
                // here** (B234). A UtlVector's elements are members named `000`–`019` all
                // referencing one table, and the vector's length travels through a member named
                // `lengthproxy` — both are the point where twenty properties become one flat name.
                string childPath = path.Length == 0 ? property.Name : path + "." + property.Name;

                bool childScoped = elementScoped
                    || string.Equals(property.Name, "lengthproxy", StringComparison.Ordinal)
                    || IsAllDigits(property.Name);

                if ((property.Flags & CollapsibleFlag) != 0)
                {
                    // Inline: contributes at the point of reference, into this same list.
                    if (stack.Add(child.Name))
                    {
                        Iterate(schema, child, excludes, local, output, stack, childPath, childScoped);
                        stack.Remove(child.Name);
                    }
                }
                else
                {
                    // Separate: the child's whole list lands before this table's own.
                    Collect(schema, child, excludes, output, stack, childPath, childScoped);
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

            local.Add(new FlatProperty(
                property,
                table.Name,
                element,
                path.Length == 0 ? property.Name : path + "." + property.Name,
                elementScoped));
        }
    }

    /// <summary>Whether a datatable member's name is a vector element ordinal.</summary>
    private static bool IsAllDigits(string name)
    {
        if (name.Length == 0)
        {
            return false;
        }

        foreach (char letter in name)
        {
            if (letter is < '0' or > '9')
            {
                return false;
            }
        }

        return true;
    }
}
