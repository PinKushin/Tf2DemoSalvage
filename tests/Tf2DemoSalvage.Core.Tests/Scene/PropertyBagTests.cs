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

    /// <summary>40 keys — two chunks — of every kind, the floats chosen to differ only in their bits.</summary>
    private static PropertyBag Filled()
    {
        PropertyBag bag = new();

        for (int key = 0; key < 40; key++)
        {
            bag.Set($"k{key}", PropertyValue.FromInt(key * 3), 100 + key);
        }

        bag.Set("negative zero", PropertyValue.FromFloat(-0f), 200);
        bag.Set("nan", PropertyValue.FromFloat(BitConverter.Int32BitsToSingle(0x7FC0_1234)), 201);
        bag.Set("vector", PropertyValue.FromVector(1.5f, -0f, 3), 202);
        bag.Set("name", PropertyValue.FromString("scout"), 203);
        bag.Set("array", PropertyValue.FromArray([PropertyValue.FromInt(7)]), 204);
        bag.Set("k3", PropertyValue.FromInt(-3), 205);
        return bag;
    }

    private static string Describe(PropertyBag bag) => string.Join(
        ";",
        bag.Select(entry => $"{entry.Key}={entry.Value.Kind}:{Bits(entry.Value)}@{bag.Sequence(entry.Key)}"));

    private static string Bits(PropertyValue value) => value.Kind switch
    {
        PropertyValueKind.Float => $"{BitConverter.SingleToInt32Bits(value.AsFloat):X8}",
        PropertyValueKind.Vector => $"{BitConverter.SingleToInt32Bits(value.AsVector.X):X8},{BitConverter.SingleToInt32Bits(value.AsVector.Y):X8},{BitConverter.SingleToInt32Bits(value.AsVector.Z):X8}",
        PropertyValueKind.Array => $"[{string.Join(",", value.AsArray.Select(item => item.AsInt))}]",
        _ => value.ToString(),
    };

    /// <remarks>
    /// **A frozen bag answers exactly as before it froze** (D217 step 4): every key, the order, every sequence, floats bit
    /// for bit (a negative zero, a NaN's payload), strings and arrays.
    /// </remarks>
    [Test]
    public void Freeze_EveryKind_AnswersAsBefore()
    {
        PropertyBag bag = Filled();
        string before = Describe(bag);

        bag.Freeze();

        Describe(bag).ShouldBe(before);
        (bag.Count, bag.ContainsKey("k39"), bag["k3"].AsInt, bag.Sequence("absent")).ShouldBe((45, true, -3L, -1L));
        bag.TryGetValue("name", out PropertyValue name).ShouldBeTrue();
        name.AsString.ShouldBe("scout");
        bag.TryGetValue("absent", out _).ShouldBeFalse();
    }

    /// <remarks>
    /// **A write after freezing thaws a copy and never touches the frozen form**, which a checkpoint's copy still reads:
    /// an overwrite in each chunk and a new key, then the copy answers as at the freeze and the bag as after the writes.
    /// </remarks>
    [Test]
    public void Set_AfterFreeze_LeavesTheFrozenFormUnchanged()
    {
        PropertyBag bag = Filled();
        bag.Freeze();
        string frozen = Describe(bag);
        PropertyBag checkpoint = StateCopy.Of(bag, new HashSet<object>());

        bag.Set("k0", PropertyValue.FromInt(1000), 300);
        bag.Set("k35", PropertyValue.FromInt(2000), 301);
        bag.Set("new", PropertyValue.FromInt(3000), 302);

        Describe(checkpoint).ShouldBe(frozen);
        (bag["k0"].AsInt, bag["k35"].AsInt, bag.Sequence("new"), bag.Keys.Last(), bag.Count).ShouldBe((1000L, 2000L, 302L, "new", 46));
        bag["k1"].AsInt.ShouldBe(3);
    }

    /// <remarks>**Clear after a freeze restarts the order on the bag alone**; the frozen form keeps every key.</remarks>
    [Test]
    public void Clear_AfterFreeze_ThenSet_RestartsOnlyTheBag()
    {
        PropertyBag bag = Filled();
        bag.Freeze();
        string frozen = Describe(bag);
        PropertyBag checkpoint = StateCopy.Of(bag, new HashSet<object>());

        bag.Clear();
        bag.Set("b", PropertyValue.FromInt(5), 1);
        bag.Set("k0", PropertyValue.FromInt(6), 2);

        bag.Keys.ShouldBe(["b", "k0"]);
        bag.Sequence("k1").ShouldBe(-1);
        Describe(checkpoint).ShouldBe(frozen);
    }
}
