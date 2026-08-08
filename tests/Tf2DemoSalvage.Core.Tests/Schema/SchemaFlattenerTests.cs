using System.Collections.Generic;
using System.Linq;
using Tf2DemoSalvage.Core.Net;
using Tf2DemoSalvage.Core.Schema;

namespace Tf2DemoSalvage.Core.Tests.Schema;

/// <summary>
/// Tests for flattening a SendTable hierarchy into the ordered list entity deltas index into.
/// </summary>
/// <remarks>
/// This is the highest-risk code in the project (<c>RISKS.md</c> B4). Entity updates address
/// properties by *position* in this list, so a wrong order does not fail — it reads real values
/// into the wrong fields. Nothing downstream would notice; the demo would simply describe a
/// different match.
///
/// Which is why the ordering rules get a test each rather than one end-to-end check.
/// </remarks>
public sealed class SchemaFlattenerTests
{
    private const int Collapsible = 1 << 12;
    private const int InsideArray = 1 << 8;
    private const int Exclude = 1 << 6;
    private const int ChangesOften = 1 << 10;

    private static SendProperty Prop(
        string name, SendPropType type = SendPropType.Int, int flags = 0, string table = "") =>
        new(type, name, flags, table, 0f, 1f, 8, 0);

    private static SendProperty Nested(string name, string table, int flags = 0) =>
        new(SendPropType.DataTable, name, flags, table, 0f, 0f, 0, 0);

    private static DemoSchema Schema(params SendTable[] tables) =>
        new(tables, [new ServerClass(0, "CTest", tables[0].Name)]);

    private static SendTable Table(string name, params SendProperty[] props) =>
        new(name, false, props);

    private static IReadOnlyList<string> Names(IEnumerable<FlatProperty> flat) =>
        [.. flat.Select(f => f.Property.Name)];

    [Fact]
    public void Flatten_PlainTable_KeepsPropertiesInOrder()
    {
        DemoSchema schema = Schema(Table("DT_A", Prop("a"), Prop("b"), Prop("c")));

        IReadOnlyList<FlatProperty> flat = SchemaFlattener.Flatten(schema, "DT_A");

        Names(flat).ShouldBe(["a", "b", "c"]);
    }

    [Fact]
    public void Flatten_CollapsibleChild_IsInlinedWhereItAppears()
    {
        // A collapsible table contributes its properties at the point of reference, as though
        // they had been written inline.
        DemoSchema schema = Schema(
            Table("DT_A", Prop("a"), Nested("sub", "DT_B", Collapsible), Prop("c")),
            Table("DT_B", Prop("b1"), Prop("b2")));

        IReadOnlyList<FlatProperty> flat = SchemaFlattener.Flatten(schema, "DT_A");

        Names(flat).ShouldBe(["a", "b1", "b2", "c"]);
    }

    [Fact]
    public void Flatten_NonCollapsibleChild_IsAppendedAfterTheParentsOwnProperties()
    {
        // The rule most easily got backwards. A non-collapsible child does *not* appear where
        // it is referenced - its properties follow the whole of the parent's own list.
        DemoSchema schema = Schema(
            Table("DT_A", Prop("a"), Nested("sub", "DT_B"), Prop("c")),
            Table("DT_B", Prop("b1"), Prop("b2")));

        IReadOnlyList<FlatProperty> flat = SchemaFlattener.Flatten(schema, "DT_A");

        Names(flat).ShouldBe(["b1", "b2", "a", "c"]);
    }

    [Fact]
    public void Flatten_ExcludedProperty_IsRemovedFromTheChild()
    {
        // An exclusion in a derived table removes an inherited property by (table, name).
        DemoSchema schema = Schema(
            Table("DT_A",
                Prop("m_iHealth", flags: Exclude, table: "DT_B"),
                Nested("sub", "DT_B", Collapsible)),
            Table("DT_B", Prop("m_iHealth"), Prop("m_iAmmo")));

        IReadOnlyList<FlatProperty> flat = SchemaFlattener.Flatten(schema, "DT_A");

        Names(flat).ShouldBe(["m_iAmmo"]);
    }

    [Fact]
    public void Flatten_ExclusionMarkers_AreNeverEmittedThemselves()
    {
        DemoSchema schema = Schema(
            Table("DT_A", Prop("keep"), Prop("gone", flags: Exclude, table: "DT_B")),
            Table("DT_B", Prop("gone")));

        Names(SchemaFlattener.Flatten(schema, "DT_A")).ShouldBe(["keep"]);
    }

    [Fact]
    public void Flatten_ArrayElementTemplate_IsAttachedNotEmitted()
    {
        // The element template carries SPROP_INSIDEARRAY and describes how each element is
        // encoded. It belongs to the array, not to the flattened list.
        DemoSchema schema = Schema(Table("DT_A",
            Prop("m_iAmmo_element", flags: InsideArray),
            new SendProperty(SendPropType.Array, "m_iAmmo", 0, string.Empty, 0f, 0f, 0, 32)));

        IReadOnlyList<FlatProperty> flat = SchemaFlattener.Flatten(schema, "DT_A");

        Names(flat).ShouldBe(["m_iAmmo"]);
        flat[0].ArrayElement.ShouldNotBeNull().Name.ShouldBe("m_iAmmo_element");
        flat[0].Property.ElementCount.ShouldBe(32);
    }

    [Fact]
    public void Flatten_ChangesOftenProperties_MoveToTheFront()
    {
        DemoSchema schema = Schema(Table("DT_A",
            Prop("slow1"),
            Prop("fast1", flags: ChangesOften),
            Prop("slow2"),
            Prop("fast2", flags: ChangesOften)));

        Names(SchemaFlattener.Flatten(schema, "DT_A")).ShouldBe(["fast1", "fast2", "slow1", "slow2"]);
    }

    [Fact]
    public void Flatten_ReorderingIsStable_WithinEachGroup()
    {
        // A stable partition, not a sort. Relative order inside each group is part of the
        // contract, and an unstable sort would pass the previous test while corrupting this one.
        DemoSchema schema = Schema(Table("DT_A",
            Prop("s1"), Prop("f1", flags: ChangesOften), Prop("s2"),
            Prop("f2", flags: ChangesOften), Prop("s3"), Prop("f3", flags: ChangesOften)));

        Names(SchemaFlattener.Flatten(schema, "DT_A"))
            .ShouldBe(["f1", "f2", "f3", "s1", "s2", "s3"]);
    }

    [Fact]
    public void Flatten_RecordsWhichTableEachPropertyCameFrom()
    {
        // Needed for diagnostics: a flattened list of 300 properties is unreadable without
        // knowing which table contributed each one.
        DemoSchema schema = Schema(
            Table("DT_A", Prop("a"), Nested("sub", "DT_B", Collapsible)),
            Table("DT_B", Prop("b")));

        IReadOnlyList<FlatProperty> flat = SchemaFlattener.Flatten(schema, "DT_A");

        flat.Single(f => f.Property.Name == "a").OwnerTable.ShouldBe("DT_A");
        flat.Single(f => f.Property.Name == "b").OwnerTable.ShouldBe("DT_B");
    }

    [Fact]
    public void Flatten_CircularReference_TerminatesRatherThanRecursingForever()
    {
        // Malformed rather than impossible, and the failure mode without a guard is a hang -
        // the worst kind, because it looks like slowness.
        DemoSchema schema = Schema(
            Table("DT_A", Prop("a"), Nested("loop", "DT_B", Collapsible)),
            Table("DT_B", Prop("b"), Nested("back", "DT_A", Collapsible)));

        IReadOnlyList<FlatProperty> flat = SchemaFlattener.Flatten(schema, "DT_A");

        Names(flat).ShouldContain("a");
        Names(flat).ShouldContain("b");
    }

    [Fact]
    public void Flatten_MissingReferencedTable_IsSkippedRatherThanThrowing()
    {
        DemoSchema schema = Schema(Table("DT_A", Prop("a"), Nested("sub", "DT_MISSING")));

        Names(SchemaFlattener.Flatten(schema, "DT_A")).ShouldBe(["a"]);
    }

    [Fact]
    public void Flatten_ExclusionNestedTwoLevelsDeep_IsStillFound()
    {
        // Exclusions are gathered by walking the whole hierarchy before anything is emitted.
        // If that walk failed to recurse through DataTable properties, a deep exclusion would
        // silently not apply - and the extra property would shift every later index by one.
        DemoSchema schema = Schema(
            Table("DT_A", Nested("mid", "DT_B", Collapsible)),
            Table("DT_B",
                Prop("m_iHealth", flags: Exclude, table: "DT_C"),
                Nested("deep", "DT_C", Collapsible)),
            Table("DT_C", Prop("m_iHealth"), Prop("m_iAmmo")));

        Names(SchemaFlattener.Flatten(schema, "DT_A")).ShouldBe(["m_iAmmo"]);
    }

    [Fact]
    public void Flatten_SameChildReferencedTwice_ContributesTwice()
    {
        // A diamond. The cycle guard must be scoped to the current path, not permanent -
        // otherwise the second reference is dropped and every index after it shifts.
        DemoSchema schema = Schema(
            Table("DT_A",
                Nested("first", "DT_Shared", Collapsible),
                Prop("mid"),
                Nested("second", "DT_Shared", Collapsible)),
            Table("DT_Shared", Prop("s")));

        Names(SchemaFlattener.Flatten(schema, "DT_A")).ShouldBe(["s", "mid", "s"]);
    }

    [Fact]
    public void Flatten_SameNonCollapsibleChildReferencedTwice_ContributesTwice()
    {
        // The same diamond as above but through the non-collapsible path, which appends the
        // child as its own group rather than inlining it. Both paths need their cycle guard
        // scoped to the current descent; a permanent one would drop the second contribution
        // and shift every index after it.
        DemoSchema schema = Schema(
            Table("DT_A",
                Nested("first", "DT_Shared"),
                Prop("mid"),
                Nested("second", "DT_Shared")),
            Table("DT_Shared", Prop("s")));

        Names(SchemaFlattener.Flatten(schema, "DT_A")).ShouldBe(["s", "s", "mid"]);
    }

    [Fact]
    public void Flatten_ArrayAsFirstProperty_HasNoElementAndDoesNotCrash()
    {
        // An array with nothing before it. Looking back unconditionally would index past the
        // start of the list.
        DemoSchema schema = Schema(Table("DT_A",
            new SendProperty(SendPropType.Array, "arr", 0, string.Empty, 0f, 0f, 0, 4),
            Prop("after")));

        IReadOnlyList<FlatProperty> flat = SchemaFlattener.Flatten(schema, "DT_A");

        Names(flat).ShouldBe(["arr", "after"]);
        flat[0].ArrayElement.ShouldBeNull();
    }

    [Fact]
    public void Flatten_ArrayPrecededByAnOrdinaryProperty_DoesNotAdoptItAsAnElement()
    {
        // Only a property carrying SPROP_INSIDEARRAY is an element template. Treating any
        // predecessor as one would attach the wrong encoding to the array.
        DemoSchema schema = Schema(Table("DT_A",
            Prop("unrelated"),
            new SendProperty(SendPropType.Array, "arr", 0, string.Empty, 0f, 0f, 0, 4)));

        IReadOnlyList<FlatProperty> flat = SchemaFlattener.Flatten(schema, "DT_A");

        Names(flat).ShouldBe(["unrelated", "arr"]);
        flat[1].ArrayElement.ShouldBeNull();
    }

    [Fact]
    public void Flatten_NonArrayPrecededByAnElementTemplate_TakesNoElement()
    {
        DemoSchema schema = Schema(Table("DT_A",
            Prop("tmpl", flags: InsideArray),
            Prop("ordinary")));

        IReadOnlyList<FlatProperty> flat = SchemaFlattener.Flatten(schema, "DT_A");

        Names(flat).ShouldBe(["ordinary"]);
        flat[0].ArrayElement.ShouldBeNull();
    }

    [Fact]
    public void Flatten_NullSchema_Throws()
    {
        Should.Throw<System.ArgumentNullException>(
            () => SchemaFlattener.Flatten(null!, "DT_A"));
        Should.Throw<System.ArgumentNullException>(
            () => SchemaFlattener.Flatten(null!, new ServerClass(0, "C", "DT_A")));
    }

    [Fact]
    public void Flatten_ByServerClass_UsesItsTable()
    {
        DemoSchema schema = Schema(Table("DT_A", Prop("a")), Table("DT_B", Prop("b")));

        Names(SchemaFlattener.Flatten(schema, new ServerClass(1, "CB", "DT_B"))).ShouldBe(["b"]);
    }

    [Fact]
    public void Flatten_UnknownTable_YieldsNothing()
    {
        DemoSchema schema = Schema(Table("DT_A", Prop("a")));

        SchemaFlattener.Flatten(schema, "DT_NOPE").ShouldBeEmpty();
    }
}
