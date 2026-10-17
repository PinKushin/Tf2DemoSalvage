using System;
using System.Collections.Generic;
using System.Linq;

using Tf2DemoSalvage.Core.Scene;
using Tf2DemoSalvage.Core.Schema;

namespace Tf2DemoSalvage.Core.Tests.Schema;

/// <summary>A decoded list stored without its definitions reads back exactly what was put in (D217 step 4, part 3).</summary>
public sealed class DecodedPropertiesTests
{
    private static readonly FlatProperty[] Flat =
    [
        .. Enumerable.Range(0, 4).Select(index => new FlatProperty(
            new SendProperty(SendPropType.Int, $"m_iValue{index}", 0, string.Empty, 0f, 0f, 32, 0), "DT_Test", null)),
    ];

    private static DecodedProperty[] Items() =>
    [
        new(0, Flat[0], PropertyValue.FromInt(1)),
        new(2, Flat[2], PropertyValue.FromFloat(-0f), IndexPayloadBits: 3, CoordShape: 1),
        new(3, Flat[3], PropertyValue.FromArray([PropertyValue.FromInt(9)]), ElementShapes: [2]),
    ];

    /// <remarks>**Lossless**: every field of every property, definitions from the class list, back as given.</remarks>
    [Test]
    public void Indexer_StoredAgainstTheClassList_ReadsBackEveryField()
    {
        DecodedProperty[] items = Items();

        DecodedProperties list = new(Flat, items);

        list.Flat.ShouldBeSameAs(Flat);
        list.Count.ShouldBe(3);

        // Field for field and bit for bit: the digest hashes every field, floats by their bits.
        HashSet<object> none = [];
        string[] read = [.. Enumerable.Range(0, items.Length).Select(index => StateCopy.Digest(list[index], none))];

        read.ShouldBe([.. items.Select(item => StateCopy.Digest(item, none))]);
    }

    /// <remarks>
    /// **A foreign definition keeps the list whole**: a property whose definition is not the class list's own is read
    /// back as given, and the list reports no class list.
    /// </remarks>
    [Test]
    public void Indexer_ADefinitionNotTheClassListsOwn_KeepsItWhole()
    {
        DecodedProperty[] items = Items();
        FlatProperty foreign = Flat[1] with { OwnerTable = "DT_Other" };
        items[1] = items[1] with { Definition = foreign };

        DecodedProperties list = new(Flat, items);

        (list.Flat, list[1].Definition.OwnerTable, list[0].Definition.OwnerTable).ShouldBe((null, "DT_Other", "DT_Test"));
    }

    /// <remarks>
    /// **What it is for, exactly**: with everything the properties reach already counted, the two lists differ only in
    /// their backing arrays, so by three properties times the difference between a whole property and a compact one.
    /// </remarks>
    [Test]
    public void Footprint_StoredAgainstTheClassList_IsSmallerByThreeCompactSavings()
    {
        DecodedProperty[] items = Items();
        HashSet<object> share = new(ReferenceEqualityComparer.Instance) { Flat };
        HashSet<object> reached = new(ReferenceEqualityComparer.Instance);
        StateCopy.Footprint(items, share, reached);
        reached.Remove(items);

        long compact = StateCopy.Footprint(new DecodedProperties(Flat, items), share, new HashSet<object>(reached, ReferenceEqualityComparer.Instance));
        long whole = StateCopy.Footprint(new DecodedProperties(items), share, new HashSet<object>(reached, ReferenceEqualityComparer.Instance));

        Type compactType = typeof(DecodedProperties).GetNestedType("Compact", System.Reflection.BindingFlags.NonPublic)!;
        int saving = System.Runtime.CompilerServices.RuntimeHelpers.SizeOf(typeof(DecodedProperty).TypeHandle)
            - System.Runtime.CompilerServices.RuntimeHelpers.SizeOf(compactType.TypeHandle);
        (whole - compact).ShouldBe(3L * saving);
        saving.ShouldBeGreaterThan(100);
    }

    private static DecodedProperties OfClass(params (int Index, long Value)[] items) =>
        DecodedProperties.OfClass(Flat, [.. Flat.Select(flat => PropertyKeys.Of(flat))], [.. items.Select(item => new DecodedProperty(item.Index, Flat[item.Index], PropertyValue.FromInt(item.Value)))]);

    /// <remarks>
    /// **The record-by-record merge is the general merge's answer**: an update over a baseline, each of one class in
    /// wire order, gives the same properties field for field as the sorted-dictionary path does over plain lists.
    /// </remarks>
    [Test]
    public void Overlay_TwoListsOfOneClass_IsTheGeneralMergesAnswer()
    {
        DecodedProperties baseline = OfClass((0, 1), (2, 3), (3, 4));
        DecodedProperties update = OfClass((1, 5), (2, 7));

        IReadOnlyList<DecodedProperty> fast = BaselineMerge.Overlay(baseline, update);
        IReadOnlyList<DecodedProperty> general = BaselineMerge.Overlay([.. baseline], [.. update]);

        HashSet<object> none = [];
        fast.ShouldBeOfType<DecodedProperties>().Flat.ShouldBeSameAs(Flat);
        Enumerable.Range(0, fast.Count).Select(index => StateCopy.Digest(fast[index], none))
            .ShouldBe(Enumerable.Range(0, general.Count).Select(index => StateCopy.Digest(general[index], none)));
        fast.Select(property => (property.Index, property.Value.AsInt)).ShouldBe([(0, 1L), (1, 5L), (2, 7L), (3, 4L)]);
    }

    /// <remarks>
    /// **Out of wire order, or of two classes, the fast merge declines** and the general one answers: an update whose
    /// indices fall (2 then 1) still merges by index, the later write winning, as the sorted dictionary always did.
    /// </remarks>
    [Test]
    public void Overlay_AnUpdateOutOfIndexOrder_FallsBackToTheGeneralMerge()
    {
        DecodedProperties baseline = OfClass((0, 1), (2, 3));
        DecodedProperties update = OfClass((2, 9), (1, 5), (1, 6));

        IReadOnlyList<DecodedProperty> merged = BaselineMerge.Overlay(baseline, update);

        merged.Select(property => (property.Index, property.Value.AsInt)).ShouldBe([(0, 1L), (1, 6L), (2, 9L)]);
        DecodedProperties.Merge(baseline, update).ShouldBeNull();
        DecodedProperties.Merge(baseline, new DecodedProperties(Flat.ToArray(), [new DecodedProperty(1, Flat[1], PropertyValue.FromInt(1))])).ShouldBeNull();
    }
}
