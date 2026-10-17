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
    /// **What it is for**: stored against the class list, a property keeps no definition, so the list's footprint is
    /// smaller than the same list kept whole by a fixed amount per property.
    /// </remarks>
    [Test]
    public void Footprint_StoredAgainstTheClassList_IsSmallerThanWhole()
    {
        HashSet<object> share = new(ReferenceEqualityComparer.Instance) { Flat };

        long compact = StateCopy.Footprint(new DecodedProperties(Flat, Items()), share, new HashSet<object>(ReferenceEqualityComparer.Instance));
        long whole = StateCopy.Footprint(new DecodedProperties(Items()), share, new HashSet<object>(ReferenceEqualityComparer.Instance));

        compact.ShouldBeLessThan(whole);
    }
}
