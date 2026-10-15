using System;
using System.Collections.Generic;
using System.Linq;

using Tf2DemoSalvage.Core.Schema;
using Tf2DemoSalvage.Core.Scene;

namespace Tf2DemoSalvage.Core.Tests.Scene;

/// <summary>An entity's property storage (D217 step 3): the old dictionary's order and answers, in chunks a checkpoint shares.</summary>
public sealed class PropertyBagTests
{
    /// <remarks>
    /// **Insertion order, an overwrite keeping its place** — the dictionary's, which readers that take the first matching
    /// key depend on.
    /// </remarks>
    [Test]
    public void Enumerate_AfterAnOverwrite_IsInsertionOrder()
    {
        PropertyBag bag = new();
        bag.Set("b", PropertyValue.FromInt(1), 1);
        bag.Set("a", PropertyValue.FromInt(2), 2);
        bag.Set("b", PropertyValue.FromInt(3), 3);

        bag.Select(entry => (entry.Key, entry.Value.AsInt)).ShouldBe([("b", 3L), ("a", 2L)]);
        (bag.Sequence("b"), bag.Sequence("a"), bag.Sequence("c")).ShouldBe((3L, 2L, -1L));
    }

    /// <remarks>**Clear restarts the order**, as `Dictionary.Clear` does, and forgets every sequence.</remarks>
    [Test]
    public void Enumerate_AfterClear_RestartsFromTheNextWrite()
    {
        PropertyBag bag = new();
        bag.Set("a", PropertyValue.FromInt(1), 1);
        bag.Set("b", PropertyValue.FromInt(2), 2);
        bag.Clear();
        bag.Set("b", PropertyValue.FromInt(5), 3);
        bag.Set("a", PropertyValue.FromInt(6), 4);

        bag.Keys.ShouldBe(["b", "a"]);
        bag.TryGetValue("z", out _).ShouldBeFalse();
    }

    /// <remarks>
    /// **What it is for**: a second copy after one write shares the key map and every chunk but the written one — one
    /// 32-slot chunk created for 100 properties.
    /// </remarks>
    [Test]
    public void StateCopy_AfterOneWrite_CreatesOneChunk()
    {
        PropertyBag bag = new();

        for (int key = 0; key < 100; key++)
        {
            bag.Set($"k{key}", PropertyValue.FromInt(key), key);
        }

        StateCopy.Of(bag, new HashSet<object>(), prior: null, out Dictionary<object, object> map);
        bag.Set("k50", PropertyValue.FromInt(-1), 101);
        Dictionary<Type, (long Objects, long Elements)> created = [];
        PropertyBag later = StateCopy.Of(bag, new HashSet<object>(), map, out _, created);

        // The chunk; the list holding the chunks is re-created too, since one of its references changed.
        created[typeof((PropertyValue, long)[])].ShouldBe((1, 32));
        created.ContainsKey(typeof(Dictionary<string, int>)).ShouldBeFalse();
        later["k50"].AsInt.ShouldBe(-1);
    }
}
