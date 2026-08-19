using System.Collections.Generic;
using System.Linq;

using Tf2DemoSalvage.Core.Net;
using Tf2DemoSalvage.Core.Schema;

namespace Tf2DemoSalvage.Core.Tests.Schema;

/// <summary>
/// Flattening a nested schema into the ordered list an entity update indexes into.
/// </summary>
/// <remarks>
/// **Converted from <c>CorpusSchemaTests</c>, whose flattening assertions were plausibility
/// checks** — every list is non-empty, every name is under 128 characters and free of control
/// characters, every bit count is between 0 and 32. Those catch a schema read at the wrong offset,
/// which is worth catching, and say nothing about whether the ORDER is right.
///
/// Order is the whole contract. An entity update names properties by their position in this list,
/// so a flattener that produces the right set in the wrong sequence decodes every property into
/// its neighbour's slot — and every plausibility check above still passes, because the values are
/// all still plausible. That failure was found once by diffing against another parser, not by any
/// assertion here; see <c>docs/memory/differential-beats-fixtures.md</c>.
///
/// A written schema states the expected order outright, which is the one thing found data cannot
/// do: nobody knows what order a real demo's flattened list should be in without reimplementing
/// the flattener to find out.
/// </remarks>
public sealed class SyntheticFlatteningTests
{
    [Test]
    public void FlattenedFor_ANestedSchema_PutsTheParentsPropertiesBeforeTheChildsOwn()
    {
        // Inheritance is expressed by nesting a DataTable property, and the engine flattens
        // depth-first at the point the nesting appears. A child that declared its own fields first
        // and then inherited would produce a different order for the same schema.
        IReadOnlyList<FlatProperty> flat = Flatten(
            new SendTable("DT_Base", NeedsDecoder: true,
            [
                Int("m_iBaseOne"),
                Int("m_iBaseTwo"),
            ]),
            new SendTable("DT_Child", NeedsDecoder: true,
            [
                Table("baseclass", "DT_Base"),
                Int("m_iChildOne"),
            ]));

        Names(flat).ShouldBe(["m_iBaseOne", "m_iBaseTwo", "m_iChildOne"]);
    }

    [Test]
    public void FlattenedFor_APropertyMarkedChangesOften_IsSortedForward()
    {
        // **SPROP_CHANGES_OFTEN reorders the list, and that is the detail a flattener gets wrong
        // silently.** The engine moves those properties to the front so the common case indexes
        // low, so a flattener that preserved declaration order produces the right set and the
        // wrong positions — which decodes every property into its neighbour's slot while every
        // value stays plausible.
        IReadOnlyList<FlatProperty> flat = Flatten(
            new SendTable("DT_Only", NeedsDecoder: true,
            [
                Int("m_iRare"),
                Int("m_iCommon", SendProperty.ChangesOftenFlag),
                Int("m_iAlsoRare"),
            ]));

        Names(flat).ShouldBe(["m_iCommon", "m_iRare", "m_iAlsoRare"]);
    }

    [Test]
    public void FlattenedFor_AnExcludedProperty_IsRemovedFromTheInheritedTable()
    {
        // An exclusion names a table and a property and removes it from what was inherited — it is
        // not a property of its own. A flattener that emitted the exclusion as an entry would
        // shift every index after it and leave the excluded field still in place, which is two
        // errors that partly mask each other.
        IReadOnlyList<FlatProperty> flat = Flatten(
            new SendTable("DT_Base", NeedsDecoder: true,
            [
                Int("m_iKept"),
                Int("m_iRemoved"),
            ]),
            new SendTable("DT_Child", NeedsDecoder: true,
            [
                Table("baseclass", "DT_Base"),
                Exclude("m_iRemoved", "DT_Base"),
                Int("m_iChildOwn"),
            ]));

        Names(flat).ShouldBe(["m_iKept", "m_iChildOwn"]);
    }

    [Test]
    public void FlattenedFor_EachProperty_KeepsTheTableItWasDeclaredIn()
    {
        // **The owner table is half of a property's identity**, because everything downstream
        // looks properties up as "DT_BaseEntity.m_iTeamNum". A flattener that recorded the class's
        // own table for inherited properties would produce a list that decodes correctly and
        // resolves nothing by name.
        IReadOnlyList<FlatProperty> flat = Flatten(
            new SendTable("DT_Base", NeedsDecoder: true, [Int("m_iInherited")]),
            new SendTable("DT_Child", NeedsDecoder: true,
            [
                Table("baseclass", "DT_Base"),
                Int("m_iOwn"),
            ]));

        flat.Select(entry => $"{entry.OwnerTable}.{entry.Property.Name}")
            .ShouldBe(["DT_Base.m_iInherited", "DT_Child.m_iOwn"]);
    }

    [Test]
    public void FlattenedFor_ADataTableProperty_IsNotItselfAnEntry()
    {
        // The nesting property is structure rather than data: it names where to descend and
        // carries no value. Emitting it would add an index an update never addresses.
        IReadOnlyList<FlatProperty> flat = Flatten(
            new SendTable("DT_Base", NeedsDecoder: true, [Int("m_iOne")]),
            new SendTable("DT_Child", NeedsDecoder: true, [Table("baseclass", "DT_Base")]));

        Names(flat).ShouldBe(["m_iOne"]);
    }

    /// <summary>Flattens a schema whose LAST table is the one the server class names.</summary>
    private static IReadOnlyList<FlatProperty> Flatten(params SendTable[] tables)
    {
        DemoSchema schema = new(
            tables,
            [new ServerClass(0, "CTest", tables[^1].Name)]);

        EntityDecoder decoder = new(
            schema, EntityDecoder.ClassIdBits(schema.ServerClasses.Count));

        return decoder.FlattenedFor(0);
    }

    private static IEnumerable<string> Names(IReadOnlyList<FlatProperty> flat) =>
        flat.Select(entry => entry.Property.Name);

    private static SendProperty Int(string name, int flags = 0) =>
        new(SendPropType.Int, name, flags, string.Empty, 0f, 0f, 11, 0);

    private static SendProperty Table(string name, string referenced) =>
        new(SendPropType.DataTable, name, 0, referenced, 0f, 0f, 0, 0);

    private static SendProperty Exclude(string name, string fromTable) =>
        new(SendPropType.Int, name, SendProperty.ExcludeFlag, fromTable, 0f, 0f, 0, 0);
}
